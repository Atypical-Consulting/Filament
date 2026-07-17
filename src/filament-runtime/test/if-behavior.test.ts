import { describe, it, expect } from 'vitest';
import { mount } from '../../../samples/If/if.js';

/**
 * In-browser (happy-dom, see vitest.config.ts's global `environment: 'happy-dom'`
 * — the same DOM environment c3-counter.test.ts runs under, no per-file
 * `@vitest-environment` pragma needed since none of this suite's files use one)
 * behavioral test of the `@if` answer key, `samples/If/if.js`. This file is NOT
 * modified — see its own header for the Blazor DOM contract it reproduces.
 *
 * `if.js`'s conditional body (`<span id="msg">hi</span>`) carries no reactive
 * `@expr` binding, so there is no effect inside it to observe being disposed.
 * What this test proves, by DOM observation instead of instrumentation, is the
 * structural half of that contract: `list()` inserts the body when its 0/1
 * source becomes `[0]` and removes it (down to the comment anchor) when the
 * source becomes `[]`, then rebuilds it fresh on the next insert. Effect
 * disposal on row removal is `list()`'s own responsibility, already covered by
 * its dedicated suite (test/list.test.ts) and is not re-proven here.
 */
describe('@if answer key behavior', () => {
  it('inserts the body on true, removes it on false (anchor persists), and re-inserts fresh on true again', () => {
    const root = document.createElement('div');
    document.body.appendChild(root);
    mount(root);

    const wrap = root.querySelector('#wrap')!;
    const btn = wrap.querySelector('#t') as HTMLButtonElement;

    // Shown initially (show = true).
    expect(wrap.querySelector('#msg')).not.toBeNull();
    expect(wrap.querySelector('#msg')?.textContent).toBe('hi');

    // Toggle off -> body removed; the comment anchor stays in the DOM.
    btn.click();
    expect(wrap.querySelector('#msg')).toBeNull();
    expect([...wrap.childNodes].some((n) => n.nodeType === Node.COMMENT_NODE)).toBe(true);

    // Toggle on -> body re-created fresh.
    btn.click();
    expect(wrap.querySelector('#msg')).not.toBeNull();
    expect(wrap.querySelector('#msg')?.textContent).toBe('hi');
  });
});
