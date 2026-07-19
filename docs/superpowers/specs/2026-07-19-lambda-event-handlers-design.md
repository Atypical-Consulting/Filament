# Lambda event handlers (`@onclick="() => …"`) — design

**Date:** 2026-07-19
**Status:** designed (fully mapped), pending implementation
**Kind:** MEASURED subset widening (BENCH entry, DECISIONS entry). The one remaining real-capability slice.

## Goal

Admit an **inline lambda event handler** — `@onclick="() => currentCount++"` (the `HandlerLambda` witness) — into
the subset. It compiles to `listen(el, 'click', () => currentCount.value++)`: the lambda body is translated by the
same C# machinery that already inlines `@code` method bodies (#68), against the component's semantic model.

Scope: a **no-argument** lambda (`() => …`) whose body is in the C# subset. Lambdas with an event parameter
(`e => …`, `HandlerLambdaArgs` — needs the DOM event object) and `async` lambdas (`HandlerAsync`) stay refused
(deferred).

## Why this needs a bridge (and why not a syntactic scrape)

The handler value is inline template C# that is **not in the `@code` AST**, so the generator's semantic model does
not currently cover it. Today `NamedByTemplate` refuses any handler that is not a bare `@code` method name
(`[compound-expression]`). Translating `currentCount++ → currentCount.value++` faithfully needs to know
`currentCount` is a signal — a fact only the semantic model has. A regex/syntactic scrape would repeat the exact
over-refuse (`const a = 1, b = 2` bound only `a`) and over-accept (a reassigned `let Foo = …` stayed "callable")
failures **decision #57 eliminated** by moving these questions to Roslyn. So the lambda body must go through the
real compilation.

**The infrastructure already exists in shape.** `CSharpFrontEnd.PrepareComponent` builds ONE `WrappedSource` that
Roslyn parses (CSharpFrontEnd.cs:342-398): the `@code` block, free-slot stubs + `__filament_free()` (template read
expressions, e.g. `@currentCount`), and region methods `void __filament_t{r}() { … }` (the re-parsed `@foreach`/
`@if` C#). A lambda handler body is exactly another **region-method-shaped** wrapping.

## The change — four phases, mirroring the free-slot/region machinery

### 1. Collect lambda handlers in the plan (TemplateCompiler, harvest phase)

Where events are processed (`EmitAttribute`, the `TryUnwrapEventCallback` branch, TemplateCompiler.cs:1382): when
the unwrapped handler text is a lambda (parses as a `ParenthesizedLambdaExpressionSyntax` with **zero parameters**
and a non-`async` modifier), instead of `NamedByTemplate` refusing it, register it:

```csharp
// plan.LambdaHandlers: a new List<LambdaHandler> on TemplatePlan.
// LambdaHandler(IntermediateNode Node, string ElVar, string DomEvent, LambdaExpressionSyntax Lambda)
```

Parsing: `SyntaxFactory.ParseExpression(handlerText)` → if `ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 0 } l && !l.Modifiers.Any(async)` → a supported lambda handler; else keep the current
`NamedByTemplate` path (bare method) or its refusal (compound-expression / e => … / async).

### 2. Wrap each lambda body as a synthetic method (CSharpFrontEnd.PrepareComponent)

Beside the region methods (CSharpFrontEnd.cs:376-394), emit one method per lambda handler:

```csharp
// void __filament_lambda_{k}() { <lambda body, as statements> }
// Expression body `() => currentCount++`  ->  { currentCount++; }
// Block body      `() => { … }`           ->  { … }
_src.Literal($"void __filament_lambda_{k}() {{\n");
_src.Node(<body node>, <body text>);        // via _src.Node, so source spans map for diagnostics
_src.Literal("\n}\n");
```

Because it is at class scope inside `__FilamentComponent`, `currentCount` resolves to the field, exactly as a
region method's body does. The body goes through the SAME subset checks (`CheckSemantics`, `ClassifyStatement`/
`ClassifyExpression`) as any method body — so an out-of-subset lambda body refuses with a located diagnostic for
free.

### 3. Translate each lambda body (CSharpFrontEnd)

Translate the synthetic method's body with the existing `Body`/`Statement`/`Expr` machinery (the same path handler
inlining uses), applying decision #68's batch rule (batch iff >1 write). Store the result keyed by the lambda
handler node:

```csharp
// _lambdaBodies[node] = the translated JS body (e.g. "currentCount.value++"
//   for an expression lambda, or "{ a.value = 1; b.value = 2; }" batched for a block).
// Expose: public string? LambdaBodyJs(IntermediateNode node)
```

