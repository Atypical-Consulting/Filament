# TodoTailwind — the Tailwind todo-list example

A three-component Filament app styled entirely with Tailwind utilities, runnable with F5. The
same sources are the repo's measured witness (`baseline/Todo.Blazor`, BENCH n°65): everything
you see here renders byte-identical to real Blazor under the DOM-contract oracle.

- **App.razor** — all the state: a plain `@bind` input (the bind alone makes `newText` reactive,
  decision 154), a keyed `@foreach` whose row class is a *reactive ternary on the loop variable*
  (`line-through` on toggle, decision 152), per-row captured-lambda handlers, LINQ counts.
- **TodoShell.razor** — the card the whole app renders inside (`ChildContent`, decision 131).
- **TodoFooter.razor** — a reactive bound string down (decision 90) + an `EventCallback` up
  (decision 130).

## Run it

```bash
# once, at the repo root (esbuild + the pinned Tailwind CLI):
npm ci
(cd src/filament-runtime && npm ci && npm run build)

dotnet run --project examples/TodoTailwind    # or open in Rider and press F5
```

The build does three things (see the csproj):

1. `Filament.Generator` compiles `App.razor` (+ sibling components, inlined) → `wwwroot/App.g.js`.
2. Tailwind v4 derives `wwwroot/app.css` by **scanning the `.razor` sources** (`tailwind.css`
   declares `@source "./*.razor"`) — exactly how a Blazor project would use Tailwind.
3. `tools/check-css-coverage.mjs` gates the build: every class token the emitted module sets
   must resolve in the derived stylesheet. It holds because the generator emits class strings
   **verbatim** — and it will fail the day a class name is composed dynamically, which is the
   same thing Tailwind's own docs forbid.

## Live reload

```bash
dotnet watch --no-hot-reload run
```

Editing any `.razor` (or `tailwind.css`) re-emits the JS, re-derives the CSS, and re-runs the
coverage gate.

## The one authoring rule

Write **full class names**, even in conditionals:

```razor
class="… @(t.Done ? "line-through text-slate-400" : "text-slate-900")"
```

Both branches are literals in the source, so the scanner sees them. String-building a class
name (`"text-" + color`) is invisible to the scanner — in this project it is also a build
failure, courtesy of the coverage gate.
