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
    public void BindParameters(IReadOnlyDictionary<string, string> bindings, IReadOnlyCollection<string>? reactive = null)
    {
        foreach (var kv in bindings) _paramEnv[kv.Key] = kv.Value;
        if (reactive is not null) foreach (var n in reactive) _paramReactive.Add(n);
    }

    /// <summary>A LEAF DISPLAY child: only [Parameter] props, no state (fields/signals), no behaviour
    /// (methods), no records. The static-leaf composition slice compiles only these; a stateful or
    /// eventful child is refused rather than half-compiled (decision 88).</summary>
    public bool IsLeafDisplay => _fields.Count == 0 && _methods.Count == 0 && _records.Count == 0;

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

        /// <summary>counter.js + rows.js decision (2), CONJOINED. Decision 67.</summary>
        public bool IsSignal => List is null && ReadByTemplate && AssignedOutsideConstruction;
    }

    /// <summary>A List&lt;T&gt; field: a mutable array, and -- iff anything mutates it -- a version signal.</summary>
    sealed class ListInfo
    {
        public bool Mutated;
        /// <summary>Every element is a literal, so the whole thing is inert data (rows.js decision 4).</summary>
        public bool LiteralData;
        public string Version = "";
        public string Changed = "";

        /// <summary>
        /// rows.js mapping decision (4), STATED THERE AS A RULE: "hoisting immutable literal
        /// lists to module scope is a generator-level constant-folding decision and changes
        /// nothing about the work done per row". Immutable AND literal, both, or it stays in
        /// mount() where the component's state lives.
        /// </summary>
        public bool Hoisted => !Mutated && LiteralData;
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
        public MethodDeclarationSyntax Syntax = null!;
        public IMethodSymbol Symbol = null!;
        public int At;
        public int CallUses;
        public List<MethodInfo> Callees = [];
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
    }

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

    /// <summary>
    /// Whether one @expression READS reactive state -- i.e. whether its binding needs an
    /// effect or is a create-time write. THE ANSWER COMES FROM THIS COMPILER'S OWN TABLE
    /// (decision 57), never from what the text looks like.
    /// </summary>
    public bool SlotIsReactive(IntermediateNode node) => Get(node).Reactive;

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
            "partial class __FilamentComponent {");
        foreach (var node in codeNodes) _src.Node(node, RawText(node));
        _src.Literal("\n}\npartial class __FilamentComponent {\n");

        void Slot(IntermediateNode n)
        {
            var i = slots.Count;
            slots.Add(n);
            _src.Literal($"__filament_s{i}(");
            _src.Node(n, RawText(n));
            _src.Literal(");\n");
        }

        var decls = new StringBuilder();
        for (var i = 0; i < CountSlots(plan); i++) decls.Append($"void __filament_s{i}(params object[] a) {{}}\n");
        _src.Literal(decls.ToString());

        var markerCount = plan.Regions.Sum(r => r.Items.OfType<MarkupItem>().Count());
        for (var i = 0; i < markerCount; i++) _src.Literal($"void __filament_m{i}() {{}}\n");

        // Free expressions: at CLASS scope, which is the scope the template reads them in.
        _src.Literal("void __filament_free() {\n");
        foreach (var n in plan.FreeSlots) Slot(n);
        _src.Literal("}\n");

        // Regions: THE REASSEMBLY. Decision 54's unbalanced spans, put back together with the
        // markup as calls, so the braces close and Roslyn can see a loop.
        var regionMethods = new List<string>();
        var m = 0;
        for (var r = 0; r < plan.Regions.Count; r++)
        {
            var name = $"__filament_t{r}";
            regionMethods.Add(name);
            _src.Literal($"void {name}() {{\n");
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
                        break;
                }
            _src.Literal("\n}\n");
        }

        // Lambda event handlers (decision 105): each body as a synthetic method at class scope, so it is
        // marked + translated exactly like a @code method body. Mapped to the attribute's source so a
        // diagnostic inside the lambda points at the handler. Bodies that are not the no-arg lambda form
        // are skipped here (the harvest regex admits only `() => …`, so this is belt-and-braces).
        var lambdaNames = new List<(IntermediateNode Attr, string Name)>();
        for (var k = 0; k < plan.LambdaHandlers.Count; k++)
        {
            if (LambdaMethodBody(plan.LambdaHandlers[k].RawHandler) is not { } mbody) continue;
            var lname = $"__filament_lambda_{k}";
            lambdaNames.Add((plan.LambdaHandlers[k].Attr, lname));
            _src.Literal($"void {lname}() ");
            _src.Node(plan.LambdaHandlers[k].Attr, mbody);
            _src.Literal("\n");
        }
        _src.Literal("}\n");

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

        var compilation = CSharpCompilation.Create("FilamentCode", [tree], ReferenceAssemblies.ForCode(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

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
            var syntax = FindMethod(classes[1], name);
            if (syntax?.Body is null || _model.GetDeclaredSymbol(syntax) is not { } sym) continue;
            _lambdaMethods.Add((attr, new MethodInfo { Name = name, Syntax = syntax, Symbol = sym }));
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
        MarkTemplateReads(slots);
        MarkConditionReads(classes[1], regionMethods);

        // 3. the call graph: who calls whom, for the inlining arbitrage AND for the emission
        //    order (callees before callers -- see MethodsInDependencyOrder).
        foreach (var mi in _methods) MarkCalls(mi.Syntax);
        foreach (var (_, lm) in _lambdaMethods) MarkCalls(lm.Syntax, lm);

        // 4. TRANSLATE EVERYTHING, NOW -- not lazily at emission. Every diagnostic this input
        //    can produce must exist before Compile() returns, because that is when the caller
        //    reads them. See _sealed for the defect that made this ordering load-bearing.
        foreach (var f in _fields.Where(f => f.List is { Hoisted: false, Mutated: true }))
        {
            f.List!.Version = Unique(f.Js + "Version");
            f.List.Changed = Unique(f.Js + "Changed");
        }
        foreach (var mi in _methods) _bodies[mi.Name] = Body(mi.Syntax.Body!);
        foreach (var (attr, lm) in _lambdaMethods)
        {
            _lambdaBodies[attr] = Body(lm.Syntax.Body!);
            if (CountWrites(lm, []) > 1) _lambdaBatched.Add(attr);
        }
        TranslateSlots(slots);
        foreach (var (container, method) in plan.Regions.Zip(regionMethods))
            _ops[container.Container] = RegionOps(FindMethod(classes[1], method).Body!.Statements, markers);

        // 5. THE BACKSTOP: is this even C#? Roslyn's OWN verdict, asked LAST.
        if (_diagnostics.Count == 0) CheckSemantics(compilation);

        _sealed = true;
    }

    static int CountSlots(TemplatePlan plan) =>
        plan.FreeSlots.Count + plan.Regions.Sum(r => r.Items.OfType<MarkupItem>().Sum(m => m.Slots.Count));

    static MethodDeclarationSyntax FindMethod(ClassDeclarationSyntax cls, string name) =>
        cls.Members.OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == name);

    // ---- the template's own C# ----------------------------------------------

    /// <summary>
    /// The re-parsed region, turned back into "emit this, then that". Every statement is
    /// positively accounted for: this is the walk decision 54 said Razor makes impossible,
    /// so it is exactly the place where an unaccounted node would become silent wrong JS.
    /// </summary>
    List<TemplateOp> RegionOps(IEnumerable<StatementSyntax> statements, IReadOnlyDictionary<string, IntermediateNode> markers)
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
                if (ForEach(fe, markers) is { } op) ops.Add(op);
                continue;
            }

            if (s is IfStatementSyntax ifs)
            {
                if (If(ifs, markers) is { } op) ops.Add(op);
                continue;
            }

            Refuse("unsupported-template-statement",
                $"{Describe(s)} in the template is not in the subset. The template admits @foreach (spec 5) " +
                "and markup; C# STATEMENTS in a template have no place to run -- a Filament module builds its " +
                "tree once and never re-renders, so there is no render method for them to be part of. " +
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
        // The collection must be a List<T> FIELD: list()'s source has to subscribe to
        // something, and the version signal is the only thing there is to subscribe to.
        if (_model.GetSymbolInfo(fe.Expression).Symbol is not IFieldSymbol fs ||
            Field(fs) is not { List: { } li } f)
        {
            Refuse("unsupported-foreach",
                $"@foreach iterates '{Trunc(fe.Expression.ToString())}', which is not a List<T> field declared " +
                "in this component. list() reconciles against a source it can SUBSCRIBE to, and the only " +
                "subscribable thing a List<T> has is the version signal this compiler gives it (rows.js " +
                "mapping decision 1). Refusing to emit.",
                fe.Expression.SpanStart);
            return null;
        }

        if (!li.Mutated)
        {
            Refuse("unsupported-foreach",
                $"@foreach iterates '{f.Name}', which nothing in this component ever mutates, so it has no " +
                "version signal and list() would have no source to re-run on. A never-mutated list rendered " +
                "once is a static tree; that mapping is not implemented and is not exercised by either " +
                "answer key. Refusing to emit.",
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

        return new ForEachOp(JsName(fe.Identifier.Text), f.Js, li.Version, markup[0].Node, key);
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

        var allMarkup = ops.Count >= 1 && ops.All(o => o is MarkupOp);
        var singleNestedIf = ops.Count == 1 && ops[0] is IfOp;

        if (!allMarkup && !singleNestedIf)
        {
            Refuse("unsupported-if-body",
                $"a template @if / @else branch body must be one or more elements, OR a single nested @if, and " +
                $"nothing else; this one produces {ops.Count} thing(s). Mixing markup with nested control flow, " +
                "multiple nested @ifs, a @foreach in a branch, or a stray text node is not in the subset. " +
                "Refusing to emit.",
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
                .All(Filament.Subset.ConstructSubset.IsParameterAttribute))
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
            case PropertyDeclarationSyntax p: ParamDecl(p); break;
            default:
                throw new GeneratorException(
                    $"FIL-WIRING: ClassifyMember admitted {member.Kind()} but Member() has no case for it. " +
                    "The subset decision and the translator have drifted. Refusing to emit.");
        }
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
        if (!CheckType(type, p.Type.SpanStart, allowList: true)) return;

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

        if (rec.ParameterList is not null)
        {
            Refuse("unsupported-member",
                $"record '{rec.Identifier.Text}' has a positional parameter list. A positional record's members " +
                "are init-only and it carries a compiler-generated constructor, Equals, GetHashCode and " +
                "Deconstruct -- none of which a Filament module has anywhere to put. Declare the properties in " +
                "a body instead. Refusing to emit.",
                rec.Identifier.SpanStart);
            return;
        }

        var info = new RecordInfo
        {
            Name = rec.Identifier.Text,
            Symbol = (INamedTypeSymbol)_model.GetDeclaredSymbol(rec)!,
        };

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
                info.Init = v.Initializer is { } init ? Expr(init.Value) : DefaultOf(type!);
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

        var elements = oc.Initializer?.Expressions ?? default;
        var parts = new List<string>();
        var literal = true;
        foreach (var e in elements)
        {
            if (e is not LiteralExpressionSyntax) literal = false;
            parts.Add(Expr(e));
        }

        info.Init = "[" + string.Join(", ", parts) + "]";
        info.List = new ListInfo { LiteralData = literal };
        return true;
    }

    /// <summary>C# default(T), SPELLED. `private int x;` is 0 in C#; the JS binding must say so.</summary>
    static string DefaultOf(ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_Int32 => "0",
        SpecialType.System_Double => "0",
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
        foreach (var mod in method.Modifiers)
            if (!IsAllowedModifier(mod))
            {
                Refuse("unsupported-modifier",
                    $"'{mod.Text}' on a method is not in the subset. Refusing to emit.", mod.SpanStart);
                return;
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
        if (returnType is { SpecialType: not SpecialType.System_Void })
            if (!CheckType(returnType, method.ReturnType.SpanStart)) return;

        var info = new MethodInfo
        {
            Name = method.Identifier.Text,
            Js = JsName(method.Identifier.Text),
            Syntax = method,
            Symbol = _model.GetDeclaredSymbol(method)!,
            At = method.Identifier.SpanStart,
        };

        foreach (var p in method.ParameterList.Parameters)
        {
            if (!CheckType(_model.GetTypeInfo(p.Type!).Type, p.SpanStart)) return;
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
    void MarkCalls(MethodDeclarationSyntax method) => MarkCalls(method, _methodsByName[method.Identifier.Text]);

    /// <summary>The caller is passed in (rather than looked up by name) so a lambda handler's synthetic
    /// method -- which is NOT in _methodsByName -- can still record the @code methods its body calls,
    /// bumping their CallUses correctly (decision 105).</summary>
    void MarkCalls(MethodDeclarationSyntax method, MethodInfo caller)
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

    void MarkConstructionSites(MethodDeclarationSyntax method)
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
    void MarkAssignments(MethodDeclarationSyntax method)
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
            if (node.Ancestors().OfType<ExpressionStatementSyntax>().FirstOrDefault() is { } st &&
                folded.Contains(st)) continue;

            switch (_model.GetSymbolInfo(target).Symbol)
            {
                case IFieldSymbol fs when Field(fs) is { } f:
                    f.AssignedOutsideConstruction = true;
                    break;
                case IPropertySymbol ps when PropAnywhere(ps) is { } p:
                    p.AssignedOutsideConstruction = true;
                    break;
            }
        }
    }

    void MarkListMutations(MethodDeclarationSyntax method)
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
        foreach (var ifs in FindMethod(regionClass, method).Body!.DescendantNodes().OfType<IfStatementSyntax>())
            MarkReads(ifs.Condition);
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
            _slots[node] = new Slot { Js = Expr(e), Reactive = IsReactive(e) };
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
            if (n is not (IdentifierNameSyntax or MemberAccessExpressionSyntax)) continue;
            switch (_model.GetSymbolInfo(n).Symbol)
            {
                case IFieldSymbol fs when Field(fs) is { IsSignal: true }: return true;
                case IPropertySymbol ps when PropAnywhere(ps) is { IsSignal: true }: return true;
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

        foreach (var f in _fields)
        {
            if (f.List is { Hoisted: true }) continue;

            if (f.List is { } li)
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

        foreach (var m in MethodsInDependencyOrder())
        {
            if (inlined.Contains(m.Name)) continue;

            // `function`, not `const f = () =>`, and that is a correctness point rather than a
            // style one: C# methods are mutually visible regardless of declaration order, and
            // only a function DECLARATION hoists.
            if (lines.Count > 0 && lines[^1].Length > 0) lines.Add("");
            lines.Add($"function {m.Js}({string.Join(", ", m.Parameters)}) {{");
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

    int CountWrites(MethodInfo m, HashSet<string> seen)
    {
        if (!seen.Add(m.Name)) return 2; // recursion: cannot prove "once". Batch it.

        var n = 0;
        foreach (var node in m.Syntax.Body!.DescendantNodes())
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

    string Expr(ExpressionSyntax e)
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

            case BinaryExpressionSyntax b when Filament.Subset.ConstructSubset.JsBinaryOperator(b) is { } op:
                return $"{Expr(b.Left)} {op} {Expr(b.Right)}";

            case PrefixUnaryExpressionSyntax p when Filament.Subset.ConstructSubset.JsPrefixOperator(p) is { } op:
                return $"{op}{Expr(p.Operand)}";

            // currentCount++ -> currentCount.value++. One node in, one node out, no syntactic
            // desugaring (counter.js's header states this mapping exactly).
            case PostfixUnaryExpressionSyntax p when p.Kind() is
                SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression:
                return $"{Expr(p.Operand)}{p.OperatorToken.Text}";

            case ConditionalExpressionSyntax c:
                return $"{Expr(c.Condition)} ? {Expr(c.WhenTrue)} : {Expr(c.WhenFalse)}";

            case AssignmentExpressionSyntax a when Filament.Subset.ConstructSubset.JsAssignmentOperator(a) is { } op:
                return $"{Expr(a.Left)} {op} {Expr(a.Right)}";

            case InvocationExpressionSyntax inv:
                return Invocation(inv);

            case MemberAccessExpressionSyntax ma:
                return MemberAccess(ma);

            // _rows[i]. A List<T> IS an array here, so this is the array's own indexer.
            case ElementAccessExpressionSyntax ea when ListReceiver(ea.Expression) is not null &&
                                                       ea.ArgumentList.Arguments.Count == 1:
                return $"{Expr(ea.Expression)}[{Expr(ea.ArgumentList.Arguments[0].Expression)}]";

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

            // A [Parameter] read, at a composition site: `@Name` resolves to the JS the parent supplied
            // — a folded CONSTANT for a static leaf (`'World'`, decision 88), or a translated parent
            // EXPRESSION for a bound param (`count.value`, decision 90). When that expression reads a
            // parent signal, IsReactive (via _paramReactive) makes this a live effect; the child inlines
            // into the parent's scope, so the signal it names is reachable.
            case IPropertySymbol ps when _paramEnv.TryGetValue(ps.Name, out var boundJs):
                return boundJs;

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
        // `_rows.Count` -- the List's length. A JS array's own property, so no call and no
        // wrapper: the mapping IS the array (rows.js decision 1).
        if (ma.Name.Identifier.Text == "Count" && ListReceiver(ma.Expression) is not null)
            return $"{Expr(ma.Expression)}.length";

        if (_model.GetSymbolInfo(ma).Symbol is IPropertySymbol ps && PropAnywhere(ps) is { } p)
            return p.IsSignal ? $"{Expr(ma.Expression)}.{p.Js}.value" : $"{Expr(ma.Expression)}.{p.Js}";

        Refuse("unsupported-expression",
            $"'{Trunc(ma.ToString())}' is not member access on a record declared in this component. Section 5 " +
            "admits member access on a LOCAL RECORD and List<T>'s .Count; a Filament module ships no BCL, so " +
            "there is nothing else for a dot to reach. Refusing to emit.",
            ma.SpanStart);
        return "/*refused*/";
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
    string NumericLiteral(LiteralExpressionSyntax lit) => lit.Token.Value switch
    {
        int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
        double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        _ => Refuse("unsupported-type",
            $"the numeric literal {Trunc(lit.ToString())} is not an int or a double. Section 5's numeric " +
            "types are int and double. Refusing to emit.", lit.SpanStart, "FIL0002"),
    };

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

    bool CheckJsNameCollisions()
    {
        var all = _fields.Select(f => (f.Name, f.Js, f.At)).Concat(_methods.Select(m => (m.Name, m.Js, m.At)));
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
