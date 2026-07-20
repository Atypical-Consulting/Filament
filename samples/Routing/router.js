/**
 * Routing — hand-written Filament answer key for the ROUTER of baseline/Routing.Blazor.
 *
 * THE POINT: @page, and the one spec §3 non-goal that could NOT be erased. Decision 139.
 *
 * Blazor DOM contract: "/" renders #where "home"; clicking #to-about renders "about" at /about
 * without a page load; a page with state is MOUNTED AFRESH on re-entry; Back works. That is the
 * measurement (BENCH n°57), together with the WEIGHT, which this slice must report and the others
 * must not.
 *
 * WHY THIS FILE EXISTS AT ALL, WHEN THE OTHER EIGHT FEATURES LEFT NOTHING BEHIND.
 *
 * @ref became a naming decision; JS interop became a direct call; a cascade became lexical scope;
 * generics erased; @inherits merged text. Every one of those was a LOOKUP the compiler could perform
 * at build time and then delete. Routing is not, and the difference is not effort: a route must be
 * matched against a URL that only exists while the page is running, re-matched when the user
 * navigates, and un-mounted and re-mounted as it changes. That is BEHAVIOUR, and behaviour has to be
 * somewhere at run time.
 *
 * SO IT IS GENERATED INTO THE APP, NOT ADDED TO THE RUNTIME, and the placement is the whole claim:
 * the shared signals runtime stays byte-frozen at 1,943 B, so an app that does not route pays exactly
 * nothing for the fact that routing exists; and the cost that IS paid lands in the app that asked for
 * it, on the wire, where BENCH n°57 measures it (425 B gzip).
 *
 * WHY THE ROUTER IMPORTS THE PAGES RATHER THAN INLINING THEM. Routing changes how pages are
 * ASSEMBLED, not how they are compiled: each page module is byte-identical whether it is routed or
 * compiled on its own. That is also why @page contributes nothing to a page's own module — a route is
 * metadata this file reads, not code the page emits.
 *
 * THE FOUR BEHAVIOURS, and each is here because leaving it out is a bug a user hits immediately:
 *   1. MATCH the pathname, with '*' as the catch-all if a page declares it.
 *   2. MOUNT into a CLEARED target — a router that appends shows two pages at once.
 *   3. INTERCEPT same-origin link clicks — without it "navigation" is a full page load.
 *   4. LISTEN for popstate — without it Back strands the user.
 */

import { mount as mountHome } from './Home.g.js';
import { mount as mountAbout } from './About.g.js';

const routes = [
  ['/', mountHome],
  ['/about', mountAbout],
];

export function mount(target) {
  function render() {
    const path = location.pathname;
    const hit = routes.find(([r]) => r === path) ?? routes.find(([r]) => r === '*');
    target.textContent = '';
    if (hit) hit[1](target);
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
