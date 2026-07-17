import { describe, it, expect } from 'vitest';
import { mount } from '../../../samples/IfElse/ifelse.js';

/**
 * In-browser (happy-dom, the global environment from vitest.config.ts — no per-file
 * `@vitest-environment` pragma, matching if-behavior.test.ts) behavioral test of the
 * `@if / @else if / @else` answer key, `samples/IfElse/ifelse.js`. That file is NOT
 * modified — see its own header for the Blazor DOM contract it reproduces.
 *
 * What this proves by DOM observation: the multi-branch conditional shows EXACTLY the
 * active branch (branch index = the list() key), swaps to the next branch on each click
 * as `n` cycles 0 -> 1 -> 2 -> 0, and the comment anchor survives every swap. Effect
 * disposal on branch removal is `list()`'s own responsibility, covered by test/list.test.ts.
 */
describe('@if/@else if/@else answer key behavior', () => {
  it('shows exactly the active branch and swaps on each click', () => {
    const root = document.createElement('div');
    document.body.appendChild(root);
    mount(root);

    const wrap = root.querySelector('#wrap')!;
    const btn = wrap.querySelector('#t') as HTMLButtonElement;
    const active = () => (['a', 'b', 'c'] as const).filter((id) => wrap.querySelector('#' + id));
    const hasAnchor = () => [...wrap.childNodes].some((n) => n.nodeType === Node.COMMENT_NODE);

    // n = 0 -> only branch a; the comment anchor is present throughout.
    expect(active()).toEqual(['a']);
    expect(hasAnchor()).toBe(true);

    btn.click(); // n = 1
    expect(active()).toEqual(['b']);

    btn.click(); // n = 2
    expect(active()).toEqual(['c']);

    btn.click(); // n = 0 again — wraps
    expect(active()).toEqual(['a']);

    // The anchor survived every swap.
    expect(hasAnchor()).toBe(true);
  });
});
