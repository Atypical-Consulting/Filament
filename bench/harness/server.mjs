#!/usr/bin/env node
/**
 * server.mjs — production-like static file server for the Filament benchmark harness.
 *
 * Design goals (Filament spec section 7):
 *   1. Serve a publish directory over HTTP with production-like compression.
 *   2. Serve the SMALLEST encoding the client accepts — brotli before gzip, exactly
 *      as a real static host / CDN does. Chrome sends `Accept-Encoding: gzip,
 *      deflate, br, zstd`, and `dotnet publish` emits BOTH .gz and .br siblings, so
 *      a gzip-only server would report Blazor ~22% heavier than any real host would
 *      transfer. That inflation would be baked into a headline metric.
 *   3. Prefer a precompressed sibling (foo.wasm.br / foo.wasm.gz) for the negotiated
 *      encoding; otherwise compress on the fly at the SAME maximum quality the
 *      publish step uses. The ORIGINAL file's Content-Type is used, with the
 *      matching Content-Encoding, so the browser transparently inflates and any
 *      integrity hash still matches the decompressed bytes.
 *   4. Compression eligibility is a DENYLIST, not an allowlist. An allowlist is only
 *      fair to file extensions its author happened to think of: an unanticipated
 *      extension would be shipped raw and that framework's weight inflated 3-4x with
 *      no signal. Everything is compressed except formats that are already
 *      compressed (png/jpg/woff2/gz/br/...), which keeps "never double-compress" a
 *      structural property while defaulting to fair for formats not yet seen.
 *   5. Correct Content-Type for .wasm (application/wasm) — Chrome refuses
 *      WebAssembly streaming compilation without it.
 *   6. Cache-Control: no-store on every response so every run is a cold cache.
 *
 * The server keeps a byte ledger (`stats`) of exactly what it wrote. bench.mjs
 * cross-checks that against Chrome's CDP encodedDataLength — two independent
 * measurements of the same quantity.
 *
 * Usage:
 *   node server.mjs --dir <publishDir> --port <port> [--host 127.0.0.1]
 *   import { startServer } from './server.mjs'
 */

import http from 'node:http';
import fsp from 'node:fs/promises';
import path from 'node:path';
import zlib from 'node:zlib';
import { promisify } from 'node:util';
import { fileURLToPath, pathToFileURL } from 'node:url';

const gzipAsync = promisify(zlib.gzip);
const brotliAsync = promisify(zlib.brotliCompress);

/**
 * On-the-fly gzip level. Precompressed artifacts emitted by `dotnet publish` are
 * produced at maximum compression, so on-the-fly compression uses level 9 too.
 * Anything lower would hand an unfair advantage to whichever framework happens to
 * ship precompressed siblings.
 */
export const GZIP_LEVEL = 9;

/**
 * On-the-fly brotli quality. Same reasoning as GZIP_LEVEL: `dotnet publish` emits
 * .br siblings at maximum quality, so a framework that ships no siblings must be
 * compressed just as hard or the comparison is rigged in favour of whoever
 * precompresses. 11 is the maximum and the publish default.
 */
export const BROTLI_QUALITY = 11;

export const DEFAULT_TYPE = 'application/octet-stream';

/** Extension -> Content-Type. */
export const MIME = new Map(Object.entries({
  '.html': 'text/html; charset=utf-8',
  '.htm': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.cjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.map': 'application/json; charset=utf-8',
  '.webmanifest': 'application/manifest+json; charset=utf-8',
  '.txt': 'text/plain; charset=utf-8',
  '.xml': 'application/xml; charset=utf-8',
  '.svg': 'image/svg+xml',
  // WebAssembly — required for streaming compile.
  '.wasm': 'application/wasm',
  // .NET / Blazor payload types.
  '.dat': 'application/octet-stream',   // ICU data
  '.blat': 'application/octet-stream',  // Blazor lazy-load blob
  '.dll': 'application/octet-stream',
  '.pdb': 'application/octet-stream',
  '.symbols': 'application/octet-stream',
  // Already-compressed binaries.
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.gif': 'image/gif',
  '.webp': 'image/webp',
  '.avif': 'image/avif',
  '.ico': 'image/x-icon',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
  '.ttf': 'font/ttf',
  '.otf': 'font/otf',
  '.eot': 'application/vnd.ms-fontobject',
  '.mp4': 'video/mp4',
  '.webm': 'video/webm',
  '.zip': 'application/zip',
  '.gz': 'application/gzip',
  '.br': 'application/octet-stream',
}));

