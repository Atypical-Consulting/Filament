/**
 * Ambient type shim for the hand-written `@if` answer key, `samples/If/if.js`.
 * That file is plain JS (no `.d.ts`, no `allowJs` in tsconfig.json/tsconfig.test.json
 * — src/ stays TS-only by design), so a static `import` of it fails typecheck under
 * `strict`/`noImplicitAny` (TS7016) without this declaration. Scoped to the one
 * module path if-behavior.test.ts imports; does not touch samples/If/if.js itself
 * or loosen JS-import handling anywhere else in the project.
 */
declare module '*/samples/If/if.js' {
  export function mount(target: Element): void;
}
