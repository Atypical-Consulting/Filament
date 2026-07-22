// The engine loader: boot the .NET runtime, hydrate the curated reference pack into MEMFS, engage
// the seam, prove the wiring (Ready compiles a known-good component), then expose the compiler.
// This module is shared by the dev host shell (index.html here) and the site page, which imports
// window.filamentPlayground after loading it.
import { dotnet } from './_framework/dotnet.js'

const status = (msg) => {
  const el = document.getElementById('status');
  if (el) el.textContent = msg;
};

async function boot() {
  status('booting .NET runtime…');
  const diag = new URLSearchParams(location.search).has('monolog');
  const builder = diag
    ? dotnet.withDiagnosticTracing(true)
        .withEnvironmentVariable('MONO_LOG_LEVEL', 'debug')
        .withEnvironmentVariable('MONO_LOG_MASK', 'aot')
    : dotnet;
  const { getAssemblyExports, getConfig } = await builder.create();
  const exports = await getAssemblyExports(getConfig().mainAssemblyName);
  const api = exports.Filament.Playground.PlaygroundApi;

  status('fetching reference pack…');
  const base = new URL('./refpack/', import.meta.url);
  const manifest = await (await fetch(new URL('refpack.manifest.json', base))).json();
  let downloaded = 0;
  for (const f of manifest.files) {
    const bytes = new Uint8Array(await (await fetch(new URL(f.path, base))).arrayBuffer());
    api.WriteRefAssembly(f.path, bytes);
    downloaded += f.bytes;
    status(`reference pack: ${(downloaded / 1024 / 1024).toFixed(1)} / ${(manifest.totalBytes / 1024 / 1024).toFixed(1)} MB`);
  }

  status('proving the wiring…');
  for (let s = 0; s <= 3; s++) {
    const r = api.Probe(s);
    console.log(`[playground probe ${s}]`, r);
    status(`probe ${s}: ${r}`);
    if (r.startsWith('EX[')) throw new Error(`probe ${s} failed: ${r}`);
  }
  const ready = JSON.parse(api.Ready());
  if (!ready.ok) {
    status('engine failed its own probe. See console.');
    console.error('Filament playground probe refused:', ready.diagnostics);
    throw new Error('playground engine probe failed');
  }

  const compile = (razor, runtimeSpecifier) => JSON.parse(api.CompileRazor(razor, runtimeSpecifier));
  window.filamentPlayground = { compile, manifestBytes: manifest.totalBytes, probeMs: ready.ms };
  window.dispatchEvent(new CustomEvent('filament-playground-ready'));
  status(`ready · probe compiled in ${ready.ms} ms`);
  return compile;
}

const compilePromise = boot();

// Dev-host wiring (absent on the site page, which does its own).
const btn = document.getElementById('compile');
if (btn) {
  compilePromise.then((compile) => {
    btn.disabled = false;
    btn.addEventListener('click', () => {
      const r = compile(document.getElementById('razor').value, './filament.js');
      document.getElementById('out').textContent = r.ok
        ? `// ${r.ms} ms\n${r.js}`
        : `REFUSED (${r.ms} ms)\n${(r.diagnostics || []).join('\n')}`;
    });
  }).catch((e) => console.error(e));
}