/**
 * Extensions NEVER eligible for on-the-fly compression, because their bytes are
 * already compressed. This is a DENYLIST on purpose.
 *
 * The previous implementation used an allowlist of compressible extensions. That is
 * unfair by construction: the list can only contain formats its author anticipated
 * (it was, verbatim, .NET-shaped — ".blat  // Blazor lazy-load blob"), so the first
 * time a framework shipped a .data/.bin/.mem/.wat or an extensionless bundle, that
 * asset went out raw at 3-4x its compressed size and inflated that framework's
 * headline weight with no signal to the reader. Inverting the policy keeps the
 * "must not compress twice" guarantee structural (these formats can never reach a
 * compressor) while making the DEFAULT fair to formats nobody has seen yet.
 */
export const INCOMPRESSIBLE = new Set([
  // Images (all entropy-coded).
  '.png', '.jpg', '.jpeg', '.gif', '.webp', '.avif',
  // Fonts. woff/woff2 embed their own compression; ttf/otf do NOT and are omitted.
  '.woff', '.woff2',
  // Audio / video.
  '.mp3', '.m4a', '.aac', '.ogg', '.opus', '.mp4', '.m4v', '.mov', '.webm',
  // Archives / compressed streams.
  '.zip', '.gz', '.tgz', '.br', '.zst', '.bz2', '.xz', '.7z', '.rar',
]);

/** True when `ext` may be compressed on the fly. Unknown extensions are compressible. */
export function isCompressible(ext) {
  return !INCOMPRESSIBLE.has(String(ext).toLowerCase());
}

/** Requests for these extensions are served byte-for-byte, never re-encoded. */
export const PRECOMPRESSED_EXT = new Set(['.gz', '.br', '.zst']);

/**
 * RFC 9110 Accept-Encoding parsing: is `token` (or `*`) present with a non-zero
 * q-value?
 */
function acceptsEncoding(headerValue, token) {
  if (!headerValue) return false;
  for (const part of String(headerValue).split(',')) {
    const segments = part.trim().split(';');
    const name = (segments[0] || '').trim().toLowerCase();
    if (name !== token && name !== '*') continue;
    let q = 1;
    for (const p of segments.slice(1)) {
      const m = /^\s*q\s*=\s*([0-9]*\.?[0-9]+)\s*$/i.exec(p);
      if (m) q = Number.parseFloat(m[1]);
    }
    if (q > 0) return true;
  }
  return false;
}

export function acceptsGzip(headerValue) {
  return acceptsEncoding(headerValue, 'gzip');
}

/**
 * Brotli negotiation. Chrome always sends `br`, and `dotnet publish` always emits
 * .br siblings; without this the harness reported Blazor ~334 KB / 21.7% heavier
 * than a real host serving the same publish output would transfer.
 */
export function acceptsBrotli(headerValue) {
  return acceptsEncoding(headerValue, 'br');
}

/** Encoding ceilings accepted by `maxEncoding`, best-first. */
export const ENCODING_CEILINGS = ['br', 'gzip', 'identity'];

/**
 * The encoding to serve: the smallest the client accepts, brotli before gzip.
 * Returns 'br' | 'gzip' | null (identity).
 *
 * `maxEncoding` caps the negotiation at an encoding weaker than the client's best.
 * It defaults to 'br' — i.e. no cap, the production-like behaviour described above —
 * and exists because a measurement protocol may specify a particular encoding (e.g.
 * "gzip on"), and the honest way to satisfy that is to actually serve gzip rather
 * than to serve brotli and label the resulting bytes "gzip". Capping only ever makes
 * the reported weight LARGER, so it cannot be used to flatter a framework; it is a
 * deliberate, recorded choice, never a silent default.
 */
export function negotiateEncoding(headerValue, maxEncoding = 'br') {
  if (!ENCODING_CEILINGS.includes(maxEncoding)) {
    throw new Error(
      `server.mjs: maxEncoding must be one of ${ENCODING_CEILINGS.join(' | ')}, got "${maxEncoding}"`,
    );
  }
  if (maxEncoding === 'identity') return null;
  if (maxEncoding === 'br' && acceptsBrotli(headerValue)) return 'br';
  if (acceptsGzip(headerValue)) return 'gzip';
  // A br-only client under a gzip ceiling gets identity: serving br here would
  // silently defeat the cap the caller asked for.
  return null;
}

export function contentTypeFor(filePath) {
  return MIME.get(path.extname(filePath).toLowerCase()) ?? DEFAULT_TYPE;
}

async function statFile(p) {
  try {
    const st = await fsp.stat(p);
    return st.isFile() ? st : null;
  } catch {
    return null;
  }
}

/**
 * Resolve a URL pathname to a file inside root, refusing traversal.
 * Returns null when the path escapes root.
 */
function resolveSafe(root, pathname) {
  let rel = pathname;
  try {
    rel = decodeURIComponent(pathname);
  } catch {
    return null;
  }
  if (rel.includes('\0')) return null;
  if (rel.endsWith('/')) rel += 'index.html';
  const resolved = path.resolve(root, '.' + path.posix.normalize(rel));
  if (resolved !== root && !resolved.startsWith(root + path.sep)) return null;
  return resolved;
}

