/**
 * RouteParams — hand-written Filament answer key for the ROUTER of baseline/RouteParams.Blazor.
 *
 * THE POINT: route parameters. Decision 163, and the closing of the honesty register's A9 and D12.
 *
 * WHAT WAS WRONG. Decision 139 shipped a router whose table is matched by string equality:
 *
 *     const hit = routes.find(([r]) => r === location.pathname);
 *
 * For `/` and `/about` that is not an approximation of anything — it is exactly right, and it is what
 * BENCH n°57 measured. For `@page "/item/{Id:int}"` it is a BLANK SCREEN: the route literal goes into the
 * table verbatim, `/item/7` equals nothing in it, and the app renders nothing at exit 0 with no
 * diagnostic. The register logged that as A9, and logged as D12 the reason it could not be patched — a
 * route parameter needs a real matcher, a precedence order, AND a channel that carries the captured value
 * into the page, and every naive version of each of those three was measured against Blazor and refuted.
 *
 * SO THIS FILE IS A SECOND EMISSION, NOT A WIDER FIRST ONE. A route table with no parameters still gets
 * decision 139's router, byte for byte — `samples/Routing/router.js` is untouched and its snapshot still
 * holds. Only a table that actually declares a `{…}` gets what is below. That is decision 139's own
 * pay-for-what-you-use argument applied one level down: the app that does not use route parameters pays
 * nothing for the fact that they exist.
 *
 * THE THREE THINGS D12 SAID WERE NEEDED, and where each one is:
 *
 *   1. A REAL MATCHER — `match()`. Blazor splits the path on '/', DROPS empty segments (so a trailing
 *      slash and a doubled slash still match), URL-decodes each one, and compares a literal segment
 *      case-INSENSITIVELY. A parameter's segment goes through its constraint's converter, and a
 *      constraint that REJECTS is a NON-MATCH rather than an error — the scan carries on to the next
 *      route. That last detail is what lets `/item/new` reach a literal page even though `/item/{Id:int}`
 *      is in the table too.
 *
 *   2. A PRECEDENCE ORDER — and it is NOT here, which is the point. Blazor is most-specific-wins
 *      independently of declaration order; this table is a linear scan. Ranking at run time would be real
 *      shipped bytes, so the COMPILER sorts the table by ASP.NET Core's own precedence digits (literal 1,
 *      constrained parameter 2, bare parameter 3, earlier segment dominating) and emits it already in
 *      order. Blazor's ordering, at zero cost on the wire. The witness is `/tag/all` against
 *      `/tag/{Slug}`: `{Slug}` matches "all" perfectly well, so nothing but rank decides it.
 *
 *   3. A VALUE CHANNEL — `mount(target, values)`, and the function mount RETURNS. D12 named the missing
 *      second argument as the blocker: `mount(target)` had nowhere to put a captured group. It also
 *      measured the harder half — Blazor REUSES a component when only its route parameters changed, so
 *      its state survives and OnInitialized does not run again. A router that re-mounts unconditionally
 *      shows a page reset to zero on /item/7 -> /item/8. So a parameterised page hands back a channel and
 *      the router calls THAT instead of clearing the target.
 *
 * THE CONVERTERS ARE THE EXACTNESS CLAIM, and each one is a divergence D12 measured, closed:
 *   `:int`  — NumberStyles.Integer (leading/trailing space, a leading sign, nothing else) PLUS the Int32
 *             range. Blazor does not route `/item/2147483648`; a bare `Number()` renders a page there.
 *   `:long` — BigInt, per decision 112. `/big/9007199254740993` is 2^53 + 1; a double renders ...992.
 *   `:bool` — bool.TryParse: case-insensitive "true"/"false", and nothing else matches.
 * All four are RUN, on these bytes, by `node tools/route-contract.mjs` — 20 steps, each with a control
 * that makes it fail.
 */

import { mount as mountHome } from './Home.g.js';
import { mount as mountNewItem } from './NewItem.g.js';
import { mount as mountTagAll } from './TagAll.g.js';
import { mount as mountBig } from './Big.g.js';
import { mount as mountFlag } from './Flag.g.js';
import { mount as mountItem } from './Item.g.js';
import { mount as mountTag } from './Tag.g.js';

// A route parameter's value, converted the way Blazor's own route constraint converts it, or undefined
// when the constraint REJECTS -- which is a non-match, not an error, so a later route still gets a turn.
const convert = {
  s: (v) => v,
  i: (v) => { if (!/^\s*[+-]?\d+\s*$/.test(v)) return undefined;
    const n = Number(v); return n >= -2147483648 && n <= 2147483647 ? n : undefined; },
  l: (v) => { if (!/^\s*[+-]?\d+\s*$/.test(v)) return undefined; const t = v.trim();
    const n = BigInt(t[0] === '+' ? t.slice(1) : t);
    return n >= -9223372036854775808n && n <= 9223372036854775807n ? n : undefined; },
  b: (v) => { const t = v.trim().toLowerCase();
    return t === 'true' ? true : t === 'false' ? false : undefined; },
};

// [segments, mount] -- a literal segment is a string, a parameter is [name, kind]. Already in Blazor's
// precedence order, because the compiler put it in that order.
const routes = [
  [[], mountHome],
  [['item', 'new'], mountNewItem],
  [['tag', 'all'], mountTagAll],
  [['big', ['N', 'l']], mountBig],
  [['flag', ['On', 'b']], mountFlag],
  [['item', ['Id', 'i']], mountItem],
  [['tag', ['Slug', 's']], mountTag],
];

export function mount(target) {
  let active = null;
  let setParams = null;

  function match(path) {
    const parts = [];
    for (const s of path.split('/')) if (s !== '') parts.push(decodeURIComponent(s));
    for (const [segs, page] of routes) {
      if (segs.length !== parts.length) continue;
      const values = {};
      let ok = true;
      for (let i = 0; i < segs.length; i++) {
        const seg = segs[i];
        if (typeof seg === 'string') {
          if (seg.toLowerCase() !== parts[i].toLowerCase()) { ok = false; break; }
        } else {
          const v = convert[seg[1]](parts[i]);
          if (v === undefined) { ok = false; break; }
          values[seg[0]] = v;
        }
      }
      if (ok) return [page, values];
    }
    return null;
  }

  function render() {
    const hit = match(location.pathname);
    if (hit && hit[0] === active && setParams) { setParams(hit[1]); return; }
    target.textContent = '';
    active = hit && hit[0];
    setParams = hit ? hit[0](target, hit[1]) : null;
  }

  addEventListener('click', (e) => {
    if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    const a = e.target.closest && e.target.closest('a[href]');
    if (!a || a.target || a.hasAttribute('download')) return;
    const url = new URL(a.getAttribute('href'), location.href);
    if (url.origin !== location.origin) return;
    e.preventDefault();
    if (url.pathname !== location.pathname) {
      history.pushState(null, '', url.pathname);
      render();
    }
  });

  addEventListener('popstate', render);

  render();
}
