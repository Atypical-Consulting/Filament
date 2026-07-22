using System.Text;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Filament.Generator;

/// <summary>
/// C# -> JS. Both the @code block AND the C# Razor left lying in the template (decision 54).
///
/// ---------------------------------------------------------------------------------
/// THE MAPPING IS SPECIFIED, NOT INVENTED. It is read off the two answer keys, and where
/// they disagree the disagreement is REPORTED (decisions 21/51), never negotiated away.
///
/// STATE -- the rule is the CONJUNCTION of the two keys (decision 67):
///     counter.js:   "A private field READ by the template is reactive state"
///     rows.js (2):  "reactive iff it is ASSIGNED anywhere other than its object's
///                    construction site"
/// Neither alone reproduces both keys; only the conjunction does, across all six state
/// declarations they contain. The READ condition alone lifts _nextId/_seed (rows.js keeps
/// them plain `let`); the WRITE condition alone lifts Row.Id (rows.js keeps it plain, and
/// says that is FORCED -- a signal read inside list()'s keyOf would subscribe the list to
/// all 1000 row ids).
///
/// `List&lt;T&gt;` -- rows.js mapping decision (1): a MUTABLE array plus a version Signal, never
/// Signal&lt;T[]&gt; with copy-on-write, because Run() is Clear() + 1000 Add() and copy-on-write
/// makes that O(n^2) against a C# List that copies nothing.
///
/// A `record` -- rows.js mapping decision (2): a plain object literal. Its properties obey
/// the same conjunction as fields, which is what makes Row.Id plain and Row.Label a signal.
///
/// THE CONSTRUCTION SITE IS COMPUTED, NOT ASSUMED, and it is what makes decision (2) work
/// at all. rows.js calls it "a bog-standard escape analysis, not cleverness":
///
///     Row row = new Row();        ->   const row = { id: _nextId, label: signal(nextLabel()) };
///     row.Id = _nextId;
///     row.Label = NextLabel();
///
/// The two assignments are folded INTO the literal because `row` has not escaped yet, so
/// they are construction, so they do not make Id reactive. Drop this analysis and Row.Id is
/// "assigned in AddRow()" -> a signal -> read inside keyOf -> 1000 dependency edges whose
/// only possible effect is to re-reconcile the whole table on every row id. The analysis is
/// LOAD-BEARING for the benchmark's headline, not a tidy-up.
///
/// ---------------------------------------------------------------------------------
/// EVERY NODE IS POSITIVELY ACCOUNTED FOR -- the same inside-out rule as the template walk,
/// and for the same reason (decision 61). A C# walk has vastly more node types to fall
/// through than a Razor one, so an allowlist is the only shape that can be honest: every
/// syntax node this class does not NAME is FIL0001 with an exact location. Adding a
/// construct to the subset starts with a failing diagnostic.
///
///   FIL0001  out-of-subset C# construct
///   FIL0002  out-of-subset type
/// A tool must not squat the namespace it reports in (decision 61), so failures of the
/// TOOL still carry FIL-WIRING.
/// </summary>
public sealed class CSharpFrontEnd
{
    /// <summary>Spec 5's scalar types, resolved through a real Compilation rather than matched as text.</summary>
    /// <summary>
    /// The prefix every name this compiler synthesises carries. User code containing it is
    /// REFUSED (see Compile): a user identifier that collided with a marker would be
    /// mistaken for one, i.e. markup would be emitted where the author wrote a call.
    /// </summary>
    const string Reserved = "__filament";

    readonly List<Diagnostic> _diagnostics = [];
    readonly List<FieldInfo> _fields = [];
    readonly List<MethodInfo> _methods = [];
    readonly List<ComputedInfo> _computeds = [];
    readonly Dictionary<string, ComputedInfo> _computedsByName = new(StringComparer.Ordinal);
    /// <summary>Every @foreach ROW node seen while building region ops (decision 158): a region
    /// container sitting under one compiles in that loop's scope -- where a nested @if is admitted
    /// (measured) and a nested @foreach is refused (its own witness is not). Sibling regions
    /// (decision 142) sit under no row and are untouched.</summary>
    readonly List<IntermediateNode> _rowScopes = [];
    readonly List<RecordInfo> _records = [];
    readonly HashSet<string> _primitives = [];

    readonly List<ParamInfo> _params = [];

    readonly Dictionary<string, FieldInfo> _fieldsByName = new(StringComparer.Ordinal);
    readonly Dictionary<string, MethodInfo> _methodsByName = new(StringComparer.Ordinal);
    readonly Dictionary<string, RecordInfo> _recordsByName = new(StringComparer.Ordinal);
    readonly Dictionary<string, ParamInfo> _paramsByName = new(StringComparer.Ordinal);

    /// <summary>When this front end compiles a CHILD component at a composition site, the parent binds
    /// each [Parameter] to the JS CONSTANT it supplied (name -> e.g. `'World'`). A read of that
    /// parameter (`@Name`) folds to the constant — the static-leaf composition mapping (decision 88).</summary>
    readonly Dictionary<string, string> _paramEnv = new(StringComparer.Ordinal);

    /// <summary>The subset of _paramEnv whose binding READS a parent signal, so `@Name` is a LIVE effect
    /// on the parent's signal (decision 90) rather than a folded constant. Populated by BindParameters
    /// from the parent's SlotIsReactive; consulted by IsReactive when it meets a bound [Parameter].</summary>
    readonly HashSet<string> _paramReactive = new(StringComparer.Ordinal);

    /// <summary>
    /// The `context` an &lt;ErrorBoundary&gt;'s ErrorContent reads, or null (decision 164). Blazor's
    /// ErrorContent is a `RenderFragment&lt;Exception&gt;`, so its one context value is the caught
    /// exception; here that value is the boundary's latch signal, and `@context.Message` reads it.
    ///
    /// DECLARED SO THE NAME BINDS, exactly like the @inject'd IJSRuntime below: Roslyn must resolve
    /// `context` to a real symbol or Identifier() refuses it as undeclared. The declaration is at CLASS
    /// scope because that is the scope every template slot is compiled in (see Slot), which is also
    /// why exactly ONE boundary per component may carry an ErrorContent -- two would need two different
    /// JS bindings for one C# name, and TemplateCompiler refuses the second rather than pick.
    /// </summary>
    string? _errorContextJs;

    /// <summary>Bind `context` to the boundary's latch read (decision 164). Call BEFORE Compile, exactly
    /// like BindParameters -- the slots are translated in one pass, so a binding registered after it has
    /// nothing left to affect.</summary>
    public void BindErrorContext(string js) => _errorContextJs = js;

    /// <summary>EventCallback [Parameter]s, mapped to the PARENT's C# METHOD NAME (`"OnBump" -> "Inc"`,
    /// decision 130). Deliberately the C# name and not the JS one: the callback is an ALIAS, and naming
    /// the parent's method is what lets the emit walk record `Inc` in _handlers, so every downstream
    /// decision -- the guard, single-use inlining, batching -- sees exactly what it would have seen had
    /// the parent written that handler on that button itself. Kept OUT of _paramEnv on purpose: a
    /// callback is not a value, so `@OnBump` must not read as one.</summary>
    readonly Dictionary<string, string> _paramHandlers = new(StringComparer.Ordinal);

    /// <summary>The name an `@inject IJSRuntime` bound, or null (decision 133). It is not a value: it
    /// names the HOST GLOBAL SCOPE, so it only ever appears as the receiver of an Invoke*Async call,
    /// which JsInvocation turns into a direct call. Declared into the compiled source so that C# binds
    /// it -- the same "ask the compiler, do not guess" rule the method-group slot marker follows.</summary>
    /// <summary>The values an ancestor &lt;CascadingValue&gt; has in scope, keyed by the C# TYPE they were
    /// declared with (decision 134). A cascade matches BY TYPE, exactly as Blazor's does. Each entry is the
    /// PARENT's translated expression plus whether it reads a parent signal, so a [CascadingParameter] read
    /// becomes the same live effect a bound [Parameter] does -- and for the same reason: the whole
    /// composition inlines into one mount(), so the ancestor's expression is simply IN SCOPE here.</summary>
    readonly Dictionary<string, (string Js, bool Reactive)> _cascades = new(StringComparer.Ordinal);

    public void BindCascades(IReadOnlyDictionary<string, (string Js, bool Reactive)> cascades)
    {
        foreach (var kv in cascades) _cascades[kv.Key] = kv.Value;
    }

    /// <summary>The component's @typeparam names (decision 135). The wrapped compilation must declare
    /// them, or `T` resolves to nothing and every use of it is reported as an unresolved type -- blaming
    /// the author for a declaration this compiler failed to carry over.</summary>
    IReadOnlyList<string> _typeParams = [];

    public void BindTypeParameters(IReadOnlyList<string> names) => _typeParams = names;

    /// <summary>`<T>` when the component is generic, else empty. Both partials must carry it identically.</summary>
    string TypeParamList => _typeParams.Count == 0 ? "" : "<" + string.Join(", ", _typeParams) + ">";

    string? _jsRuntime;

    /// <summary>The name an `@inject HttpClient` bound, or null (decision 147). Like _jsRuntime it is not
    /// a value: in a browser an HttpClient IS fetch (Blazor WASM implements it on top of fetch), so it only
    /// ever appears as the receiver of a Get*/Post* call, which HttpInvocation turns into the fetch itself.</summary>
    string? _httpClient;

    /// <summary>The author's `@using` namespaces (decision 147), appended to the wrapped source so the
    /// @code compiles under the same resolution Blazor gives it. Pure name resolution: every construct
    /// still passes the subset gates. Validated (namespace exists) by TemplateCompiler before binding.</summary>
    readonly List<string> _usings = [];

    public void BindHttpClient(string name) => _httpClient = name;

    public void BindUsings(IEnumerable<string> namespaces)
    {
        foreach (var ns in namespaces) _usings.Add(ns);
    }

    public void BindJsRuntime(string name) => _jsRuntime = name;

    /// <summary>Every method body, TRANSLATED DURING Compile() and cached. See _sealed.</summary>
    readonly Dictionary<string, List<string>> _bodies = new(StringComparer.Ordinal);

    /// <summary>Inline lambda EVENT handlers (decision 105): the synthetic method for each, kept SEPARATE
    /// from _methods so it is marked + translated but never emitted as a top-level function (it is inlined
    /// into its listen()). Keyed to the attribute node so emission reads the translated body back.</summary>
    readonly List<(IntermediateNode Attr, MethodInfo Method)> _lambdaMethods = [];
    readonly Dictionary<IntermediateNode, List<string>> _lambdaBodies = new();
    readonly HashSet<IntermediateNode> _lambdaBatched = [];

    /// <summary>The translated body lines of an inline lambda event handler, or null if the attribute is
    /// not one. See decision 105.</summary>
    public IReadOnlyList<string>? LambdaBodyJs(IntermediateNode attr) =>
        _lambdaBodies.TryGetValue(attr, out var lines) ? lines : null;

    /// <summary>Whether the lambda handler writes more than once, so its arrow needs batch() (decision 68).</summary>
    public bool LambdaBatched(IntermediateNode attr) => _lambdaBatched.Contains(attr);

    /// <summary>Whether target sits under root in the IR tree WITHOUT crossing into a nested region
    /// container -- how a harvested lambda handler is matched to the region markup item that carries its
    /// element, so it is planted in that region's scope (decision 141) instead of at class scope. The
    /// boundary is TemplateCompiler.SlotsIn's exactly (decision 142): a descendant that itself holds
    /// template C# is another region's territory, and a lambda there belongs to THAT region's method --
    /// the scope whose braces (a @foreach's, say) bind the names the lambda captures.</summary>
    static bool IsUnder(IntermediateNode root, IntermediateNode target)
    {
        if (ReferenceEquals(root, target)) return true;
        if (root.Children.Any(c => c is CSharpCodeIntermediateNode)) return false;
        foreach (var c in root.Children)
            if (IsUnder(c, target)) return true;
        return false;
    }