### 4. Emit the arrow (TemplateCompiler, handler emission)

Where handlers are emitted (TemplateCompiler.cs:355-362), a lambda handler emits its arrow directly rather than
`HandlerArrow(name)`:

```csharp
// listen(el, 'click', () => currentCount.value++);          // expression body
// listen(el, 'click', () => batch(() => { a.value = 1; b.value = 2; }));  // block, >1 write
_events.Add($"listen({h.ElVar}, {JsString(h.DomEvent)}, () => {_code.LambdaBodyJs(h.Node)});");
```

`() => currentCount.value++` mirrors what handler inlining already produces for a single-use named method
(`() => { currentCount.value++; }`), so the shapes are consistent.

## Runtime

**Unchanged.** `listen` already ships; an arrow function is a JS builtin. `git diff --stat src/filament-runtime`
stays empty.

## The witness moves (refused → compiles)

`Unsupported/HandlerLambda.razor` (`@onclick="() => currentCount++"`) flips refused→compiles
(`HandlerLambda_NowCompiles_ToAnInlineArrow`). `Unsupported/HandlerLambdaArgs.razor` (`e => …`, needs the event
object) and `Unsupported/HandlerAsync.razor` (`async () => …`) **stay refused** — they are the boundary witnesses
for the deferred cases (a NEW `compound-expression`/`unsupported-handler` reason distinguishing "has a parameter"
and "is async" from the old blanket refusal).

## The measured app — `LambdaHandler`

`baseline/LambdaHandler.Blazor/App.razor`: a counter driven by an inline lambda, so the click is the measurement.

```razor
@* Lambda event handler (BENCH n°24): @onclick is an inline `() => count++` lambda, not a named method.
   The lambda body is translated (count -> count.value) through the same machinery @code methods use. *@

<p>Count: <span id="count">@count</span></p>

<button id="inc" @onclick="() => count++">Increment</button>

@code {
    private int count = 0;
}
```

`count` is read by `@count` and assigned by the lambda body — but the lambda's assignment is template-side, so
(like `@bind`) SCOPE this slice to a `count` that is ALSO a signal by ordinary means, OR extend the field-read
marking so the lambda body's write marks `AssignedOutsideConstruction`. **Decision to make at implementation:** the
region-method wrapping already runs through the assignment-marking pass (`MarkConditionReads`/the write scan at
CSharpFrontEnd.cs:1255-1270 walks method bodies), so a `__filament_lambda_k` body's `count++` SHOULD mark `count`
assigned-outside-construction automatically — verify this makes `count` a signal without extra work (it likely
does, which is cleaner than `@bind`'s explicit `IsStringSignal` scoping). If it does not, add a display/second use
in the baseline so `count` is a signal by the conjunction rule.

Answer key `samples/LambdaHandler/lambdahandler.js`. Host shim, baseline modelled on `Counter.Blazor`.

## Measurement — canon gate + snapshot + oracle

Oracle `lambdahandler`: `readySelector '#inc'`, `observeSelector '#count'`. `verifyContract`: `#count` initial
`"0"`; click `#inc` → `"1"`; click → `"2"`. Assert on both builds (a spliced/un-translated lambda would leave
`#count` at `"0"` — the dead-button failure #68 warns of). `build-filament.sh` arms mirror `filament-counter-gen`.
Publish `blazor-lambdahandler`. **BENCH n°24**, `HARNESS 1.18.0 → 1.19.0` disclosed.

## Tests (TDD)

`LambdaHandlerTests.cs` (canon/snapshot/contract asserting `listen(_el1, 'click', () => count.value++)` /
closed-runtime). `RepoPaths` + `Generate.LambdaHandlerToTemp`. Witness flip (HandlerLambda → compiles;
HandlerLambdaArgs/HandlerAsync stay refused, reasons distinguished). Regression: named-method handlers (Counter)
byte-identical.

## Non-goals / disclosure

- No-arg, non-async lambdas only. `e => …` (event object), `async () => …`, and lambdas whose body is out of the
  C# subset stay refused (the last for free, via the synthetic method's own subset checks).
- No runtime change.

## Decision record

Append **DECISIONS #105** (French): inline no-arg lambda event handlers enter §5 — the lambda body wrapped as a
synthetic `void __filament_lambda_k()` in the compilation (mirroring region methods), translated by the same
machinery `@code` methods use (so `currentCount++ → currentCount.value++` via the semantic model, NOT a syntactic
scrape — decision #57), emitted as `listen(el, event, () => <body>)`; `e => …` and `async` deferred; runtime
unchanged; measured (a counter driven by `() => count++`, BENCH n°24).
