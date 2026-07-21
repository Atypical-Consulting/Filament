# Todo v2 — the capability program behind a real todo app

**Goal.** Upgrade the measured Tailwind todo (BENCH n°65) into a REAL app — persistence,
keyboard UX, in-place editing, derived state — by widening the five capabilities the probe
session (2026-07-21, this repo's scratchpad) showed are missing. Each widening is its own
slice; the upgraded app is the single measurement vehicle (BENCH n°66).

## Ground truth (probed before design)

| Probe | Verdict |
|---|---|
| `$"{n} left"` interpolation | already SUPPORTED |
| `@bind` inside a `@foreach` row | already SUPPORTED (effect+listener land in the row create — #152's routing) |
| `protected override void OnInitialized()` | REFUSED `[unsupported-modifier] 'override'` |
| Expression-bodied property `int Active => …` | REFUSED `[unsupported-member] PropertyDeclaration` — while the runtime EXPORTS `computed`, never used by the generator |
| Handler with `KeyboardEventArgs` | REFUSED (type outside subset + handler parameter unresolvable) |
| `EventCallback<int>` (payload) | REFUSED — out of scope here (composition-at-scale program) |
| `@if` inside a `@foreach` row | REFUSED — the nested region compiles at class scope, the loop variable unreachable |
| `JsonSerializer.Serialize/Deserialize` | absent — and the JS record shape carries per-record SIGNALS (`t.done` is `signal(bool)`), so a naive `JSON.stringify` would serialize signal internals |

## Waves

### W1 — Lifecycle: `OnInitialized` / `OnInitializedAsync` (#156)

Admit exactly these two overrides (`protected override void OnInitialized()`,
`protected override async Task OnInitializedAsync()`), no parameters; the `override`
modifier stays refused everywhere else (OnParametersSet/OnAfterRender refused BY NAME with
guidance). Semantics mirrored: the body runs ONCE, before the first paint — emitted as a
call placed after the state/method prologue and BEFORE create(), so initial signal writes
are what the first render shows (exactly Blazor's before-first-render). The async form is
invoked un-awaited: its sync prefix runs pre-create, each continuation writes signals and
the effects re-render — Blazor's StateHasChanged-per-continuation, expressed by the signal
graph. An uncaught throw logs and the app lives (the documented error stance).

### W2 — Local JSON: `JsonSerializer.Serialize/Deserialize<T>` (#157)

Default-options only (an options argument refuses); `T` gated by the EXISTING
`TypeSubset.JsonUnfaithful` (int/double/bool/string, local records of those, `List<T>`).
The crux the probe exposed: the generator's record objects are camelCase and their mutated
props are per-record SIGNALS, while C#'s default JsonSerializer writes the DECLARED
PascalCase names, case-sensitively. So emission is SHAPE-AWARE per record, derived from
RecordInfo: `__serItem(v)` writes declared names reading `.value` off signal props;
`__desItem(o)` reads declared names and re-wraps mutated props in `signal(…)`. Lists map.
Faithfulness oracle: BOTH shells write localStorage — Blazor through the real
System.Text.Json — and the driver asserts the stored STRING byte-equal.

### W3 — `@if` inside a `@foreach` row (#158)

The nested region's C# must compile in the scope the author wrote it in: the reassembly
nests the inner region method INSIDE the outer one (a local function captures the loop
variable, exactly like region lambdas, decision 141). Emission: inside the row create
function, the nested @if becomes its own anchored `list()` whose source reads the
per-record signal (`() => t.done.value ? [0] : []`) — the same @if lowering as everywhere
(#81/#98 ranges), just row-scoped. Boundary: whatever depth beyond one level (or a nested
@foreach) still refuses today refuses the same way after, with its location. If the
reassembly cost explodes, the honest fallback is a located refusal naming the
`hidden="@(…)"` idiom — but the attempt comes first; the app's in-place edit is the witness.

### W4 — Keyboard events: `KeyboardEventArgs` (#159)

A handler MAY take one parameter typed `KeyboardEventArgs` when bound to `@onkeydown` /
`@onkeyup`. The type is admitted as a PARAMETER type only (not state); member access on it
maps by allowlist — `Key`→`key`, `Code`→`code`, `CtrlKey/ShiftKey/AltKey/MetaKey`→ their
DOM names; anything else refuses naming the list. `listen(el, 'keydown', onKey)` already
passes the DOM event as the first argument — the translation is the natural one.
`MouseEventArgs` et al. stay refused (not needed by the deliverable; separate slice).

### W5 — Computed properties → `computed()` (#160)

A private expression-bodied property (`private int Active => …;`, subset-typed, get-only)
becomes `const active = computed(() => …);` — the FIRST generator use of the runtime's
`computed` export (shipped since day one, never emitted). Reads (`@Active`, method bodies)
become `active.value`. The body goes through the same Expr subset; out-of-subset bodies
refuse at their span. Property with a setter / statement body stays refused.

### W6 — The todo v2, measured (#161, BENCH n°66, HARNESS 1.59.0)

`baseline/Todo.Blazor` evolves in place (n°65 remains the historical record):
- `@inject IJSRuntime JS`; `OnInitializedAsync` loads `localStorage.getItem("todos")`,
  `Deserialize<List<Item>>`, seeds tasks (W1+W2); every mutation calls `Save()` →
  `Serialize` + `localStorage.setItem`.
- `#new` gains `@onkeydown="OnKey"`: Enter adds (W4).
- In-place edit: `#edit-{…}` flow — a row shows either its label or an edit `<input>`
  under `@if (t.Id == editingId)` (W3) with `@bind` (already supported in rows).
- `#left` becomes the computed `Left` (W5) — the `Refresh()` recompute boilerplate for the
  count DIES.
- Driver (todo contract, 1.59.0): clear localStorage → add ×2 (one via Enter) → toggle →
  assert DOM AND the stored JSON string byte-equal vs Blazor → start in-place edit,
  change a label, commit → assert → `ctx.page.reload()` → assert the app RESTORED from
  storage identically on both sides. `examples/TodoTailwind` follows the same sources.

## Non-goals (this program)

Composition at scale (stateful children, typed params, `EventCallback<T>`, component-in-row)
— the next program, a model change. `MouseEventArgs`. `OnParametersSet`/`OnAfterRender`/
`IDisposable`. JSON options/case-insensitivity.

## Invariants

Unchanged: runtime firewall empty (the ENTIRE program is generator-only — `computed` and
`list` already ship), every slice pinned by tests, the app measured vs Blazor before it
ships, baselines `dotnet build` first, French DECISIONS/BENCH (#156–#161, n°66), HARNESS
bump disclosed, CI 3-OS green before done.
