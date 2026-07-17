/**
 * Ambient type shim for the hand-written `@if / @else if / @else` answer key,
 * `samples/IfElse/ifelse.js`. That file is plain JS (no `.d.ts`, no `allowJs` in
 * tsconfig.json/tsconfig.test.json — src/ stays TS-only by design), so a static `import`
 * of it fails typecheck under `strict`/`noImplicitAny` (TS7016) without this declaration.
 * Scoped to the one module path ifelse-behavior.test.ts imports; does not touch
 * samples/IfElse/ifelse.js itself or loosen JS-import handling anywhere else. Mirrors
 * if-answer-key.d.ts.
 */
declare module '*/samples/IfElse/ifelse.js' {
  export function mount(target: Element): void;
}