    /// <summary>The C# method body block of a no-arg, non-async lambda (`() => …`), e.g. "{ currentCount++; }",
    /// or null if it is not that form (an `e => …` or `async` lambda, or unparseable).</summary>
    static string? LambdaMethodBody(string raw)
    {
        if (SyntaxFactory.ParseExpression(raw.Trim()) is not ParenthesizedLambdaExpressionSyntax lam) return null;
        if (lam.ParameterList.Parameters.Count != 0) return null;
        if (lam.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword))) return null;
        return lam.Body switch
        {
            BlockSyntax b => b.ToFullString(),
            ExpressionSyntax e => "{ " + e.ToFullString() + "; }",
            _ => null,
        };
    }

    /// <summary>Every @expression/@key the template reads -> its translation. See Slot.</summary>
    readonly Dictionary<IntermediateNode, Slot> _slots = [];

    /// <summary>Every container that held template C# -> what to emit for it, in order.</summary>
    readonly Dictionary<IntermediateNode, List<TemplateOp>> _ops = [];

    /// <summary>
    /// Set when Compile() has finished raising diagnostics. After that point a Refuse() is a
    /// BUG IN THIS COMPILER, not a statement about the input, and it throws.
    ///
    /// THIS FLAG IS A REAL DEFECT'S HEADSTONE (decision 69). Body translation used to be
    /// LAZY -- it happened at emission, i.e. AFTER the caller had already read Diagnostics --
    /// so a refusal raised while translating a body went into a list nobody would read again
    /// and the generator exited 0 AND WROTE THE FILE. Measured: `while (c &lt; 10) { c++; }` ->
    /// exit 0, `function Increment() {}`, the entire loop GONE. Fixed on the INVARIANT, not
    /// on the line the repro pointed at: everything is decided inside Compile(), emission is
    /// a lookup, and this makes "a diagnostic after Compile()" throw rather than be unlikely.
    /// </summary>
    bool _sealed;

    WrappedSource _src = new();
    SemanticModel _model = null!;
    INamedTypeSymbol _component = null!;

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    /// <summary>Bind each [Parameter] to the JS the parent supplies at a composition site — a CONSTANT
    /// for a static fold (#88) or a translated EXPRESSION for a bound param (decision 90). Call BEFORE
    /// Compile so a read of the parameter resolves to it. `reactive` names the params whose binding
    /// READS a parent signal: the child's @Name is then a LIVE effect (its IsReactive returns true)
    /// rather than a folded constant. The child inlines into the parent's scope, so the effect
    /// references the parent's signal directly. Static string folds pass an empty `reactive`.</summary>
    public void BindParameters(IReadOnlyDictionary<string, string> bindings, IReadOnlyCollection<string>? reactive = null,
        IReadOnlyDictionary<string, string>? handlers = null)
    {
        foreach (var kv in bindings) _paramEnv[kv.Key] = kv.Value;
        if (reactive is not null) foreach (var n in reactive) _paramReactive.Add(n);
        if (handlers is not null) foreach (var kv in handlers) _paramHandlers[kv.Key] = kv.Value;
    }

    /// <summary>
    /// The route parameters this page's own `@page` template declares, keyed by name (decision 163).
    /// Blazor matches a route parameter to its `[Parameter]` case-INSENSITIVELY, and so does this.
    /// </summary>
    readonly Dictionary<string, RouteSegment> _routeParams = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The route parameters that found their `[Parameter]`, in declaration order: the route
    /// template's spelling of the name, the JS signal it became, the C# name as written, and where —
    /// the last two only so a name COLLISION can be reported at the declaration that caused it.</summary>
    readonly List<(string RouteName, string Js, string Name, int At)> _routeSignals = [];

    /// <summary>
    /// Bind this component's own ROUTE parameters, from its own `@page` directive (decision 163). Call
    /// BEFORE Compile, exactly like BindParameters.
    ///
    /// THE VALUE CHANNEL IS A SIGNAL, AND THAT IS THE WHOLE DESIGN. A composed `[Parameter]` folds to the
    /// parent's expression because the parent is known at compile time; a ROUTE parameter's value exists
    /// only while the page runs and CHANGES under it, because Blazor reuses the component when only the
    /// route parameters moved. So the parameter becomes `const id = signal(__route.Id)`, a read of it is
    /// the same live effect a reactively-bound parameter already is (decision 90), and the router hands
    /// new values in through the channel mount() returns. Nothing new is invented: this reuses the
    /// _paramEnv/_paramReactive path that composition already established.
    /// </summary>
    public void BindRouteParameters(IEnumerable<RouteSegment> parameters)
    {
        foreach (var s in parameters) _routeParams[s.Text] = s;
    }

    /// <summary>The route parameters that became signals: what the emitted mount() must accept and what
    /// its returned channel must re-assign. Empty for every component that declares none, which is what
    /// keeps `mount(target)` unchanged everywhere else.</summary>
    public IReadOnlyList<(string RouteName, string Js, string Name, int At)> RouteChannel => _routeSignals;

    /// <summary>Route parameter names that never found a `[Parameter]` to bind to. Blazor accepts this at
    /// BUILD time and then fails at RUN time (the register measured `blazor-error-ui` on `/e/7`), so
    /// refusing it is strictly better than what Blazor does, and never wrong.</summary>
    public IEnumerable<string> UnboundRouteParameters =>
        _routeParams.Keys.Where(n => !_paramsByName.ContainsKey(n)
                                     && !_paramsByName.Keys.Any(k => string.Equals(k, n, StringComparison.OrdinalIgnoreCase)));

    /// <summary>The parent method an EventCallback [Parameter] aliases, or null (decision 130). The emit
    /// walk resolves `@onclick="OnBump"` through this BEFORE recording the handler, so what it records is
    /// the parent's `Inc` -- the thing that will actually be called.</summary>
    public string? HandlerParamTarget(string name) => _paramHandlers.TryGetValue(name, out var t) ? t : null;

    /// <summary>The [Parameter] names bound to a parent METHOD rather than a value (decision 130).</summary>
    public IReadOnlyCollection<string> HandlerParameterNames => _paramHandlers.Keys;

    /// <summary>A LEAF DISPLAY child: only [Parameter] props, no state (fields/signals), no behaviour
    /// (methods), no records. The static-leaf composition slice compiles only these; a stateful or
    /// eventful child is refused rather than half-compiled (decision 88).</summary>
    public bool IsLeafDisplay => _fields.Count == 0 && _methods.Count == 0 && _records.Count == 0 && _computeds.Count == 0;

    /// <summary>
    /// The init-lifecycle calls (decision 156), in ComponentBase's own order: OnInitialized, then
    /// OnInitializedAsync. Emitted after the prologue and BEFORE create(), so a sync write -- and the
    /// async body's synchronous prefix -- is what the first paint shows, exactly Blazor's
    /// before-first-render. The async call is NOT awaited: each continuation writes signals and the
    /// effects re-render, which is StateHasChanged-per-continuation expressed by the graph.
    /// </summary>
    public IReadOnlyList<string> InitCalls =>
        _methods.Where(m => m.IsInit).OrderBy(m => m.IsAsync ? 1 : 0).Select(m => $"{m.Js}();").ToList();

    /// <summary>Does this handler take the DOM event (a single KeyboardEventArgs, decision 159)?</summary>
    public bool HandlerTakesEvent(string name) => _methodsByName.TryGetValue(name, out var m) && m.EventArg;

    /// <summary>The JS name of that handler's event parameter -- the arrow must bind the SAME name
    /// the inlined body reads.</summary>
    public string HandlerEventParamJs(string name) => _methodsByName[name].Parameters[0];

    /// <summary>The name of the first STATICALLY-FOLDED parameter whose declared type is not `string`,
    /// or null. A static fold splices a JS STRING literal into the child, so a numeric/bool param would
    /// fold a string where a number is meant (refused, deferred). A REACTIVELY bound param carries its
    /// own type — the parent's translated expression IS type-correct (`count.value` is a number), so it
    /// is exempt (decision 90): a bound `int` counter displays faithfully.</summary>
    public string? FirstBoundNonStringParameter()
    {
        foreach (var name in _paramEnv.Keys)
            if (!_paramReactive.Contains(name)
                && _paramsByName.TryGetValue(name, out var p) && p.Type.SpecialType != SpecialType.System_String)
                return name;
        return null;
    }

    /// <summary>The [Parameter] names this child declares — for validating the parent supplied exactly
    /// the parameters that exist (an unknown attribute, or a missing required one, is a clear error).</summary>
    public IReadOnlyCollection<string> ParameterNames => _paramsByName.Keys;

    /// <summary>The runtime primitives the emitted @code needs (signal, and for a List<T> nothing more).</summary>
    public IReadOnlySet<string> Primitives => _primitives;

    // ---- the tables ---------------------------------------------------------

    sealed class FieldInfo
    {
        public string Name = "";
        public string Js = "";
        public string Init = "";
        public bool AssignedOutsideConstruction;
        public bool ReadByTemplate;
        public int At;
        public IFieldSymbol Symbol = null!;

        /// <summary>Non-null iff this field is a List&lt;T&gt; -- rows.js mapping decision (1).</summary>
        public ListInfo? List;

        /// <summary>
        /// The field's initialiser, TRANSLATED LATE (decision 137). It cannot be translated during the
        /// member walk, because whether a record property is a signal is not known until the reactivity
        /// marking has run -- and the answer changes what the literal must EMIT (`signal('a')` vs `'a'`).
        /// Translating it eagerly produced a literal that disagreed with every later read of it.
        /// </summary>
        public Func<string>? InitThunk;

        /// <summary>An @ref target (decision 132): the element the template captured into this name. Never
        /// a signal -- it is not state, it is a handle on a node that exists for the module's lifetime.</summary>
        public bool IsElementRef;

        /// <summary>counter.js + rows.js decision (2), CONJOINED. Decision 67. A List is carved out of
        /// signal-hood only while its reactivity lives in a version signal (mutation, rows.js decision 1);
        /// a REASSIGNED-never-mutated List has no version and IS the signal, exactly as a reassigned T[]
        /// (decisions 124/140) -- one generalised condition, not a parallel IsListSignal to keep in step.</summary>
        public bool IsSignal => !IsElementRef && ReadByTemplate && AssignedOutsideConstruction &&
            (List is null || List is { Mutated: false, Reassigned: true });
    }

    /// <summary>A List&lt;T&gt; field: a mutable array, and -- iff anything mutates it -- a version signal.</summary>
    sealed class ListInfo
    {
        public bool Mutated;
        /// <summary>The FIELD itself is assigned a new list (`items = …`), never an element or a mutating
        /// call. A reassigned-never-mutated List is an ordinary signal (decision 140), like a reassigned
        /// T[] (decision 124): there is no version to bump because the reference itself changes.</summary>
        public bool Reassigned;
        /// <summary>Wrapped-source offset of the FIRST wholesale reassignment -- where decision 153's
        /// mixed-idiom refusal points, so the author lands on the write that collided with the
        /// mutations rather than on the field declaration.</summary>
        public int? ReassignSite;
        /// <summary>Every element is a literal, so the whole thing is inert data (rows.js decision 4).</summary>
        public bool LiteralData;
        public string Version = "";
        public string Changed = "";

        /// <summary>
        /// rows.js mapping decision (4), STATED THERE AS A RULE: "hoisting immutable literal
        /// lists to module scope is a generator-level constant-folding decision and changes
        /// nothing about the work done per row". Immutable AND literal, both, or it stays in
        /// mount() where the component's state lives. A REASSIGNED list is state even when its
        /// initialiser is literal data -- hoisting it to a module const would be hoisting a write
        /// target (decision 140).
        /// </summary>
        public bool Hoisted => !Mutated && !Reassigned && LiteralData;
    }

    sealed class RecordInfo
    {
        public string Name = "";
        public List<PropInfo> Props = [];
        public INamedTypeSymbol Symbol = null!;
    }

    sealed class PropInfo
    {
        public string Name = "";
        public string Js = "";
        public string Init = "";
        public bool AssignedOutsideConstruction;
        public bool ReadByTemplate;
        public IPropertySymbol Symbol = null!;

        /// <summary>The same conjunction as a field's. Row.Id: read, never assigned outside
        /// its construction site -> plain. Row.Label: read AND assigned by Update() -> signal.</summary>
        public bool IsSignal => ReadByTemplate && AssignedOutsideConstruction;
    }

    sealed class MethodInfo
    {
        public string Name = "";
        public string Js = "";
        public List<string> Parameters = [];
        /// <summary>A @code method's MethodDeclarationSyntax -- or, for a REGION-planted lambda handler
        /// (decision 141), the LocalFunctionStatementSyntax the region method carries it as. Everything
        /// downstream walks DescendantNodes or reads <see cref="Block"/>, so one field serves both.</summary>
        public SyntaxNode Syntax = null!;
        public BlockSyntax? Block => Syntax switch
        {
            MethodDeclarationSyntax md => md.Body,
            LocalFunctionStatementSyntax lf => lf.Body,
            _ => null,
        };
        public IMethodSymbol Symbol = null!;
        public int At;
        public int CallUses;
        public List<MethodInfo> Callees = [];
        public bool IsAsync;   // `async Task` method -> emitted `async function` (decision 119)
        public bool IsInit;    // OnInitialized/OnInitializedAsync (decision 156) -> called before create()
        public bool EventArg;  // single KeyboardEventArgs parameter (decision 159) -> the arrow takes `e`
    }

    /// <summary>A computed property (decision 160): `private int Active => expr;` becomes
    /// `const active = computed(() => expr);` -- the FIRST generator use of the runtime's
    /// computed export. BodyJs is translated LATE (after reactivity marking), like field
    /// initialisers, because what `.value`s inside the body is not known before.</summary>
    sealed class ComputedInfo
    {
        public string Name = "";
        public string Js = "";
        public PropertyDeclarationSyntax Syntax = null!;
        public string BodyJs = "";
    }

    /// <summary>A `[Parameter]` component parameter: a scalar value supplied by the parent at the
    /// composition site. It has no top-level emission; a read of it folds to the parent's constant.</summary>
    sealed class ParamInfo
    {
        public string Name = "";
        public IPropertySymbol Symbol = null!;
        public ITypeSymbol Type = null!;
    }

    /// <summary>One @expression or @key, re-parsed IN ITS OWN SCOPE and translated.</summary>
    sealed class Slot
    {
        public string Js = "";
        public bool Reactive;
        public bool IsFloat;     // the slot's expression is float-typed -> the template must format it (decision 113)
        public bool IsDecimal;   // the slot's expression is decimal-typed -> format it through __decStr (decision 114)
        public bool IsDateTime;  // the slot's expression is DateTime-typed -> format it through __dtStr (decision 115)

        /// <summary>The slot names a METHOD GROUP (`OnBump="@Inc"`) rather than a value. At a component
        /// attribute that is an EventCallback binding: the parent handing the child one of its own
        /// methods (decision 130). Nowhere else can a slot be a method group -- the marker overload that
        /// lets C# bind one is emitted only for component-attribute slots.</summary>
        public bool IsMethodGroup;

        /// <summary>The slot expression's C# type (decision 134), for cascade matching.</summary>
        public string? TypeDisplay;

        /// <summary>The slot is a whole-expression read of a RenderFragment [Parameter] -- `@ChildContent`
        /// (decision 131). It has no JS: the template compiler inlines the composing parent's markup here
        /// instead of binding anything.</summary>
        public bool IsFragment;

        /// <summary>The slot reads a computed property (decision 160). Only an &lt;ErrorBoundary&gt; asks:
        /// a computed refreshes from inside flush()/checkDirty(), which is BEFORE the binding's own body
        /// runs, so a throw raised there passes no guard the boundary can place (decision 164).</summary>
        public bool ReadsComputed;
    }

    /// <summary>The decimal helpers this module actually used (`decAdd`, `decStr`, …). A `decimal` value is a
    /// boxed { m: BigInt mantissa, s: scale } (JS has no native decimal), so its arithmetic is helper calls, not
    /// operators. The template compiler emits exactly these, in a fixed order, when the set is non-empty
    /// (decision 114). The template adds `decStr` when it formats a decimal display.</summary>
    public readonly HashSet<string> DecimalHelpers = [];

    /// <summary>The wall-clock helpers this module actually used (decision 145). DateTime.UtcNow reads the
    /// SAME clock on both sides -- __dtUtcNow() derives the BigInt tick count from Date.now(); __dtNow
    /// subtracts the current local offset; __dtToday truncates Now to the local day. The template compiler
    /// emits exactly these, in dependency order, when the set is non-empty.</summary>
    public readonly HashSet<string> ClockHelpers = [];

    /// <summary>The Random machinery this module actually used (decision 146): "rnd" -> the __rnd factory
    /// (seeded = the exact .NET Knuth-subtractive sequence; unseeded = Math.random behind the same
    /// interface), "rndShared" -> the ONE module-level unseeded instance backing Random.Shared.</summary>
    public readonly HashSet<string> RandomHelpers = [];

    /// <summary>The HTTP machinery this module actually used (decision 147): "getJson" -> __getJson
    /// (fetch + throw on !ok + JSON.parse + __camel), "getText" -> __getText, "postJson" -> __postJson.
    /// "getJson" implies "camel", the key normalizer faithful to System.Text.Json's Web defaults for the
    /// Pascal-to-camel leading character.</summary>
    public readonly HashSet<string> HttpHelpers = [];

    // ---- the public surface the template compiler consumes ------------------

    public bool Declares(string name) => _fieldsByName.ContainsKey(name) || _methodsByName.ContainsKey(name);
    public bool IsMethod(string name) => _methodsByName.ContainsKey(name);

    /// <summary>A `string` field that is already a SIGNAL (read by the template AND assigned outside
    /// construction, conjunction rule #67). The `@bind` slice binds only such fields: for a string the
    /// BindConverter is identity, and requiring an established signal sidesteps marking reactivity from
    /// the template-side @bind lowering (a pure @bind-only field is a deferred, distinct case).</summary>
    public bool IsStringSignal(string name) =>
        _fieldsByName.TryGetValue(name, out var f) && f.IsSignal &&
        f.Symbol.Type.SpecialType == SpecialType.System_String;

    /// <summary>A `bool` field that is already a signal — for `@bind` on a checkbox (the converter is the
    /// identity `.checked` property, no parsing, so this is faithful with no parse-failure edge).</summary>
    public bool IsBoolSignal(string name) =>
        _fieldsByName.TryGetValue(name, out var f) && f.IsSignal &&
        f.Symbol.Type.SpecialType == SpecialType.System_Boolean;

    /// <summary>An `int` field that is already a signal — for `@bind` on a number/text input. Unlike bool
    /// and string, this DOES parse: the change handler mirrors int.TryParse (NumberStyles.Integer + invariant)
    /// with a regex + int32-range check + revert-on-invalid, so an unparseable/overflowing entry keeps the
    /// field and re-renders the old value, exactly as Blazor's BindConverter does.</summary>
    public bool IsIntSignal(string name) =>
        _fieldsByName.TryGetValue(name, out var f) && f.IsSignal &&
        f.Symbol.Type.SpecialType == SpecialType.System_Int32;

    /// <summary>Does @code declare this name as an ElementReference field — i.e. is it a valid @ref
    /// target (decision 132)? Asked of the compiler's own table, never of the spelling.</summary>
    public bool IsElementRefField(string name) =>
        _fieldsByName.TryGetValue(name, out var f) && f.IsElementRef;

    /// <summary>The JS name of a field (e.g. `text`), so a signal read is `{FieldJs}.value`.</summary>
    public string? FieldJs(string name) => _fieldsByName.TryGetValue(name, out var f) ? f.Js : null;
    public IEnumerable<string> DeclaredNames => _fieldsByName.Keys.Concat(_methodsByName.Keys);
    public IEnumerable<string> MethodNames => _methodsByName.Keys;

    /// <summary>The JS binding a C# method name became. See JsName: C# is PascalCase, JS is not.</summary>
    public string MethodJs(string name) => _methodsByName[name].Js;

    /// <summary>How many times @code itself CALLS this method. Feeds TemplateCompiler.ShouldInline.</summary>
    public int CallsTo(string name) => _methodsByName.TryGetValue(name, out var m) ? m.CallUses : 0;

    /// <summary>The JS for one @expression / @key, translated in the scope it is written in.</summary>
    public string SlotJs(IntermediateNode node) => Get(node).Js;

    /// <summary>Does this slot read a computed property (decision 160)? Asked by &lt;ErrorBoundary&gt; only,
    /// and the reason is measured: a computed is refreshed by checkDirty() from INSIDE flush(), before the
    /// binding that reads it is entered, so a throw raised in the computed passes no guard the boundary
    /// wraps around that binding -- it is re-thrown at the write site instead (decision 38). The boundary
    /// refuses such content rather than appear to guard it.</summary>
    public bool SlotReadsComputed(IntermediateNode node) => Get(node).ReadsComputed;

    /// <summary>Does this expression read a computed property this component declares? Asked of the
    /// SEMANTIC MODEL rather than of the text, so an alias or a nested read counts the same.</summary>
    bool ReadsAComputed(ExpressionSyntax e) =>
        e.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(id =>
            _model.GetSymbolInfo(id).Symbol is IPropertySymbol ps &&
            _computedsByName.ContainsKey(ps.Name) &&
            SymbolEqualityComparer.Default.Equals(ps.ContainingType, _component));

    /// <summary>
    /// Whether one @expression READS reactive state -- i.e. whether its binding needs an
    /// effect or is a create-time write. THE ANSWER COMES FROM THIS COMPILER'S OWN TABLE
    /// (decision 57), never from what the text looks like.
    /// </summary>
    public bool SlotIsReactive(IntermediateNode node) => Get(node).Reactive;

    /// <summary>The slot's expression is float-typed. A float VALUE in JS is a Math.fround'd double whose bare
    /// coercion (`node.data = x`) would print the DOUBLE string, not C#'s float string (0.1f -> "0.1", not
    /// "0.10000000149011612"). So the template formats a float display through its __f32 helper (decision 113).</summary>
    public bool SlotIsFloat(IntermediateNode node) => Get(node).IsFloat;

    /// <summary>The slot's expression is decimal-typed. A decimal is a boxed { m, s } object; displaying it
    /// means rendering that object as C#'s decimal string (scale preserved: {m:110n,s:2} -> "1.10"), which the
    /// template does through the emitted __decStr helper (decision 114).</summary>
    public bool SlotIsDecimal(IntermediateNode node) => Get(node).IsDecimal;

    /// <summary>The slot's expression is DateTime-typed. A DateTime is a BigInt of ticks; displaying it renders
    /// that as C#'s default DateTime string ("MM/dd/yyyy HH:mm:ss") through the emitted __dtStr helper (decision 115).</summary>
    public bool SlotIsDateTime(IntermediateNode node) => Get(node).IsDateTime;

    /// <summary>The slot NAMES A METHOD rather than computing a value — `OnBump="@Inc"` at a composition
    /// site, i.e. an EventCallback binding (decision 130). The parent is handing the child one of its own
    /// methods; there is no value to display and no signal to track, so the bound-parameter reactivity
    /// rule does not apply to it.</summary>
    public bool SlotIsMethodGroup(IntermediateNode node) => Get(node).IsMethodGroup;

    /// <summary>The slot is `@ChildContent` — the POSITION a composing parent's markup is inlined at,
    /// not a value to bind (decision 131).</summary>
    public bool SlotIsFragment(IntermediateNode node) => Get(node).IsFragment;

    /// <summary>The slot expression's C# type, as a display string — the key a cascade is matched on
    /// (decision 134). Asked of the semantic model, so `@level` cascades as `int`, not as "level".</summary>
    public string? SlotTypeDisplay(IntermediateNode node) => Get(node).TypeDisplay;

    /// <summary>Is this [Parameter] declared as a RenderFragment? (decision 131)</summary>
    public bool IsFragmentParameter(string name) =>
        _paramsByName.TryGetValue(name, out var p) && Filament.Subset.TypeSubset.IsRenderFragment(p.Type);

    /// <summary>The RenderFragment [Parameter] names this child declares — the holes a parent may fill.</summary>
    public IEnumerable<string> FragmentParameterNames =>
        _paramsByName.Where(kv => Filament.Subset.TypeSubset.IsRenderFragment(kv.Value.Type)).Select(kv => kv.Key);

    /// <summary>What to emit for a container whose children held template C#, in order.</summary>
    /// <summary>
    /// What to emit for a container whose children held template C#, in order.
    ///
    /// EMPTY WHEN THE FILE IS BEING REFUSED, and that is not laziness. Decision 69's second
    /// defect was exactly this shape: @code failed, so the bodies were never translated, and
    /// the template walk -- which runs anyway, so that the template's OWN diagnostics get
    /// reported alongside @code's -- asked for something that did not exist and got a
    /// KeyNotFoundException, exit 134, a stack trace where the author should have read a
    /// located error. Nothing is emitted for a refused file, so an empty answer costs nothing.
    /// When there is NO diagnostic, the same question is the TOOL being broken and says so.
    /// </summary>
    public IReadOnlyList<TemplateOp> OpsFor(IntermediateNode container) =>
        _ops.TryGetValue(container, out var ops) ? ops
        : _diagnostics.Count > 0 ? []
        : throw new GeneratorException(
            "FIL-WIRING: the template asked to emit a container the C# front end never saw a region for. " +
            "The collect walk and the emit walk disagree about what the template contains, so one of them " +
            "is describing a different file. This is the TOOL being broken, not the input.");

    Slot Get(IntermediateNode node) =>
        _slots.TryGetValue(node, out var s) ? s
        : _diagnostics.Count > 0 ? new Slot { Js = "/*refused*/" }
        : throw new GeneratorException(
            "FIL-WIRING: the template asked to emit an expression the C# front end never saw. The " +
            "collect walk and the emit walk disagree about what the template contains, so one of them " +
            "is describing a different file. This is the TOOL being broken, not the input.");

    // ---- the walk -----------------------------------------------------------

    /// <summary>
    /// Parse, validate and translate @code AND every C# span the template holds -- in ONE
    /// compilation, because `foreach (Row row in _rows)` in the template only means anything
    /// against the `Row` and `_rows` that @code declares. Two compilations would be two
    /// answers to the same question.
    /// </summary>
    public void Compile(IReadOnlyList<CSharpCodeIntermediateNode> codeNodes, TemplatePlan plan)
    {
        var markers = new Dictionary<string, IntermediateNode>(StringComparer.Ordinal);
        var slots = new List<IntermediateNode>();

        // THE MARKER NAMESPACE IS RESERVED, AND IT IS CHECKED. A user identifier containing
        // this prefix would be READ AS a marker by the walk below -- markup emitted where the
        // author wrote a call, at exit 0. Checked against the raw text of every C# span the
        // template and @code contain, before any of it is spliced together.
        var authored = codeNodes.Cast<IntermediateNode>()
            .Concat(plan.Regions.SelectMany(r => r.Items).OfType<CodeItem>().Select(c => (IntermediateNode)c.Node))
            .Concat(plan.FreeSlots)
            .Concat(plan.Regions.SelectMany(r => r.Items).OfType<MarkupItem>().SelectMany(mi => mi.Slots));
        foreach (var node in authored)
        {
            var raw = RawText(node);
            var at = raw.IndexOf(Reserved, StringComparison.Ordinal);
            if (at < 0) continue;
            _diagnostics.Add(new Diagnostic("FIL0001", "reserved-name",
                $"this C# contains '{Reserved}', which the compiler reserves for the markers it splices into " +
                "the template's C# before re-parsing it (decision 54). An identifier that collided with a " +
                "marker would be read AS one -- markup emitted where you wrote a call, at exit 0. " +
                "Refusing to emit.",
                SourceOffset.At(node, raw, at)));
            return;
        }

        // ---- build the ONE source Roslyn parses -----------------------------
        _src = new WrappedSource();
        _src.Literal(
            "using System;using System.Collections.Generic;using System.Linq;" +
            "using System.Threading.Tasks;using Microsoft.AspNetCore.Components;" +
            // The Web namespace is what every Blazor template's _Imports.razor brings in -- it is
            // where KeyboardEventArgs lives (decision 159), and authors write it bare.
            "using Microsoft.AspNetCore.Components.Web;using Microsoft.JSInterop;" +
            // The author's @usings (decision 147), validated by TemplateCompiler to resolve. Same
            // resolution Blazor's generated component gets from the directive.
            string.Concat(_usings.Select(ns => $"using {ns};")) +
            $"partial class __FilamentComponent{TypeParamList} : __FilamentComponentBase {{");
        foreach (var node in codeNodes) _src.Node(node, RawText(node));
        _src.Literal($"\n}}\npartial class __FilamentComponent{TypeParamList} {{\n");

        // The @inject'd IJSRuntime, declared so the name BINDS (decision 133). Razor declares it as a
        // property on the generated component; this compiler only needs C# to resolve `JS.InvokeVoidAsync`
        // to a real symbol, so a field is enough. It is skipped by the member walk (FieldDecl) because it
        // is not the author's declaration and emits nothing: the service is erased.
        if (_jsRuntime is not null)
            _src.Literal($"Microsoft.JSInterop.IJSRuntime {_jsRuntime} = null!;\n");

        // The @inject'd HttpClient, declared so the name BINDS (decision 147) -- same mechanics as the
        // IJSRuntime above: a field in the second partial block, skipped by the member walk, emitting
        // nothing (the service is erased into fetch at its call sites).
        if (_httpClient is not null)
            _src.Literal($"System.Net.Http.HttpClient {_httpClient} = null!;\n");

        // The <ErrorBoundary>'s ErrorContent context (decision 164), declared so `context` BINDS -- the
        // same mechanics as the two services above: a field is enough for C# to resolve it, it is skipped
        // by the member walk (FieldDecl), and it emits nothing. Its VALUE is the latch read, registered in
        // _paramEnv so Identifier() folds `context` to it.
        if (_errorContextJs is not null)
        {
            _src.Literal("System.Exception context = null!;\n");
            _paramEnv["context"] = _errorContextJs;
        }

        void Slot(IntermediateNode n)
        {
            var i = slots.Count;
            slots.Add(n);
            _src.Literal($"__filament_s{i}(");
            _src.Node(n, RawText(n));
            _src.Literal(");\n");
        }

        var decls = new StringBuilder();
        for (var i = 0; i < CountSlots(plan); i++)
        {
            decls.Append($"void __filament_s{i}(params object[] a) {{}}\n");

            // A COMPONENT-ATTRIBUTE slot may name a METHOD GROUP (`OnBump="@Inc"`) -- the EventCallback
            // half of composition (decision 130). A method group has NO conversion to object, so against
            // the object-only marker C# binds nothing and GetSymbolInfo reports no symbol at all: the
            // name would be refused as undeclared though @code declares it. This overload lets C# ITSELF
            // bind the group, so Identifier() receives a real IMethodSymbol rather than a guess. It is
            // emitted ONLY for component-attribute slots, so a method group stays unbindable -- and so
            // still refused -- at every other binding site, where splicing a function name into the DOM
            // would render "inc" where the author wrote a value.
            var free = plan.FreeSlots;
            if (i < free.Count && plan.ComponentSlots.Contains(free[i]))
                decls.Append($"void __filament_s{i}(System.Action a) {{}}\n");
        }
        _src.Literal(decls.ToString());

        var markerCount = plan.Regions.Sum(r => r.Items.OfType<MarkupItem>().Count());
        for (var i = 0; i < markerCount; i++) _src.Literal($"void __filament_m{i}() {{}}\n");

        // Free expressions: at CLASS scope, which is the scope the template reads them in.
        _src.Literal("void __filament_free() {\n");
        foreach (var n in plan.FreeSlots) Slot(n);
        _src.Literal("}\n");

        // Regions: THE REASSEMBLY. Decision 54's unbalanced spans, put back together with the
        // markup as calls, so the braces close and Roslyn can see a loop.
        //
        // A lambda handler harvested from markup INSIDE a region is planted HERE, as a LOCAL FUNCTION
        // right after its markup item's marker -- i.e. inside whatever braces the author's own @foreach/@if
        // put around that markup -- so a body like `Del(r.Id)` is compiled in the scope the author wrote it
        // in and the loop variable resolves (decision 141). This is the slots' own scope story (see "row is
        // a loop local and only this tree knows it" below) applied to handlers; the class-scope planting
        // (decision 105) remains for lambdas on mount-level markup, where there is no region to carry them.
        // A NESTED region (decision 158) -- an @if whose container sits under another region's
        // markup item, reading that region's loop variable -- is planted the SAME way, as a LOCAL
        // FUNCTION right after its item's marker, so the loop variable resolves in it too. The
        // recursion mirrors the author's own lexical nesting; sibling regions (decision 142) stay
        // flat exactly as before.
        var lambdaNames = new List<(IntermediateNode Attr, string Name)>();
        var claimedLambdas = new bool[plan.LambdaHandlers.Count];
        var emittedRegions = new bool[plan.Regions.Count];
        var regionMethods = Enumerable.Range(0, plan.Regions.Count).Select(r => $"__filament_t{r}").ToList();
        var m = 0;
        void EmitRegionMethod(int r)
        {
            emittedRegions[r] = true;
            _src.Literal($"void {regionMethods[r]}() {{\n");
            foreach (var item in plan.Regions[r].Items)
                switch (item)
                {
                    case CodeItem c:
                        _src.Node(c.Node, RawText(c.Node));
                        break;
                    case MarkupItem mi:
                        markers[$"__filament_m{m}"] = mi.Node;
                        _src.Literal($"\n__filament_m{m++}();\n");
                        foreach (var s in mi.Slots) Slot(s);
                        for (var k = 0; k < plan.LambdaHandlers.Count; k++)
                        {
                            if (claimedLambdas[k] || !IsUnder(mi.Node, plan.LambdaHandlers[k].Attr)) continue;
                            if (LambdaMethodBody(plan.LambdaHandlers[k].RawHandler) is not { } lbody) continue;
                            claimedLambdas[k] = true;
                            var lname = $"__filament_lambda_{k}";
                            lambdaNames.Add((plan.LambdaHandlers[k].Attr, lname));
                            _src.Literal($"void {lname}() ");
                            _src.Node(plan.LambdaHandlers[k].Attr, lbody);
                            _src.Literal("\n");
                        }
                        for (var cr = 0; cr < plan.Regions.Count; cr++)
                            if (!emittedRegions[cr] && IsUnder(mi.Node, plan.Regions[cr].Container))
                                EmitRegionMethod(cr);
                        break;
                }
            _src.Literal("\n}\n");
        }
        for (var r = 0; r < plan.Regions.Count; r++)
            if (!emittedRegions[r]) EmitRegionMethod(r);

        // Lambda event handlers (decision 105): each body as a synthetic method at class scope, so it is
        // marked + translated exactly like a @code method body. Mapped to the attribute's source so a
        // diagnostic inside the lambda points at the handler. Bodies that are not the no-arg lambda form
        // are skipped here (the harvest regex admits only `() => …`, so this is belt-and-braces). Region
        // lambdas were already planted above, in the scope that declares their captures (decision 141).
        for (var k = 0; k < plan.LambdaHandlers.Count; k++)
        {
            if (claimedLambdas[k]) continue;
            if (LambdaMethodBody(plan.LambdaHandlers[k].RawHandler) is not { } mbody) continue;
            var lname = $"__filament_lambda_{k}";
            lambdaNames.Add((plan.LambdaHandlers[k].Attr, lname));
            _src.Literal($"void {lname}() ");
            _src.Node(plan.LambdaHandlers[k].Attr, mbody);
            _src.Literal("\n");
        }
        _src.Literal("}\n");

        // The virtual init pair (decision 156), so the author's `protected override` BINDS -- the one
        // sliver of ComponentBase this compilation needs to exist. Declared LAST so the member walk's
        // class indices (classes[0]=@code partial, classes[1]=regions partial) are untouched; nothing
        // else is on it, so every other lifecycle override still fails CS0115 (and the by-name refusal
        // in Method() answers first, with the reason).
        _src.Literal(
            "class __FilamentComponentBase { protected virtual void OnInitialized() { } " +
            "protected virtual System.Threading.Tasks.Task OnInitializedAsync() => System.Threading.Tasks.Task.CompletedTask; }\n");

        var text = _src.ToString();
        var tree = CSharpSyntaxTree.ParseText(text);

        // SYNTAX errors first: a block that does not parse must never be compiled past. This
        // is what catches Phase 2's hand-written JS seam being fed to a C# front end -- with a
        // location, instead of a splice.
        var syntaxErrors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (syntaxErrors.Count > 0)
        {
            foreach (var d in syntaxErrors.Take(4))
                Refuse("not-csharp",
                    $"@code does not parse as C#: {d.GetMessage()}. Phase 3 compiles @code as C# (spec 5); " +
                    "a block that is not C# has nothing to translate. If this is Phase 2's hand-written " +
                    "JavaScript seam, it is no longer the input -- the compiler now does the state lifting " +
                    "itself. Refusing to emit.",
                    d.Location.SourceSpan.Start);
            return;
        }

        // concurrentBuild: false -- Roslyn's default parallelises semantic work behind Task.Wait,
        // which is Monitor.Wait, which a single-threaded WASM host cannot do ("Cannot wait on
        // monitors on this runtime", measured in the playground; decision 144). One compilation of
        // one @code block gains nothing from parallelism anywhere else either.
        var compilation = CSharpCompilation.Create("FilamentCode", [tree], ReferenceAssemblies.ForCode(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, concurrentBuild: false));

        // VERIFY THE COMPILATION FROM THE ARTIFACT (decision 10/53). If the references did not
        // load, every type is an error symbol and this class would emit FIL0002 for `int` -- a
        // wall of false diagnostics blaming the author for the tool being broken.
        if (compilation.GetTypeByMetadataName("System.Int32") is null)
            throw new GeneratorException(
                "FIL-WIRING: the C# compilation resolved no System.Int32, so no type in @code can be " +
                "resolved and every one of them would be reported as out-of-subset. The reference " +
                "assemblies did not load. Refusing to emit.");

        _model = compilation.GetSemanticModel(tree);

        // The slots, RESOLVED: each `__filament_s{i}(<expr>)` call's argument is the author's
        // expression, parsed in the scope the template reads it in. THAT is what a regex over
        // the raw text could never be -- `row` is a loop local and only this tree knows it.
        foreach (var inv in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (inv.Expression is not IdentifierNameSyntax sid ||
                !sid.Identifier.Text.StartsWith("__filament_s", StringComparison.Ordinal)) continue;
            var i = int.Parse(sid.Identifier.Text["__filament_s".Length..]);
            if (inv.ArgumentList.Arguments.Count != 1)
            {
                Refuse("unsupported-expression",
                    "an @expression in the template is empty or is not a single C# expression. Refusing to emit.",
                    inv.SpanStart);
                return;
            }
            _slotSyntax[slots[i]] = inv.ArgumentList.Arguments[0].Expression;
        }

        var classes = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();
        var cls = classes[0];
        _component = (INamedTypeSymbol)_model.GetDeclaredSymbol(cls)!;

        // 1. members: the allowlist. ONLY the first partial's -- the second holds the markers
        //    this compiler synthesised, and walking them would refuse `params object[]`.
        foreach (var member in cls.Members.OfType<RecordDeclarationSyntax>()) Record(member);
        if (_diagnostics.Count > 0) return;
        foreach (var member in cls.Members)
        {
            // NO EARLY RETURN, and that is not a style choice -- it is what keeps the SECOND
            // diagnostic true. Bailing on the first refusal leaves _methodsByName EMPTY, and the
            // template's binding gate then reports `@onclick="Increment"` as "a name the @code
            // block does not declare (it declares none)" -- which is FALSE: @code declares it,
            // and this compiler gave up before looking. Decision 69 already ruled on that exact
            // shape ("un diagnostic qui blame l'auteur pour une omission du compilateur").
            // MEASURED: with the return, [System.Obsolete] on Increment() produced 2 diagnostics,
            // the second one false; without it, 1 diagnostic and it is the true one.
            CheckNoAttributes(member);
            if (member is RecordDeclarationSyntax) continue;
            Member(member);
        }
        if (_diagnostics.Count > 0) return;
        if (!CheckJsNameCollisions()) return;

        // Lambda event handlers (decision 105): find each synthetic method now that the tree is bound,
        // as SEPARATE MethodInfos -- so the reactivity passes and Body() below cover them (a lambda's
        // `currentCount++` marks `currentCount` a signal, exactly as a @code method's would), but they are
        // NEVER emitted as top-level functions (they inline into their listen()).
        foreach (var (attr, name) in lambdaNames)
        {
            // A mount-level lambda is a class-scope synthetic METHOD (decision 105); a region lambda is a
            // LOCAL FUNCTION inside its region method (decision 141). Same MethodInfo either way -- the
            // marking passes walk DescendantNodes and the translator reads Block, and both syntaxes carry
            // an IMethodSymbol.
            SyntaxNode? syntax = (SyntaxNode?)classes[1].Members.OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(md => md.Identifier.Text == name)
                ?? tree.GetRoot().DescendantNodes().OfType<LocalFunctionStatementSyntax>()
                    .FirstOrDefault(lf => lf.Identifier.Text == name);
            if (syntax is null || _model.GetDeclaredSymbol(syntax) is not IMethodSymbol sym) continue;
            var lm = new MethodInfo { Name = name, Syntax = syntax, Symbol = sym };
            if (lm.Block is null) continue;
            _lambdaMethods.Add((attr, lm));
        }

        // 2. reactivity, in the order the rule requires:
        //    (a) the construction sites, so an assignment inside one is not a write;
        //    (b) who assigns what, outside those;
        //    (c) what the template reads -- FROM SYMBOLS, resolved in the right scope.
        foreach (var mi in _methods) MarkConstructionSites(mi.Syntax);
        foreach (var (_, lm) in _lambdaMethods) MarkConstructionSites(lm.Syntax);
        foreach (var mi in _methods) MarkAssignments(mi.Syntax);
        foreach (var (_, lm) in _lambdaMethods) MarkAssignments(lm.Syntax);
        foreach (var mi in _methods) MarkListMutations(mi.Syntax);
        foreach (var (_, lm) in _lambdaMethods) MarkListMutations(lm.Syntax);

        // ONE FIELD, ONE IDIOM (decision 153). A List that is BOTH structurally mutated and
        // wholesale reassigned has no consistent declaration: mutate+version declares `const`
        // (rows.js decision 1), reassign-as-signal makes the field itself the signal (decision
        // 140). The mixed shape emitted `const items = […]` and then assigned to it -- valid
        // authored Blazor compiled to a module that THROWS at the first event, the outcome
        // section 10 forbids. Refused at the colliding write until a faithful mapping is measured.
        foreach (var f in _fields)
            if (f.List is { Mutated: true, Reassigned: true } li)
                Refuse("list-mutate-and-reassign",
                    $"field '{f.Name}' is both structurally mutated (Add/RemoveAt/Clear/element write) " +
                    "and wholesale reassigned. One field supports one idiom: mutate + version signal " +
                    "(rows.js) or reassign-as-signal (decision 140) -- split it into two fields, the " +
                    "mutated store and the reassigned view (the Duel's tasks/visible). The mixed " +
                    "emission declared 'const' and assigned to it, a module that throws at the first " +
                    "event; refusing to emit instead.",
                    li.ReassignSite ?? 0);

        MarkTemplateReads(slots);
        // A COMPUTED's dependencies are template reads BY TRANSITIVITY (decision 160): the template
        // reads the computed, the computed reads the field, so the field must lift exactly as if
        // the template read it directly -- otherwise the closure captures a PLAIN binding and the
        // computed evaluates once, silently stale even on a reassigned collection.
        foreach (var c in _computeds)
            foreach (var n in c.Syntax.ExpressionBody!.Expression.DescendantNodesAndSelf())
            {
                if (n is not (IdentifierNameSyntax or MemberAccessExpressionSyntax)) continue;
                switch (_model.GetSymbolInfo(n).Symbol)
                {
                    case IFieldSymbol fs when Field(fs) is { } f: f.ReadByTemplate = true; break;
                    case IPropertySymbol ps when PropAnywhere(ps) is { } pr: pr.ReadByTemplate = true; break;
                }
            }
        // A TWO-WAY BIND IS A WRITE (decision 138), and marking it is what closes decision 104's
        // deferral ("a pure @bind-only field needs its reactivity marked from the template"). Without
        // it the bound target is read-but-never-assigned, decision 67's conjunction leaves it a plain
        // binding, and the input would render once and then track nothing.
        MarkTemplateWrites(plan.FormBinds.Select(b => b.Value));
        // A PLAIN @bind is a read AND a write BY ITSELF (decision 154, lifting #104's
        // "pure @bind-only field" deferral with #138's argument): Razor's lowering reads the
        // field into `value` and assigns it in the binder, so the input is the display and the
        // writer at once. A name that matches no field marks nothing -- the undeclared-field
        // refusal in TryBind is untouched.
        foreach (var name in plan.ElementBindNames)
            if (_fields.FirstOrDefault(f => f.Name == name) is { } bf)
            {
                bf.ReadByTemplate = true;
                bf.AssignedOutsideConstruction = true;
            }
        MarkConditionReads(classes[1], regionMethods);

        // 3. the call graph: who calls whom, for the inlining arbitrage AND for the emission
        //    order (callees before callers -- see MethodsInDependencyOrder).
        foreach (var mi in _methods) MarkCalls(mi.Syntax, mi);
        foreach (var (_, lm) in _lambdaMethods) MarkCalls(lm.Syntax, lm);

        // 3b. FIELD INITIALISERS, now that the marking is settled (decision 137). They are translated
        //     BEFORE bodies and slots for no ordering reason of their own -- only so that everything
        //     downstream reads one settled Init. Doing this in the member walk emitted `{ name: 'a' }`
        //     while every read of it emitted `model.name.value`: the literal and the reads disagreed,
        //     the page rendered "undefined", and the click threw. Measured, not reasoned about.
        foreach (var f in _fields)
            if (f.InitThunk is { } thunk) f.Init = thunk();

        // 4. TRANSLATE EVERYTHING, NOW -- not lazily at emission. Every diagnostic this input
        //    can produce must exist before Compile() returns, because that is when the caller
        //    reads them. See _sealed for the defect that made this ordering load-bearing.
        foreach (var f in _fields.Where(f => f.List is { Hoisted: false, Mutated: true }))
        {
            f.List!.Version = Unique(f.Js + "Version");
            f.List.Changed = Unique(f.Js + "Changed");
        }
        // COMPUTED bodies (decision 160), translated with the marking settled. One admission rule
        // beyond the expression subset: the body's reactivity must come from SIGNALS (signal
        // fields, reassigned collections, per-record signals, other computeds). A structurally
        // MUTATED List's reactivity lives in a version signal the body never reads -- computed()
        // would evaluate once and go silently stale, the outcome section 10 forbids.
        foreach (var c in _computeds)
        {
            var mutated = c.Syntax.ExpressionBody!.Expression.DescendantNodesAndSelf()
                .OfType<IdentifierNameSyntax>()
                .Select(n => _model.GetSymbolInfo(n).Symbol)
                .OfType<IFieldSymbol>()
                .Select(Field)
                .FirstOrDefault(f => f?.List is { Mutated: true });
            if (mutated is not null)
            {
                Refuse("unsupported-member",
                    $"computed property '{c.Name}' reads '{mutated.Name}', a structurally MUTATED List whose " +
                    "reactivity lives in a version signal (rows.js decision 1) -- the computed would evaluate " +
                    "once and go silently stale. Read a REASSIGNED collection (decision 140, the Duel's " +
                    "tasks/visible split) or recompute in a method. Refusing to emit.",
                    c.Syntax.Identifier.SpanStart);
                continue;
            }
            c.BodyJs = Expr(c.Syntax.ExpressionBody.Expression);
        }

        foreach (var mi in _methods) _bodies[mi.Name] = Body(mi.Block!);
        foreach (var (attr, lm) in _lambdaMethods)
        {
            _lambdaBodies[attr] = Body(lm.Block!);
            if (CountWrites(lm, []) > 1) _lambdaBatched.Add(attr);
        }
        TranslateSlots(slots);
        foreach (var (container, method) in plan.Regions.Zip(regionMethods))
        {
            _ops[container.Container] = RegionOps(FindMethod(classes[1], method).Statements, markers,
                underRow: _rowScopes.Any(row => IsUnder(row, container.Container)));
            foreach (var feOp in _ops[container.Container].OfType<ForEachOp>())
                _rowScopes.Add(feOp.Body);
        }

        // 5. THE BACKSTOP: is this even C#? Roslyn's OWN verdict, asked LAST.
        if (_diagnostics.Count == 0) CheckSemantics(compilation);

        _sealed = true;
    }

    static int CountSlots(TemplatePlan plan) =>
        plan.FreeSlots.Count + plan.Regions.Sum(r => r.Items.OfType<MarkupItem>().Sum(m => m.Slots.Count));

    /// <summary>A region method's BODY. A top-level region is a class member; a NESTED region
    /// (decision 158) is a LOCAL FUNCTION planted inside its parent region's loop braces -- the
    /// scope that binds its loop variable -- so both shapes are searched.</summary>
    static BlockSyntax FindMethod(ClassDeclarationSyntax cls, string name) =>
        (cls.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault(m => m.Identifier.Text == name)?.Body
         ?? cls.DescendantNodes().OfType<LocalFunctionStatementSyntax>()
             .First(lf => lf.Identifier.Text == name).Body)!;

    // ---- the template's own C# ----------------------------------------------

    /// <summary>
    /// The re-parsed region, turned back into "emit this, then that". Every statement is
    /// positively accounted for: this is the walk decision 54 said Razor makes impossible,
    /// so it is exactly the place where an unaccounted node would become silent wrong JS.
    /// </summary>
    List<TemplateOp> RegionOps(IEnumerable<StatementSyntax> statements, IReadOnlyDictionary<string, IntermediateNode> markers,
        bool underRow = false)
    {
        var ops = new List<TemplateOp>();
        foreach (var s in statements)
        {
            if (MarkerName(s) is { } marker)
            {
                if (marker.StartsWith("__filament_s", StringComparison.Ordinal)) continue; // a scope anchor
                ops.Add(new MarkupOp(markers[marker]));
                continue;
            }

            if (s is ForEachStatementSyntax fe)
            {
                // Decision 158 admits a nested @if (the row-anchored list() over the per-record
                // signal, measured); a nested @foreach compiles TOO -- and that is exactly why it is
                // refused here: it has no witness, and an unmeasured emission is the thing this
                // repo does not ship. Its own slice can lift this.
                if (underRow)
                {
                    Refuse("unsupported-template-statement",
                        "a @foreach nested inside another @foreach's row is not measured -- decision 158 " +
                        "admits a nested @if; a nested list() needs its own witness before it ships. " +
                        "Refusing to emit.",
                        fe.SpanStart);
                    continue;
                }
                if (ForEach(fe, markers) is { } op) ops.Add(op);
                continue;
            }

            if (s is IfStatementSyntax ifs)
            {
                if (If(ifs, markers) is { } op) ops.Add(op);
                continue;
            }

            // A LOCAL DECLARATION in a template code block (@{ int x = 5; }) declares a value the template
            // reads. It runs ONCE in mount() -- where the tree is built -- so it is not "a statement with no
            // place to run"; it is a one-time const. Translate it and carry it as a CodeOp; the read (@x)
            // resolves to the local. Any OTHER statement stays refused (it would need a re-render to matter).
            if (s is LocalDeclarationStatementSyntax ld)
            {
                ops.Add(new CodeOp(Statement(ld)));
                continue;
            }

            // A planted lambda-handler local function (decision 141): this compiler's own scope carrier,
            // not an author statement. It emits nothing here -- its body is translated via _lambdaBodies
            // and wired as the element's listener where the row/branch template is emitted.
            if (s is LocalFunctionStatementSyntax lf &&
                lf.Identifier.Text.StartsWith("__filament_lambda_", StringComparison.Ordinal))
                continue;

            // A NESTED region method (decision 158): the same scope-carrier idiom, for an @if under a
            // row. Its ops are parsed from ITS OWN body (keyed by its container); nothing to emit here.
            if (s is LocalFunctionStatementSyntax nested &&
                nested.Identifier.Text.StartsWith("__filament_t", StringComparison.Ordinal))
                continue;

            Refuse("unsupported-template-statement",
                $"{Describe(s)} in the template is not in the subset. The template admits @foreach and @if " +
                "(spec 5), a local declaration in a @{{ }} code block, and markup; any OTHER C# STATEMENT in a " +
                "template has no place to run -- a Filament module builds its tree once and never re-renders. " +
                "Refusing to emit.",
                s.SpanStart);
        }
        return ops;
    }

    /// <summary>The name of a `__filament_*();` marker call, or null for a real statement.</summary>
    static string? MarkerName(StatementSyntax s) =>
        s is ExpressionStatementSyntax
        {
            Expression: InvocationExpressionSyntax { Expression: IdentifierNameSyntax id },
        } && id.Identifier.Text.StartsWith(Reserved, StringComparison.Ordinal)
            ? id.Identifier.Text
            : null;

    /// <summary>
    /// `@foreach (Row row in _rows) { &lt;tr @key="row.Id"&gt;...&lt;/tr&gt; }` -> list().
    ///
    /// Every condition below is refused rather than approximated, because list()'s contract
    /// is narrow and every violation of it is a silent wrong render, not a crash.
    /// </summary>
    ForEachOp? ForEach(ForEachStatementSyntax fe, IReadOnlyDictionary<string, IntermediateNode> markers)
    {
        // The collection must be a FIELD with something list() can SUBSCRIBE to. Four shapes qualify, each
        // fixing the source lambda list() re-runs on:
        //   - a MUTATED List<T>       -> a BLOCK that reads the version signal then returns the plain array
        //     (rows.js decision 1): `() => { xVersion.value; return x; }`.
        //   - a REASSIGNED List<T>    -> never mutated in place, so no version exists; the field is itself a
        //     signal (decision 140, the List twin of 124): `() => x.value`.
        //   - a REASSIGNED T[]        -> the array field is itself a signal (decision 124), so reading it both
        //     subscribes AND yields the array: `() => x.value`.
        //   - a REASSIGNED Dictionary -> the Map field is a signal too (decision 125); list() needs an ARRAY,
        //     so the source spreads the Map to its [k,v][] entries: `() => [...x.value]`.
        // A never-written List / never-reassigned array or Dict is a STATIC tree, whose mapping is not
        // implemented -> refused.
        string sourceJs;
        if (_model.GetSymbolInfo(fe.Expression).Symbol is not IFieldSymbol fs || Field(fs) is not { } f)
        {
            Refuse("unsupported-foreach",
                $"@foreach iterates '{Trunc(fe.Expression.ToString())}', which is not a List<T>, T[] or " +
                "Dictionary<K,V> field declared in this component. list() reconciles against a source it can " +
                "SUBSCRIBE to. Refusing to emit.",
                fe.Expression.SpanStart);
            return null;
        }
        if (f.List is { } li)
        {
            if (li.Mutated)
            {
                sourceJs = $"() => {{\n  {li.Version}.value;\n  return {f.Js};\n}}";
            }
            else if (f.IsSignal)
            {
                // REASSIGNED, never mutated (decision 140): no version signal exists or is needed -- the
                // FIELD is the subscribable thing, exactly as a reassigned T[] (decision 124), so the
                // source collapses to the same single self-subscribing read.
                sourceJs = $"() => {f.Js}.value";
            }
            else
            {
                Refuse("unsupported-foreach",
                    $"@foreach iterates '{f.Name}', a List that is neither mutated in place (which would give " +
                    "it a version signal) nor reassigned wholesale (which would make the field itself a signal, " +
                    "decision 140), so list() has no source to re-run on. A never-written list rendered once is " +
                    "a static tree; that mapping is not implemented. Refusing to emit.",
                    fe.Expression.SpanStart);
                return null;
            }
        }
        else if (f.Symbol.Type is IArrayTypeSymbol { Rank: 1 })
        {
            if (!f.IsSignal)
            {
                Refuse("unsupported-foreach",
                    $"@foreach iterates '{f.Name}', a T[] that is never reassigned, so it is not a signal and " +
                    "list() has no source to re-run on. A never-reassigned array rendered once is a static tree; " +
                    "that mapping is not implemented. Refusing to emit.",
                    fe.Expression.SpanStart);
                return null;
            }
            sourceJs = $"() => {f.Js}.value";
        }
        else if (Filament.Subset.TypeSubset.DictionaryTypes(f.Symbol.Type) is ({ } _, { } _))
        {
            if (!f.IsSignal)
            {
                Refuse("unsupported-foreach",
                    $"@foreach iterates '{f.Name}', a Dictionary that is never reassigned, so it is not a signal " +
                    "and list() has no source to re-run on. A never-reassigned Dictionary rendered once is a static " +
                    "tree; that mapping is not implemented. Refusing to emit.",
                    fe.Expression.SpanStart);
                return null;
            }
            sourceJs = $"() => [...{f.Js}.value]";
        }
        else
        {
            Refuse("unsupported-foreach",
                $"@foreach iterates '{f.Name}', which is neither a List<T>, a T[] nor a Dictionary<K,V> field. " +
                "Refusing to emit.",
                fe.Expression.SpanStart);
            return null;
        }

        if (fe.Type is IdentifierNameSyntax { IsVar: true })
        {
            Refuse("unsupported-foreach",
                "`var` in a @foreach is not in the subset: the element type is what decides the row's mapping " +
                "(a record becomes an object literal, a scalar does not), and spelling it is the one place the " +
                "author says which. Refusing to emit.",
                fe.Type.SpanStart);
            return null;
        }

        // The ORIGINAL statement nodes, never a SyntaxFactory copy: a re-parented node is not
        // in the tree the SemanticModel was built for, and asking it about one throws.
        IEnumerable<StatementSyntax> body = fe.Statement is BlockSyntax b ? b.Statements : [fe.Statement];
        var ops = RegionOps(body, markers);
        var markup = ops.OfType<MarkupOp>().ToList();

        if (markup.Count != 1 || ops.Count != markup.Count)
        {
            Refuse("unsupported-foreach",
                $"a @foreach body must be exactly ONE element and nothing else; this one produces {ops.Count} " +
                "thing(s). list() maps one @key to ONE root node -- it inserts, moves and removes that node, so " +
                "a body with two roots (or with a stray text node beside the element) has no single thing for a " +
                "key to name. Refusing to emit.",
                fe.Statement.SpanStart);
            return null;
        }

        var key = KeyOf(markup[0].Node);
        if (key is null)
        {
            Refuse("unsupported-foreach",
                "a @foreach in the subset must carry @key on its element. Without one, list() has no identity " +
                "to reconcile against, and rows.js's mapping decision (2) -- Row.Id is plain PRECISELY because " +
                "@key compiles to keyOf, which reconcile() calls with the list effect active -- has nothing to " +
                "be about. Refusing to emit rather than invent an identity.",
                fe.Statement.SpanStart);
            return null;
        }

        return new ForEachOp(JsName(fe.Identifier.Text), sourceJs, markup[0].Node, key);
    }

    /// <summary>
    /// `@if (c0) { &lt;e0&gt; } else if (c1) { &lt;e1&gt; } … else { &lt;en&gt; }` -> IfOp with one
    /// IfBranch per branch (the trailing @else, if any, is a branch with a null condition), lowered
    /// to a keyed list() by TemplateCompiler. This cut's subset: each branch body is exactly one
    /// element. Multi-node branch bodies and nested control flow in a branch are refused PER BRANCH
    /// by BranchBody; @if at the template root is refused earlier by the root-code guard.
    /// </summary>
    IfOp? If(IfStatementSyntax ifs, IReadOnlyDictionary<string, IntermediateNode> markers)
    {
        var branches = new List<IfBranch>();
        var cur = ifs;
        while (true)
        {
            if (BranchBody(cur.Statement, markers) is not { } body) return null;
            branches.Add(new IfBranch(Expr(cur.Condition), body));

            if (cur.Else is not { } els) break;               // if / else-if chain ended, no @else
            if (els.Statement is IfStatementSyntax nested)    // "else if (...)"
            {
                cur = nested;
                continue;
            }
            if (BranchBody(els.Statement, markers) is not { } elseBody) return null;  // trailing "else { … }"
            branches.Add(new IfBranch(null, elseBody));
            break;
        }
        return new IfOp(branches);
    }

    /// <summary>
    /// One @if/@else branch body -> the ops it produces, or null (a located refusal already emitted).
    /// A branch body may be ONE OR MORE markup elements (each a leaf of the conditional list), OR a
    /// single nested @if (an IfOp, flattened into the decision-tree source by EmitIf). Mixing markup
    /// with a nested @if, multiple nested @ifs, a @foreach in a branch, or a stray text node is not in
    /// the subset (deferred).
    /// </summary>
    IReadOnlyList<TemplateOp>? BranchBody(StatementSyntax stmt, IReadOnlyDictionary<string, IntermediateNode> markers)
    {
        // The ORIGINAL statement nodes, never a SyntaxFactory copy (see ForEach).
        IEnumerable<StatementSyntax> body = stmt is BlockSyntax b ? b.Statements : [stmt];
        var ops = RegionOps(body, markers);

        // A branch body is one or more MARKUP elements and/or nested @ifs, in any mix (decision 120). Each
        // markup element is a constant active leaf; each nested @if spreads its own decision-tree active
        // indices into the same conditional list (BranchExpr). A @foreach in a branch, a code block, or a
        // stray text node is still refused.
        if (ops.Count < 1 || !ops.All(o => o is MarkupOp or IfOp))
        {
            Refuse("unsupported-if-body",
                $"a template @if / @else branch body must be markup elements and/or nested @ifs, and nothing " +
                $"else; this one produces {ops.Count} thing(s), not all markup or nested @if. A @foreach in a " +
                "branch, a code block, or a stray text node is not in the subset. Refusing to emit.",
                stmt.SpanStart);
            return null;
        }
        return ops;
    }

    static IntermediateNode? KeyOf(IntermediateNode markup) =>
        markup.Children.OfType<SetKeyIntermediateNode>().FirstOrDefault();

    // ---- members ------------------------------------------------------------

    /// <summary>
    /// ROSLYN'S OWN VERDICT ON THE AUTHOR'S C#, WHICH THIS FRONT END NEVER ASKED FOR.
    ///
    /// THE DEFECT, MEASURED BEFORE THIS EXISTED -- exit 0, module written:
    ///     private int currentCount = "this is a string, not an int";
    ///         -> `const currentCount = 'this is a string, not an int';`
    /// That is CS0029. It is not out-of-subset C#, it is NOT C# -- Blazor refuses to build the
    /// file -- and Filament emitted a module that renders the string where the source says int.
    /// Section 10's forbidden mode, reached without a single out-of-subset construct.
    ///
    /// WHY IT WAS OPEN. Compile() checked `tree.GetDiagnostics()` -- SYNTAX only, with a comment
    /// saying "a block that does not parse must never be compiled past" -- and never asked
    /// `compilation.GetDiagnostics()`. The semantic half of that same claim simply was not made.
    /// Decision 41's pattern at the level of the diagnostic SOURCE: the guard was on one of
    /// Roslyn's two verdicts and the identical hole sat one call over. Each subset rule was
    /// individually right and none of them composes: CheckType says `int` is in section 5, Expr
    /// says a string literal is in section 5, and nothing asks whether the pair type-checks.
    ///
    /// WHY IT RUNS LAST, WHICH IS THE ARBITRAGE. Asked FIRST, it would win races it should lose
    /// and DEGRADE two diagnostics this repo already got right (measured, both):
    ///   - `@nowhereDeclared` compiles to `__filament_s0(nowhereDeclared)`, so Roslyn calls it
    ///     CS0103. The subset's own [unresolved-name] says "a Filament module has no `this`, no
    ///     base class and no injected services to reach for" -- the author's actual problem.
    ///   - `goto done;` with no label is CS0159, but [unsupported-statement] is the true answer:
    ///     the label would not have helped, goto is not in section 5.
    /// So the ALLOWLIST answers first and this is the net under it -- the two-independent-gates
    /// shape decision 61 established for the Razor side, with the specific gate first. It is a
    /// BACKSTOP: `if (_diagnostics.Count == 0)`. Its coverage is therefore exactly what the
    /// allowlist let through, which is the only thing a backstop is for.
    ///
    /// VERIFIED NOT TO BE A WALL OF FALSE POSITIVES, from the artifact and not from reading:
    /// both real apps and every in-subset negative control still compile clean with it on. The
    /// reference set is the full BCL + ASP.NET ref pack, i.e. what a Blazor project resolves
    /// against, so "valid in the baseline" and "valid here" are the same question (decision 69's
    /// third defect was exactly this set being too thin).
    /// </summary>
    void CheckSemantics(CSharpCompilation compilation)
    {
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        foreach (var d in errors.Take(4))
        {
            var at = _src.Map(d.Location.SourceSpan.Start);

            // An error on text the AUTHOR did not write is this compiler's wrapper being wrong,
            // and saying "your @code" about it would blame the author for the tool. WrappedSource's
            // header states that rule; this is the one caller that can hit it.
            if (at is null)
                throw new GeneratorException(
                    $"FIL-WIRING: the C# compilation reported '{d.Id}: {d.GetMessage()}' at an offset that maps " +
                    "to text this compiler SYNTHESISED, not to the author's @code. The wrapper is wrong. " +
                    "Refusing to emit rather than blame the author for it.");

            _diagnostics.Add(new Diagnostic("FIL0001", "not-csharp",
                $"@code is not valid C#: {d.Id}: {d.GetMessage()}. Phase 3 compiles @code as C# (spec 5) and " +
                "Roslyn refuses this, so there is nothing to translate -- Blazor would refuse to build this " +
                "file too. Refusing to emit rather than emit a module built from code that does not compile.",
                at));
        }
    }

    /// <summary>
    /// NO C# ATTRIBUTE, ANYWHERE UNDER A MEMBER. Section 5 lists types, expressions, statements
    /// and (for @code) fields, methods and records; an attribute is a DECLARATION-LEVEL construct
    /// it never names, and this compiler has no host to give one meaning. The only thing it could
    /// do with one is ERASE it, which is section 10's forbidden mode.
    ///
    /// MEASURED, BEFORE THIS GUARD EXISTED -- all three member paths, exit 0, module written:
    ///     [Microsoft.AspNetCore.Components.CascadingParameter] public int Depth = 0;
    ///         -> `const depth = 0;`   the cascading parameter -- a spec 3 NON-GOAL -- silently gone
    ///     [System.Obsolete] private void Increment() { currentCount++; }   -> `function increment()`
    ///     [System.Obsolete] public int Id { get; set; }  (in a record) -> `{ id: 1, label: 'a' }`
    ///
    /// AND IT IS DECISION 41'S PATTERN FOR THE SIXTH TIME, WHICH IS WHY THE GUARD IS HERE AND NOT
    /// IN FieldDecl. The gate suite's own "cascading parameters" case (Gate/CascadingParameter.razor)
    /// was GREEN throughout: it declares the parameter as a PROPERTY, and Member()'s default arm
    /// refuses properties. So the case passed for a reason that had nothing to do with cascading,
    /// and the identical non-goal one frame over -- the same attribute on a FIELD -- compiled in
    /// silence. Gate/CascadingParameterField.razor is that hole's regression test.
    ///
    /// The walk is DescendantNodesAndSelf, not `member.AttributeLists`: an attribute on a record's
    /// property, on a parameter or on a local function is the same construct in the same @code, and
    /// naming only the shapes a repro happened to use is how the sixth occurrence gets a seventh.
    /// It runs over the FIRST partial only -- the second holds this compiler's own synthesised
    /// markers -- so it can never refuse text the author did not write.
    /// </summary>
    bool CheckNoAttributes(MemberDeclarationSyntax member)
    {
        // THE ONE ADMITTED ATTRIBUTE: [Parameter] on a component-parameter property (single-sourced
        // with ClassifyMember). Admitted only when EVERY attribute on the member is a parameter
        // attribute -- a foreign attribute alongside it still refuses, so the carve-out cannot be a
        // hole through which `[JsonIgnore]` &c. reach a Filament module.
        if (member is PropertyDeclarationSyntax pp && Filament.Subset.ConstructSubset.IsComponentParameter(pp)
            && member.DescendantNodesAndSelf().OfType<AttributeListSyntax>()
                .SelectMany(l => l.Attributes)
                .All(a => Filament.Subset.ConstructSubset.IsParameterAttribute(a)
                          || Filament.Subset.ConstructSubset.IsCascadingParameterAttribute(a)))
            return true;

        if (member.DescendantNodesAndSelf().OfType<AttributeListSyntax>().FirstOrDefault() is not { } list)
            return true;

        Refuse("unsupported-attribute",
            $"the attribute {Trunc(list.ToString())} is not in the C# subset. @code admits FIELDS (state), " +
            "METHODS (behaviour) and RECORDS (row shapes) (spec 5), and an attribute is none of them: it is a " +
            "declaration-level construct whose meaning lives in a HOST -- Blazor's parameter binding, DI, a " +
            "serializer -- and a Filament module has no host. There is nothing to emit for it but silence, and " +
            "`[CascadingParameter] public int Depth = 0;` compiling to `const depth = 0;` is exactly the " +
            "silently-wrong module section 10 forbids. Refusing to emit.",
            list.SpanStart);
        return false;
    }

    void Member(MemberDeclarationSyntax member)
    {
        // The member-KIND decision is single-sourced in Filament.Subset (decisions 53/61).
        if (Filament.Subset.ConstructSubset.ClassifyMember(member) is { } refusal)
        {
            Refuse(refusal.Reason, refusal.Message, member.SpanStart);
            return;
        }

        switch (member)
        {
            case FieldDeclarationSyntax f: FieldDecl(f); break;
            case MethodDeclarationSyntax m: Method(m); break;
            case PropertyDeclarationSyntax p when Filament.Subset.ConstructSubset.IsComputedProperty(p):
                ComputedDecl(p); break;
            case PropertyDeclarationSyntax p: ParamDecl(p); break;
            default:
                throw new GeneratorException(
                    $"FIL-WIRING: ClassifyMember admitted {member.Kind()} but Member() has no case for it. " +
                    "The subset decision and the translator have drifted. Refusing to emit.");
        }
    }

    /// <summary>
    /// A COMPUTED property (decision 160): `private int Active => xs.Where(…).Count();`. Derived
    /// state -- registered here, TRANSLATED late (after marking, like field initialisers), emitted
    /// as `const active = computed(() => …);` in the prologue. computed() is lazy (born dirty), so
    /// declaration order cannot mis-order evaluation.
    /// </summary>
    void ComputedDecl(PropertyDeclarationSyntax p)
    {
        foreach (var mod in p.Modifiers)
            if (!IsAllowedModifier(mod))
            {
                Refuse("unsupported-modifier",
                    $"'{mod.Text}' on a computed property is not in the subset. Refusing to emit.",
                    mod.SpanStart);
                return;
            }

        if (!CheckType(_model.GetTypeInfo(p.Type).Type, p.Type.SpanStart)) return;

        var info = new ComputedInfo
        {
            Name = p.Identifier.Text,
            Js = JsName(p.Identifier.Text),
            Syntax = p,
        };
        _computeds.Add(info);
        _computedsByName[info.Name] = info;
    }

    /// <summary>
    /// A `[Parameter] public T X { get; set; }` — a COMPONENT PARAMETER. It emits NOTHING on its own:
    /// its value comes from the PARENT at the composition site (see TemplateCompiler.EmitComposition),
    /// and a read of it (`@X`) folds to that parent-supplied constant. Standalone (no parent), the
    /// parameter is simply declared and unread. The shape check mirrors a record property's: a scalar
    /// `{ get; set; }` auto-property. Anything else is refused, loud and located.
    /// </summary>
    void ParamDecl(PropertyDeclarationSyntax p)
    {
        if (p.AccessorList is null ||
            p.AccessorList.Accessors.Any(a => a.Body is not null || a.ExpressionBody is not null) ||
            !p.AccessorList.Accessors.Any(a => a.Kind() == SyntaxKind.GetAccessorDeclaration) ||
            !p.AccessorList.Accessors.Any(a => a.Kind() == SyntaxKind.SetAccessorDeclaration))
        {
            Refuse("unsupported-member",
                $"component parameter '{p.Identifier.Text}' is not a `{{ get; set; }}` auto-property. A " +
                "[Parameter] in this subset carries a scalar VALUE supplied by the parent; a computed or " +
                "init-only property has no such value to fold. Refusing to emit.",
                p.Identifier.SpanStart);
            return;
        }

        var type = _model.GetTypeInfo(p.Type).Type;
        var name = p.Identifier.Text;

        // A [CascadingParameter] (decision 134). It is NOT supplied by the immediate parent as an
        // attribute -- it is matched, BY TYPE, against whatever an ancestor <CascadingValue> put in scope.
        // Because the whole composition inlines into one mount(), that ancestor's expression is literally
        // in scope here, so the parameter binds to it directly: no context object, no dictionary, no
        // subscription. Reactivity carries across with it, so a cascaded signal stays live.
        if (Filament.Subset.ConstructSubset.IsCascadingParameter(p))
        {
            if (!CheckType(type, p.Type.SpanStart, allowList: true)) return;

            var key = type!.ToDisplayString();
            if (!_cascades.TryGetValue(key, out var cascaded))
            {
                Refuse("unbound-cascading-parameter",
                    $"the [CascadingParameter] '{name}' has no cascaded '{key}' in scope. A cascade is matched " +
                    "BY TYPE against an enclosing <CascadingValue Value=\"@…\">; with none, the parameter would " +
                    "silently hold the type's default and render it as though it were real data. Refusing to " +
                    "emit rather than cascade nothing.",
                    p.Identifier.SpanStart, "FIL0002");
                return;
            }

            _paramEnv[name] = cascaded.Js;
            if (cascaded.Reactive) _paramReactive.Add(name);

            _params.Add(new ParamInfo { Name = name, Symbol = (IPropertySymbol)_model.GetDeclaredSymbol(p)!, Type = type! });
            _paramsByName[name] = _params[^1];
            return;
        }

        // A ROUTE PARAMETER (decision 163) -- this page's own `@page "/item/{Id:int}"` names it, so its
        // value comes from the URL rather than from a composing parent. It is checked BEFORE the
        // composition branches because it is a different supplier, not a different kind of parameter.
        if (_routeParams.TryGetValue(name, out var seg))
        {
            // THE DECLARED TYPE MUST AGREE WITH THE CONSTRAINT. Blazor requires it too, and gets it wrong
            // at RUN time; here it is a build error. The route's constraint decides what the router
            // converts the segment to -- `:int` produces a JS number -- so a `[Parameter] public string
            // Id` would receive a number and render it as one while the C# said string.
            var want = seg.Constraint switch
            {
                null => SpecialType.System_String,
                "int" => SpecialType.System_Int32,
                "long" => SpecialType.System_Int64,
                "bool" => SpecialType.System_Boolean,
                _ => SpecialType.None,
            };
            if (type is null || type.SpecialType != want)
            {
                Refuse("route-parameter-type",
                    $"the route parameter '{{{seg.Text}{(seg.Constraint is null ? "" : ":" + seg.Constraint)}}}' " +
                    $"captures a '{seg.ClrType}', but '{name}' is declared '{type?.ToDisplayString() ?? "?"}'. " +
                    "The route's constraint is what decides the captured type, so the two must agree -- " +
                    $"declare it `[Parameter] public {seg.ClrType} {name} {{ get; set; }}` or change the " +
                    "constraint. Refusing to emit.",
                    p.Type.SpanStart, "FIL0002");
                return;
            }

            // The signal, and the read that resolves to it. `@Id` in the markup becomes a LIVE effect on
            // this signal for the same reason a reactively-bound parameter does (decision 90): the value
            // really can change under the page, when the router reuses it across /item/7 -> /item/8.
            var js = JsName(name);
            _routeSignals.Add((seg.Text, js, name, p.Identifier.SpanStart));
            _paramEnv[name] = js + ".value";
            _paramReactive.Add(name);

            _params.Add(new ParamInfo
            {
                Name = name,
                Symbol = (IPropertySymbol)_model.GetDeclaredSymbol(p)!,
                Type = type,
            });
            _paramsByName[name] = _params[^1];
            return;
        }

        // AN EVENTCALLBACK PARAMETER -- the child->parent half of composition (decision 130). It is not a
        // value the child holds: at the composition site it ALIASES one of the parent's methods, and the
        // child inlines into the parent's mount(), so raising it IS calling that method. The compiler
        // erases it entirely; nothing about it survives into the emitted module.
        //
        // Admitted ONLY when a parent actually bound it. Standalone -- or composed by a parent that left
        // it out -- there is no method to alias, and a callback that silently never fires is exactly the
        // dead button section 10 forbids, so it is refused rather than wired to nothing.
        if (Filament.Subset.TypeSubset.IsEventCallback(type))
        {
            if (!_paramHandlers.ContainsKey(name))
            {
                Refuse("unbound-event-callback",
                    $"the EventCallback parameter '{name}' is not bound. An EventCallback is admitted only " +
                    "where a composing parent supplies one of its own methods for it (`<Child " +
                    $"{name}=\"@Method\" />`), because that method is all this compiler emits for it -- the " +
                    "child inlines into the parent, so the callback IS the parent's method. Unbound there " +
                    "is nothing to call, and a handler wired to nothing renders correctly and then does " +
                    "nothing for the life of the page. Refusing to emit.",
                    p.Identifier.SpanStart, "FIL0002");
                return;
            }
        }
        // A RENDERFRAGMENT PARAMETER -- the STRUCTURAL half of composition (decision 131). It names the
        // HOLE the composing parent's markup drops into. Like EventCallback it holds no runtime value:
        // the parent's markup subtree is inlined at the child's @ChildContent position, and emitting it
        // is the template compiler's job, not this one's -- see TemplateCompiler.EmitFragment.
        //
        // UNBOUND IS ADMITTED HERE, and that is a real difference from EventCallback rather than an
        // inconsistency. An unbound EventCallback is a DEAD BUTTON -- it renders and then never fires.
        // An unbound RenderFragment renders NOTHING, which is exactly what Blazor does with a null
        // fragment, so admitting it is faithful and refusing it would refuse `<Card Title="x" />`.
        else if (Filament.Subset.TypeSubset.IsRenderFragment(type))
        {
            // nothing to check: the parameter emits nothing on its own, bound or not.
        }
        // The mirror image: the parent bound a METHOD to a parameter that declares a VALUE. Folding a
        // function where a number or a string is meant would render "inc" where the author wrote data.
        else if (_paramHandlers.ContainsKey(name))
        {
            Refuse("unsupported-member",
                $"parameter '{name}' is declared '{type?.ToDisplayString()}' but the composing parent bound " +
                "a METHOD to it. A method is bound to an EventCallback parameter; binding one to a value " +
                "parameter would fold a function name in where a value is meant. Refusing to emit.",
                p.Identifier.SpanStart, "FIL0002");
            return;
        }
        // A GENERIC parameter -- its declared type is the component's own @typeparam (decision 135).
        //
        // Generics ERASE, and they erase for free: a type parameter constrains what may be substituted
        // at compile time, and this compiler resolves every composition at compile time into a scope
        // where the value is simply the PARENT's translated expression. JS has no type to carry, so
        // there is nothing left to emit -- and no monomorphisation to do either, because the child is
        // INLINED at each use site and each site therefore already has its own copy.
        //
        // ADMITTED ONLY WHERE THE PARENT'S EXPRESSION IS ALREADY TYPE-CORRECT, i.e. a REACTIVELY bound
        // parameter -- decision 90's exemption, and for the same reason: `count.value` is a number
        // because the parent's C# said so. A STATICALLY folded T would splice a JS string literal
        // wherever T was substituted, which is the mistranslation #88 refuses for concrete types too
        // (FirstBoundNonStringParameter catches that case). An UNBOUND T has no type at all.
        else if (type is { TypeKind: TypeKind.TypeParameter })
        {
            if (!_paramReactive.Contains(name))
            {
                Refuse("unbound-generic-parameter",
                    $"the generic parameter '{name}' is not bound to a reactive expression by a composing " +
                    "parent. A type parameter carries no type of its own: what makes it faithful is that the " +
                    "PARENT's expression is already type-correct, so it is admitted only where a parent " +
                    "supplied one. Refusing to emit rather than substitute a type nothing determined.",
                    p.Identifier.SpanStart, "FIL0002");
                return;
            }
        }
        else if (!CheckType(type, p.Type.SpanStart, allowList: true)) return;

        _params.Add(new ParamInfo
        {
            Name = p.Identifier.Text,
            Symbol = (IPropertySymbol)_model.GetDeclaredSymbol(p)!,
            Type = type!,
        });
        _paramsByName[p.Identifier.Text] = _params[^1];
    }

    /// <summary>
    /// `record Row { public int Id { get; set; } ... }` -> nothing at all: a record is a SHAPE,
    /// and its instances are object literals (rows.js decision 2). There is no class, no
    /// prototype and no constructor in the emitted module -- Filament ships no machinery whose
    /// only job is to describe machinery.
    /// </summary>
    void Record(RecordDeclarationSyntax rec)
    {
        if (rec.TypeParameterList is { } rtp)
        {
            Refuse("unsupported-generic",
                $"record '{rec.Identifier.Text}' is generic. User-defined generics are a spec 3 NON-GOAL, and " +
                "a record in this subset compiles to an object literal whose property mapping is decided per " +
                "property AT COMPILE TIME (rows.js decision 2) -- a type parameter has no value at that moment " +
                "and JS has nothing to erase it to. Refusing to emit.",
                rtp.SpanStart);
            return;
        }

        var info = new RecordInfo
        {
            Name = rec.Identifier.Text,
            Symbol = (INamedTypeSymbol)_model.GetDeclaredSymbol(rec)!,
        };

        // POSITIONAL record: `record Row(int Id, string Name)` (decision 111). Each primary-constructor
        // parameter is an (init-only) property -- the SAME data shape a body record declares, written
        // shorter -- so it compiles to the SAME object literal. The compiler-generated ctor/Equals/
        // GetHashCode/Deconstruct are simply unused: a read-only shape needs only the literal, and the
        // subset admits neither value-equality nor deconstruction. Construction maps the positional ARGS
        // to these props by order (Expr's RecordLiteral). A positional param is never assigned outside
        // construction, so it is never a signal.
        if (rec.ParameterList is { } pl)
            foreach (var param in pl.Parameters)
            {
                var ptype = _model.GetTypeInfo(param.Type!).Type;
                if (!CheckType(ptype, param.Type!.SpanStart, allowList: false)) return;
                info.Props.Add(new PropInfo
                {
                    Name = param.Identifier.Text,
                    Js = JsName(param.Identifier.Text),
                    Init = DefaultOf(ptype!),
                    Symbol = info.Symbol.GetMembers(param.Identifier.Text).OfType<IPropertySymbol>().First(),
                });
            }

        foreach (var member in rec.Members)
        {
            if (member is not PropertyDeclarationSyntax p)
            {
                Refuse("unsupported-member",
                    $"{Describe(member)} in record '{rec.Identifier.Text}' is not in the subset. A record in this " +
                    "subset is a DATA SHAPE: auto-properties and nothing else, because it compiles to an object " +
                    "literal and a literal has no place for behaviour. Refusing to emit.",
                    member.SpanStart);
                return;
            }

            if (p.AccessorList is null ||
                p.AccessorList.Accessors.Any(a => a.Body is not null || a.ExpressionBody is not null) ||
                !p.AccessorList.Accessors.Any(a => a.Kind() == SyntaxKind.GetAccessorDeclaration) ||
                !p.AccessorList.Accessors.Any(a => a.Kind() == SyntaxKind.SetAccessorDeclaration))
            {
                Refuse("unsupported-member",
                    $"property '{p.Identifier.Text}' is not a `{{ get; set; }}` auto-property. A computed or " +
                    "init-only property has no meaning on an object literal, and a body is behaviour a data " +
                    "shape does not carry. Refusing to emit.",
                    p.Identifier.SpanStart);
                return;
            }

            var type = _model.GetTypeInfo(p.Type).Type;
            if (!CheckType(type, p.Type.SpanStart, allowList: false)) return;

            // A record property's initialiser must be a LITERAL, and this is not fussiness. The
            // construction-site fold DROPS the initialiser when the site assigns the property
            // (`Label = ""` then `row.Label = NextLabel()` -- the "" is a dead store). Dropping a
            // dead store is safe; dropping a CALL is not. So the subset admits only the shape
            // where dropping it cannot change behaviour.
            if (p.Initializer is { } init && init.Value is not LiteralExpressionSyntax)
            {
                Refuse("unsupported-expression",
                    $"the initialiser of '{p.Identifier.Text}' is not a literal. This compiler folds a record's " +
                    "construction site into one object literal (rows.js decision 2), which DROPS an initialiser " +
                    "the site overwrites. That is safe for a literal (a dead store) and unsafe for anything with " +
                    "an effect. Refusing to emit.",
                    init.Value.SpanStart);
                return;
            }

            info.Props.Add(new PropInfo
            {
                Name = p.Identifier.Text,
                Js = JsName(p.Identifier.Text),
                Init = p.Initializer is { } i ? Expr(i.Value) : DefaultOf(type!),
                Symbol = (IPropertySymbol)_model.GetDeclaredSymbol(p)!,
            });
        }

        var dup = info.Props.GroupBy(p => p.Js, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null)
        {
            Refuse("name-collision",
                $"record '{rec.Identifier.Text}' declares {string.Join(" and ", dup.Select(p => $"'{p.Name}'"))}, " +
                $"which both map to the JS property '{dup.Key}'. A record compiles to an object literal and C#'s " +
                "PascalCase members become JS's camelCase ones (both answer keys: `Row.Id` is `row.id`), so these " +
                "two would silently become one. Refusing to emit.",
                rec.Identifier.SpanStart);
            return;
        }

        _records.Add(info);
        _recordsByName[info.Name] = info;
    }

    void FieldDecl(FieldDeclarationSyntax field)
    {
        foreach (var mod in field.Modifiers)
            if (!IsAllowedModifier(mod))
            {
                Refuse("unsupported-modifier",
                    $"'{mod.Text}' on a field is not in the subset. A Filament module's state is plain " +
                    $"module-scope bindings; there is nothing for '{mod.Text}' to mean. Refusing to emit.",
                    mod.SpanStart);
                return;
            }

        var type = _model.GetTypeInfo(field.Declaration.Type).Type;

        // AN ELEMENTREFERENCE FIELD (decision 132) -- the target of an @ref. It is not state and it
        // EMITS NOTHING here: the element it names is created by the TEMPLATE, and @ref makes the
        // template emit it into a const of exactly this name, so `const box = …` is already the
        // declaration. Registered so that a read of it resolves; skipped so it is not declared twice.
        //
        // Admitted only in this shape, and never through CheckType: an ElementReference is not a value
        // §5 can hold, compare or display -- its only faithful surface is FocusAsync() (see
        // ElementRefMethod). ElementReference.Id is deliberately NOT mapped: in Blazor it is a
        // framework-internal GUID with no DOM meaning, so any value emitted for it would be invented.
        if (Filament.Subset.TypeSubset.IsElementReference(type))
        {
            foreach (var v in field.Declaration.Variables)
            {
                if (_model.GetDeclaredSymbol(v) is not IFieldSymbol sym) continue;
                if (v.Initializer is not null)
                {
                    Refuse("unsupported-expression",
                        $"the ElementReference field '{v.Identifier.Text}' has an initialiser. An @ref target is " +
                        "assigned by the TEMPLATE, from the element that captures it; there is no value to " +
                        "initialise it with. Refusing to emit.",
                        v.Identifier.SpanStart);
                    return;
                }
                var er = new FieldInfo
                {
                    Name = v.Identifier.Text, Js = JsName(v.Identifier.Text),
                    Symbol = sym, At = v.Identifier.SpanStart, IsElementRef = true,
                };
                _fields.Add(er);
                _fieldsByName[er.Name] = er;
            }
            return;
        }

        if (!CheckType(type, field.Declaration.Type.SpanStart)) return;

        foreach (var v in field.Declaration.Variables)
        {
            if (_model.GetDeclaredSymbol(v) is not IFieldSymbol symbol) continue;

            var info = new FieldInfo
            {
                Name = v.Identifier.Text, Js = JsName(v.Identifier.Text),
                Symbol = symbol, At = v.Identifier.SpanStart,
            };

            if (Filament.Subset.TypeSubset.ListElement(type!) is not null)
            {
                // rows.js mapping decision (1). A List<T> IS a mutable collection, so it maps to a
                // mutable array; what reactivity needs on TOP is a way to say "the structure
                // changed", which is the version signal. NOT Signal<T[]> with copy-on-write:
                // Run() is Clear() + 1000 Add(), and copy-on-write makes that O(n^2) -- ~500k
                // element copies per #run against a C# List that does none. That is an asymptotic
                // handicap Blazor never pays, landing on C4's headline.
                if (v.Initializer is not { Value: ObjectCreationExpressionSyntax oc })
                {
                    Refuse("unsupported-expression",
                        $"the List<T> field '{v.Identifier.Text}' has no `new List<...>` initialiser. C#'s default " +
                        "for it is null; the array mapping has no null to represent and `const x = null` then " +
                        "`x.push(...)` is a runtime TypeError -- the loud-but-late failure this front end exists " +
                        "to turn into a compile-time answer. Refusing to emit.",
                        v.Identifier.SpanStart);
                    return;
                }
                if (!ListInitializer(oc, info)) return;
            }
            else
            {
                // A field with no initialiser is C#'s default value, and the default must be
                // SPELLED in JS -- `let x;` is `undefined`, not 0, and setText(t, undefined)
                // renders "undefined" where C# renders "0".
                //
                // DEFERRED, NOT TRANSLATED HERE (decision 137). See FieldInfo.InitThunk: a record literal
                // must know whether each property is a signal, and nothing knows that until the marking
                // passes have run. The default has no such dependency, so it is spelled immediately.
                if (v.Initializer is { } init)
                {
                    var initValue = init.Value;
                    info.InitThunk = () => Expr(initValue);
                }
                else info.Init = DefaultOf(type!);
            }

            _fields.Add(info);
            _fieldsByName[info.Name] = info;
        }
    }

    bool ListInitializer(ObjectCreationExpressionSyntax oc, FieldInfo info)
    {
        if (oc.ArgumentList is { Arguments.Count: > 0 })
        {
            Refuse("unsupported-expression",
                "a List<T> constructed with arguments is not in the subset: a capacity or a copy-from-collection " +
                "argument has no counterpart in the array mapping (rows.js decision 1). Refusing to emit.",
                oc.SpanStart);
            return false;
        }

        var elements = (oc.Initializer?.Expressions ?? default).ToList();

        // Whether every element is a literal is SYNTACTIC, so it is decided now; the elements themselves
        // are translated late for decision 137's reason (a record element's properties may be signals).
        var literal = elements.All(e => e is LiteralExpressionSyntax);

        info.InitThunk = () => "[" + string.Join(", ", elements.Select(Expr)) + "]";
        info.List = new ListInfo { LiteralData = literal };
        return true;
    }

    /// <summary>C# default(T), SPELLED. `private int x;` is 0 in C#; the JS binding must say so.</summary>
    static string DefaultOf(ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_Int32 => "0",
        SpecialType.System_Int64 => "0n",   // long's JS home is BigInt (decision 112); default(long) is 0L -> 0n
        SpecialType.System_Single => "0",   // default(float) is 0f -> 0 (exactly representable; decision 113)
        SpecialType.System_Double => "0",
        SpecialType.System_Decimal => "{ m: 0n, s: 0 }",   // default(decimal) is 0m -> the boxed zero (decision 114)
        SpecialType.System_DateTime => "0n",   // default(DateTime) is 0 ticks -> 01/01/0001 00:00:00 (decision 115)
        SpecialType.System_Boolean => "false",
        SpecialType.System_String => "null",
        _ => "null",
    };

    static bool IsAllowedModifier(SyntaxToken mod) => mod.Kind() is
        SyntaxKind.PrivateKeyword or SyntaxKind.ProtectedKeyword or SyntaxKind.PublicKeyword or
        SyntaxKind.InternalKeyword or SyntaxKind.ReadOnlyKeyword;

    /// <summary>Spec 5's type list, resolved through the Compilation. FIL0002's whole job — the
    /// DECISION now lives in Filament.Subset.TypeSubset.Classify (single source, decisions 53/61).
    /// This adapter keeps the Refuse() calls and the `at` location; only the allow/deny table moved.</summary>
    bool CheckType(ITypeSymbol? type, int at, bool allowList = true)
    {
        var records = _recordsByName.Values.Select(r => r.Symbol).ToArray();
        if (Filament.Subset.TypeSubset.Classify(type, records, allowList) is { } refusal)
        {
            Refuse(refusal.Reason, refusal.Message, at, "FIL0002");
            return false;
        }
        return true;
    }

    // ---- methods ------------------------------------------------------------

    void Method(MethodDeclarationSyntax method)
    {
        // `async` is admitted (decision 119): an `async Task` method emits an `async function`, and `await`
        // maps to JS's own await. `override` is admitted for exactly the two init lifecycle methods
        // (decision 156); every OTHER non-access modifier stays refused.
        var isAsync = false;
        var isInit = false;
        foreach (var mod in method.Modifiers)
        {
            if (mod.IsKind(SyntaxKind.AsyncKeyword)) { isAsync = true; continue; }
            if (mod.IsKind(SyntaxKind.OverrideKeyword))
            {
                var name = method.Identifier.Text;
                if (name is "OnInitialized" or "OnInitializedAsync") { isInit = true; continue; }
                // The REST of ComponentBase's lifecycle is refused BY NAME with its reason: there is
                // nothing for it to mean in a module with no re-render pass and no parameter re-set.
                if (name is "OnParametersSet" or "OnParametersSetAsync" or "OnAfterRender"
                    or "OnAfterRenderAsync" or "SetParametersAsync" or "ShouldRender")
                {
                    Refuse("unsupported-lifecycle",
                        $"'{name}' is not in the subset. A Filament module renders through signals -- there is " +
                        "no re-render pass to hook, so only the INIT pair has a faithful meaning: " +
                        "OnInitialized/OnInitializedAsync run once, before the first paint (decision 156). " +
                        "A ROUTE parameter really is re-set on a parameter-only navigation (decision 163), but " +
                        "it is re-set by assigning its signal, and every read of it is already an effect -- so " +
                        "the re-render OnParametersSet exists to request has happened before it could run. " +
                        "Refusing to emit.",
                        method.Identifier.SpanStart);
                    return;
                }
                Refuse("unsupported-modifier",
                    $"'{mod.Text}' on a method is not in the subset (only the OnInitialized/OnInitializedAsync " +
                    "overrides are, decision 156). Refusing to emit.", mod.SpanStart);
                return;
            }
            if (!IsAllowedModifier(mod))
            {
                Refuse("unsupported-modifier",
                    $"'{mod.Text}' on a method is not in the subset. Refusing to emit.", mod.SpanStart);
                return;
            }
        }

        // The init pair's SHAPE is ComponentBase's own: no parameters, void (sync) / async Task. A
        // divergent shape would not override anything in Blazor either -- refuse it here with the fix.
        if (isInit)
        {
            var wantAsync = method.Identifier.Text == "OnInitializedAsync";
            if (method.ParameterList.Parameters.Count != 0 || isAsync != wantAsync)
            {
                Refuse("unsupported-lifecycle",
                    $"'{method.Identifier.Text}' must be exactly ComponentBase's shape: " +
                    (wantAsync ? "`protected override async Task OnInitializedAsync()`"
                               : "`protected override void OnInitialized()`") +
                    ". Refusing to emit.",
                    method.Identifier.SpanStart);
                return;
            }
        }

        // USER-DEFINED GENERICS ARE A SPEC 3 NON-GOAL, AND THIS GUARD IS ON THE DECLARATION
        // RATHER THAN ON THE TYPES IT HAPPENS TO MENTION. Decision 41's pattern, measured:
        // CheckType covers the return type and the parameter types, so `T Echo<T>(T v)` was
        // refused -- and `void Noop<T>() {}` called as `Noop<System.DateTime>()` was NOT. It
        // exited 0 and emitted `function noop() {}` + `noop()`, ERASING the type argument in
        // silence, with System.DateTime -- a type section 5 does not admit -- never reaching
        // CheckType at all. The construct is out of subset whether or not T lands somewhere
        // this compiler already looks, so the guard is on the construct.
        if (method.TypeParameterList is { } tp)
        {
            Refuse("unsupported-generic",
                $"method '{method.Identifier.Text}' is generic. User-defined generics are a spec 3 NON-GOAL. " +
                "JS has no type arguments, so the only thing this compiler could do with one is erase it -- and " +
                "erasing it silently is how `Noop<System.DateTime>()` compiles to `noop()` at exit 0 while " +
                "naming a type section 5 does not admit. Refusing to emit.",
                tp.SpanStart);
            return;
        }

        if (method.Body is null)
        {
            Refuse("unsupported-member",
                $"method '{method.Identifier.Text}' has no block body. Expression-bodied members are not " +
                "in the subset. Refusing to emit.",
                method.Identifier.SpanStart);
            return;
        }

        var returnType = _model.GetTypeInfo(method.ReturnType).Type;
        if (isAsync)
        {
            // An async method returns Task (a void-like async -> `async function` whose Promise JS discards) OR
            // Task<T> (a value-returning async -> `async function` that returns T; `await Compute()` unwraps it).
            // T must be a subset type. Any other async return type is refused. Decisions 119/123.
            var rt = returnType as INamedTypeSymbol;
            var isTask = rt?.ToDisplayString() == "System.Threading.Tasks.Task";
            var isTaskT = rt?.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.Task<TResult>"
                          && rt.TypeArguments.Length == 1;
            if (!isTask && !isTaskT)
            {
                Refuse("unsupported-type",
                    $"async method '{method.Identifier.Text}' must return Task or Task<T> (T a subset type). Refusing to emit.",
                    method.ReturnType.SpanStart);
                return;
            }
            if (isTaskT && !CheckType(rt!.TypeArguments[0], method.ReturnType.SpanStart)) return;   // the awaited value type
        }
        else if (returnType is { SpecialType: not SpecialType.System_Void })
            if (!CheckType(returnType, method.ReturnType.SpanStart)) return;

        var info = new MethodInfo
        {
            Name = method.Identifier.Text,
            Js = JsName(method.Identifier.Text),
            Syntax = method,
            Symbol = _model.GetDeclaredSymbol(method)!,
            At = method.Identifier.SpanStart,
            IsAsync = isAsync,
            IsInit = isInit,
        };

        foreach (var p in method.ParameterList.Parameters)
        {
            var pt = _model.GetTypeInfo(p.Type!).Type;
            // KeyboardEventArgs is a PARAMETER-ONLY admission (decision 159): the DOM listener
            // receives the event, the method declares it, the bridge erases. One parameter total --
            // the listener has exactly one argument to give.
            if (Filament.Subset.TypeSubset.IsKeyboardEventArgs(pt))
            {
                if (method.ParameterList.Parameters.Count != 1)
                {
                    Refuse("unsupported-type",
                        $"'{method.Identifier.Text}' mixes KeyboardEventArgs with other parameters. A keyboard " +
                        "handler takes exactly the event -- the DOM listener has exactly one argument to give. " +
                        "Refusing to emit.",
                        p.SpanStart);
                    return;
                }
                info.EventArg = true;
                info.Parameters.Add(JsName(p.Identifier.Text));
                continue;
            }
            if (!CheckType(pt, p.SpanStart)) return;
            info.Parameters.Add(JsName(p.Identifier.Text));
        }

        // Two locals whose C# names DIFFER but whose JS names collide would silently become
        // one binding. Same-named locals in disjoint scopes are already one name in C#, so
        // they are not a collision -- only distinct C# names that converge are.
        var locals = method.DescendantNodes().OfType<VariableDeclaratorSyntax>().Select(v => v.Identifier.Text)
            .Concat(method.DescendantNodes().OfType<ForEachStatementSyntax>().Select(f => f.Identifier.Text))
            .Distinct(StringComparer.Ordinal)
            .GroupBy(JsName, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (locals is not null)
        {
            Refuse("name-collision",
                $"method '{method.Identifier.Text}' declares {string.Join(" and ", locals.Select(l => $"'{l}'"))}, " +
                $"which both map to the JS binding '{locals.Key}'. Refusing to emit.",
                method.Identifier.SpanStart);
            return;
        }

        _methods.Add(info);
        _methodsByName[info.Name] = info;
    }

    /// <summary>Count @code's own calls to each method, and record the edges. See CallsTo and MethodsInDependencyOrder.</summary>
    /// <summary>The caller is passed in (rather than looked up by name) so a lambda handler's synthetic
    /// method -- which is NOT in _methodsByName -- can still record the @code methods its body calls,
    /// bumping their CallUses correctly (decision 105).</summary>
    void MarkCalls(SyntaxNode method, MethodInfo caller)
    {
        foreach (var inv in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
            if (_model.GetSymbolInfo(inv).Symbol is IMethodSymbol s &&
                _methodsByName.TryGetValue(s.Name, out var callee) &&
                SymbolEqualityComparer.Default.Equals(s.ContainingType, _component))
            {
                callee.CallUses++;
                caller.Callees.Add(callee);
            }
    }

    // ---- reactivity ---------------------------------------------------------

    /// <summary>
    /// THE CONSTRUCTION SITES, and they are the reason rows.js's mapping decision (2) works.
    ///
    ///     Row row = new Row();     <- a fresh, unescaped local
    ///     row.Id = _nextId;        <- construction
    ///     row.Label = NextLabel(); <- construction
    ///     _rows.Add(row);          <- ESCAPE. Anything after this is a write.
    ///
    /// The site is the MAXIMAL RUN of `x.P = e;` statements immediately after the `new`. It
    /// stops at the first statement that is anything else, which is conservative in the only
    /// direction that matters: a statement we do not fold is a statement whose assignments
    /// COUNT, so a mistake here lifts something that did not need lifting -- it never fails
    /// to lift something that did.
    /// </summary>
    readonly Dictionary<SyntaxNode, ConstructionSite> _sites = [];

    sealed class ConstructionSite
    {
        public RecordInfo Record = null!;
        public List<(PropInfo Prop, ExpressionSyntax Value)> Assignments = [];
        public HashSet<StatementSyntax> Folded = [];
    }

    void MarkConstructionSites(SyntaxNode method)
    {
        foreach (var block in method.DescendantNodes().OfType<BlockSyntax>())
            for (var i = 0; i < block.Statements.Count; i++)
            {
                if (block.Statements[i] is not LocalDeclarationStatementSyntax
                    {
                        Declaration.Variables.Count: 1,
                    } d) continue;
                var v = d.Declaration.Variables[0];
                if (v.Initializer?.Value is not ObjectCreationExpressionSyntax oc) continue;
                if (_model.GetTypeInfo(oc).Type is not { } t || RecordOf(t) is not { } rec) continue;
                if (_model.GetDeclaredSymbol(v) is not ILocalSymbol local) continue;

                var site = new ConstructionSite { Record = rec };
                for (var j = i + 1; j < block.Statements.Count; j++)
                {
                    if (block.Statements[j] is not ExpressionStatementSyntax
                        {
                            Expression: AssignmentExpressionSyntax
                            {
                                RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
                                Left: MemberAccessExpressionSyntax ma,
                            } a,
                        } stmt) break;

                    if (_model.GetSymbolInfo(ma.Expression).Symbol is not ILocalSymbol recv ||
                        !SymbolEqualityComparer.Default.Equals(recv, local)) break;
                    if (_model.GetSymbolInfo(ma).Symbol is not IPropertySymbol ps ||
                        Prop(rec, ps) is not { } prop) break;

                    site.Assignments.Add((prop, a.Right));
                    site.Folded.Add(stmt);
                }
                _sites[v] = site;
            }
    }

    RecordInfo? RecordOf(ITypeSymbol t) =>
        _recordsByName.TryGetValue(t.Name, out var r) && SymbolEqualityComparer.Default.Equals(r.Symbol, t) ? r : null;

    static PropInfo? Prop(RecordInfo rec, IPropertySymbol s) =>
        rec.Props.FirstOrDefault(p => SymbolEqualityComparer.Default.Equals(p.Symbol, s));

    /// <summary>
    /// rows.js decision (2)'s escape analysis, applied: "assigned anywhere OTHER THAN its
    /// object's construction site". For a component field the construction site is its own
    /// initialiser, so any assignment reached from a method body marks it. For a record
    /// property it is the run computed above, so those assignments are excluded HERE.
    /// </summary>
    void MarkAssignments(SyntaxNode method)
    {
        var folded = _sites.Values.SelectMany(s => s.Folded).ToHashSet();

        foreach (var node in method.DescendantNodes())
        {
            var target = node switch
            {
                AssignmentExpressionSyntax a => a.Left,
                PostfixUnaryExpressionSyntax p => p.Operand,
                PrefixUnaryExpressionSyntax p when p.Kind() is SyntaxKind.PreIncrementExpression
                    or SyntaxKind.PreDecrementExpression => p.Operand,
                _ => null,
            };
            if (target is null) continue;
            // `arr[i] = v` / `d[k] = v` mutate the FIELD (decision 127): the assigned thing is the element, but the
            // field is what must lift to a signal (its copy-on-write reassignment fires the effects). So the receiver
            // of an element-access target is what gets marked -- not the indexer symbol, which is no field at all.
            // Whether the WHOLE field was the target is remembered separately: only `items = …` makes a List
            // Reassigned (decision 140); `items[i] = …` does not -- that is decision 127's element write.
            var wholeField = target is not ElementAccessExpressionSyntax;
            if (target is ElementAccessExpressionSyntax ea) target = ea.Expression;
            if (node.Ancestors().OfType<ExpressionStatementSyntax>().FirstOrDefault() is { } st &&
                folded.Contains(st)) continue;

            switch (_model.GetSymbolInfo(target).Symbol)
            {
                case IFieldSymbol fs when Field(fs) is { } f:
                    f.AssignedOutsideConstruction = true;
                    if (wholeField && node is AssignmentExpressionSyntax && f.List is { } fl)
                    {
                        fl.Reassigned = true;
                        fl.ReassignSite ??= node.SpanStart;
                    }
                    break;
                case IPropertySymbol ps when PropAnywhere(ps) is { } p:
                    p.AssignedOutsideConstruction = true;
                    break;
            }
        }
    }

    void MarkListMutations(SyntaxNode method)
    {
        foreach (var s in method.DescendantNodes().OfType<StatementSyntax>())
            if (MutatedList(s) is { } f)
                f.List!.Mutated = true;
    }

    /// <summary>
    /// What the template READS -- the other half of the conjunction -- resolved FROM SYMBOLS.
    ///
    /// Phase 3's first cut matched bare identifiers with a regex, which cannot see `row.Label`
    /// at all and cannot tell a field from a local that shadows it. The slots are parsed in
    /// the scope they are written in, so this is exact: `@row.Label` marks the PROPERTY Label,
    /// `@currentCount` marks the FIELD currentCount, and a template that reads neither marks
    /// nothing.
    /// </summary>
    /// <summary>
    /// The targets the TEMPLATE assigns -- today, two-way form binds (decision 138). A bind writes to
    /// what it names, so what it names is "assigned outside construction" in exactly decision 67's
    /// sense, and saying so here is what lets a bound record property or field become a signal.
    ///
    /// Asked of the SEMANTIC MODEL, never of the text: `model.Name` is a record property and `text` is
    /// a field, and only the bound tree can tell them apart.
    /// </summary>
    void MarkTemplateWrites(IEnumerable<IntermediateNode> boundSlots)
    {
        foreach (var node in boundSlots)
        {
            if (!_slotSyntax.TryGetValue(node, out var e)) continue;

            switch (_model.GetSymbolInfo(e).Symbol)
            {
                case IPropertySymbol ps when PropAnywhere(ps) is { } p:
                    p.AssignedOutsideConstruction = true;
                    break;
                case IFieldSymbol fs when Field(fs) is { } f:
                    f.AssignedOutsideConstruction = true;
                    break;
            }
        }
    }

    void MarkTemplateReads(IReadOnlyList<IntermediateNode> slots)
    {
        foreach (var node in slots) MarkReads(SlotSyntax(node));
    }

    /// <summary>Mark every field/prop READ inside one expression as read-by-template.</summary>
    void MarkReads(ExpressionSyntax e)
    {
        foreach (var id in e.DescendantNodesAndSelf())
        {
            if (id is not (IdentifierNameSyntax or MemberAccessExpressionSyntax)) continue;
            switch (_model.GetSymbolInfo(id).Symbol)
            {
                case IFieldSymbol fs when Field(fs) is { } f: f.ReadByTemplate = true; break;
                case IPropertySymbol ps when PropAnywhere(ps) is { } p: p.ReadByTemplate = true; break;
            }
        }
    }

    /// <summary>
    /// A template @if condition reads state the way a slot does, so its reads must count as template
    /// reads -- otherwise a bool read ONLY in `@if (show)` is never lifted and the conditional renders
    /// once. MUST run with MarkTemplateReads (step 2c), BEFORE method bodies and slots are translated
    /// (Body/TranslateSlots), so IsSignal is settled when Expr() runs on the condition.
    /// </summary>
    void MarkConditionReads(ClassDeclarationSyntax regionClass, IReadOnlyList<string> regionMethods)
    {
        foreach (var method in regionMethods)
        {
            var body = FindMethod(regionClass, method);
            foreach (var ifs in body.DescendantNodes().OfType<IfStatementSyntax>())
                MarkReads(ifs.Condition);
            // A @foreach collection is read by the template the same way an @if condition is: a T[]
            // read ONLY by `@foreach (var x in items)` must be lifted to a signal (decision 124), else
            // reassigning it would not re-run list(). A List<T> is unaffected -- it is never a signal.
            foreach (var fe in body.DescendantNodes().OfType<ForEachStatementSyntax>())
                MarkReads(fe.Expression);
        }
    }

    FieldInfo? Field(IFieldSymbol s) =>
        _fieldsByName.TryGetValue(s.Name, out var f) && SymbolEqualityComparer.Default.Equals(f.Symbol, s) ? f : null;

    PropInfo? PropAnywhere(IPropertySymbol s) =>
        _records.SelectMany(r => r.Props).FirstOrDefault(p => SymbolEqualityComparer.Default.Equals(p.Symbol, s));

    // ---- slots --------------------------------------------------------------

    ExpressionSyntax SlotSyntax(IntermediateNode node) => _slotSyntax.TryGetValue(node, out var e)
        ? e
        : throw new GeneratorException(
            "FIL-WIRING: an @expression was planned but never spliced into the compiled source, so it was " +
            "never parsed. This is the TOOL being broken, not the input.");
    readonly Dictionary<IntermediateNode, ExpressionSyntax> _slotSyntax = [];

    void TranslateSlots(IReadOnlyList<IntermediateNode> slots)
    {
        foreach (var node in slots)
        {
            var e = SlotSyntax(node);

            // `@ChildContent` ON ITS OWN is not an expression to translate -- it is the position where a
            // composing parent's markup is inlined (decision 131). Recognised HERE, at the WHOLE-slot
            // level, so that a fragment used as part of a larger expression never reaches this branch and
            // is refused by Identifier() instead: there is no value to concatenate or compare.
            var isFragment = e is IdentifierNameSyntax fid
                && _model.GetSymbolInfo(fid).Symbol is IPropertySymbol fp
                && IsFragmentParameter(fp.Name);
            if (isFragment)
            {
                _slots[node] = new Slot { Js = "/*fragment*/", IsFragment = true };
                continue;
            }

            // A Random-typed slot has no faithful display (decision 146): C# renders the type name
            // ("System.Random"), a JS object would render "[object Object]" -- section 10's silent
            // wrong render. Refused at the slot, where the author's fix (render a drawn number) is obvious.
            if (Filament.Subset.TypeSubset.IsRandom(_model.GetTypeInfo(e).Type))
            {
                Refuse("unsupported-expression",
                    $"'@{Trunc(e.ToString())}' displays a Random itself, which has no faithful display -- C# renders " +
                    "the type name where JS would render \"[object Object]\". Render a number drawn from it instead. " +
                    "Refusing to emit.", e.SpanStart);
                continue;
            }

            _slots[node] = new Slot
            {
                Js = Expr(e),
                Reactive = IsReactive(e),
                ReadsComputed = ReadsAComputed(e),
                IsFloat = _model.GetTypeInfo(e).Type?.SpecialType == SpecialType.System_Single,
                IsDecimal = _model.GetTypeInfo(e).Type?.SpecialType == SpecialType.System_Decimal,
                IsDateTime = _model.GetTypeInfo(e).Type?.SpecialType == SpecialType.System_DateTime,
                TypeDisplay = _model.GetTypeInfo(e).Type?.ToDisplayString(),
                // Asked of the SEMANTIC MODEL, not of the text: `Inc` is a method group iff Roslyn bound
                // it to a method this component declares. The Action marker overload above is what lets
                // it bind at all (decision 130).
                IsMethodGroup = e is IdentifierNameSyntax
                    && _model.GetSymbolInfo(e).Symbol is IMethodSymbol ms
                    && _methodsByName.ContainsKey(ms.Name),
            };
        }
    }

    /// <summary>
    /// Does this expression READ reactive state? That is the whole question at a binding
    /// site: yes -> one effect that owns a Text node; no -> one create-time write and no
    /// subscription at all, because the source can never change (rows.js's @row.Id).
    /// </summary>
    bool IsReactive(ExpressionSyntax e)
    {
        foreach (var n in e.DescendantNodesAndSelf())
        {
            // kvp.Value in a @foreach over a Dictionary compiles to `d.value.get(kvp[0])` (decision 125), a read of
            // the Dict signal -- so it is REACTIVE though no signal appears syntactically. Detected here so the slot
            // becomes an effect and a reused row's value refreshes. kvp.Key is the stable reconcile key, never this.
            if (n is MemberAccessExpressionSyntax { Name.Identifier.Text: "Value" } kv &&
                IsKeyValuePair(kv.Expression) && DictOfForeachVar(kv.Expression) is not null)
                return true;

            if (n is not (IdentifierNameSyntax or MemberAccessExpressionSyntax)) continue;
            switch (_model.GetSymbolInfo(n).Symbol)
            {
                case IFieldSymbol fs when Field(fs) is { IsSignal: true }: return true;
                case IPropertySymbol ps when PropAnywhere(ps) is { IsSignal: true }: return true;
                // A computed IS reactive by construction (decision 160): it re-runs on its deps.
                case IPropertySymbol ps when _computedsByName.ContainsKey(ps.Name): return true;
                // A bound [Parameter] a composition parent wired to a REACTIVE expression: the child's
                // @Name is a live read of the parent's signal (decision 90). The reactivity is the
                // PARENT's fact, carried in by BindParameters -- the child's own tables cannot see it.
                case IPropertySymbol ps when _paramReactive.Contains(ps.Name): return true;
            }
        }
        return false;
    }

    // ---- emission -----------------------------------------------------------

    /// <summary>
    /// MODULE scope. rows.js mapping decision (4), which states the rule itself: "hoisting
    /// immutable literal lists to module scope is a generator-level constant-folding decision
    /// and changes nothing about the work done per row". Immutable AND literal, both.
    ///
    /// The LABELS are emphatically not here, and that is the point of decision (4): they are
    /// generated per row by the LCG, 3 draws and a three-part concatenation each, 3000 + 1000
    /// per #run, exactly as Blazor does them. Hoisting or interning one is the cheat this POC
    /// exists not to commit.
    /// </summary>
    public List<string> EmitModule()
    {
        var lines = new List<string>();
        foreach (var f in _fields)
            if (f.List is { Hoisted: true })
                lines.Add($"const {f.Js} = {f.Init};");
        return lines;
    }

    /// <summary>
    /// mount() scope: the state declarations, then the methods that survive as functions.
    ///
    /// FIELDS IN SOURCE ORDER, because a field initialiser may read an earlier field and JS
    /// `const` does not hoist. METHODS IN DEPENDENCY ORDER -- see MethodsInDependencyOrder.
    /// </summary>
    public List<string> EmitPrologue(IReadOnlySet<string> inlined)
    {
        var lines = new List<string>();

        // ROUTE PARAMETERS FIRST (decision 163), because Blazor sets a component's parameters BEFORE
        // anything else runs on it: a field initialiser or an OnInitialized body may read one, and it has
        // to already hold the URL's value when it does.
        foreach (var (routeName, js, _, _) in _routeSignals)
        {
            _primitives.Add("signal");
            lines.Add($"const {js} = signal(__route.{routeName});");
        }

        foreach (var f in _fields)
        {
            if (f.List is { Hoisted: true }) continue;

            // An @ref target declares NOTHING here: the TEMPLATE emits `const box = document.createElement(…)`
            // and that IS its declaration (decision 132). Emitting one here too produced `const box = ;` --
            // invalid JS, caught by generating the slice's own baseline rather than by reasoning about it.
            if (f.IsElementRef) continue;

            // A reassigned-never-mutated List is an ordinary signal (decision 140): it must NOT take this
            // branch's plain-const + version emission -- it falls through to the IsSignal path below and
            // becomes `const items = signal([...])`, the same declaration a reassigned T[] gets.
            if (f.List is { } li && !f.IsSignal)
            {
                lines.Add($"const {f.Js} = {f.Init};");
                if (!li.Mutated) continue;

                // rows.js decision (1): the array is the collection, the signal is the only
                // thing reactivity needs on top of it -- "the structure changed".
                _primitives.Add("signal");
                lines.Add($"const {li.Version} = signal(0);");
                lines.Add("");
                lines.Add($"function {li.Changed}() {{");
                lines.Add($"  {li.Version}.value++;");
                lines.Add("}");
                lines.Add("");
                continue;
            }

            if (f.IsSignal)
            {
                _primitives.Add("signal");
                lines.Add($"const {f.Js} = signal({f.Init});");
            }
            else if (f.AssignedOutsideConstruction)
            {
                // Mutated, but nothing reactive reads it: a plain binding. rows.js's _nextId
                // and _seed are exactly this, and keeping them plain is what stops a signal
                // being allocated for state no effect can ever observe.
                lines.Add($"let {f.Js} = {f.Init};");
            }
            else
            {
                lines.Add($"const {f.Js} = {f.Init};");
            }
        }

        // COMPUTEDS after the fields they read, before the methods that may read them. computed()
        // is LAZY (born dirty; the first .value read evaluates), so source order here can never
        // mis-order evaluation -- the closure captures bindings, not values.
        foreach (var c in _computeds)
        {
            _primitives.Add("computed");
            lines.Add($"const {c.Js} = computed(() => {c.BodyJs});");
        }

        foreach (var m in MethodsInDependencyOrder())
        {
            if (inlined.Contains(m.Name)) continue;

            // `function`, not `const f = () =>`, and that is a correctness point rather than a
            // style one: C# methods are mutually visible regardless of declaration order, and
            // only a function DECLARATION hoists.
            if (lines.Count > 0 && lines[^1].Length > 0) lines.Add("");
            lines.Add($"{(m.IsAsync ? "async " : "")}function {m.Js}({string.Join(", ", m.Parameters)}) {{");
            foreach (var l in _bodies[m.Name]) lines.Add("  " + l);
            lines.Add("}");
        }

        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    /// <summary>
    /// CALLEES BEFORE CALLERS: a depth-first walk of the call graph in source order, emitting
    /// each method after everything it calls.
    ///
    /// This is NOT semantics -- function declarations hoist, so any order runs. It is the
    /// order a reader can follow without scrolling forward, and it is the order rows.js emits
    /// (next, nextLabel, addRow, clear, run, update, swapRows) from a source that declares
    /// Clear LAST. Source order would put run() before the clear() it calls; the DFS puts the
    /// definition first, which is what the answer key does and the only rule that reproduces
    /// it. Cycles are cut at the first repeat, so a recursive pair still emits once each.
    /// </summary>
    IEnumerable<MethodInfo> MethodsInDependencyOrder()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var order = new List<MethodInfo>();

        void Visit(MethodInfo m)
        {
            if (!seen.Add(m.Name)) return;
            foreach (var c in m.Callees) Visit(c);
            order.Add(m);
        }

        foreach (var m in _methods) Visit(m);
        return order;
    }

    /// <summary>The translated statements of a method body, for inlining at a handler site.</summary>
    public List<string> InlineBody(string method) => _bodies[method];

    /// <summary>
    /// Whether a handler body may perform MORE THAN ONE reactive write -- the only thing
    /// batch() exists to coalesce.
    ///
    /// THE RULE IS READ OFF BOTH ANSWER KEYS, which disagree on the surface:
    ///   counter.js:  "No batch(): the body performs exactly one write, so there is nothing
    ///                 to coalesce and a batch would only add a try/finally."
    ///   rows.js (3): "Every @onclick handler body runs inside batch()."
    /// Both are the same rule. Counter's Increment writes once; every Rows handler writes many
    /// times (Run bumps the version 1001 times, and rows.js's own note is that without batch
    /// each bump flushes a full reconcile).
    ///
    /// Conservative by construction: a write inside a LOOP counts as many even though it is
    /// one site, a call carries the callee's writes, and anything this cannot PROVE is exactly
    /// one write gets the batch.
    /// </summary>
    public bool MayWriteMoreThanOnce(string method) => CountWrites(_methodsByName[method], []) > 1;

    /// <summary>An `async Task` handler (decision 119): when inlined into a listen(), its arrow must be
    /// `async () => { … }` so the `await` inside is legal JS.</summary>
    public bool IsAsyncHandler(string method) => _methodsByName.TryGetValue(method, out var m) && m.IsAsync;

    // True when the nearest enclosing method or lambda is `async` -- the only place `await` is valid (decision 119).
    static bool InAsyncContext(SyntaxNode node)
    {
        foreach (var a in node.Ancestors())
            switch (a)
            {
                case MethodDeclarationSyntax m: return m.Modifiers.Any(x => x.IsKind(SyntaxKind.AsyncKeyword));
                case ParenthesizedLambdaExpressionSyntax pl: return pl.Modifiers.Any(x => x.IsKind(SyntaxKind.AsyncKeyword));
                case SimpleLambdaExpressionSyntax sl: return sl.Modifiers.Any(x => x.IsKind(SyntaxKind.AsyncKeyword));
            }
        return false;
    }

    int CountWrites(MethodInfo m, HashSet<string> seen)
    {
        if (!seen.Add(m.Name)) return 2; // recursion: cannot prove "once". Batch it.

        var n = 0;
        foreach (var node in m.Block!.DescendantNodes())
        {
            var writes = 0;

            var target = node switch
            {
                AssignmentExpressionSyntax a => a.Left,
                PostfixUnaryExpressionSyntax p => p.Operand,
                PrefixUnaryExpressionSyntax p when p.Kind() is SyntaxKind.PreIncrementExpression
                    or SyntaxKind.PreDecrementExpression => p.Operand,
                _ => null,
            };
            // `arr[i] = v` / `d[k] = v` is a copy-on-write reassignment of the field (decision 127): count it
            // against the RECEIVER signal, so two element writes batch like any other pair (decision 68).
            if (target is ElementAccessExpressionSyntax ea) target = ea.Expression;
            if (target is not null)
                switch (_model.GetSymbolInfo(target).Symbol)
                {
                    case IFieldSymbol fs when Field(fs) is { IsSignal: true }: writes = 1; break;
                    case IPropertySymbol ps when PropAnywhere(ps) is { IsSignal: true }: writes = 1; break;
                }

            // A structural mutation bumps the version signal, which is a write like any other.
            if (node is StatementSyntax st && MutatedList(st) is not null) writes = 1;

            // A call into the component's own code carries that method's writes with it.
            if (node is InvocationExpressionSyntax inv &&
                _model.GetSymbolInfo(inv).Symbol is IMethodSymbol called &&
                _methodsByName.TryGetValue(called.Name, out var callee) &&
                SymbolEqualityComparer.Default.Equals(called.ContainingType, _component))
                writes = CountWrites(callee, seen);

            if (writes == 0) continue;

            // One site inside a loop is many writes at runtime. This is the case that makes
            // Rows' Update() -- a single `Label +=` inside a for -- batch.
            foreach (var a in node.Ancestors())
            {
                if (a == m.Syntax) break;
                if (a is ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax)
                {
                    writes = 2;
                    break;
                }
            }

            n += writes;
            if (n > 1) return n;
        }
        return n;
    }

    // ---- statements ---------------------------------------------------------

    /// <summary>
    /// A block, with the version bumps placed.
    ///
    /// THE BUMP RULE, and where it comes from. rows.js decision (1): "every mutating operation
    /// bumps it ... a mechanical rule a generator applies per mutation site". Applied
    /// literally that emits TWO bumps for swapRows' two index assignments; the answer key
    /// emits ONE, and says why on the line itself: "One logical mutation; inside a batch the
    /// bump count is unobservable anyway, since the reconcile happens once when the batch
    /// closes."
    ///
    /// So the rule implemented here is: a bump after every mutating statement, and a MAXIMAL
    /// RUN of consecutive mutating statements emits ONE. That is redundant-store elimination
    /// on the version signal -- N stores with no read between them are one store -- which is a
    /// peephole, not an insight. It reproduces all three of the key's shapes (push+bump,
    /// splice+bump inside the loop, two swaps and one bump).
    ///
    /// DISCLOSED: the key's PROSE says per-site and the key's CODE says per-run. They cannot
    /// both be followed. This follows the code, because the code is the artifact and decisions
    /// 21/51 make the artifact the reference -- and because the key's own comment states the
    /// justification for the run.
    /// </summary>
    List<string> Body(BlockSyntax block)
    {
        var lines = new List<string>();
        var pending = new List<FieldInfo>();

        void Flush()
        {
            foreach (var f in pending) lines.Add($"{f.List!.Changed}();");
            pending.Clear();
        }

        foreach (var s in block.Statements)
        {
            if (_sites.Values.Any(site => site.Folded.Contains(s))) continue; // folded into the literal

            var mutated = MutatedList(s);
            if (mutated is null) Flush();
            lines.AddRange(Statement(s));
            if (mutated is not null && !pending.Contains(mutated)) pending.Add(mutated);
        }

        Flush();
        return lines;
    }

    /// <summary>The List&lt;T&gt; field this statement structurally mutates, or null.</summary>
    FieldInfo? MutatedList(StatementSyntax s)
    {
        if (s is not ExpressionStatementSyntax e) return null;
        return e.Expression switch
        {
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax ma }
                when ma.Name.Identifier.Text is "Add" or "RemoveAt" or "Clear" => ListReceiver(ma.Expression),
            AssignmentExpressionSyntax { Left: ElementAccessExpressionSyntax ea } => ListReceiver(ea.Expression),
            _ => null,
        };
    }

    FieldInfo? ListReceiver(ExpressionSyntax e) =>
        _model.GetSymbolInfo(e).Symbol is IFieldSymbol fs && Field(fs) is { List: not null } f ? f : null;

    /// <summary>A LOCAL whose type is List&lt;T&gt; or T[] (decision 161): the same JS array as a
    /// field's -- Deserialize's result is the witness. Locality was never the mapping's argument.</summary>
    bool IsLocalList(ExpressionSyntax e) =>
        _model.GetSymbolInfo(e).Symbol is ILocalSymbol { Type: { } lt } &&
        (Filament.Subset.TypeSubset.ListElement(lt) ?? Filament.Subset.TypeSubset.ArrayElement(lt)) is not null;

    // A field whose type is a single-rank array (decision 117). Its indexer and .Length are the JS array's own.
    bool IsArrayReceiver(ExpressionSyntax e) =>
        _model.GetSymbolInfo(e).Symbol is IFieldSymbol { Type: IArrayTypeSymbol { Rank: 1 } };

    // The FieldInfo an element-access receiver names, or null. Used to check IsSignal on the array/Dict a
    // `arr[i] = v` / `d[k] = v` mutates (decision 127) -- the copy-on-write write is admitted only if it fires.
    FieldInfo? FieldOfReceiver(ExpressionSyntax e) =>
        _model.GetSymbolInfo(e).Symbol is IFieldSymbol fs ? Field(fs) : null;

    // A T[] literal's elements -> a JS array literal, in order.
    string ArrayLiteral(InitializerExpressionSyntax init) =>
        "[" + string.Join(", ", init.Expressions.Select(Expr)) + "]";

    // A field whose type is a Dictionary<K,V> (decision 118). Its indexer is `.get`, .Count is `.size`.
    bool IsDictReceiver(ExpressionSyntax e) =>
        _model.GetSymbolInfo(e).Symbol is IFieldSymbol fs && Filament.Subset.TypeSubset.DictionaryTypes(fs.Type).Key is not null;

    // An expression typed KeyValuePair<K,V> (decision 125): a @foreach over a Dictionary iterates these, and the
    // Map-spread source `[...d.value]` materialises each as a [k, v] pair -- so .Key is [0] and .Value is a lookup.
    bool IsKeyValuePair(ExpressionSyntax e) =>
        _model.GetTypeInfo(e).Type?.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.KeyValuePair<TKey, TValue>";

    // An expression typed IGrouping<K,T> (decision 128): GroupBy yields these, modelled as a JS array of the group's
    // elements with a `.key` property. `.Key` -> `.key` (MemberAccess); every sequence op flows through the array path.
    bool IsGrouping(ExpressionSyntax e) =>
        _model.GetTypeInfo(e).Type?.OriginalDefinition.ToDisplayString() == "System.Linq.IGrouping<TKey, TElement>";

    // The JS signal name of the Dictionary a @foreach loop variable `kvp` iterates, or null. The variable is
    // declared BY the ForEachStatement, so its declaring syntax gives back the foreach whose collection is the
    // Dict field. Used to compile `kvp.Value` to the reactive `d.value.get(kvp[0])` lookup (decision 125).
    string? DictOfForeachVar(ExpressionSyntax kvp)
    {
        if (_model.GetSymbolInfo(kvp).Symbol is not ILocalSymbol local ||
            local.DeclaringSyntaxReferences is not [var r, ..] ||
            r.GetSyntax() is not ForEachStatementSyntax fe ||
            _model.GetSymbolInfo(fe.Expression).Symbol is not IFieldSymbol dfs || Field(dfs) is not { } df ||
            Filament.Subset.TypeSubset.DictionaryTypes(df.Symbol.Type).Key is null)
            return null;
        return df.Js;
    }

    // `new Dictionary<K,V>()` / `{ {k,v}, … }` -> a JS Map. The collection initialiser's elements are complex
    // element initialisers `{k, v}` (each an InitializerExpression of two expressions) -> `[k, v]` pairs.
    string DictionaryLiteral(ObjectCreationExpressionSyntax oc)
    {
        if (oc.ArgumentList is { Arguments.Count: > 0 })
            return Refuse("unsupported-expression",
                "a Dictionary constructed with arguments (a capacity or a copy-from) is not in the subset. Refusing to emit.",
                oc.SpanStart);
        if (oc.Initializer is null) return "new Map()";
        var entries = new List<string>();
        foreach (var e in oc.Initializer.Expressions)
        {
            if (e is not InitializerExpressionSyntax { Expressions.Count: 2 } kv)
                return Refuse("unsupported-expression",
                    "a Dictionary initialiser element must be a `{ key, value }` pair. Refusing to emit.", e.SpanStart);
            entries.Add($"[{Expr(kv.Expressions[0])}, {Expr(kv.Expressions[1])}]");
        }
        return $"new Map([{string.Join(", ", entries)}])";
    }

    List<string> Statement(StatementSyntax s)
    {
        // The statement-KIND decision is single-sourced in Filament.Subset (decisions 53/61).
        // Validate first; a validated statement is guaranteed to have a case below.
        if (Filament.Subset.ConstructSubset.ClassifyStatement(s) is { } refusal)
        {
            Refuse(refusal.Reason, refusal.Message, s.SpanStart);
            return [];
        }

        switch (s)
        {
            case LocalDeclarationStatementSyntax d:
            {
                var type = _model.GetTypeInfo(d.Declaration.Type).Type;
                if (!CheckType(type, d.Declaration.Type.SpanStart)) return [];

                var lines = new List<string>();
                foreach (var v in d.Declaration.Variables)
                {
                    // A CONSTRUCTION SITE collapses into one object literal: `Row row = new Row();
                    // row.Id = ...; row.Label = ...;` is ONE expression in JS, and it has to be, or
                    // Row.Id would be "assigned in AddRow" and become a signal (rows.js decision 2).
                    var init = _sites.TryGetValue(v, out var site)
                        ? ObjectLiteral(site)
                        : v.Initializer is { } i ? Expr(i.Value) : DefaultOf(type!);

                    // `const` when nothing assigns it again, `let` otherwise. C# locals are
                    // assignable unless marked const, so the blanket `let` was safe -- but a
                    // never-reassigned binding IS a const, both keys say so, and a later
                    // assignment to a JS const is a TypeError, i.e. exactly the loud-but-late
                    // failure this front end exists to turn into a compile-time answer. The
                    // analysis is what makes `const` safe rather than optimistic.
                    var keyword = Reassigned(v) ? "let" : "const";
                    lines.Add($"{keyword} {JsName(v.Identifier.Text)} = {init};");
                }
                return lines;
            }

            case ExpressionStatementSyntax e when ListMutation(e.Expression) is { } lines:
                return lines;

            case ExpressionStatementSyntax e:
                return [$"{Expr(e.Expression)};"];

            case IfStatementSyntax i:
            {
                var lines = new List<string> { $"if ({Expr(i.Condition)}) {{" };
                lines.AddRange(Nest(i.Statement));
                if (i.Else is { } els)
                {
                    lines.Add("} else {");
                    lines.AddRange(Nest(els.Statement));
                }
                lines.Add("}");
                return lines;
            }

            case ForStatementSyntax f:
            {
                var init = f.Declaration is { } decl
                    ? "let " + string.Join(", ", decl.Variables.Select(v =>
                        $"{JsName(v.Identifier.Text)} = {(v.Initializer is { } i ? Expr(i.Value) : "0")}"))
                    : string.Join(", ", f.Initializers.Select(Expr));
                var cond = f.Condition is { } c ? Expr(c) : "";
                var inc = string.Join(", ", f.Incrementors.Select(Expr));

                var lines = new List<string> { $"for ({init}; {cond}; {inc}) {{" };
                lines.AddRange(Nest(f.Statement));
                lines.Add("}");
                return lines;
            }

            case ForEachStatementSyntax fe:
            {
                var lines = new List<string>
                    { $"for (const {JsName(fe.Identifier.Text)} of {Expr(fe.Expression)}) {{" };
                lines.AddRange(Nest(fe.Statement));
                lines.Add("}");
                return lines;
            }

            case WhileStatementSyntax w:
            {
                var lines = new List<string> { $"while ({Expr(w.Condition)}) {{" };
                lines.AddRange(Nest(w.Statement));
                lines.Add("}");
                return lines;
            }

            case DoStatementSyntax dw:
            {
                var lines = new List<string> { "do {" };
                lines.AddRange(Nest(dw.Statement));
                lines.Add($"}} while ({Expr(dw.Condition)});");
                return lines;
            }

            case BreakStatementSyntax:
                return ["break;"];

            // switch with constant case labels + default (pattern/when labels refused upstream in
            // ClassifyStatement). Each case's `break` is a BreakStatementSyntax, emitted like any statement.
            case SwitchStatementSyntax sw:
            {
                var lines = new List<string> { $"switch ({Expr(sw.Expression)}) {{" };
                foreach (var section in sw.Sections)
                {
                    foreach (var label in section.Labels)
                        lines.Add(label is CaseSwitchLabelSyntax cl ? $"case {Expr(cl.Value)}:" : "default:");
                    foreach (var stmt in section.Statements)
                        lines.AddRange(Nest(stmt));
                }
                lines.Add("}");
                return lines;
            }

            case TryStatementSyntax t:
            {
                var lines = new List<string> { "try {" };
                lines.AddRange(Nest(t.Block));
                foreach (var c in t.Catches)
                {
                    // JS bindingless `catch {` when C# names no variable; `catch (e) {` when it does.
                    var id = c.Declaration?.Identifier.ValueText;
                    lines.Add(string.IsNullOrEmpty(id) ? "} catch {" : $"}} catch ({JsName(id)}) {{");
                    lines.AddRange(Nest(c.Block));
                }
                if (t.Finally is { } fin) { lines.Add("} finally {"); lines.AddRange(Nest(fin.Block)); }
                lines.Add("}");
                return lines;
            }

            case ThrowStatementSyntax th:
                // A CAUGHT throw is faithful (both C# and JS unwind to the catch). An UNCAUGHT throw is a
                // disclosed edge: Blazor's error boundary catches it, a Filament module propagates to the
                // host -- exceptional-path only, like div-by-zero. `throw;` (rethrow) keeps its bare form.
                return [th.Expression is null ? "throw;" : $"throw {Expr(th.Expression)};"];

            case LockStatementSyntax l:
            {
                // JS is SINGLE-THREADED: a lock provides mutual exclusion that can never be contended, so
                // it is a no-op. Emit the body in a bare block (C#'s lock body is block-scoped); the lock
                // object is not evaluated. Dropping a no-op is not silent behaviour loss -- there is no
                // behaviour to lose in a single-threaded runtime.
                var lines = new List<string> { "{" };
                lines.AddRange(Nest(l.Statement));
                lines.Add("}");
                return lines;
            }

            case ReturnStatementSyntax r:
                return [r.Expression is null ? "return;" : $"return {Expr(r.Expression)};"];

            case BlockSyntax b:
                return Body(b);

            default:
                throw new GeneratorException(
                    $"FIL-WIRING: ClassifyStatement admitted {s.Kind()} but Statement() has no case for it. " +
                    "The subset decision and the translator have drifted. Refusing to emit.");
        }
    }

    /// <summary>Is this local assigned again after its declaration? Decides `const` vs `let`.</summary>
    bool Reassigned(VariableDeclaratorSyntax v)
    {
        if (_model.GetDeclaredSymbol(v) is not ILocalSymbol local) return true;
        var method = v.Ancestors().OfType<MethodDeclarationSyntax>().First();

        foreach (var node in method.DescendantNodes())
        {
            var target = node switch
            {
                AssignmentExpressionSyntax a => a.Left,
                PostfixUnaryExpressionSyntax p => p.Operand,
                PrefixUnaryExpressionSyntax p when p.Kind() is SyntaxKind.PreIncrementExpression
                    or SyntaxKind.PreDecrementExpression => p.Operand,
                _ => null,
            };
            if (target is null) continue;
            if (SymbolEqualityComparer.Default.Equals(_model.GetSymbolInfo(target).Symbol, local)) return true;
        }
        return false;
    }

    /// <summary>
    /// The construction site, folded. Property ORDER is the ASSIGNMENT order, not the
    /// declaration order, because that is the evaluation order the C# has and an object
    /// literal evaluates its values in source order: rows.js's own note on AddRow is "Field
    /// order matters and is preserved: Id is read from _nextId, THEN Label is drawn (3 LCG
    /// draws), THEN _nextId advances". A property the site never assigns keeps its declared
    /// initialiser.
    /// </summary>
    string ObjectLiteral(ConstructionSite site)
    {
        var parts = new List<string>();
        foreach (var (prop, value) in site.Assignments)
            parts.Add($"{prop.Js}: {Wrap(prop, Expr(value))}");
        foreach (var p in site.Record.Props)
            if (site.Assignments.All(a => a.Prop != p))
                parts.Add($"{p.Js}: {Wrap(p, p.Init)}");
        return "{ " + string.Join(", ", parts) + " }";
    }

    /// <summary>An INLINE `new Row(...)` / `new Row { … }` (decision 111) -> one object literal, in property
    /// declaration order. Positional args bind to props by CONSTRUCTOR ORDER (which is the order Record()
    /// added them); object-initialiser assignments bind by NAME. A prop neither positioned nor initialised
    /// falls to its declared default. Signal props (a body record read+assigned elsewhere) are wrapped, exactly
    /// as the construction-site fold does -- so an inline construction and a folded one agree byte for byte.</summary>
    string RecordLiteral(ObjectCreationExpressionSyntax oc, RecordInfo rec)
    {
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);

        if (oc.ArgumentList is { } al)
            for (var i = 0; i < al.Arguments.Count && i < rec.Props.Count; i++)
                byName[rec.Props[i].Js] = Wrap(rec.Props[i], Expr(al.Arguments[i].Expression));

        if (oc.Initializer is { } init)
            foreach (var e in init.Expressions)
                if (e is AssignmentExpressionSyntax { Left: IdentifierNameSyntax id } asg &&
                    rec.Props.FirstOrDefault(p => p.Name == id.Identifier.Text) is { } p)
                    byName[p.Js] = Wrap(p, Expr(asg.Right));

        var parts = rec.Props.Select(p =>
            $"{p.Js}: {(byName.TryGetValue(p.Js, out var v) ? v : Wrap(p, p.Init))}");
        return "{ " + string.Join(", ", parts) + " }";
    }

    string Wrap(PropInfo p, string js)
    {
        if (!p.IsSignal) return js;
        _primitives.Add("signal");
        return $"signal({js})";
    }

    /// <summary>
    /// `_rows.Add(row)` -> `_rows.push(row)`, `_rows.RemoveAt(i)` -> `_rows.splice(i, 1)`.
    /// Statement position ONLY: these are the two List operations spec 5 admits, both return
    /// void in C#, and the version bump Body() places after them has nowhere to go inside an
    /// expression. RemoveAt from the tail is O(1) in JS exactly as it is in C#.
    /// </summary>
    List<string>? ListMutation(ExpressionSyntax e)
    {
        if (e is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax ma } inv) return null;
        if (ListReceiver(ma.Expression) is null) return null;

        var recv = Expr(ma.Expression);
        var args = inv.ArgumentList.Arguments;
        switch (ma.Name.Identifier.Text)
        {
            case "Add" when args.Count == 1:
                return [$"{recv}.push({Expr(args[0].Expression)});"];
            case "RemoveAt" when args.Count == 1:
                return [$"{recv}.splice({Expr(args[0].Expression)}, 1);"];
            case "Clear" when args.Count == 0:
                // Empty the array IN PLACE (rows.js maps a List<T> to a live array; length = 0 clears it),
                // then MutatedList's version bump re-runs the list() -- so @foreach reconciles to empty.
                return [$"{recv}.length = 0;"];
            default:
                Refuse("unsupported-call",
                    $"'{Trunc(inv.ToString())}' is not one of the List<T> operations in the subset. Section 5 " +
                    "admits indexing, .Count, .Add, .RemoveAt and .Clear. Refusing to emit.",
                    inv.SpanStart);
                return [];
        }
    }

    List<string> Nest(StatementSyntax s) =>
        (s is BlockSyntax b ? Body(b) : Statement(s)).Select(l => "  " + l).ToList();

    // ---- expressions --------------------------------------------------------

    /// <summary>Translate an expression, then apply any IMPLICIT NUMERIC CONVERSION C# performed on it that
    /// crosses JS's BigInt boundary (decision 112). C# widens int->long and long->double silently; JS cannot
    /// mix BigInt and number, so the widening must be spelled: an int promoted to long becomes `BigInt(x)`, a
    /// long promoted to double becomes `Number(x)`. A literal is skipped -- NumericLiteral already emits it in
    /// its converted form (`5` in a long context is `5n`, not `BigInt(5)`). No conversion crosses the boundary
    /// for the pre-#112 types (int and double are both JS number), so this is inert for every existing app.</summary>
    string Expr(ExpressionSyntax e)
    {
        var js = ExprCore(e);
        if (e is LiteralExpressionSyntax) return js;

        var ti = _model.GetTypeInfo(e);
        var from = ti.Type?.SpecialType;
        var to = ti.ConvertedType?.SpecialType;

        // FLOAT arithmetic rounds to single precision at EVERY operation (decision 113): C#'s float/float is
        // computed then rounded to float32, and `(a+b)*c` rounds the inner sum first. Math.fround around each
        // arithmetic op reproduces that exactly. Only OPERATIONS are wrapped -- a read or literal is already a
        // frounded value, and a comparison's result is bool, not Single, so this never fires on those.
        if (from == SpecialType.System_Single && e is BinaryExpressionSyntax or PrefixUnaryExpressionSyntax)
            return $"Math.fround({js})";

        if (from == SpecialType.System_Int32 && to == SpecialType.System_Int64) return $"BigInt({js})";
        if (from == SpecialType.System_Int64 && to == SpecialType.System_Double) return $"Number({js})";

        // int -> decimal (decision 114): C# widens the int to a decimal; JS must box it as { m, s }. A LITERAL
        // in a decimal context is already the object (NumericLiteral), so this fires only on a non-literal int
        // (a field/local/expression) used where a decimal is expected -- e.g. `price * quantity`.
        if (from == SpecialType.System_Int32 && to == SpecialType.System_Decimal)
        {
            DecimalHelpers.Add("decFromInt");
            return $"__decFromInt({js})";
        }
        return js;
    }

    string ExprCore(ExpressionSyntax e)
    {
        // Expression-FORM decision single-sourced in Filament.Subset (decisions 53/61). Validate the
        // current node first; a blessed form is guaranteed a case below. Call/member/name refusals
        // INSIDE a blessed form still happen in Invocation()/MemberAccess()/Identifier().
        if (Filament.Subset.ConstructSubset.ClassifyExpression(e, _model) is { } refusal)
        {
            Refuse(refusal.Reason, refusal.Message, e.SpanStart);
            return "/*refused*/";
        }

        switch (e)
        {
            case LiteralExpressionSyntax lit:
                return Literal(lit);

            case IdentifierNameSyntax id:
                return Identifier(id);

            case ParenthesizedExpressionSyntax p:
                return $"({Expr(p.Expression)})";

            // Double division: C#'s double `/` and JS's `/` are the same IEEE-754 op (int/int is
            // refused upstream in ClassifyExpression). Faithful, so emit it verbatim. Decided
            // semantically, exactly like the (int)double cast below -- JsBinaryOperator stays syntactic.
            case BinaryExpressionSyntax b when Filament.Subset.ConstructSubset.IsFaithfulDivision(b, _model):
                return $"{Expr(b.Left)} / {Expr(b.Right)}";

            // Integer division: C# int/int truncates toward zero (7/2 = 3); JS `/` is float. Math.trunc
            // restores it. The call parenthesizes its argument, so operand precedence is already handled
            // by Expr(). int/int is admitted upstream in ClassifyExpression (result-type dependent).
            case BinaryExpressionSyntax b when Filament.Subset.ConstructSubset.IsIntegerDivision(b, _model):
                return $"Math.trunc({Expr(b.Left)} / {Expr(b.Right)})";

            // Long division: a BARE `/` on BigInt operands. BigInt division truncates toward zero exactly as
            // C#'s long/long, and needs NO Math.trunc (a BigInt has no fractional part). Decision 112.
            case BinaryExpressionSyntax b when Filament.Subset.ConstructSubset.IsLongDivision(b, _model):
                return $"{Expr(b.Left)} / {Expr(b.Right)}";

            // Float division: a bare `/`; the enclosing Expr() wraps this Single-typed op in Math.fround
            // (round the double quotient to single, exactly C#'s float/float). Decision 113.
            case BinaryExpressionSyntax b when Filament.Subset.ConstructSubset.IsFloatDivision(b, _model):
                return $"{Expr(b.Left)} / {Expr(b.Right)}";

            // DECIMAL arithmetic/comparison -> the __dec* helpers (decision 114): a decimal is a boxed { m, s }
            // object, so `a + b` is __decAdd(a, b), never the `+` operator (which would add two objects to NaN).
            // Checked BEFORE JsBinaryOperator so no decimal binary ever slips through to a raw operator.
            case BinaryExpressionSyntax b when EitherDecimal(b):
                return DecimalBinary(b);

            // DateTime COMPARISON is free (a DateTime is a BigInt of ticks, so `<`/`==` fall through to
            // JsBinaryOperator as BigInt ops -- faithful). DateTime ARITHMETIC (+/-) produces a TimeSpan, which
            // is not in the subset, so it is refused here rather than emitted as a bare tick subtraction (which
            // would be a silently wrong number). Decision 115.
            case BinaryExpressionSyntax b when EitherDateTime(b) && !IsComparisonKind(b.Kind()):
                return Refuse("unsupported-expression",
                    $"DateTime arithmetic ('{b.OperatorToken.Text}') produces a TimeSpan, which is not in the subset. " +
                    "Only DateTime comparison and .AddDays(constant int) are admitted. Refusing to emit.", b.SpanStart);

            case BinaryExpressionSyntax b when Filament.Subset.ConstructSubset.JsBinaryOperator(b) is { } op:
                return $"{Expr(b.Left)} {op} {Expr(b.Right)}";

            // A decimal unary op (decision 114): `-x` negates the boxed mantissa (__decNeg), `+x` is identity.
            // Intercepted BEFORE JsPrefixOperator so `-x` never becomes `-{m,s}` (which is NaN).
            case PrefixUnaryExpressionSyntax p when _model.GetTypeInfo(p).Type?.SpecialType == SpecialType.System_Decimal:
                if (p.Kind() == SyntaxKind.UnaryPlusExpression) return Expr(p.Operand);
                if (p.Kind() == SyntaxKind.UnaryMinusExpression)
                {
                    DecimalHelpers.Add("decNeg");
                    return $"__decNeg({Expr(p.Operand)})";
                }
                return Refuse("unsupported-expression",
                    $"decimal '{p.OperatorToken.Text}' is not in the subset (only unary + and - are). Refusing to emit.",
                    p.SpanStart);

            case PrefixUnaryExpressionSyntax p when Filament.Subset.ConstructSubset.JsPrefixOperator(p) is { } op:
                return $"{op}{Expr(p.Operand)}";

            // currentCount++ -> currentCount.value++. One node in, one node out, no syntactic
            // desugaring (counter.js's header states this mapping exactly).
            case PostfixUnaryExpressionSyntax p when p.Kind() is
                SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression:
                return $"{Expr(p.Operand)}{p.OperatorToken.Text}";

            case ConditionalExpressionSyntax c:
                return $"{Expr(c.Condition)} ? {Expr(c.WhenTrue)} : {Expr(c.WhenFalse)}";

            // `arr[i] = v` on a reactive array -> COPY-ON-WRITE (decision 127). The signal fires only on a NEW
            // reference (its setter guards with Object.is), so a plain in-place `arr.value[i] = v` would leave every
            // effect reading the array stale -- the silently-wrong outcome §10 forbids, and why #117 refused it.
            // `.with(i, v)` returns a COPY with element i replaced; assigning it fires the signal and every display
            // re-runs. Only on a SIGNAL array (the template reads it AND this write lifts it, decision 67); on a
            // non-reactive array the write has no observer, so it stays refused. Placed BEFORE the general case.
            case AssignmentExpressionSyntax { Left: ElementAccessExpressionSyntax lea } asg when IsArrayReceiver(lea.Expression):
                return FieldOfReceiver(lea.Expression) is { IsSignal: true }
                    ? $"{Expr(lea.Expression)} = {Expr(lea.Expression)}.with({Expr(lea.ArgumentList.Arguments[0].Expression)}, {Expr(asg.Right)})"
                    : Refuse("unsupported-expression",
                        "`arr[i] = v` is admitted only on a REACTIVE array -- one the template reads AND this method " +
                        "writes, so it lifts to a signal whose copy-on-write reassignment fires the display. A write " +
                        "to an array nothing displays has no observer. Refusing to emit.", asg.SpanStart);

            // `d[key] = v` on a reactive Dictionary -> COPY-ON-WRITE (decision 127), the Map sibling of the array
            // write. `new Map(d.value).set(key, v)` returns a fresh Map with the entry replaced; assigning it fires
            // the signal. Only on a SIGNAL Dict; otherwise the write has no observer and stays refused.
            case AssignmentExpressionSyntax { Left: ElementAccessExpressionSyntax dea } asg when IsDictReceiver(dea.Expression):
                return FieldOfReceiver(dea.Expression) is { IsSignal: true }
                    ? $"{Expr(dea.Expression)} = new Map({Expr(dea.Expression)}).set({Expr(dea.ArgumentList.Arguments[0].Expression)}, {Expr(asg.Right)})"
                    : Refuse("unsupported-expression",
                        "`d[key] = v` is admitted only on a REACTIVE Dictionary -- one the template reads AND this " +
                        "method writes, so it lifts to a signal whose copy-on-write reassignment fires the display. " +
                        "Refusing to emit.", asg.SpanStart);

            case AssignmentExpressionSyntax a when Filament.Subset.ConstructSubset.JsAssignmentOperator(a) is { } op:
                return $"{Expr(a.Left)} {op} {Expr(a.Right)}";

            case InvocationExpressionSyntax inv:
                return Invocation(inv);

            case MemberAccessExpressionSyntax ma:
                return MemberAccess(ma);

            // _rows[i] / arr[i]. A List<T> and a T[] are both JS arrays here, so this is the array's own
            // indexer (decision 117 generalises the List-only case of decision 1).
            case ElementAccessExpressionSyntax ea when (ListReceiver(ea.Expression) is not null || IsArrayReceiver(ea.Expression)) &&
                                                       ea.ArgumentList.Arguments.Count == 1:
                return $"{Expr(ea.Expression)}[{Expr(ea.ArgumentList.Arguments[0].Expression)}]";

            // A LOCAL List/array indexes through the same JS indexer (decision 161 lifts the field-only
            // restriction: Deserialize's `data[i]` is the witness; receiver locality was never part of
            // the mapping's faithfulness argument).
            case ElementAccessExpressionSyntax ea when ea.ArgumentList.Arguments.Count == 1 &&
                    _model.GetSymbolInfo(ea.Expression).Symbol is ILocalSymbol { Type: { } lt } &&
                    (Filament.Subset.TypeSubset.ListElement(lt) ?? Filament.Subset.TypeSubset.ArrayElement(lt)) is not null:
                return $"{Expr(ea.Expression)}[{Expr(ea.ArgumentList.Arguments[0].Expression)}]";

            // d[key] -- a Dictionary is a JS Map, so its indexer is `.get(key)` (decision 118).
            case ElementAccessExpressionSyntax ea when IsDictReceiver(ea.Expression) && ea.ArgumentList.Arguments.Count == 1:
                return $"{Expr(ea.Expression)}.get({Expr(ea.ArgumentList.Arguments[0].Expression)})";

            // A T[] literal -> a JS array literal (decision 117): `new int[]{10,20,30}` -> `[10, 20, 30]`. A SIZED
            // `new T[n]` -> `new Array(n).fill(default(T))` (decision 122): `new int[3]` -> `new Array(3).fill(0)`,
            // exactly C#'s n-defaults array (int->0, string->null, bool->false).
            case ArrayCreationExpressionSyntax ac when ac.Initializer is { } init:
                return ArrayLiteral(init);

            case ArrayCreationExpressionSyntax ac
                when ac.Type.RankSpecifiers is [{ Sizes: [var size] }] && size is not OmittedArraySizeExpressionSyntax:
                return $"new Array({Expr(size)}).fill({DefaultOf(_model.GetTypeInfo(ac.Type.ElementType).Type!)})";

            case ImplicitArrayCreationExpressionSyntax iac:
                return ArrayLiteral(iac.Initializer);

            case AwaitExpressionSyntax aw:
                // `await x` -> JS's own `await x` (decision 119). Valid ONLY inside an async method/lambda: in a
                // non-async context it is invalid C# (CS4033), and `await` in a non-async JS function is a syntax
                // error too -- so refuse it here (Code/Await.razor: `await` in a `void` method).
                if (!InAsyncContext(aw))
                    return Refuse("unsupported-expression",
                        "`await` outside an async method is not valid C# and has no non-async JS form. Refusing to emit.",
                        aw.SpanStart);
                return $"await {Expr(aw.Expression)}";

            case InterpolatedStringExpressionSyntax s:
                return Interpolated(s);

            case CastExpressionSyntax cast when
                _model.GetTypeInfo(cast.Type).Type?.SpecialType == SpecialType.System_Int32 &&
                _model.GetTypeInfo(cast.Expression).Type?.SpecialType is SpecialType.System_Double:
                // C#'s (int) on a double truncates toward zero; Math.trunc is that semantic
                // exactly. rows.js's header calls this out because floor would agree only for
                // positive values and the cast's real meaning is trunc. The LCG's parity with
                // C# depends on it: the seed stays in DOUBLE arithmetic on both sides, so the
                // two label streams are byte-identical and the harness's oracle can check it.
                return $"Math.trunc({Expr(cast.Expression)})";

            case ObjectCreationExpressionSyntax oc when Filament.Subset.ConstructSubset.IsExceptionCreation(oc, _model):
                // `new Exception(msg)` -> `new Error(msg)`: the one object creation in §5, for `throw`.
                // C#'s Exception.Message and JS's Error.message carry the same string.
                return $"new Error({string.Join(", ", oc.ArgumentList?.Arguments.Select(a => Expr(a.Expression)) ?? Enumerable.Empty<string>())})";

            case ObjectCreationExpressionSyntax oc when _model.GetTypeInfo(oc).Type is { } rt && RecordOf(rt) is { } rec:
                // INLINE record construction (decision 111) -> an object literal. This is the expression-
                // position sibling of the construction-site fold (ObjectLiteral): a positional `new Row(a, b)`
                // maps its args to the props by order; an object-initialiser `new Row { X = a }` maps by name.
                return RecordLiteral(oc, rec);

            case ObjectCreationExpressionSyntax oc when _model.GetTypeInfo(oc).Type is { } dt &&
                                                        Filament.Subset.TypeSubset.DictionaryTypes(dt).Key is not null:
                // `new Dictionary<K,V>(){…}` -> a JS Map (decision 118).
                return DictionaryLiteral(oc);

            case ObjectCreationExpressionSyntax oc when Filament.Subset.ConstructSubset.IsRandomCreation(oc, _model):
                return RandomConstruction(oc);

            case ObjectCreationExpressionSyntax oc when Filament.Subset.ConstructSubset.IsDateTimeCreation(oc, _model):
                // `new DateTime(...)` -> a BigInt tick count (decision 115). Computed HERE, from constant args:
                // a DateTime is not a compile-time constant in C#, but its constructor args are, so the exact
                // ticks are known now -- no runtime Date math for construction. Non-constant args are refused.
                return DateTimeConstruction(oc);

            default:
                throw new GeneratorException(
                    $"FIL-WIRING: ClassifyExpression admitted {e.Kind()} but Expr() has no case for it. " +
                    "The subset decision and the translator have drifted. Refusing to emit.");
        }
    }

    /// <summary>
    /// A name. THIS is where decision 57's hole is closed: whether `x` reads a signal is
    /// answered from the lifting table this compiler built, never inferred.
    /// </summary>
    string Identifier(IdentifierNameSyntax id)
    {
        switch (_model.GetSymbolInfo(id).Symbol)
        {
            // The <ErrorBoundary> ErrorContent context (decision 164). Admitted ONLY as the receiver of
            // `.Message`. Blazor renders a bare `@context` as Exception.ToString() -- the CLR type name,
            // the message, and the CLR stack trace -- where a JS Error stringifies to "Error: boom". The
            // shapes are not the same text, so the bare read is REFUSED rather than approximated; this is
            // the same rule the type-directed text position applies everywhere else (decision 149).
            case IFieldSymbol { Name: "context" } ec when _errorContextJs is not null &&
                    ec.Type.ToDisplayString() == "System.Exception" && Field(ec) is null:
                return id.Parent is MemberAccessExpressionSyntax { Name.Identifier.Text: "Message" } acc
                       && acc.Expression == id
                    ? _errorContextJs
                    : Refuse("unsupported-expression",
                        "a bare `@context` inside an <ErrorBoundary>'s ErrorContent is not mapped. Blazor " +
                        "renders it as Exception.ToString() -- the CLR type name, the message and the CLR " +
                        "stack trace -- and a JS Error carries none of that shape, so the text would differ " +
                        "from Blazor's. Write `@context.Message`, which IS `Error.message`. Refusing to emit.",
                        id.SpanStart);

            case IFieldSymbol fs when Field(fs) is { } f:
                // The read protocol is decision 22's: `s.Value` in C# is `s.value` in JS,
                // character for character. A field this compiler did NOT lift is a plain binding
                // and reads as itself -- no `.value`, which is precisely the false emission
                // decision 57 disclosed and could not then prevent.
                return f.IsSignal ? $"{f.Js}.value" : f.Js;

            case ILocalSymbol l:
                return JsName(l.Name);

            case IParameterSymbol p:
                return JsName(p.Name);

            case IMethodSymbol m when _methodsByName.TryGetValue(m.Name, out var mi):
                return mi.Js;

            // A COMPUTED property (decision 160): reads as a signal -- computed() memoizes and
            // re-runs on its dependencies, so `.value` is the whole read protocol here too.
            case IPropertySymbol ps when _computedsByName.TryGetValue(ps.Name, out var ci) &&
                    SymbolEqualityComparer.Default.Equals(ps.ContainingType, _component):
                return $"{ci.Js}.value";

            // A [Parameter] read, at a composition site: `@Name` resolves to the JS the parent supplied
            // — a folded CONSTANT for a static leaf (`'World'`, decision 88), or a translated parent
            // EXPRESSION for a bound param (`count.value`, decision 90). When that expression reads a
            // parent signal, IsReactive (via _paramReactive) makes this a live effect; the child inlines
            // into the parent's scope, so the signal it names is reachable.
            case IPropertySymbol ps when _paramEnv.TryGetValue(ps.Name, out var boundJs):
                return boundJs;

            // A RenderFragment reached here, which means it is NOT the whole slot (TranslateSlots takes
            // that case first) -- it is part of a larger expression. A fragment is MARKUP, not a value:
            // there is nothing to concatenate it with, compare it to, or print (decision 131).
            case IPropertySymbol ps when IsFragmentParameter(ps.Name):
                return Refuse("unsupported-expression",
                    $"'{ps.Name}' is a RenderFragment used inside a larger expression. A fragment is MARKUP, " +
                    "and it is admitted only as a whole binding on its own (`@" + ps.Name + "`), which is the " +
                    "position its content is inlined at. There is no value here to combine with anything. " +
                    "Refusing to emit.",
                    id.SpanStart);

            default:
                Refuse("unresolved-name",
                    $"'{id.Identifier.Text}' is not declared in this component. The subset admits state and " +
                    "methods declared in the SAME component (spec 5); a Filament module has no `this`, no " +
                    "base class and no injected services to reach for. Refusing to emit.",
                    id.SpanStart);
                return "/*refused*/";
        }
    }

    /// <summary>`row.Label` -> `row.label.value`; `_rows.Count` -> `_rows.length`. Nothing else.</summary>
    string MemberAccess(MemberAccessExpressionSyntax ma)
    {
        // KeyboardEventArgs members (decision 159): direct projections of the DOM event the
        // listener received -- `e.Key` IS `e.key`. Allowlisted; anything else names Blazor-side
        // machinery with no DOM twin and refuses with the list.
        if (_model.GetSymbolInfo(ma.Expression).Symbol is IParameterSymbol kp &&
            Filament.Subset.TypeSubset.IsKeyboardEventArgs(kp.Type))
        {
            var mapped = ma.Name.Identifier.Text switch
            {
                "Key" => "key", "Code" => "code", "CtrlKey" => "ctrlKey", "ShiftKey" => "shiftKey",
                "AltKey" => "altKey", "MetaKey" => "metaKey", _ => null,
            };
            return mapped is not null
                ? $"{Expr(ma.Expression)}.{mapped}"
                : Refuse("unsupported-expression",
                    $"KeyboardEventArgs.{ma.Name.Identifier.Text} is not mapped. Key, Code, CtrlKey, ShiftKey, " +
                    "AltKey and MetaKey are -- each a direct DOM event property. Refusing to emit.",
                    ma.SpanStart);
        }

        // The <ErrorBoundary> context's members (decision 164). `context` IS the caught value, and in a
        // Filament module that value is a JS Error, so `.Message` IS `.message` -- the same direct
        // projection `e.Key` is. Allowlisted for the same reason KeyboardEventArgs is: every other
        // Exception member names CLR machinery with no JS twin (StackTrace's format, InnerException's
        // chain, the CLR type name) and approximating one would render text .NET never produced.
        if (_errorContextJs is not null &&
            _model.GetTypeInfo(ma.Expression).Type is { } rt && rt.ToDisplayString() == "System.Exception")
        {
            if (ma.Name.Identifier.Text == "Message") return $"{Expr(ma.Expression)}.message";
            return Refuse("unsupported-expression",
                $"Exception.{ma.Name.Identifier.Text} is not mapped on an <ErrorBoundary> context. Only " +
                ".Message is -- it is the one member with a direct JS twin (`Error.prototype.message`). " +
                "StackTrace, InnerException, Data, HResult and GetType() name CLR machinery a JS Error " +
                "does not carry, and rendering an approximation would show text .NET never produced. " +
                "Refusing to emit.",
                ma.SpanStart);
        }

        // `_rows.Count` -- the List's length. A JS array's own property, so no call and no
        // wrapper: the mapping IS the array (rows.js decision 1). A LOCAL List counts the same
        // way (decision 161: Deserialize's `data.Count` -- locality was never the argument).
        if (ma.Name.Identifier.Text == "Count" &&
            (ListReceiver(ma.Expression) is not null || IsLocalList(ma.Expression)))
            return $"{Expr(ma.Expression)}.length";

        // arr.Length -- a JS array's own property, so no call and no wrapper (decision 117, the array sibling
        // of List's .Count).
        if (ma.Name.Identifier.Text == "Length" && IsArrayReceiver(ma.Expression))
            return $"{Expr(ma.Expression)}.length";

        // d.Count -- a JS Map's `.size` (decision 118, the Dictionary sibling of List's .Count).
        if (ma.Name.Identifier.Text == "Count" && IsDictReceiver(ma.Expression))
            return $"{Expr(ma.Expression)}.size";

        // g.Key on an IGrouping (decision 128): a group is modelled as a JS array of its elements with a `.key`
        // property, so its Key is `.key`. The group's OWN sequence ops (g.Count() -> .length, g.Sum() -> reduce)
        // need no special case -- a group IS an array, so they flow through the normal array/LINQ mappings.
        if (ma.Name.Identifier.Text == "Key" && IsGrouping(ma.Expression))
            return $"{Expr(ma.Expression)}.key";

        // kvp.Key / kvp.Value on a @foreach-over-Dictionary variable (decision 125). The Map-spread source gives
        // each row as a [k, v] pair, so .Key is kvp[0] -- the reconcile key, STATIC per row. But .Value must NOT be
        // the frozen kvp[1]: list() REUSES a persisting key's row (it never re-runs create), so a static value goes
        // STALE when that key's value changes, where Blazor re-renders the reused element. So .Value compiles to a
        // REACTIVE lookup `d.value.get(kvp[0])` -- reading the Dict signal makes the slot an effect (see IsReactive),
        // and the effect re-runs on reassignment to fetch the current value for this row's stable key.
        if (IsKeyValuePair(ma.Expression) && ma.Name.Identifier.Text is "Key" or "Value")
        {
            var kvpJs = Expr(ma.Expression);
            if (ma.Name.Identifier.Text == "Key") return $"{kvpJs}[0]";
            if (DictOfForeachVar(ma.Expression) is { } dictJs) return $"{dictJs}.value.get({kvpJs}[0])";
        }

        // stamp.Ticks -- the IDENTITY on the ticks model (decision 145): a DateTime IS its BigInt tick
        // count (decision 115), and .Ticks is that count typed long, whose JS home is the same BigInt
        // (decision 112). Nothing to compute, nothing to wrap.
        if (ma.Name.Identifier.Text == "Ticks" &&
            _model.GetTypeInfo(ma.Expression).Type?.SpecialType == SpecialType.System_DateTime)
            return Expr(ma.Expression);

        // DateTime.UtcNow / Now / Today -- the wall clock (decision 145), the FIRST admitted
        // non-deterministic value. The clock is the same clock on both sides: the helpers derive the
        // tick count from Date.now() (+ the current local offset for Now, truncated to the local day
        // for Today), faithful to C#'s value within clock resolution. Emitted per use, in dependency
        // order. Everything else static on DateTime (Parse, MinValue, ...) stays refused below.
        if (_model.GetSymbolInfo(ma).Symbol is IPropertySymbol { IsStatic: true } clock &&
            clock.ContainingType.SpecialType == SpecialType.System_DateTime &&
            clock.Name is "UtcNow" or "Now" or "Today")
        {
            ClockHelpers.Add("dtUtcNow");
            if (clock.Name is "Now" or "Today") ClockHelpers.Add("dtNow");
            if (clock.Name is "Today") ClockHelpers.Add("dtToday");
            return $"__{(clock.Name switch { "UtcNow" => "dtUtcNow", "Now" => "dtNow", _ => "dtToday" })}()";
        }

        // Random.Shared (decision 146): the process-wide unseeded instance. ONE module-level __rndShared
        // backs every use -- same lifetime C# gives it (a static singleton), same regime (unseeded ->
        // Math.random; C#'s unseeded xoshiro is not reproducible across runs either).
        if (_model.GetSymbolInfo(ma).Symbol is IPropertySymbol { IsStatic: true, Name: "Shared" } shared &&
            Filament.Subset.TypeSubset.IsRandom(shared.ContainingType))
        {
            RandomHelpers.Add("rnd");
            RandomHelpers.Add("rndShared");
            return "__rndShared";
        }

        // A DateTime instance member OTHER than .Ticks -- most pointedly .Kind, which does not survive
        // the ticks erasure: a tick count has no Kind (decision 145). Refused HERE so the message can
        // name the erasure instead of the generic "no BCL" shrug.
        if (_model.GetTypeInfo(ma.Expression).Type?.SpecialType == SpecialType.System_DateTime)
        {
            Refuse("unsupported-member",
                $"'{Trunc(ma.ToString())}' is not a DateTime member in the subset. A DateTime is its BigInt " +
                "tick count (decision 115): .Ticks is that value, comparison and AddDays are tick arithmetic, " +
                "and the wall clock enters at DateTime.UtcNow/Now/Today (decision 145). .Kind in particular " +
                "does not survive the erasure -- a tick count has no Kind. Refusing to emit.",
                ma.SpanStart);
            return "/*refused*/";
        }

        if (_model.GetSymbolInfo(ma).Symbol is IPropertySymbol ps && PropAnywhere(ps) is { } p)
            return p.IsSignal ? $"{Expr(ma.Expression)}.{p.Js}.value" : $"{Expr(ma.Expression)}.{p.Js}";

        Refuse("unsupported-expression",
            $"'{Trunc(ma.ToString())}' is not member access on a record declared in this component. Section 5 " +
            "admits member access on a LOCAL RECORD and List<T>'s .Count; a Filament module ships no BCL, so " +
            "there is nothing else for a dot to reach. Refusing to emit.",
            ma.SpanStart);
        return "/*refused*/";
    }

    /// <summary>
    /// A method on an @ref'd element (decision 132). ONLY FocusAsync(), and that narrowness is the point:
    /// the rest of the ElementReference surface is interop plumbing whose meaning is a framework's, not a
    /// DOM node's. `await box.FocusAsync()` -> `await box.focus()` -- the await is KEPT rather than
    /// stripped, because preserving it preserves the ordering the C# states, and `await` on a non-promise
    /// is exactly the same program.
    /// </summary>
    /// <summary>
    /// `JS.InvokeVoidAsync("localStorage.setItem", k, v)` -> `localStorage.setItem(k, v)` (decision 133).
    ///
    /// THE BRIDGE IS ERASED, NOT IMPLEMENTED. Blazor resolves the dotted identifier against the browser's
    /// global scope at RUNTIME and marshals the arguments across the .NET/JS boundary. This module IS on
    /// the other side already: the identifier is resolved at COMPILE time -- into the very same dotted
    /// path, which is legal JS as written -- and the arguments are the translated expressions themselves.
    ///
    /// THE IDENTIFIER MUST BE A STRING LITERAL, and that is the load-bearing gate. A computed identifier
    /// would have to be resolved by walking globalThis at runtime, i.e. by shipping the bridge this slice
    /// claims to have removed. Each dotted segment is checked to be a plain JS identifier, so nothing that
    /// is not a call path can be spliced into call position.
    /// </summary>
    string JsInvocation(InvocationExpressionSyntax inv, IMethodSymbol m)
    {
        if (_jsRuntime is null)
            return Refuse("unsupported-call",
                "a JS interop call needs an `@inject IJSRuntime` to name the host scope. Refusing to emit.",
                inv.SpanStart);

        if (m.Name is not ("InvokeVoidAsync" or "InvokeAsync"))
            return Refuse("unsupported-call",
                $"'{m.Name}' is not in the subset. JS interop admits InvokeVoidAsync and InvokeAsync<T>; " +
                "the rest of IJSRuntime (module imports, .NET object references, streams) exists to manage a " +
                "boundary this module does not have. Refusing to emit.",
                inv.SpanStart);

        var args = inv.ArgumentList.Arguments;
        if (args.Count == 0 || args[0].Expression is not LiteralExpressionSyntax lit ||
            lit.Token.Value is not string identifier)
            return Refuse("unsupported-call",
                "the JS identifier must be a literal string. A computed identifier could only be resolved by " +
                "walking the global scope at RUNTIME -- which is precisely the interop bridge this compiler " +
                "erases instead of shipping. Refusing to emit.",
                inv.SpanStart);

        var segments = identifier.Split('.');
        if (segments.Length == 0 || !segments.All(IsJsIdentifier))
            return Refuse("unsupported-call",
                $"'{Trunc(identifier)}' is not a dotted JS identifier path (e.g. \"localStorage.setItem\"). " +
                "Only a path this compiler can verify is emitted in call position. Refusing to emit.",
                inv.SpanStart);

        var rest = args.Skip(1).Select(a => Expr(a.Expression));
        return $"{identifier}({string.Join(", ", rest)})";
    }

    /// <summary>A plain JS identifier — letters/digits/_/$ not starting with a digit. Deliberately strict:
    /// it decides what may be emitted in CALL position (decision 133).</summary>
    static bool IsJsIdentifier(string s) =>
        s.Length > 0 && (char.IsLetter(s[0]) || s[0] is '_' or '$') &&
        s.All(c => char.IsLetterOrDigit(c) || c is '_' or '$');

    string ElementRefMethod(InvocationExpressionSyntax inv, IMethodSymbol m)
    {
        if (m.Name != "FocusAsync" || inv.ArgumentList.Arguments.Count > 0 ||
            inv.Expression is not MemberAccessExpressionSyntax ma)
            return Refuse("unsupported-call",
                $"'{Trunc(inv.ToString())}' is not in the subset. An @ref'd element admits FocusAsync() and " +
                "nothing else: the rest of the ElementReference API exists to carry an id across the " +
                ".NET/JS boundary, which a module that already holds the node does not have. Refusing to emit.",
                inv.SpanStart);

        return $"{Expr(ma.Expression)}.focus()";
    }

    string Invocation(InvocationExpressionSyntax inv)
    {
        var info = _model.GetSymbolInfo(inv);
        var symbol = info.Symbol as IMethodSymbol;

        // A CALL THAT NAMES SOMETHING AND DOES NOT BIND IS NOT "NOT DECLARED HERE", AND SAYING SO
        // WAS A LIE THIS COMPILER TOLD. Measured, before this arm existed:
        //     private int seed = Compute();          // Compute() is declared THREE LINES BELOW
        //     -> "'Compute()' is not a call to a method declared in this component."
        // It is declared in this component. The truth is CS0236 -- a field initializer cannot
        // reference an instance method -- and the input is invalid C#, not out-of-subset C#.
        // Roslyn bound the candidate and rejected it, so Roslyn's verdict is the one that is
        // true; CheckSemantics carries the same text for the cases no subset rule reaches.
        // Decision 69's third defect ("un diagnostic qui blame l'auteur pour une omission du
        // compilateur") in its second form: here the author is blamed for a rule they obeyed.
        if (symbol is null && !info.CandidateSymbols.IsDefaultOrEmpty)
        {
            var err = _model.GetDiagnostics(inv.Span)
                .FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
            Refuse("not-csharp",
                $"the call '{Trunc(inv.ToString())}' names something this component declares and does not " +
                $"compile as C#: {(err is null ? "it does not bind" : $"{err.Id}: {err.GetMessage()}")}. " +
                "Phase 3 compiles @code as C# (spec 5), so a call C# itself rejects has nothing to translate " +
                "-- Blazor would refuse to build this file too. Refusing to emit.",
                inv.SpanStart);
            return "/*refused*/";
        }

        // JS INTEROP (decision 133). InvokeVoidAsync / InvokeAsync<T> on the @inject'd IJSRuntime, whether
        // reached through the interface or through JSRuntimeExtensions. This is where the bridge is erased.
        if (symbol is not null && symbol.ContainingType?.ToDisplayString() is
                "Microsoft.JSInterop.JSRuntimeExtensions" or "Microsoft.JSInterop.IJSRuntime")
            return JsInvocation(inv, symbol);

        // HTTP (decision 147). Get*/Post* on the @inject'd HttpClient, whether reached through the class
        // or through the System.Net.Http.Json extensions. This is where THAT bridge is erased: in a
        // browser an HttpClient IS fetch, so the request becomes the fetch itself.
        if (symbol is not null && symbol.ContainingType?.ToDisplayString() is
                "System.Net.Http.Json.HttpClientJsonExtensions" or "System.Net.Http.HttpClient")
            return HttpInvocation(inv, symbol);

        // LOCAL JSON (decision 157). Serialize/Deserialize on System.Text.Json.JsonSerializer,
        // default options only, T through the SAME shape gate the network uses (decision 147).
        if (symbol is not null && symbol.ContainingType?.ToDisplayString() == "System.Text.Json.JsonSerializer")
            return JsonInvocation(inv, symbol);

        // FocusAsync() on an @ref'd element (decision 132). ElementReference's ONLY faithful surface: Blazor
        // needs the reference because it hands an id across the .NET/JS boundary, while the emitted module
        // already holds the node in a const, so this is just `.focus()`.
        if (symbol is not null &&
            symbol.ContainingType?.ToDisplayString() == "Microsoft.AspNetCore.Components.ElementReferenceExtensions")
            return ElementRefMethod(inv, symbol);

        // A DateTime instance method (decision 115): .AddDays(constant int) is tick arithmetic. The rest of the
        // DateTime API is deferred (refused in DateTimeMethod), so it can never slip through to a wrong emission.
        if (symbol is not null && symbol.ContainingType.SpecialType == SpecialType.System_DateTime)
            return DateTimeMethod(inv, symbol);

        // A Random instance method (decision 146): Next()/Next(max)/Next(min,max)/NextDouble() map onto the
        // emitted __rnd instance's interface. Everything else (NextBytes, NextInt64, ...) refuses in
        // RandomMethod, so nothing unfaithful slips through.
        if (symbol is not null && Filament.Subset.TypeSubset.IsRandom(symbol.ContainingType))
            return RandomMethod(inv, symbol);

        // A LINQ operator (decision 116): the common ones over a List map to JS array methods -- Where -> filter,
        // Select -> map, Count -> length, Any -> some, All -> every, ToList -> the array. LINQ over a JS array is
        // faithful because the source is already a materialised array (rows.js decision 1), and a chain that ends
        // in Count/Any/All produces a scalar. The predicate/selector lambda translates through the same Expr the
        // rest of @code uses (its parameter is an ordinary local). The rest of LINQ is refused in LinqInvocation.
        if (symbol is not null && symbol.ContainingType?.ToDisplayString() == "System.Linq.Enumerable")
            return LinqInvocation(inv, symbol);

        // A Dictionary instance method (decision 118): .ContainsKey(k) -> .has(k). Everything else (Add/Remove/
        // TryGetValue) is refused -- a Dictionary is admitted READ-ONLY, so a mutation cannot slip through.
        if (symbol is not null && Filament.Subset.TypeSubset.DictionaryTypes(symbol.ContainingType).Key is not null)
            return DictionaryMethod(inv, symbol);

        // Task.Delay(ms) -> a Promise that resolves after `ms` ms (decision 119): the one Task member in §5, for
        // `await Task.Delay(n)`. Every other Task API (Run, WhenAll, ContinueWith, …) stays refused below.
        if (symbol is { Name: "Delay" } && symbol.ContainingType?.ToDisplayString() == "System.Threading.Tasks.Task"
            && inv.ArgumentList.Arguments.Count == 1)
            return $"new Promise((resolve) => setTimeout(resolve, {Expr(inv.ArgumentList.Arguments[0].Expression)}))";

        // Spec 5: "calls to methods declared in the same component". Anything else --
        // Console.WriteLine, DateTime.Now, an extension method -- has no meaning in a Filament
        // module and there is nothing honest to emit for it. (List<T>'s Add/RemoveAt are
        // handled at STATEMENT level, where their version bump has somewhere to go.)
        if (symbol is null || !_methodsByName.TryGetValue(symbol.Name, out var mi) ||
            !SymbolEqualityComparer.Default.Equals(symbol.ContainingType, _component))
        {
            Refuse("unsupported-call",
                $"'{Trunc(inv.ToString())}' is not a call to a method declared in this component. The " +
                "subset admits calls to methods declared in the SAME component (spec 5) and nothing else: " +
                "a Filament module ships no BCL. Refusing to emit.",
                inv.SpanStart);
            return "/*refused*/";
        }

        return $"{mi.Js}({string.Join(", ", inv.ArgumentList.Arguments.Select(a => Expr(a.Expression)))})";
    }

    /// <summary>A C# decimal -> `{ m: <mantissa>n, s: <scale> }` (decision 114). decimal.GetBits gives the
    /// exact 96-bit mantissa and the base-10 scale (0..28), which IS the value with no rounding: 1.10m is
    /// mantissa 110, scale 2 -> "{ m: 110n, s: 2 }", and __decStr renders it "1.10" (the trailing zero the
    /// scale records). The sign rides on the mantissa's BigInt.</summary>
    // True when either operand is decimal (after C#'s implicit conversions): the operation is a decimal one,
    // whatever its result type -- `a + b` is decimal, and `a > b` is bool but its operands are decimal.
    bool EitherDecimal(BinaryExpressionSyntax b) =>
        _model.GetTypeInfo(b.Left).ConvertedType?.SpecialType == SpecialType.System_Decimal ||
        _model.GetTypeInfo(b.Right).ConvertedType?.SpecialType == SpecialType.System_Decimal;

    bool EitherDateTime(BinaryExpressionSyntax b) =>
        _model.GetTypeInfo(b.Left).Type?.SpecialType == SpecialType.System_DateTime ||
        _model.GetTypeInfo(b.Right).Type?.SpecialType == SpecialType.System_DateTime;

    static bool IsComparisonKind(SyntaxKind k) => k is
        SyntaxKind.LessThanExpression or SyntaxKind.LessThanOrEqualExpression or
        SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression or
        SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression;

    /// <summary>A decimal binary -> a __dec* helper call (decision 114). +, -, * are exact base-10 on the
    /// { m, s } representation; the six comparisons go through __decCmp (scale-aligned). Division and modulo are
    /// REFUSED: faithful decimal division needs System.Decimal's 28-29-significant-digit rounding, which is a
    /// separate, larger problem -- refusing beats a silently wrong quotient (section 10).</summary>
    string DecimalBinary(BinaryExpressionSyntax b)
    {
        var l = Expr(b.Left);
        var r = Expr(b.Right);
        string Use(string helper, string js) { DecimalHelpers.Add(helper); return js; }
        return b.Kind() switch
        {
            SyntaxKind.AddExpression => Use("decAdd", $"__decAdd({l}, {r})"),
            SyntaxKind.SubtractExpression => Use("decSub", $"__decSub({l}, {r})"),
            SyntaxKind.MultiplyExpression => Use("decMul", $"__decMul({l}, {r})"),
            SyntaxKind.LessThanExpression => Use("decCmp", $"(__decCmp({l}, {r}) < 0)"),
            SyntaxKind.LessThanOrEqualExpression => Use("decCmp", $"(__decCmp({l}, {r}) <= 0)"),
            SyntaxKind.GreaterThanExpression => Use("decCmp", $"(__decCmp({l}, {r}) > 0)"),
            SyntaxKind.GreaterThanOrEqualExpression => Use("decCmp", $"(__decCmp({l}, {r}) >= 0)"),
            SyntaxKind.EqualsExpression => Use("decCmp", $"(__decCmp({l}, {r}) === 0)"),
            SyntaxKind.NotEqualsExpression => Use("decCmp", $"(__decCmp({l}, {r}) !== 0)"),
            _ => Refuse("unsupported-expression",
                $"decimal '{b.OperatorToken.Text}' is not in the subset: section 5 admits decimal +, -, * and the " +
                "comparisons. Division and modulo need System.Decimal's 28-digit rounding, which is deferred. " +
                "Refusing to emit.", b.SpanStart),
        };
    }

    /// <summary>A Dictionary instance method (decision 118). `.ContainsKey(k)` -> `.has(k)`; every other method
    /// (Add/Remove/TryGetValue/…) is refused, keeping a Dictionary READ-ONLY so no mutation slips through.</summary>
    string DictionaryMethod(InvocationExpressionSyntax inv, IMethodSymbol symbol)
    {
        var args = inv.ArgumentList.Arguments;
        if (symbol.Name == "ContainsKey" && inv.Expression is MemberAccessExpressionSyntax ma && args.Count == 1)
            return $"{Expr(ma.Expression)}.has({Expr(args[0].Expression)})";
        return Refuse("unsupported-call",
            $"'{Trunc(inv.ToString())}' is not a Dictionary operation in the subset: a Dictionary is admitted " +
            "READ-ONLY (indexing, .Count, .ContainsKey). Add/Remove/TryGetValue are deferred. Refusing to emit.",
            inv.SpanStart);
    }

    /// <summary>A LINQ operator over a List (decision 116) -> a JS array method. The source array is already
    /// materialised (a List is a JS array), so eager JS methods are faithful to LINQ terminating in a scalar
    /// (Count/Any/All) or ToList. Only these operators are admitted; the rest of LINQ (GroupBy, OrderBy, Join,
    /// aggregate overloads, …) is refused rather than half-translated.</summary>
    string LinqInvocation(InvocationExpressionSyntax inv, IMethodSymbol symbol)
    {
        if (inv.Expression is not MemberAccessExpressionSyntax ma)
            return Refuse("unsupported-call",
                "a LINQ operator must be called on a source (e.g. items.Where(...)). Refusing to emit.", inv.SpanStart);
        var recv = Expr(ma.Expression);
        var args = inv.ArgumentList.Arguments;
        // The numeric aggregates (Sum/Min/Max/Average) are admitted only when the RESULT is int or double -- a
        // long (BigInt) or decimal (boxed) aggregate would need a typed reduction, deferred (decision 121).
        var numAgg = _model.GetTypeInfo(inv).Type?.SpecialType is SpecialType.System_Int32 or SpecialType.System_Double;
        return (symbol.Name, args.Count) switch
        {
            ("Where", 1) => $"{recv}.filter({Lambda(args[0])})",
            ("Select", 1) => $"{recv}.map({Lambda(args[0])})",
            ("Count", 0) => $"{recv}.length",
            ("Count", 1) => $"{recv}.filter({Lambda(args[0])}).length",
            ("Any", 0) => $"({recv}.length > 0)",
            ("Any", 1) => $"{recv}.some({Lambda(args[0])})",
            ("All", 1) => $"{recv}.every({Lambda(args[0])})",
            ("ToList", 0) => recv,
            ("ToArray", 0) => recv,
            // Aggregates over a number list (decision 121). Sum -> reduce; Min/Max -> Math.min/max spread;
            // Average -> sum/length. Empty-source divergence (C# throws; JS gives 0/Infinity/NaN) is disclosed.
            ("Sum", 0) when numAgg => $"{recv}.reduce((a, b) => a + b, 0)",
            ("Min", 0) when numAgg => $"Math.min(...{recv})",
            ("Max", 0) when numAgg => $"Math.max(...{recv})",
            ("Average", 0) when numAgg => $"({recv}.reduce((a, b) => a + b, 0) / {recv}.length)",
            // First/Last -> index (element type is whatever the list holds). First(pred) -> find.
            ("First", 0) => $"{recv}[0]",
            ("First", 1) => $"{recv}.find({Lambda(args[0])})",
            ("Last", 0) => $"{recv}.at(-1)",
            // Ordering + paging (decision 126), each producing a SEQUENCE (observed via a scalar terminal like
            // First/ElementAt, or a @foreach). OrderBy/OrderByDescending sort a COPY (`[...]` so the source array is
            // never mutated) by a NUMERIC key selector -- the comparator SUBTRACTS keys, so the key must be int/
            // double; JS Array.sort is STABLE (ES2019), matching LINQ's stable OrderBy. Skip/Take are slices;
            // Reverse copies-then-reverses; ElementAt indexes. A non-numeric key selector is refused (deferred).
            ("OrderBy", 1) when NumericKeySelector(args[0]) =>
                $"[...{recv}].sort((__a, __b) => ({Lambda(args[0])})(__a) - ({Lambda(args[0])})(__b))",
            ("OrderByDescending", 1) when NumericKeySelector(args[0]) =>
                $"[...{recv}].sort((__a, __b) => ({Lambda(args[0])})(__b) - ({Lambda(args[0])})(__a))",
            ("Skip", 1) => $"{recv}.slice({Expr(args[0].Expression)})",
            ("Take", 1) => $"{recv}.slice(0, {Expr(args[0].Expression)})",
            ("Reverse", 0) => $"[...{recv}].reverse()",
            ("ElementAt", 1) => $"{recv}[{Expr(args[0].Expression)}]",
            // GroupBy (decision 128) -> an ARRAY of groups in first-key-appearance order (LINQ's order). Each
            // IGrouping<K,T> is modelled as a JS array of its elements WITH a `.key` property -- so g.Key is g.key
            // (see MemberAccess) and every sequence op on a group (g.Count(), g.Sum(), First()) already works because
            // a group IS an array. The reduce builds a Map<K, group>, first-seen key inserts a fresh keyed array,
            // and .values() yields the groups in insertion (= first-appearance) order. A scalar key (Map value
            // equality: int/string/bool) is required -- a boxed key (decimal/record) would group by reference.
            ("GroupBy", 1) when ScalarGroupKey(args[0]) =>
                $"[...{recv}.reduce((__m, __x) => {{ const __k = ({Lambda(args[0])})(__x); let __g = __m.get(__k); " +
                $"if (!__g) {{ __g = []; __g.key = __k; __m.set(__k, __g); }} __g.push(__x); return __m; }}, new Map()).values()]",
            _ => Refuse("unsupported-call",
                $"the LINQ operator '{symbol.Name}' (arity {args.Count}) is not in the subset. Section 5 admits " +
                "Where, Select, Count, Any, All, ToList/ToArray, the number aggregates Sum/Min/Max/Average/First/" +
                "Last, the ordering/paging operators OrderBy/OrderByDescending (numeric key)/Skip/Take/Reverse/" +
                "ElementAt, and GroupBy (scalar key) over a List. Refusing to emit.", inv.SpanStart),
        };
    }

    // The expression body of a single-parameter LINQ key selector (`x => key`), or null (block-bodied -> refused).
    ExpressionSyntax? KeySelectorBody(ArgumentSyntax arg) => arg.Expression switch
    {
        SimpleLambdaExpressionSyntax l => l.Body as ExpressionSyntax,
        ParenthesizedLambdaExpressionSyntax l => l.Body as ExpressionSyntax,
        _ => null,
    };

    // OrderBy(x => key)'s comparator subtracts keys, so the key selector must return int or double (decision 126).
    // A string / other key is refused: `"a" - "b"` is NaN, a silent mis-sort. Block-bodied selectors are refused too.
    bool NumericKeySelector(ArgumentSyntax arg) =>
        KeySelectorBody(arg) is { } body &&
        _model.GetTypeInfo(body).Type?.SpecialType is SpecialType.System_Int32 or SpecialType.System_Double;

    // GroupBy(x => key) groups into a JS Map, which compares keys by SameValueZero (decision 128). A VALUE-equal
    // scalar key -- int/string/bool -- groups correctly; a boxed key (decimal object, a record) would group by
    // REFERENCE, silently splitting equal keys, so it is refused (deferred). Block-bodied selectors refused too.
    bool ScalarGroupKey(ArgumentSyntax arg) =>
        KeySelectorBody(arg) is { } body &&
        _model.GetTypeInfo(body).Type?.SpecialType is
            SpecialType.System_Int32 or SpecialType.System_String or SpecialType.System_Boolean;

    /// <summary>A single-parameter lambda argument to a LINQ operator (decision 116) -> a JS arrow. Its parameter
    /// is an ordinary local (Identifier resolves an IParameterSymbol to its name), and its body is translated by
    /// the same Expr as the rest of @code -- so `x => x > 0` becomes `x => x > 0`. Only an EXPRESSION body is
    /// admitted (a statement-block lambda is refused).</summary>
    string Lambda(ArgumentSyntax arg)
    {
        switch (arg.Expression)
        {
            case SimpleLambdaExpressionSyntax { Body: ExpressionSyntax body } sl:
                return $"{JsName(sl.Parameter.Identifier.Text)} => {Expr(body)}";
            case ParenthesizedLambdaExpressionSyntax { Body: ExpressionSyntax body } pl when pl.ParameterList.Parameters.Count == 1:
                return $"{JsName(pl.ParameterList.Parameters[0].Identifier.Text)} => {Expr(body)}";
            default:
                return Refuse("unsupported-expression",
                    "a LINQ operator's argument must be a single-parameter lambda with an expression body. Refusing to emit.",
                    arg.SpanStart);
        }
    }

    /// <summary>A DateTime instance method (decision 115). `.AddDays(n)` with a constant integer n adds
    /// n·TicksPerDay to the tick count -- exactly C#'s AddDays for whole days -- computed at generate-time. Every
    /// other DateTime method (other Add*, ToString(format), …) is deferred and refused, so admitting the type
    /// cannot silently mistranslate a call it does not handle.</summary>
    string DateTimeMethod(InvocationExpressionSyntax inv, IMethodSymbol symbol)
    {
        var args = inv.ArgumentList.Arguments;
        if (symbol.Name == "AddDays" && inv.Expression is MemberAccessExpressionSyntax ma && args.Count == 1 &&
            _model.GetConstantValue(args[0].Expression) is { HasValue: true, Value: int days })
            return $"({Expr(ma.Expression)} + {(long)days * System.TimeSpan.TicksPerDay}n)";
        return Refuse("unsupported-call",
            $"'{Trunc(inv.ToString())}' is not a DateTime operation in the subset. Section 5 admits `new DateTime(...)`, " +
            ".AddDays(constant int), comparison and default display; the rest of the DateTime API (other Add*, properties, " +
            "ToString(format), .Now) is deferred. Refusing to emit.", inv.SpanStart);
    }

    /// <summary>`new DateTime(y, m, d[, h, mi, s])` -> the exact tick count as a BigInt literal (decision 115),
    /// computed at generate-time by CONSTRUCTING the DateTime in the generator's own runtime and reading .Ticks.
    /// A DateTime value is a BigInt of ticks (100ns since 0001-01-01); __dtStr renders it, AddDays adds ticks,
    /// comparison is BigInt. Non-constant arguments are refused: the ticks are only known now if the args are.</summary>
    string DateTimeConstruction(ObjectCreationExpressionSyntax oc)
    {
        var args = oc.ArgumentList?.Arguments ?? default;
        var vals = new List<int>();
        foreach (var a in args)
        {
            var c = _model.GetConstantValue(a.Expression);
            if (!c.HasValue || c.Value is not int i)
                return Refuse("unsupported-expression",
                    "a `new DateTime(...)` in the subset needs CONSTANT int arguments -- its ticks are computed at " +
                    "generate-time (decision 115), so a non-constant argument has no value to compute from. Refusing to emit.",
                    a.SpanStart);
            vals.Add(i);
        }
        if (vals.Count is not (3 or 6))
            return Refuse("unsupported-expression",
                "only `new DateTime(y, m, d)` and `new DateTime(y, m, d, h, mi, s)` are in the subset. Refusing to emit.",
                oc.SpanStart);
        try
        {
            var dt = vals.Count == 3
                ? new System.DateTime(vals[0], vals[1], vals[2])
                : new System.DateTime(vals[0], vals[1], vals[2], vals[3], vals[4], vals[5]);
            return $"{dt.Ticks}n";
        }
        catch (System.ArgumentOutOfRangeException)
        {
            return Refuse("unsupported-expression",
                $"the DateTime {Trunc(oc.ToString())} is out of range. Refusing to emit.", oc.SpanStart);
        }
    }

    /// <summary>
    /// `await Http.GetFromJsonAsync&lt;T&gt;(url)` -> `await __getJson(url)` (decision 147) -- and the
    /// siblings GetStringAsync -> __getText, PostAsJsonAsync -> __postJson. THE BRIDGE IS ERASED, NOT
    /// IMPLEMENTED, on decision 133's exact argument: Blazor WASM's HttpClient is implemented ON TOP of
    /// fetch -- the bridge exists because .NET must marshal across a boundary this module does not have.
    ///
    /// THE TYPE GATE IS THE HONESTY CORE: GetFromJsonAsync&lt;T&gt; admits T only where the JSON shape and
    /// the Filament shape COINCIDE -- int/double/bool/string, records of those, List&lt;T&gt;/T[] of those.
    /// A `long` member would arrive as a JS number where the subset's long is a BigInt; `decimal` is not
    /// the boxed {m,s}; `DateTime` arrives as a STRING, not ticks; `float` is not fround'ed. Each refuses
    /// with the reason (TypeSubset.JsonUnfaithful). __getJson normalizes each key's FIRST character to
    /// lower case -- faithful to System.Text.Json's Web defaults (camelCase + case-insensitive) for the
    /// Pascal/camel case real APIs use; full case-insensitivity is NOT reproduced, and is disclosed.
    /// </summary>
    string HttpInvocation(InvocationExpressionSyntax inv, IMethodSymbol m)
    {
        if (_httpClient is null)
            return Refuse("unsupported-call",
                "an HTTP call needs an `@inject HttpClient` to name the client fetch stands in for. " +
                "Refusing to emit.", inv.SpanStart);

        var args = inv.ArgumentList.Arguments;
        switch (m.Name)
        {
            case "GetFromJsonAsync" when m.TypeArguments.Length == 1 && args.Count == 1:
            {
                if (Filament.Subset.TypeSubset.JsonUnfaithful(m.TypeArguments[0], _recordsByName.Values.Select(r => r.Symbol).ToArray()) is { } offender)
                    return Refuse("unsupported-type",
                        $"GetFromJsonAsync<{m.TypeArguments[0].ToDisplayString()}> deserializes {offender}, whose JSON " +
                        "shape is not its Filament shape (decision 147). JSON-faithful types are int, double, bool, " +
                        "string, records of those, and List<T>/T[] of those. Refusing to emit.",
                        inv.SpanStart);
                HttpHelpers.Add("getJson");
                return $"__getJson({Expr(args[0].Expression)})";
            }
            case "GetStringAsync" when args.Count == 1:
                HttpHelpers.Add("getText");
                return $"__getText({Expr(args[0].Expression)})";
            case "PostAsJsonAsync" when m.TypeArguments.Length <= 1 && args.Count == 2:
            {
                var valueType = _model.GetTypeInfo(args[1].Expression).Type;
                if (Filament.Subset.TypeSubset.JsonUnfaithful(valueType, _recordsByName.Values.Select(r => r.Symbol).ToArray()) is { } offender)
                    return Refuse("unsupported-type",
                        $"PostAsJsonAsync serializes {offender}, whose JSON shape is not its Filament shape " +
                        "(decision 147). JSON-faithful types are int, double, bool, string, records of those, and " +
                        "List<T>/T[] of those. Refusing to emit.",
                        inv.SpanStart);
                HttpHelpers.Add("postJson");
                return $"__postJson({Expr(args[0].Expression)}, {Expr(args[1].Expression)})";
            }
        }
        return Refuse("unsupported-call",
            $"'{Trunc(inv.ToString())}' is not an HttpClient operation in the subset. Section 5 admits " +
            "GetFromJsonAsync<T>(url), GetStringAsync(url) and PostAsJsonAsync(url, value) -- the shapes fetch " +
            "maps faithfully (decision 147). Delete/Put/headers/HttpResponseMessage handling are deferred. " +
            "Refusing to emit.", inv.SpanStart);
    }

    /// <summary>
    /// LOCAL JSON (decision 157): JsonSerializer.Serialize(value) / .Deserialize&lt;T&gt;(json), default
    /// options only, T through the SAME shape gate the network uses (decision 147). The part the probe
    /// exposed: the runtime shape is camelCase with SIGNAL-wrapped mutated props, while C#'s default
    /// serializer writes the DECLARED PascalCase names, case-sensitively, in declaration order. So the
    /// conversion is SHAPE-AWARE per record, derived from RecordInfo: __serR writes declared names
    /// reading `.value` off signal props; __desR reads declared names and re-wraps mutated props in
    /// signal(). Scalars and lists of scalars need no converter at all. DISCLOSED divergence: STJ's
    /// default encoder escapes HTML-sensitive characters ('&lt;' -> <) where JSON.stringify does
    /// not -- ASCII-alphanumeric content is byte-identical, angle-bracketed content is not.
    /// </summary>
    string JsonInvocation(InvocationExpressionSyntax inv, IMethodSymbol m)
    {
        var args = inv.ArgumentList.Arguments;
        var records = _recordsByName.Values.Select(r => r.Symbol).ToArray();
        switch (m.Name)
        {
            case "Serialize" when args.Count == 1:
            {
                var t = _model.GetTypeInfo(args[0].Expression).Type;
                if (Filament.Subset.TypeSubset.JsonUnfaithful(t, records) is { } offender)
                    return Refuse("unsupported-type",
                        $"JsonSerializer.Serialize serializes {offender}, whose JSON shape is not its Filament " +
                        "shape (decision 157; the network's gate, decision 147). JSON-faithful types are int, " +
                        "double, bool, string, records of those, and List<T>/T[] of those. Refusing to emit.",
                        inv.SpanStart);
                return $"JSON.stringify({JsonConvertJs(t!, Expr(args[0].Expression), ser: true)})";
            }
            case "Deserialize" when m.TypeArguments.Length == 1 && args.Count == 1:
            {
                var t = m.TypeArguments[0];
                if (Filament.Subset.TypeSubset.JsonUnfaithful(t, records) is { } offender)
                    return Refuse("unsupported-type",
                        $"JsonSerializer.Deserialize<{t.ToDisplayString()}> deserializes {offender}, whose JSON " +
                        "shape is not its Filament shape (decision 157; the network's gate, decision 147). " +
                        "JSON-faithful types are int, double, bool, string, records of those, and List<T>/T[] " +
                        "of those. Refusing to emit.",
                        inv.SpanStart);
                return JsonConvertJs(t, $"JSON.parse({Expr(args[0].Expression)})", ser: false);
            }
        }
        return Refuse("unsupported-call",
            $"'{Trunc(inv.ToString())}' is not a JsonSerializer operation in the subset. Section 5 admits " +
            "Serialize(value) and Deserialize<T>(json) with DEFAULT options -- an options argument changes " +
            "the wire shape this compiler mirrors (decision 157). Refusing to emit.", inv.SpanStart);
    }

    /// <summary>The converter EXPRESSION between the runtime shape and the declared JSON shape:
    /// identity for scalars and lists of scalars, __serR/__desR for a record, .map(__serR) for a
    /// list/array of records. Also serves nested record-typed props (no parse/stringify here).</summary>
    string JsonConvertJs(ITypeSymbol t, string expr, bool ser)
    {
        if (t is INamedTypeSymbol && _recordsByName.TryGetValue(t.Name, out var ri))
            return $"{EnsureJsonHelper(ri, ser)}({expr})";
        if ((Filament.Subset.TypeSubset.ListElement(t) ?? Filament.Subset.TypeSubset.ArrayElement(t)) is { } el
            && _recordsByName.TryGetValue(el.Name, out var eri))
            return $"{expr}.map({EnsureJsonHelper(eri, ser)})";
        return expr;
    }

    /// <summary>Per-record JSON helpers (decision 157), emitted once each into the module.</summary>
    public readonly List<string> JsonHelperLines = [];
    readonly HashSet<string> _jsonHelpersDone = new(StringComparer.Ordinal);

    string EnsureJsonHelper(RecordInfo ri, bool ser)
    {
        var name = (ser ? "__ser" : "__des") + ri.Name;
        if (!_jsonHelpersDone.Add(name)) return name;
        var fields = new List<string>();
        foreach (var p in ri.Props)
        {
            if (ser)
            {
                var v = $"v.{p.Js}" + (p.IsSignal ? ".value" : "");
                fields.Add($"{p.Name}: {JsonConvertJs(p.Symbol.Type, v, ser: true)}");
            }
            else
            {
                var v = JsonConvertJs(p.Symbol.Type, $"o.{p.Name}", ser: false);
                if (p.IsSignal) { _primitives.Add("signal"); v = $"signal({v})"; }
                fields.Add($"{p.Js}: {v}");
            }
        }
        JsonHelperLines.Add($"function {name}({(ser ? "v" : "o")}) {{");
        JsonHelperLines.Add($"  return {{ {string.Join(", ", fields)} }};");
        JsonHelperLines.Add("}");
        return name;
    }

    /// <summary>`new Random(seed)` / `new Random()` -> `__rnd(seed)` / `__rnd(null)` (decision 146). The seed
    /// may be any int EXPRESSION -- the Knuth-subtractive algorithm runs at runtime in the emitted factory, so
    /// nothing needs to be constant. `null` selects the Math.random regime (both sides' unseeded sequences are
    /// arbitrary; range and distribution are the observable contract).</summary>
    string RandomConstruction(ObjectCreationExpressionSyntax oc)
    {
        var args = oc.ArgumentList?.Arguments ?? default;
        RandomHelpers.Add("rnd");
        if (args.Count == 0) return "__rnd(null)";
        if (args.Count == 1 &&
            _model.GetTypeInfo(args[0].Expression).ConvertedType?.SpecialType == SpecialType.System_Int32)
            return $"__rnd({Expr(args[0].Expression)})";
        return Refuse("unsupported-expression",
            "only `new Random()` and `new Random(int seed)` are in the subset (decision 146). Refusing to emit.",
            oc.SpanStart);
    }

    /// <summary>A Random instance method (decision 146): Next() -> .next(), Next(max) -> .nextTo(max),
    /// Next(min, max) -> .nextIn(min, max), NextDouble() -> .nextDouble() -- the emitted __rnd instance's
    /// interface, which reproduces the EXACT .NET seeded sequence. NextBytes fills a byte[] (not in the
    /// subset); NextInt64/NextSingle are deferred. Refused here so nothing unfaithful slips through.</summary>
    string RandomMethod(InvocationExpressionSyntax inv, IMethodSymbol symbol)
    {
        var args = inv.ArgumentList.Arguments;
        if (inv.Expression is MemberAccessExpressionSyntax ma)
            switch (symbol.Name, args.Count)
            {
                case ("Next", 0): return $"{Expr(ma.Expression)}.next()";
                case ("Next", 1): return $"{Expr(ma.Expression)}.nextTo({Expr(args[0].Expression)})";
                case ("Next", 2):
                    return $"{Expr(ma.Expression)}.nextIn({Expr(args[0].Expression)}, {Expr(args[1].Expression)})";
                case ("NextDouble", 0): return $"{Expr(ma.Expression)}.nextDouble()";
            }
        return Refuse("unsupported-call",
            $"'{Trunc(inv.ToString())}' is not a Random operation in the subset. Section 5 admits Next(), " +
            "Next(max), Next(min, max) and NextDouble() -- the exact .NET sequence when seeded (decision 146). " +
            "NextBytes fills a byte[] (not in the subset); NextInt64/NextSingle are deferred. Refusing to emit.",
            inv.SpanStart);
    }

    static string DecimalObject(decimal d)
    {
        var bits = decimal.GetBits(d);
        var mantissa = ((System.Numerics.BigInteger)(uint)bits[2] << 64)
                     | ((System.Numerics.BigInteger)(uint)bits[1] << 32)
                     | (uint)bits[0];
        if ((bits[3] & unchecked((int)0x80000000)) != 0) mantissa = -mantissa;
        var scale = (bits[3] >> 16) & 0xFF;
        return $"{{ m: {mantissa.ToString(System.Globalization.CultureInfo.InvariantCulture)}n, s: {scale} }}";
    }

    /// <summary>$"a {b}" -> `a ${b}`. A template literal, which is the same construct.</summary>
    string Interpolated(InterpolatedStringExpressionSyntax s)
    {
        var sb = new StringBuilder("`");
        foreach (var part in s.Contents)
            switch (part)
            {
                case InterpolatedStringTextSyntax t:
                    sb.Append(t.TextToken.ValueText.Replace("\\", @"\\").Replace("`", @"\`").Replace("${", @"\${"));
                    break;
                case InterpolationSyntax i when i.AlignmentClause is null && i.FormatClause is null:
                    sb.Append("${").Append(Expr(i.Expression)).Append('}');
                    break;
                default:
                    Refuse("unsupported-expression",
                        "an interpolation alignment/format clause is not in the subset: its semantics are " +
                        "C#'s composite formatting, which a Filament module does not ship. Refusing to emit.",
                        part.SpanStart);
                    break;
            }
        return sb.Append('`').ToString();
    }

    string Literal(LiteralExpressionSyntax lit) => lit.Kind() switch
    {
        SyntaxKind.NumericLiteralExpression => NumericLiteral(lit),
        SyntaxKind.StringLiteralExpression => JsString(lit.Token.ValueText),
        SyntaxKind.TrueLiteralExpression => "true",
        SyntaxKind.FalseLiteralExpression => "false",
        SyntaxKind.NullLiteralExpression => "null",
        _ => Refuse("unsupported-expression",
            $"the literal {Trunc(lit.ToString())} is not in the C# subset. Refusing to emit.", lit.SpanStart),
    };

    /// <summary>
    /// A number, via its RESOLVED VALUE and not its source text. `42.0` and `1_000` and `0x2A`
    /// are all just numbers in JS, and JS has one numeric type, so the C# suffix (`42.0d`)
    /// must not survive into the output -- it is not JS.
    /// </summary>
    string NumericLiteral(LiteralExpressionSyntax lit)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        // A literal in a LONG context -> a BigInt literal (`5` in `long x = 5`, a `5L`, or a value past int
        // range). `long`'s JS home is BigInt (decision 112): its integer display is exact where a double
        // would lose precision past 2^53, and BigInt division truncates toward zero exactly as C#'s long/long.
        if (_model.GetTypeInfo(lit).ConvertedType?.SpecialType == SpecialType.System_Int64)
            return lit.Token.Value switch
            {
                int i => $"{i.ToString(inv)}n",
                long l => $"{l.ToString(inv)}n",
                _ => Refuse("unsupported-type",
                    $"the numeric literal {Trunc(lit.ToString())} is long-typed but not integral. Refusing to emit.",
                    lit.SpanStart, "FIL0002"),
            };

        // A literal in a FLOAT context -> Math.fround of its decimal (decision 113). `3.14f` is the double
        // NEAREST the single 3.14f; storing it frounded keeps arithmetic in single precision, exactly C#.
        if (_model.GetTypeInfo(lit).ConvertedType?.SpecialType == SpecialType.System_Single)
            return lit.Token.Value switch
            {
                float f => $"Math.fround({f.ToString(inv)})",       // the float's shortest round-trip: 3.14f -> 3.14
                int i => $"Math.fround({i.ToString(inv)})",
                _ => Refuse("unsupported-type",
                    $"the numeric literal {Trunc(lit.ToString())} is float-typed but not numeric. Refusing to emit.",
                    lit.SpanStart, "FIL0002"),
            };

        // A literal in a DECIMAL context -> a { m: mantissa, s: scale } object (decision 114). `decimal` is a
        // 128-bit base-10 type with TRACKED SCALE (1.10m keeps its trailing zero); JS has no native decimal, so
        // it is a boxed BigInt mantissa + scale, and its arithmetic is the __dec* helpers (exact base-10).
        if (_model.GetTypeInfo(lit).ConvertedType?.SpecialType == SpecialType.System_Decimal)
            return lit.Token.Value switch
            {
                decimal d => DecimalObject(d),
                int i => DecimalObject(i),
                _ => Refuse("unsupported-type",
                    $"the numeric literal {Trunc(lit.ToString())} is decimal-typed but not numeric. Refusing to emit.",
                    lit.SpanStart, "FIL0002"),
            };

        return lit.Token.Value switch
        {
            int i => i.ToString(inv),
            long l => l.ToString(inv),   // a `5L` whose target is NOT long (e.g. `double d = 5L`) -> a plain number
            double d => d.ToString("R", inv),
            _ => Refuse("unsupported-type",
                $"the numeric literal {Trunc(lit.ToString())} is not an int, long or a double. Section 5's numeric " +
                "types are int, long and double. Refusing to emit.", lit.SpanStart, "FIL0002"),
        };
    }

    // JsBinaryOperator / JsPrefixOperator / JsAssignmentOperator moved to Filament.Subset.ConstructSubset
    // (single source of the operator subset; decisions 53/61). Expr's cases call them there.

    // ---- names --------------------------------------------------------------

    /// <summary>
    /// C# -> JS naming, and it is FORCED by the answer key rather than chosen.
    ///
    /// rows.js emits `row.id` and `row.label`, and canon keeps a property name after a dot
    /// LITERAL (its spelling carries meaning), so `row.Id` is a divergence and `row.id` is not
    /// -- the gate can see this and cannot see the rest. So: lower the first character when it
    /// is upper-case, uniformly, for every C# name that becomes a JS binding or property.
    /// `Id`->`id`, `Label`->`label`, `NextLabel`->`nextLabel`, `SwapRows`->`swapRows`,
    /// `_adjectives`->`_adjectives`, `currentCount`->`currentCount`. That reproduces every
    /// name in both keys.
    ///
    /// Names that CONVERGE under it are refused, not silently merged -- see the collision
    /// checks. Two members whose only difference is a capital letter are legal C# and would
    /// become one JS binding.
    /// </summary>
    static string JsName(string name) =>
        name.Length > 0 && char.IsUpper(name[0]) ? char.ToLowerInvariant(name[0]) + name[1..] : name;

    /// <summary>
    /// Two C# members that become ONE JS binding.
    ///
    /// ROUTE PARAMETERS ARE IN THIS SET (decision 163), and they were added because leaving them out was
    /// MEASURED, not imagined: `[Parameter] public int Id` beside `private int id` — legal C#, legal
    /// Blazor — emitted `const id = signal(__route.Id);` followed by `const id = 5;`. That is invalid
    /// JavaScript written at exit 0: a module that looks fine and does not load. A route parameter
    /// declares a binding in mount()'s scope exactly as a field does, so it belongs to the same rule.
    /// </summary>
    bool CheckJsNameCollisions()
    {
        var all = _fields.Select(f => (f.Name, f.Js, f.At))
            .Concat(_methods.Select(m => (m.Name, m.Js, m.At)))
            .Concat(_routeSignals.Select(r => (r.Name, r.Js, r.At)));
        // An ErrorContent puts `context` in the SAME class scope every slot is compiled in (decision 164),
        // so an author member of that name would be read by the ErrorContent instead of the caught
        // exception -- silently, and only inside the boundary. Refused at the author's declaration, which
        // is the one place a reader can act on it. Same rule, same reason, as the route parameter that
        // collides with a field (decision 163).
        if (_errorContextJs is not null &&
            all.FirstOrDefault(x => string.Equals(x.Name, "context", StringComparison.Ordinal)) is { Name: not null } clash)
        {
            Refuse("name-collision",
                $"@code declares '{clash.Name}', and this component's <ErrorBoundary> declares an ErrorContent, " +
                "whose caught exception Blazor also names `context`. Inside the ErrorContent the two are one " +
                "name, so `@context.Message` would read the declared member rather than the exception -- " +
                "silently, and only there. Rename the member. Refusing to emit.",
                clash.At);
            return false;
        }

        var dup = all.GroupBy(x => x.Js, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (dup is null) return true;

        Refuse("name-collision",
            $"@code declares {string.Join(" and ", dup.Select(x => $"'{x.Name}'"))}, which both map to the JS " +
            $"binding '{dup.Key}'. C#'s PascalCase members become JS's camelCase ones (both answer keys: " +
            "`SwapRows` is `swapRows`), so these two would silently become one. Refusing to emit.",
            dup.Last().At);
        return false;
    }

    /// <summary>Every JS binding @code puts in mount()'s scope. The template must not shadow one.</summary>
    public bool IsJsNameTaken(string js) =>
        _fields.Any(f => f.Js == js) || _methods.Any(m => m.Js == js) ||
        _routeSignals.Any(r => r.Js == js) ||
        _fields.Any(f => f.List is { } l && (l.Version == js || l.Changed == js));

    /// <summary>A synthesised binding name that cannot collide with one the author chose.</summary>
    string Unique(string want)
    {
        var taken = _fields.Select(f => f.Js).Concat(_methods.Select(m => m.Js)).ToHashSet(StringComparer.Ordinal);
        var name = want;
        for (var i = 2; taken.Contains(name); i++) name = want + i;
        return name;
    }

    // ---- diagnostics --------------------------------------------------------

    string Refuse(string reason, string message, int wrappedOffset, string code = "FIL0001")
    {
        // A refusal AFTER Compile() has returned is a refusal nobody will read: the caller has
        // already decided whether to write the file. That shipped once (decision 69), as a
        // module carrying `/*refused*/` at exit 0. It is now a loud tool failure.
        if (_sealed)
            throw new GeneratorException(
                $"FIL-WIRING: the C# front end raised [{reason}] after Compile() had finished. Diagnostics " +
                "raised at emission time are never reported -- the caller has already read the list -- so " +
                "this would exit 0 and write a module built from a refused translation. Every construct must " +
                "be decided inside Compile(). This is the TOOL being broken, not the input.");

        _diagnostics.Add(new Diagnostic(code, reason, message, _src.Map(wrappedOffset)));
        return "/*refused*/";
    }

    static string Describe(SyntaxNode n) =>
        n.Kind().ToString().Replace("Syntax", "") + " (`" + Trunc(n.ToString(), 40) + "`)";

    static string Trunc(string s, int n = 60)
    {
        s = s.Replace("\r", "").Replace("\n", "\\n");
        return s.Length <= n ? s : s[..n] + "...";
    }

    /// <summary>
    /// One IR node's C#, as the author wrote it.
    ///
    /// @key IS A SPECIAL CASE AND IT HAD TO BE FOUND BY RUNNING. SetKeyIntermediateNode has
    /// NO token children -- its value hangs off a KeyValueToken PROPERTY -- so the generic
    /// descendant walk returns "" for it. Measured: `__filament_s2();` with no argument, i.e.
    /// the @key expression silently vanishing on its way to the parser. It is exactly the
    /// class of hole this compiler exists to not have, and it is caught here rather than
    /// downstream because a `@key` that reaches list() as nothing is a list with no identity.
    /// </summary>
    static string RawText(IntermediateNode n) => n is SetKeyIntermediateNode k
        ? k.KeyValueToken?.Content ?? ""
        : string.Concat(n.FindDescendantNodes<IntermediateToken>().Select(t => t.Content));

    static string JsString(string s)
    {
        var sb = new StringBuilder("'");
        foreach (var c in s)
            sb.Append(c switch
            {
                '\\' => @"\\",
                '\'' => @"\'",
                '\n' => @"\n",
                '\r' => @"\r",
                '\t' => @"\t",
                _ => c.ToString(),
            });
        return sb.Append('\'').ToString();
    }
}