export async function startServer({ dir, port = 0, host = '127.0.0.1', quiet = false, maxEncoding = 'br' } = {}) {
  const root = path.resolve(dir);
  const rootStat = await fsp.stat(root).catch(() => null);
  if (!rootStat || !rootStat.isDirectory()) {
    throw new Error(`server.mjs: --dir is not a directory: ${root}`);
  }
  if (!ENCODING_CEILINGS.includes(maxEncoding)) {
    throw new Error(
      `server.mjs: maxEncoding must be one of ${ENCODING_CEILINGS.join(' | ')}, got "${maxEncoding}"`,
    );
  }

  /**
   * body cache: filePath|variant -> { body, encoding, mtimeMs }
   * Purely a CPU optimisation. Bytes on the wire are identical with or without it;
   * it only keeps level-9 gzip of a 20 MB dotnet.native.wasm off the hot path so
   * server CPU never contaminates a timing run.
   */
  const cache = new Map();

  const stats = {
    totalRequests: 0,
    totalBodyBytes: 0,
    notFound: [],
    byPath: new Map(),
    /** Negotiated Content-Encoding -> { responses, bytes }. Proves what was actually shipped. */
    byEncoding: new Map(),
    reset() {
      stats.totalRequests = 0;
      stats.totalBodyBytes = 0;
      stats.notFound = [];
      stats.byPath = new Map();
      stats.byEncoding = new Map();
    },
    snapshot() {
      return {
        totalRequests: stats.totalRequests,
        totalBodyBytes: stats.totalBodyBytes,
        notFound: [...stats.notFound],
        byPath: Object.fromEntries(stats.byPath),
        byEncoding: Object.fromEntries(stats.byEncoding),
      };
    },
  };

  const SIBLING_EXT = { br: '.br', gzip: '.gz' };

  async function compress(raw, encoding) {
    if (encoding === 'br') {
      return brotliAsync(raw, {
        params: {
          [zlib.constants.BROTLI_PARAM_QUALITY]: BROTLI_QUALITY,
          [zlib.constants.BROTLI_PARAM_SIZE_HINT]: raw.length,
        },
      });
    }
    return gzipAsync(raw, { level: GZIP_LEVEL });
  }

  /**
   * Resolve the bytes to send for `filePath` under the negotiated `want`
   * ('br' | 'gzip' | null). Order, for the negotiated encoding only:
   *   1. precompressed sibling emitted by the publish step (Blazor ships both),
   *   2. on-the-fly compression at publish-equivalent quality (denylist-gated),
   *   3. raw.
   * Serving the best encoding the CLIENT accepts — rather than the best encoding
   * the FRAMEWORK happened to precompress — is what makes this symmetric.
   */
  async function loadVariant(filePath, st, want) {
    const ext = path.extname(filePath).toLowerCase();
    const key = `${filePath}|${want ?? 'raw'}`;
    const hit = cache.get(key);
    if (hit && hit.mtimeMs === st.mtimeMs) return hit;

    let entry = null;
    if (want === 'br' || want === 'gzip') {
      const sibling = filePath + SIBLING_EXT[want];
      const siblingStat = await statFile(sibling);
      if (siblingStat) {
        entry = {
          body: await fsp.readFile(sibling),
          encoding: want,
          mtimeMs: st.mtimeMs,
          source: 'precompressed',
        };
      } else if (isCompressible(ext)) {
        const raw = await fsp.readFile(filePath);
        entry = {
          body: await compress(raw, want),
          encoding: want,
          mtimeMs: st.mtimeMs,
          source: 'ondemand',
        };
      }
    }
    // Already-compressed format, or the client accepts nothing we can produce:
    // serve raw. This is the ONLY path that reaches a denylisted extension, so
    // double-encoding remains structurally impossible.
    if (!entry) {
      entry = { body: await fsp.readFile(filePath), encoding: null, mtimeMs: st.mtimeMs, source: 'raw' };
    }
    cache.set(key, entry);
    return entry;
  }

  const server = http.createServer(async (req, res) => {
    const started = Date.now();
    let requestPath = '/';
    try {
      if (req.method !== 'GET' && req.method !== 'HEAD') {
        res.writeHead(405, { 'Allow': 'GET, HEAD', 'Cache-Control': 'no-store', 'Content-Length': '0' });
        res.end();
        return;
      }

      const parsed = new URL(req.url, `http://${host}`);
      requestPath = parsed.pathname;

      let filePath = resolveSafe(root, parsed.pathname);
      if (!filePath) {
        res.writeHead(403, { 'Cache-Control': 'no-store', 'Content-Length': '0' });
        res.end();
        return;
      }

      let st = await statFile(filePath);

      // SPA fallback: extension-less paths (client-side routes) fall back to index.html.
      // Asset-looking requests are honestly 404'd rather than silently answered with
      // HTML, which would corrupt both byte accounting and app-ready detection.
      if (!st && path.extname(filePath) === '') {
        const indexPath = path.join(root, 'index.html');
        const indexStat = await statFile(indexPath);
        if (indexStat) {
          filePath = indexPath;
          st = indexStat;
        }
      }

      if (!st) {
        stats.totalRequests += 1;
        stats.notFound.push(parsed.pathname);
        if (!quiet) process.stderr.write(`[server] 404 ${parsed.pathname}\n`);
        const body = Buffer.from('404 Not Found');
        res.writeHead(404, {
          'Content-Type': 'text/plain; charset=utf-8',
          'Content-Length': String(body.length),
          'Cache-Control': 'no-store',
        });
        res.end(req.method === 'HEAD' ? undefined : body);
        return;
      }

      const ext = path.extname(filePath).toLowerCase();

      // A request that explicitly targets a .gz/.br file gets those bytes verbatim,
      // with no Content-Encoding — re-encoding them would be the "compress twice" bug.
      const isPrecompressedRequest = PRECOMPRESSED_EXT.has(ext);
      const want = isPrecompressedRequest ? null : negotiateEncoding(req.headers['accept-encoding'], maxEncoding);

      const variant = await loadVariant(filePath, st, want);
      const contentType = contentTypeFor(filePath);

      const headers = {
        'Content-Type': contentType,
        'Content-Length': String(variant.body.length),
        'Cache-Control': 'no-store',
        'Vary': 'Accept-Encoding',
        'X-Bench-Encoding-Source': variant.source,
      };
      if (variant.encoding) headers['Content-Encoding'] = variant.encoding;

      stats.totalRequests += 1;
      if (req.method === 'GET') {
        stats.totalBodyBytes += variant.body.length;
        const prev = stats.byPath.get(parsed.pathname) ?? { hits: 0, bytes: 0, encoding: variant.encoding, source: variant.source };
        prev.hits += 1;
        prev.bytes += variant.body.length;
        stats.byPath.set(parsed.pathname, prev);

        const encKey = variant.encoding ?? 'identity';
        const enc = stats.byEncoding.get(encKey) ?? { responses: 0, bytes: 0 };
        enc.responses += 1;
        enc.bytes += variant.body.length;
        stats.byEncoding.set(encKey, enc);
      }

      res.writeHead(200, headers);
      res.end(req.method === 'HEAD' ? undefined : variant.body);
    } catch (err) {
      if (!quiet) process.stderr.write(`[server] 500 ${requestPath}: ${err && err.stack || err}\n`);
      if (!res.headersSent) {
        res.writeHead(500, { 'Cache-Control': 'no-store', 'Content-Length': '0' });
      }
      res.end();
    } finally {
      void started;
    }
  });

  server.keepAliveTimeout = 5000;

  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(port, host, () => {
      server.removeListener('error', reject);
      resolve();
    });
  });

  const actualPort = server.address().port;
  const url = `http://${host}:${actualPort}`;

  return {
    server,
    url,
    port: actualPort,
    host,
    root,
    maxEncoding,
    stats,
    async close() {
      await new Promise((resolve) => server.close(() => resolve()));
    },
  };
}

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------
function parseCliArgs(argv) {
  const out = { dir: null, port: 8080, host: '127.0.0.1' };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    const eq = a.indexOf('=');
    const key = eq > -1 ? a.slice(0, eq) : a;
    const inlineVal = eq > -1 ? a.slice(eq + 1) : null;
    const next = () => (inlineVal !== null ? inlineVal : argv[++i]);
    switch (key) {
      case '--dir': out.dir = next(); break;
      case '--port': out.port = Number.parseInt(next(), 10); break;
      case '--host': out.host = next(); break;
      case '--help': case '-h': out.help = true; break;
      default:
        throw new Error(`server.mjs: unknown argument ${a}`);
    }
  }
  return out;
}

const isMain = process.argv[1] && pathToFileURL(process.argv[1]).href === import.meta.url;
if (isMain) {
  const args = parseCliArgs(process.argv.slice(2));
  if (args.help || !args.dir) {
    process.stdout.write('Usage: node server.mjs --dir <publishDir> [--port 8080] [--host 127.0.0.1]\n');
    process.exit(args.help ? 0 : 1);
  }
  const s = await startServer(args);
  process.stdout.write(`[server] serving ${s.root}\n[server] listening on ${s.url}\n`);
  const shutdown = async () => { await s.close(); process.exit(0); };
  process.on('SIGINT', shutdown);
  process.on('SIGTERM', shutdown);
}

void fileURLToPath;
