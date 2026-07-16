import { describe, it, expect } from 'vitest';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';

/**
 * Every other test in this suite imports ../src. What SHIPS is dist/filament.js
 * after --define + minify + dead-code elimination. Those are not the same
 * artefact, and DCE is exactly the kind of step that can turn a passing runtime
 * into a broken one — the stats guards are woven through the hot path, so a bad
 * fold would take real code with it.
 *
 * This file exercises the built bundle itself. It is skipped (loudly) when dist
 * is absent so that `vitest run` alone still works; `npm run verify` builds first.
 */
const dist = path.resolve(__dirname, '../dist/filament.js');
const built = existsSync(dist);

describe.skipIf(!built)('dist/filament.js — the artefact C1 measures', () => {
  it('the production bundle actually works after DCE', async () => {
    const m = await import(/* @vite-ignore */ dist);

    // Counter, end to end, through the shipped code.
    const count = m.signal(0);
    const t = document.createTextNode('');
    m.effect(() => m.setText(t, count.value));
    expect(t.data).toBe('0');
    count.value++;
    expect(t.data).toBe('1');

    // Computed laziness survives minification.
    let ran = 0;
    const c = m.computed(() => {
      ran++;
      return count.value * 2;
    });
    expect(ran).toBe(0);
    expect(c.value).toBe(2);
    expect(ran).toBe(1);

    // Keyed list + LIS survive minification.
    const parent = document.createElement('div');
    const items = m.signal([1, 2, 3].map((id: number) => ({ id })));
    m.list(
      parent,
      () => items.value,
      (i: { id: number }) => i.id,
      (i: { id: number }) => {
        const el = document.createElement('i');
        el.textContent = String(i.id);
        return el;
      },
    );
    expect([...parent.children].map((n) => n.textContent)).toEqual(['1', '2', '3']);
    const first = parent.children[0];
    items.value = [3, 2, 1].map((id: number) => ({ id }));
    expect([...parent.children].map((n) => n.textContent)).toEqual(['3', '2', '1']);
    expect(parent.children[2]).toBe(first); // keyed: moved, not recreated
  });

  it('exports exactly the documented API surface — no more, no less', async () => {
    const m = await import(/* @vite-ignore */ dist);
    expect(Object.keys(m).sort()).toEqual(
      [
        'Computed',
        'Effect',
        'Signal',
        'batch',
        'computed',
        'effect',
        'insert',
        'list',
        'listen',
        'remove',
        'setAttr',
        'setText',
        'signal',
        'untrack',
      ].sort(),
    );
  });

  it('does NOT install the __filament global (that is the dev build)', async () => {
    await import(/* @vite-ignore */ dist);
    expect((globalThis as Record<string, unknown>).__filament).toBeUndefined();
  });

  it('contains no stats instrumentation (the C1 gate, asserted here too)', () => {
    const src = readFileSync(dist, 'utf8');
    expect(src).not.toContain('__filament');
    expect(src).not.toContain('stats');
    expect(src).not.toMatch(/\.\s*(text|links|runs|computes)\s*\+\+/);
  });

  it('has no .NET runtime, no wasm, no network fetch — C5, mechanically', () => {
    // C5 is a property of the whole app, not of this file alone, but the runtime
    // is where a violation would hide. It must not reach for anything at all.
    const src = readFileSync(dist, 'utf8');
    for (const forbidden of ['wasm', 'WebAssembly', 'fetch(', 'XMLHttpRequest', 'dotnet', 'blazor', 'import(']) {
      expect(src.toLowerCase()).not.toContain(forbidden.toLowerCase());
    }
  });
});

it('dist exists when verify runs it', () => {
  // Never let the skipIf above silently hide a missing build in CI.
  if (process.env.FILAMENT_REQUIRE_DIST) expect(built).toBe(true);
});
