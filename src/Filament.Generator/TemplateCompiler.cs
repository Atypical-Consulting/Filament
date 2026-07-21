using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Intermediate;

namespace Filament.Generator;

/// <summary>
/// Razor IR -> Filament JS. The whole compiler.
///
/// SHAPE OF THE OUTPUT, and why it is this shape (it is the answer key's shape,
/// samples/Counter/counter.js, which is the reference this is judged against):
///
///   mount(target) {
///     &lt;@code JS, spliced verbatim&gt;      state lives here
///     &lt;create&gt;                          the whole tree, DETACHED, depth-first
///     &lt;bindings&gt;                        one effect per binding point
///     &lt;events&gt;                          one listen per event attribute
///     &lt;attach&gt;                          roots into target, LAST
///   }
///
/// Attach is last on purpose: the effects' first run then writes into a detached
/// tree and produces no MutationRecord, so everything the harness's observer sees
/// at startup is the attach itself, and every increment after it is the single
/// characterData write. That is the C3 claim, and it is a property of the emission
/// ORDER, not of the runtime.
///
/// PHASE 3 CHANGED THE SEAM. @code is C# again (spec 5) and CSharpFrontEnd compiles it:
/// `private int currentCount = 0` is LIFTED to `const currentCount = signal(0)` by this
/// compiler rather than declared by hand in a JS seam. Two consequences here:
///
///   - The prologue is EMITTED, not spliced. There is no verbatim user text left in the
///     output at all, which retires the whole class of defect the Phase 2 header below
///     describes -- there is nothing left to splice.
///   - The read path no longer GUESSES. Decision 57's disclosed hole ("un @x sur un
///     `let x = 5` ordinaire emettrait `x.value` -- faux") existed because @code was
///     opaque and "assume every binding is a signal" was the only rule available.
///     EmitBinding now asks CSharpFrontEnd.IsSignal() -- the compiler's own record of
///     what it lifted. See CSharpFrontEnd's header.
///
/// STILL NOT HERE: @foreach/@if. Per decision 54 they arrive as RAW C# TEXT with
/// unbalanced braces and the element is a SIBLING of the loop header, not a child --
/// Razor never structures control flow because Blazor never needs it to. Rows is the
/// next step; they are still refused, with located diagnostics.
///
/// ---------------------------------------------------------------------------------
/// HOW THIS COMPILER REFUSES, AND WHY IT IS BUILT INSIDE-OUT (section 10: "Toute
/// construction hors sous-ensemble doit produire un diagnostic, jamais du JS
/// silencieusement faux.")
///
/// The dangerous shape for a compiler is a walk that handles the nodes it knows and
/// LETS THE REST FALL THROUGH. This one used to have that shape at the declaration
/// level: it reached into the class for BuildRenderTree and for @code and IGNORED
/// every other child. Measured consequences, each of which emitted a clean, plausible,
/// WRONG module with no diagnostic at all:
///
///     @inject IFoo Foo   ->  ComponentInjectIntermediateNode   (class child)      DROPPED
///     @page "/counter"   ->  RouteAttributeExtensionNode       (namespace child)  DROPPED
///     @layout Main       ->  CSharpCodeIntermediateNode        (namespace child)  DROPPED
///     @attribute [X]     ->  CSharpCodeIntermediateNode        (namespace child)  DROPPED
///     @using Foo         ->  UsingDirectiveIntermediateNode    (namespace child)  DROPPED
///     @inherits/@implements/@typeparam -> ClassDeclaration PROPERTIES             DROPPED
///     &lt;Counter /&gt;        ->  MarkupElementIntermediateNode &lt;Counter&gt;             EMITTED
///                            as document.createElement('Counter') -- an unknown
///                            element that renders nothing. Razor itself says nothing.
///
/// So the rule is inverted: EVERY node must be positively ACCOUNTED FOR, and anything
/// this compiler does not structurally understand is FIL0003. Adding a Razor feature
/// therefore starts with a failing diagnostic, never with silence.
///
/// TWO INDEPENDENT GATES, because they fail differently:
///   1. the DIRECTIVE gate  -- driven by Razor's own directive table, so it is
///      complete by construction and carries the directive's EXACT span.
///   2. the NODE gate       -- an allowlist over the IR, which catches everything that
///      is not spelled as a directive (@if, @bind, components, unknown nodes).
/// Either one alone would have holes. Over-reporting is safe; silence is the sin.
///
/// ---------------------------------------------------------------------------------
/// EVERY C# THE TEMPLATE NAMES GOES THROUGH ONE GUARD -- NamedByTemplate().
///
/// This is the fix for the worst defect this generator has shipped, and it is decision
/// 41's pattern for the THIRD time in this repo: the guard existed on the READ path and
/// the IDENTICAL hole sat ONE FRAME OVER on the EVENT path.
///
///     EmitBinding   (@currentCount)   GUARDED: bare identifier, or FIL0003.
///     EmitAttribute (@onclick="...")  UNGUARDED: it emitted `listen(el,'click',{handler})`
///                                     with the handler SPLICED VERBATIM, no checks at all.
///
/// Measured against the committed tree, not theorised:
///
///     @onclick="() => currentCount++"  ->  exit 0, ZERO diagnostics, FILE WRITTEN,
///                                          listen(_el0,'click',() => currentCount++)
///
/// Driven in a browser, that module LOADS, mount() RETURNS, the page renders "0", and
/// after three clicks it still renders "0". The page looks perfect and THE BUTTON IS
/// DEAD FOREVER. Both halves of section 10 violated at once: no diagnostic AND silently
/// false JS. (`currentCount` is a signal OBJECT that @code binds with `const`, so
/// `currentCount++` throws inside the listener -- once per click, forever, changing
/// nothing.)
///
/// So the guard goes on the INVARIANT, not on the line the repro pointed at (decision
/// 36's rule): NamedByTemplate() is the ONLY way a name the template writes reaches the
/// output, and BOTH paths call it. In Phase 3 a handler may legitimately be more than a
/// bare identifier -- but it will get there through the C# subset's validation, never
/// through a verbatim splice.
///
/// DIAGNOSTIC CODES. Phase 2 owns exactly ONE: FIL0003, "Razor construct outside the
/// phase's subset", carrying a [reason] tag and a location. FIL0001/FIL0002 are the
/// C# subset's, i.e. Phase 3's, and are deliberately NOT emitted here. The generator
/// used to mint its own FIL001..FIL011 out of a private 3-digit scheme that reads
/// exactly like the spec's reserved codes at a glance; a tool must not squat the
/// namespace it reports in. Failures of the TOOL (bad wiring, an IR shape that cannot
/// exist) are not "your Razor is unsupported" and carry FIL-WIRING, which cannot be
/// mistaken for a spec code.
/// </summary>
public sealed class TemplateCompiler
{
    /// <summary>
    /// The runtime's exports, in the order src/filament-runtime/src/index.ts declares
    /// them. The import clause is emitted in THIS order, filtered to what is used, so
    /// that the clause is a function of the runtime's surface and not of the order in
    /// which this compiler happened to discover things.
    /// </summary>
    static readonly string[] RuntimeExports =
        ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];

    /// <summary>
    /// The ONLY directive this compiler accepts. @code is the seam, and decision 57
    /// pins that Razor hands it back as one opaque verbatim token -- which is what lets
    /// Phase 3 hand the whole block to Roslyn. Every other directive Razor recognises --
    /// @page, @inject, @layout, @inherits, @implements, @typeparam, @attribute,
    /// @namespace, @preservewhitespace, ... -- is out of subset and must SAY SO.
    /// Driving this off Razor's own directive table (rather than off a list of node
    /// types this compiler happens to know) is what makes the gate complete: a
    /// directive nobody here has heard of is refused too.
    /// </summary>
    static readonly HashSet<string> AllowedDirectives = new(StringComparer.Ordinal) { "code", "inject", "typeparam", "inherits", "page", "using" };

    /// <summary>
    /// The base class Razor gives EVERY component whether or not @inherits was written.
    /// It is the default, so it is the thing to compare against; treating a non-null
    /// BaseType as evidence of @inherits refuses Counter itself.
    /// </summary>
    const string ComponentBaseType = "Microsoft.AspNetCore.Components.ComponentBase";

    /// <summary>
    /// Static attributes written as DOM PROPERTIES rather than via setAttr(). Both answer
    /// keys write `h1.id = 'title'` / `main.id = 'main'`, and a property write is one less
    /// runtime call and one less string lookup than setAttribute.
    ///
    /// `class` USED TO BE HERE, MAPPED TO className, AND IT WAS WRONG -- not unsafe, but not
    /// the reference. rows.js writes `setAttr(td1, 'class', 'col-md-1')`, and rows.js is the
    /// only artifact that says anything at all about `class`: Counter has no class attribute,
    /// so the className mapping was a Phase 2 guess that NO measurement covered. Decisions
    /// 21/51 make the answer key the reference, so the guess is withdrawn rather than defended.
    /// The map stays an ALLOWLIST because the attribute->property correspondence is not general
    /// (plenty of attributes have no property at all); anything not named here goes through
    /// setAttr, which is always correct.
    /// </summary>
    static readonly Dictionary<string, string> PropertyAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["id"] = "id",
    };

    /// <summary>
    /// Attribute names whose value MAY be a compiled dynamic expression (reactive or create-time),
    /// mirroring EmitBinding's text path. An ALLOWLIST, like PropertyAttributes and AllowedDirectives:
    /// `class` is the MEASURED one (BENCH n°13); every other name keeps the dynamic-attribute refusal,
    /// which is precisely what keeps @bind's lowered `value=` refused with its exact message. Widening
    /// this set is a NEW measured slice each time -- boolean/present-absent attributes (disabled) need a
    /// different emission (present/absent, not setAttr of "true"), so they are not admitted by adding a
    /// name here.
    /// </summary>
    static readonly HashSet<string> DynamicAttributes = new(StringComparer.OrdinalIgnoreCase) { "class", "title", "href", "aria-label", "role", "style" };

    /// <summary>A reactive/composed STRING attribute: an allow-listed name OR any `data-*` custom attribute
    /// (all data-* names carry string values and are safe to compose, so they are admitted by prefix rather
    /// than one at a time). `value` is deliberately absent so @bind's lowered value= stays refused.</summary>
    static bool IsDynamicStringAttribute(string name) =>
        DynamicAttributes.Contains(name) || name.StartsWith("data-", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Attribute names whose value MAY be a compiled BOOLEAN expression, rendered present/absent (a second
    /// allowlist beside DynamicAttributes, disjoint from it). `disabled` is the MEASURED one (BENCH n°14):
    /// `disabled="@b"` compiles to `effect(() => setAttr(el, 'disabled', b.value ? '' : null))` -- the
    /// present/absent contract via setAttr's own null->remove branch (true -> '' -> setAttribute, false ->
    /// null -> removeAttribute), NOT the naive `setAttr(el,'disabled',true)` that yields disabled="true".
    /// Name-based because the generator does no type inference: `disabled` is COMMITTED to boolean
    /// present/absent (a string-typed `disabled` is a deferred, distinct case). Widening this set is a NEW
    /// measured slice each time.
    /// </summary>
    static readonly HashSet<string> BooleanAttributes = new(StringComparer.OrdinalIgnoreCase) { "disabled", "hidden", "required" };

    /// <summary>
    /// A bare identifier, and nothing else. The ONE spelling this phase's template may
    /// use to name state or a handler; see NamedByTemplate().
    /// </summary>
    static readonly Regex BareIdentifier = new(@"^[A-Za-z_$][A-Za-z0-9_$]*$", RegexOptions.Compiled);

    /// <summary>
    /// THE @code BLOCK, PARSED. Phase 2 scraped names out of the JS seam with a regex and
    /// disclosed the limits it could not fix without a parser; Phase 3 has a parser, so
    /// the scrape is GONE and with it both of its documented residuals:
    ///   - it over-refused (`const a = 1, b = 2;` bound only `a`; destructuring bound
    ///     nothing), so valid handlers were refused;
    ///   - it over-accepted in the dangerous direction (`let Foo = () => {}; Foo = {};`
    ///     read as callable forever, because a regex cannot see a reassignment).
    /// Roslyn answers both from symbols instead of from spelling.
    /// </summary>
    // Not readonly: swapped to a CHILD component's front end during EmitComposition, then restored
    // (the same save/restore idiom EmitBranchFn uses for _create/_bindings).
    CSharpFrontEnd _code = new();

    List<string> _create = [];
    List<string> _bindings = [];
    readonly List<string> _events = [];
    readonly List<string> _attach = [];
    readonly HashSet<string> _used = [];
    readonly List<Diagnostic> _diagnostics = [];
    bool _needsFloatFormat;   // some @expr is float-typed -> Render emits the __f32 helper (decision 113)
    bool _needsDateTimeFormat;   // some @expr is DateTime-typed -> Render emits the __dtStr helper (decision 115)

    /// <summary>The author's `@using` namespaces for the component being prepared (decision 147): a
    /// NAME-RESOLUTION directive, harvested into the wrapped source's usings after resolving against the
    /// reference assemblies -- an unresolvable one is refused at its span exactly as Blazor's build
    /// (CS0246) would refuse it. Cleared per PrepareComponent.</summary>
    readonly List<string> _authorUsings = [];

    /// <summary>
    /// Every event site the template names, RECORDED during the walk and emitted after
    /// it. The delay is load-bearing: whether a handler's body is INLINED depends on how
    /// many times the whole template names it, which is not known until the walk ends.
    /// Pre-scanning the tree a second time to find out would mean a SECOND copy of the
    /// EventCallback unwrapping -- decision 53's exact trap, where the wiring existed
    /// twice and a test measured the copy.
    /// </summary>
    readonly List<(string El, string Event, string Handler)> _handlers = [];

    /// <summary>Elements whose recorded handler must call preventDefault() first -- today only a
    /// &lt;form&gt;'s submit (decision 138). Without it the browser navigates and the page reloads, which
    /// is exactly what Blazor's EditForm suppresses; it is part of the mapping, not a nicety.</summary>
    readonly HashSet<string> _preventDefault = [];

    /// <summary>
    /// The @key node the enclosing list() has already consumed. @key outside a list is still
    /// refused -- it names an identity nothing reconciles.
    /// </summary>
    IntermediateNode? _consumedKey;

    /// <summary>
    /// The containers whose children held template C#. Their emission comes from OpsFor.
    /// A root @foreach/@if makes the METHOD a container (decision 89), so this can hold it too.
    /// </summary>
    HashSet<IntermediateNode> _regions = [];

    string _file = "";
    int _el, _tx;
    int _if;

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    void Diag(string reason, string message, SourceSpan? source) =>
        _diagnostics.Add(new Diagnostic("FIL0003", reason, message, source));

    /// <summary>
    /// Gates 1–3 for ONE component (the parent, OR a child at a composition site): the directive
    /// allowlist, AccountForDocument, the root-level-C# refusal, and the single @code compilation.
    /// Returns the render method and its region set. Shared by Compile and EmitComposition so a child
    /// is gated EXACTLY as the top-level component is — one setup, not two copies (decisions 53/60).
    /// </summary>
    (MethodDeclarationIntermediateNode method, HashSet<IntermediateNode> regions)
        PrepareComponent(ParseResult parse, CSharpFrontEnd code)
    {
        // Per-component (decision 147): a child's @usings must not leak into a parent or sibling.
        _authorUsings.Clear();

        // --- gate 1: the directives, from Razor's own table, with exact spans ----
        foreach (var d in parse.Directives)
        {
            if (AllowedDirectives.Contains(d.Name)) continue;
            Diag("unsupported-directive",
                $"@{d.Name} is not in Phase 2's subset. This phase compiles the TEMPLATE only " +
                "(@expression, @if, @foreach, @key, attributes, events) and keeps @code as hand-written JS; " +
                $"@{d.Name} has no meaning in a Filament module and there is nothing honest to emit for it. " +
                "Refusing to emit rather than drop it silently.",
                d.Source);
        }

        // --- gate 2: the tree, every node accounted for --------------------------
        var cls = AccountForDocument(parse);

        var method = cls.Children.OfType<MethodDeclarationIntermediateNode>()
            .FirstOrDefault(m => m.MethodName == "BuildRenderTree")
            ?? throw new GeneratorException("FIL-WIRING: no BuildRenderTree method in the IR.");

        var codeNodes = cls.Children.OfType<CSharpCodeIntermediateNode>()
            .Where(n => !string.IsNullOrWhiteSpace(RawText(n)))
            .ToList();

        // --- gate 3: ALL the C#, in ONE compilation, BEFORE the walk (decision 54) ----
        var plan = new TemplatePlan();

        // ROOT-LEVEL CONTROL FLOW (decision 89, #77's third false positive). Collect() keys a
        // region by its CONTAINING element; a root @foreach/@if has none, which is why this used
        // to refuse [template-code-at-root]. So when the root itself holds template C#, the METHOD
        // IS the region container: its ops emit against target, the mount point, exactly as an
        // in-element region emits against its created element (EmitOps in Compile). RegionOps
        // refuses any statement that is not @foreach/@if (unsupported-template-statement), so the
        // re-parse is its OWN guard -- no root construct is admitted that it does not map.
        if (method.Children.Any(c => c is CSharpCodeIntermediateNode))
            Collect(method, plan);
        else
            foreach (var child in method.Children) Collect(child, plan);

        CollectComponentBindings(method, plan);
        CollectDynamicAttributes(method, plan);
        CollectLambdaHandlers(method, plan);
        CollectFormBinds(method, plan);
        CollectElementBinds(method, plan);

        // @inherits (decision 136). A derived component overrides BuildRenderTree, so the base contributes
        // its MEMBERS and not its markup -- exactly what Blazor renders. Merging those members into this
        // component's own compilation, BEFORE state lifting runs, is the whole mapping: `count` is then
        // lifted as if it had been written here, and nothing about inheritance survives into the module.
        // A Filament module has no base class, no vtable and no `this`.
        //
        // The base must be a SIBLING .razor, because that is the only C# this compiler ever reads: a base
        // in a .cs file is invisible to it, and silently inheriting nothing would produce a module missing
        // exactly the state the author put in the base.
        if (!string.Equals(cls.BaseType, ComponentBaseType, StringComparison.Ordinal))
        {
            var baseName = cls.BaseType ?? "";
            var basePath = Path.Combine(Path.GetDirectoryName(parse.FilePath)!, baseName + ".razor");
            var span = parse.Directives.FirstOrDefault(d => d.Name == "inherits").Source;

            if (!File.Exists(basePath))
            {
                Diag("unsupported-directive",
                    $"@inherits {baseName} resolves to a same-directory component {baseName}.razor, which does " +
                    "not exist. A base component must be a sibling .razor file: it is the only C# this " +
                    "compiler reads, so a base declared in a .cs file would silently contribute nothing and " +
                    "leave the module missing exactly the state the base holds. Refusing to emit.",
                    span);
            }
            else
            {
                var baseParse = RazorFrontEnd.Parse(basePath);
                var baseCls = AccountForDocument(baseParse);
                codeNodes.InsertRange(0, baseCls.Children.OfType<CSharpCodeIntermediateNode>()
                    .Where(n => !string.IsNullOrWhiteSpace(RawText(n))));
            }
        }

        // @typeparam (decision 135), carried into the compilation so `T` RESOLVES there. Without this the
        // author's own type parameter is reported as an unresolved type -- the compiler blaming the author
        // for a declaration it failed to carry over (decision 69's pattern).
        if (cls.TypeParameters is { Count: > 0 } tps)
            code.BindTypeParameters(tps.Select(t => t.ParameterName).ToList());

        // @inject (decision 133), harvested BEFORE the compilation so the injected name RESOLVES in it.
        // Razor drops the directive's span during lowering, so the location comes from DirectiveSpyPass.
        foreach (var inject in RazorFrontEnd.Injects(cls))
        {
            var span = parse.Directives.FirstOrDefault(d => d.Name == "inject").Source;
            var typeName = inject.TypeName.Trim();
            var member = inject.MemberName.Trim();

            // TWO injectable services, and the narrowness is still the honesty. Each is admitted because
            // it has a COMPILE-TIME meaning that erases: IJSRuntime denotes the browser's global scope
            // (decision 133), and HttpClient in a browser IS fetch -- Blazor WASM's HttpClient is
            // implemented on top of it, the bridge existing only because .NET must marshal across a
            // boundary this module does not have (decision 147). A general container resolves arbitrary
            // implementations at RUNTIME, which a static module has no home for, and a user's own service
            // type lives in a .cs file this compiler never sees. Both are separate questions; neither is
            // quietly approximated here.
            if (typeName.EndsWith("IJSRuntime", StringComparison.Ordinal) && member.Length > 0)
            {
                code.BindJsRuntime(member);
                continue;
            }
            if (typeName.EndsWith("HttpClient", StringComparison.Ordinal) && member.Length > 0)
            {
                code.BindHttpClient(member);
                continue;
            }
            Diag("unsupported-directive",
                $"@inject {typeName} is not in the subset. The injectable services are IJSRuntime " +
                "(decision 133: it denotes the browser's global scope, so the call it carries becomes a " +
                "direct call) and HttpClient (decision 147: in a browser it IS fetch, so the request it " +
                "carries becomes the fetch itself) -- each admitted because it ERASES at compile time. A " +
                "general DI container resolves an implementation at RUNTIME, and a Filament module has no " +
                "container to ask; a service type of your own lives in a .cs file this compiler never " +
                "reads. Refusing to emit rather than inject something that resolves to nothing.",
                span);
        }

        code.BindUsings(_authorUsings);
        code.Compile(codeNodes, plan);
        _diagnostics.AddRange(code.Diagnostics);

        return (method, plan.Regions.Select(r => r.Container).ToHashSet());
    }

    public string Compile(ParseResult parse, string runtimeSpecifier, string sourceName)
    {
        _file = parse.FilePath;

        var (method, regions) = PrepareComponent(parse, _code);
        _regions = regions;

        // NOT an early return, on purpose: the template gate must report too. A file
        // whose @code is out of subset AND whose template is out of subset should say
        // both, once, with locations -- fixing one and re-running to discover the other
        // is how a refusal becomes a guessing game. (Rows is exactly this file.)

        // --- the template -----------------------------------------------------
        // A root region (decision 89) emits as ONE unit against target: EmitOps lays down its
        // markup/list/anchor ops in source order, the same shape an in-element region emits, only
        // with target -- the mount point -- as the container instead of a created element.
        if (_regions.Contains(method))
        {
            EmitOps(_code.OpsFor(method), "target");
        }
        else
        {
            foreach (var child in method.Children)
            {
                var v = EmitNode(child, parent: null);
                if (v is not null) _attach.Add($"insert(target, {v});");
            }
        }

        // --- emission, and ONLY if nothing has been refused ----------------------
        //
        // NOTHING IS EMITTED FOR A FILE THAT IS BEING REFUSED, and this is a guard on the
        // invariant rather than a tidy-up. It shipped as a CRASH: when @code fails to
        // compile, the C# front end has registered the METHOD NAMES but never translated
        // their bodies, so the template walk happily resolved @onclick="Run" and the
        // emitter then asked for a body that does not exist -- KeyNotFoundException, exit
        // 134, a stack trace where the author should have seen "List<T>'s mapping is the
        // next step, at line 41". Measured on baseline/Rows.Blazor/RowsApp.razor.
        //
        // A refused compile writes no file, so its emission was never worth anything: the
        // only thing building it could produce is a crash or a garbage module. The
        // template walk above still ran, so the template's OWN diagnostics are reported
        // alongside @code's -- which is the point of not returning early.
        var prologue = new List<string>();
        var module = new List<string>();
        if (_diagnostics.Count == 0)
        {
            var inlined = _handlers
                .Select(h => h.Handler)
                .Distinct(StringComparer.Ordinal)
                .Where(ShouldInline)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var h in _handlers)
                _events.Add($"listen({h.El}, {JsString(h.Event)}, {HandlerArrow(h.Handler, inlined, _preventDefault.Contains(h.El))});");

            prologue = _code.EmitPrologue(inlined);
            module = _code.EmitModule();
            foreach (var p in _code.Primitives) _used.Add(p);
        }

        return Render(module, prologue, runtimeSpecifier, sourceName);
    }

    /// <summary>Does `content` name a NAMESPACE in the reference assemblies? Walked segment by segment off
    /// a refs-only compilation (cached: the references never change within a process). This is the gate an
    /// author's @using passes (decision 147) -- resolution only; `@using static X` and `@using A = B` fail
    /// the walk by construction, which is the refusal they deserve until they are their own slices.</summary>
    static bool NamespaceResolves(string content)
    {
        Microsoft.CodeAnalysis.INamespaceSymbol ns = _refsProbe.Value.GlobalNamespace;
        foreach (var segment in content.Trim().Split('.'))
        {
            var next = ns.GetNamespaceMembers().FirstOrDefault(m => m.Name == segment);
            if (next is null) return false;
            ns = next;
        }
        return true;
    }

    static readonly Lazy<Microsoft.CodeAnalysis.Compilation> _refsProbe = new(() =>
        Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "FilamentUsingProbe", references: ReferenceAssemblies.ForCode()));

    /// <summary>
    /// Walk the document/namespace/class levels and REFUSE anything not on the
    /// allowlist. This is the half that used to be missing: the emitter reached in for
    /// the two node types it liked and never looked at the rest, so @inject and @page
    /// compiled to a clean module with the injection and the route simply gone.
    /// Returns the class to compile; a document without one is the TOOL being broken
    /// (FIL-WIRING), not an unsupported input.
    /// </summary>
    ClassDeclarationIntermediateNode AccountForDocument(ParseResult parse)
    {
        var ns = parse.Ir.Children.OfType<NamespaceDeclarationIntermediateNode>().FirstOrDefault()
            ?? throw new GeneratorException("FIL-WIRING: no NamespaceDeclarationIntermediateNode in the IR.");

        foreach (var child in parse.Ir.Children)
            if (child is not NamespaceDeclarationIntermediateNode)
                Unaccounted(child, "at the top of the document");

        ClassDeclarationIntermediateNode? cls = null;

        foreach (var child in ns.Children)
        {
            switch (child)
            {
                case ClassDeclarationIntermediateNode c:
                    cls = c;
                    break;

                // Razor SYNTHESISES the default @usings (they have no file path). A @using the AUTHOR
                // wrote carries this component's own path -- telling the two apart by the span's file is
                // measured, not guessed (see the probe in DiagnosticTests).
                //
                // An author's @using is a NAME-RESOLUTION directive (decision 147): it brings a namespace
                // into scope for the @code compilation and NOTHING more -- every construct still passes
                // the subset gates, so admitting resolution can never admit an unfaithful emission. It is
                // harvested into the wrapped source's usings IF the namespace resolves against the
                // reference assemblies; one that does not (a component library, a typo, `@using static`,
                // an alias) is refused at its span -- exactly where Blazor's own build fails it (CS0246).
                case UsingDirectiveIntermediateNode u when IsFromThisFile(u.Source):
                    if (NamespaceResolves(u.Content)) _authorUsings.Add(u.Content);
                    else
                        Diag("unsupported-directive",
                            $"@using {u.Content} does not name a namespace in the reference assemblies. A " +
                            "resolving @using is admitted as pure name resolution (decision 147); this one " +
                            "would fail Blazor's own build too (CS0246). `@using static` and aliases are not " +
                            "in the subset. Refusing to emit rather than drop it silently.",
                            u.Source);
                    break;

                case UsingDirectiveIntermediateNode:
                    break; // synthesised by Razor; not the author's, nothing to compile

                // @page (decision 139). Razor lowers it to a route-attribute node BESIDE the class, with no
                // span. It contributes nothing to this component's own module: a route is metadata the
                // generated ROUTER reads (RazorFrontEnd.RouteOf), and the page compiles identically with or
                // without it -- which is what lets a page module be byte-identical routed or standalone.
                case { } route when route.GetType().Name == "RouteAttributeExtensionNode":
                    break;

                default:
                    Unaccounted(child, "beside the component class");
                    break;
            }
        }

        if (cls is null) throw new GeneratorException("FIL-WIRING: no ClassDeclarationIntermediateNode in the IR.");

        // @inherits / @implements / @typeparam do not appear as CHILDREN at all -- they
        // set PROPERTIES on the class, which is exactly how they used to slip through.
        // The directive gate above already reports all three WITH their span, so these
        // are a backstop for the class arriving in a shape no directive explains.
        //
        // The comparison is against Razor's DEFAULT for a component, not against null:
        // every component gets BaseType = ComponentBase whether or not anyone wrote
        // @inherits. Asserting "BaseType is not null" here refused Counter itself --
        // caught by running the generator, which is the only reason this comment is
        // accurate rather than confident.
        // A NON-DEFAULT BASE TYPE IS NO LONGER REFUSED HERE (decision 136). @inherits is resolved in
        // PrepareComponent, which is the only place that can look for the sibling .razor the base must
        // be; a base it cannot resolve is refused THERE, with the reason that actually applies.
        if (cls.Interfaces is { Count: > 0 })
            ClassShape(cls, "declares interfaces (@implements); a Filament module implements none");

        foreach (var child in cls.Children)
        {
            switch (child)
            {
                case MethodDeclarationIntermediateNode m when m.MethodName == "BuildRenderTree":
                case CSharpCodeIntermediateNode: // the @code seam -- spliced verbatim
                    break;

                // @page (decision 139). Razor lowers it to a route-attribute extension node with no span. It
                // contributes NOTHING to this component's own module: a route is metadata the ROUTER reads
                // (RazorFrontEnd.RouteOf), and the page itself compiles exactly as it would without it.
                case { } r when r.GetType().Name == "RouteAttributeExtensionNode":
                    break;

                // @inject (decision 133). Admitted for IJSRuntime and nothing else; the site itself is read
                // back by RazorFrontEnd.Injects in PrepareComponent, which is where it is validated.
                case { } n when n.GetType().Name == "ComponentInjectIntermediateNode":
                    break;
                default:
                    Unaccounted(child, "inside the component class");
                    break;
            }
        }

        return cls;
    }

    /// <summary>
    /// THE COLLECT WALK. It reads the template's SHAPE -- which containers hold C#, which
    /// expressions are read in which scope -- and emits nothing.
    ///
    /// It exists because the compilation has to be built BEFORE the emit walk can ask it
    /// anything, and it walks the SAME IR the emit walk will emit from, so it cannot describe
    /// a different template. Where the two could still drift, the drift is LOUD, not silent:
    /// the emit walk looks every node up (CSharpFrontEnd.OpsFor / SlotJs) and a node the
    /// collect walk never saw is FIL-WIRING, not a guess. That is decision 53's lesson --
    /// wiring described twice drifts, so the second description must be unable to disagree
    /// quietly.
    ///
    /// Event handlers are deliberately NOT slots: `@onclick="Increment"` NAMES a method, and
    /// Razor gives it a different node type (CSharpExpressionAttributeValue...), so naming a
    /// method cannot make a field reactive.
    /// </summary>
    void Collect(IntermediateNode node, TemplatePlan plan)
    {
        // A RESOLVED FORM COMPONENT'S PARAMETERS ARE NOT ORDINARY SLOTS (decision 138). Razor lowers
        // `@bind-Value` into THREE parameters -- Value, plus a synthesised ValueChanged binder lambda and
        // a ValueExpression accessor -- which are Blazor's own plumbing for a runtime binder. This
        // compiler reads the ONE that carries the author's expression (CollectFormBinds takes `Value`) and
        // emits the two-way pair itself, so compiling the other two would be compiling machinery that is
        // being REPLACED: it produced an [unsupported-call] on RuntimeHelpers and an [unsupported-
        // expression] on `() => model.Name`, both about code the author never wrote. EditForm's `Model` is
        // skipped for a different reason -- nothing validates it here, so counting it as a template READ
        // would assert a read that does not happen.
        var isFormComponent = node is ComponentIntermediateNode { TagName: "InputText" or "EditForm" };

        var kids = node.Children
            .Where(c => c is not HtmlAttributeIntermediateNode)
            .Where(c => !(isFormComponent && c is ComponentAttributeIntermediateNode))
            .ToList();

        // A container whose children hold RAW C# is decision 54's shape: no loop node, braces
        // that do not balance, the body element a SIBLING of the header. It is reassembled and
        // re-parsed -- see TemplatePlan.
        if (kids.Any(k => k is CSharpCodeIntermediateNode))
        {
            var items = new List<RegionItem>();
            foreach (var kid in kids)
                items.Add(kid is CSharpCodeIntermediateNode c
                    ? new CodeItem(c)
                    : new MarkupItem(kid, SlotsIn(kid)));
            plan.Regions.Add(new TemplateRegion { Container = node, Items = items });

            // A markup item may ITSELF contain template C# further down -- a <ul> holding a @foreach
            // beside a component-level @if is one component with TWO control-flow regions, not "a region
            // inside a region" (decision 142): each container's C# is lexically independent, so each gets
            // its OWN region method and its own scope, exactly as if the other did not exist. Only the
            // DESCENDANT REGION CONTAINERS are collected here (SlotsIn stopped at exactly those, so no
            // slot is claimed twice); plain markup under this region contributes nothing new. This is what
            // used to be RefuseNestedCode; a genuinely scope-entangled shape (a branch-local @{ } read
            // across regions) now fails LOUDLY in the C# compilation instead of being guessed at.
            foreach (var kid in kids)
                if (kid is not CSharpCodeIntermediateNode) CollectNestedRegions(kid, plan);
            return;
        }

        foreach (var kid in kids)
            if (kid is CSharpExpressionIntermediateNode or SetKeyIntermediateNode) plan.FreeSlots.Add(kid);
            else Collect(kid, plan);
    }

    /// <summary>
    /// Component BOUND parameters (decision 90). A `&lt;Display Value="@count" /&gt;` carries C# inside an
    /// attribute, which Collect() does not descend into (it filters HtmlAttributeIntermediateNode), so
    /// #88's static leaf never needed it. A bound param DOES: the parent must COMPILE `@count` so it is
    /// translated (`count.value`) AND counts as a template read -- the read is what lifts `count` to a
    /// signal (the conjunction rule), without which the child's binding would subscribe to nothing.
    /// Harvesting each into FreeSlots does exactly that; EmitComposition reads SlotJs/SlotIsReactive back
    /// off the SAME node. The node is compiled but NOT emitted as a stray text node: the emit walk hits
    /// the component element and EmitComposition consumes its translated JS instead of walking into it.
    /// </summary>
    /// <summary>
    /// Two-way form binds (decision 138). `<InputText @bind-Value="model.Name" />` resolves to a
    /// component node whose `Value` parameter carries the bound expression -- Blazor's OWN lowering,
    /// which is why the Forms namespace is imported rather than the binding re-derived here.
    ///
    /// The expression is harvested into FreeSlots (so it is COMPILED, and so it counts as a template
    /// READ) and recorded as a bind (so it can also be marked WRITTEN -- see MarkTemplateWrites). Both
    /// halves are needed: decision 67's conjunction makes a target reactive only if it is read AND
    /// assigned, and for a pure @bind target the template is the only thing that assigns it.
    /// </summary>
    void CollectFormBinds(IntermediateNode node, TemplatePlan plan)
    {
        if (node is ComponentIntermediateNode c && c.TagName == "InputText")
            foreach (var attr in c.Children.OfType<ComponentAttributeIntermediateNode>())
                if (attr.AttributeName == "Value")
                    foreach (var expr in attr.Children.OfType<CSharpExpressionIntermediateNode>())
                    {
                        plan.FreeSlots.Add(expr);
                        plan.FormBinds.Add(new FormBind(c, expr));
                    }

        foreach (var child in node.Children) CollectFormBinds(child, plan);
    }

    /// <summary>
    /// Plain-element @bind targets, by name (decision 154). TryBind's own detection pair -- the
    /// synthesised `value`/`checked` FormatValue attribute plus the CreateBinder `onchange` --
    /// re-consulted at PLAN time, because the reactivity marking runs inside Compile() and a
    /// pure-@bind field (nothing else reads or writes it) must already be marked read+assigned
    /// there. BoundField yields a bare field name; anything else matches no field and changes
    /// nothing, so the undeclared-field refusal is untouched.
    /// </summary>
    void CollectElementBinds(IntermediateNode node, TemplatePlan plan)
    {
        if (node is MarkupElementIntermediateNode el && !LooksLikeComponent(el.TagName))
        {
            var attrs = el.Children.OfType<HtmlAttributeIntermediateNode>().ToList();
            var change = attrs.FirstOrDefault(a =>
                string.Equals(a.AttributeName, "onchange", StringComparison.OrdinalIgnoreCase) &&
                AttrCs(a).Contains("CreateBinder"));
            var bound = attrs.FirstOrDefault(a =>
                (string.Equals(a.AttributeName, "value", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(a.AttributeName, "checked", StringComparison.OrdinalIgnoreCase)) &&
                AttrCs(a).Contains("BindConverter.FormatValue"));
            if (change is not null && bound is not null && BoundField(bound) is { } field)
                plan.ElementBindNames.Add(field);
        }
        foreach (var child in node.Children) CollectElementBinds(child, plan);
    }

    void CollectComponentBindings(IntermediateNode node, TemplatePlan plan)
    {
        if (node is MarkupElementIntermediateNode el && LooksLikeComponent(el.TagName))
            foreach (var attr in el.Children.OfType<HtmlAttributeIntermediateNode>())
                foreach (var expr in attr.Children.OfType<CSharpExpressionAttributeValueIntermediateNode>())
                {
                    plan.FreeSlots.Add(expr);
                    // Marked as a COMPONENT slot: this is the one binding site where the C# may name a
                    // method group instead of a value -- an EventCallback (decision 130).
                    plan.ComponentSlots.Add(expr);
                }
        foreach (var child in node.Children) CollectComponentBindings(child, plan);
    }

    /// <summary>
    /// Reactive/dynamic ATTRIBUTE values on plain elements (the reactive-`class` slice, BENCH n°13).
    /// Collect() filters out HtmlAttributeIntermediateNode, so an attribute expression is never harvested
    /// there; without a slot the front end never compiles it and SlotJs/SlotIsReactive cannot answer.
    /// Harvest the value expression of an ALLOW-LISTED attribute into FreeSlots -- the same harvest
    /// CollectComponentBindings does for a component's bound params -- so EmitAttribute can read SlotJs /
    /// SlotIsReactive back off the SAME node. Two guards (in DynamicValue) keep everything else out: the
    /// value must be a single pure C# expression (no literal part), and it must NOT be an event handler.
    /// </summary>
    /// <summary>An inline no-argument, non-async lambda: `() => …`. The `e => …` (event object) and
    /// `async () => …` forms are NOT this -- they stay refused (deferred), so the regex is deliberately
    /// anchored to `()`.</summary>
    static bool IsNoArgLambda(string handler) => Regex.IsMatch(handler.Trim(), @"^\(\s*\)\s*=>");

    /// <summary>
    /// Harvest inline lambda EVENT handlers into plan.LambdaHandlers so CSharpFrontEnd wraps each body as
    /// a synthetic method and translates it (decision 105) -- the same "answer it from the compiler, not a
    /// regex" harvest CollectDynamicAttributes does for reactive attributes.
    /// </summary>
    void CollectLambdaHandlers(IntermediateNode node, TemplatePlan plan)
    {
        if (node is MarkupElementIntermediateNode el && !LooksLikeComponent(el.TagName))
            foreach (var attr in el.Children.OfType<HtmlAttributeIntermediateNode>())
                if (TryUnwrapEventCallback(AttrCs(attr), out var handler) && IsNoArgLambda(handler))
                {
                    var name = attr.AttributeName;
                    var domEvent = name.StartsWith("on", StringComparison.Ordinal) ? name[2..] : name;
                    plan.LambdaHandlers.Add(new LambdaHandler(attr, domEvent, handler));
                }
        foreach (var child in node.Children) CollectLambdaHandlers(child, plan);
    }

    void CollectDynamicAttributes(IntermediateNode node, TemplatePlan plan)
    {
        // A region's markup items already claimed their attribute-value slots (SlotsIn, decision
        // 152) so those compile inside the region method, where the loop variable resolves.
        // Free-slot them TOO and the same node is planted twice -- the class-scope copy cannot
        // see `t` and the whole compilation dies on CS0103. One node, one scope.
        var claimed = plan.Regions.SelectMany(r => r.Items).OfType<MarkupItem>()
            .SelectMany(mi => mi.Slots).ToHashSet();
        void Walk(IntermediateNode n)
        {
            if (n is MarkupElementIntermediateNode el && !LooksLikeComponent(el.TagName))
                foreach (var attr in el.Children.OfType<HtmlAttributeIntermediateNode>())
                    if (IsDynamicStringAttribute(attr.AttributeName) && ComposableValue(attr) is { } parts)
                    {
                        foreach (var e in parts.OfType<CSharpExpressionAttributeValueIntermediateNode>())
                            if (!claimed.Contains(e)) plan.FreeSlots.Add(e);
                    }
                    else if (BooleanAttributes.Contains(attr.AttributeName) && DynamicValue(attr) is { } expr)
                    {
                        if (!claimed.Contains(expr)) plan.FreeSlots.Add(expr);
                    }
            foreach (var child in n.Children) Walk(child);
        }
        Walk(node);
    }

    /// <summary>
    /// The single pure C# expression value of an attribute that is NOT an event handler, or null. The ONE
    /// predicate both the harvest (CollectDynamicAttributes) and the emission (EmitAttribute) consult, so
    /// they cannot disagree about which attributes are dynamic values (decision 53: wiring described twice
    /// drifts). Pure = exactly one CSharpExpressionAttributeValueIntermediateNode and NO literal
    /// (HtmlAttributeValue) part -- a concatenation (`class="box @x"`) returns null and stays refused. An
    /// event handler (its value unwraps as an EventCallback) returns null and keeps its listen() path.
    /// </summary>
    static CSharpExpressionAttributeValueIntermediateNode? DynamicValue(HtmlAttributeIntermediateNode attr)
    {
        var csharp = attr.Children.OfType<CSharpExpressionAttributeValueIntermediateNode>().ToList();
        var html = attr.Children.OfType<HtmlAttributeValueIntermediateNode>().ToList();
        if (csharp.Count != 1 || html.Count != 0) return null;
        var expr = string.Concat(csharp[0].Children.OfType<IntermediateToken>().Select(t => t.Content));
        return TryUnwrapEventCallback(expr, out _) ? null : csharp[0];
    }

    /// <summary>
    /// The ordered value parts of an attribute that COMPOSES to a string, or null. Composable = every
    /// child is a literal (HtmlAttributeValue) or an expression (CSharpExpressionAttributeValue) part,
    /// there is at least one expression, and no expression part is an event handler. A control-flow value
    /// node (CSharpCodeAttributeValue -- `class="@if(c){…}"`) makes it null, so that value stays on the
    /// `unaccounted-attribute-value` refusal (distinct slice). The pure `@expr` case is the degenerate
    /// composable value (one expression, no literals). The ONE predicate the harvest
    /// (CollectDynamicAttributes) and the emission (EmitAttribute) both consult (decision 53).
    /// </summary>
    static IReadOnlyList<IntermediateNode>? ComposableValue(HtmlAttributeIntermediateNode attr)
    {
        var parts = attr.Children
            .Where(c => c is HtmlAttributeValueIntermediateNode or CSharpExpressionAttributeValueIntermediateNode)
            .ToList();
        if (parts.Count != attr.Children.Count) return null;   // a non-value node (control flow) -> not composable
        var exprs = parts.OfType<CSharpExpressionAttributeValueIntermediateNode>().ToList();
        if (exprs.Count == 0) return null;                     // no expression -> the static-literal path handles it
        foreach (var e in exprs)
        {
            var text = string.Concat(e.Children.OfType<IntermediateToken>().Select(t => t.Content));
            if (TryUnwrapEventCallback(text, out _)) return null; // an event handler keeps its listen() path
        }
        return parts;
    }

    /// <summary>
    /// Fold the ordered value parts into a single JS string expression, prefix-aware. Each part
    /// contributes its Prefix (the literal text before it) then its body: a literal part appends its
    /// content to a running buffer; an expression part flushes the buffer as a JS string term, then emits
    /// SlotJs (never a splice). Terms are joined with ` + `. `class="badge @x rounded"` folds to
    /// `'badge ' + x.value + ' rounded'`; the pure `class="@x"` folds to just `x.value` (byte-identical to
    /// the reactive-`class` slice). `reactive` is true iff ANY expression part is reactive.
    /// </summary>
    (string js, bool reactive) ComposeAttributeValue(IReadOnlyList<IntermediateNode> parts)
    {
        var terms = new List<(string Js, bool IsExpr)>();
        var buf = new System.Text.StringBuilder();
        var reactive = false;
        foreach (var part in parts)
        {
            if (part is HtmlAttributeValueIntermediateNode h)
            {
                buf.Append(h.Prefix);
                buf.Append(string.Concat(h.Children.OfType<IntermediateToken>().Select(t => t.Content)));
            }
            else if (part is CSharpExpressionAttributeValueIntermediateNode c)
            {
                buf.Append(c.Prefix);
                if (buf.Length > 0) { terms.Add((JsString(buf.ToString()), false)); buf.Clear(); }
                terms.Add((_code.SlotJs(c), true));
                if (_code.SlotIsReactive(c)) reactive = true;
            }
        }
        if (buf.Length > 0) terms.Add((JsString(buf.ToString()), false));

        // In a MULTI-term fold, a compound expression term must be parenthesised: `+` binds
        // tighter than `?:`, so 'flex ' + t.done.value ? 'a' : 'b' is (truthy-string) ? 'a' : 'b'
        // -- the prefix silently gone and one branch dead (decision 152). A bare dotted chain
        // (`cls.value`, `t.done.value`) stays bare, which is what keeps the mixed-class answer key
        // ('badge ' + statusClass.value + ' rounded') byte-identical.
        var js = string.Join(" + ", terms.Select(t =>
            t.IsExpr && terms.Count > 1 && !Regex.IsMatch(t.Js, @"^[\w$]+(\.[\w$]+)*$")
                ? "(" + t.Js + ")" : t.Js));
        return (js, reactive);
    }

    /// <summary>Every @expression and @key in a subtree, in document order.</summary>
    /// <summary>Find each nested REGION CONTAINER under a region's markup item and Collect it as its own
    /// region (decision 142). Everything between this region and that one is plain markup whose slots the
    /// outer region's MarkupItem already carries -- SlotsIn and this walk stop/fire on the SAME condition,
    /// so nothing is collected twice and nothing is skipped.</summary>
    void CollectNestedRegions(IntermediateNode node, TemplatePlan plan)
    {
        if (node.Children.Any(c => c is CSharpCodeIntermediateNode))
        {
            Collect(node, plan);
            return;
        }
        foreach (var c in node.Children)
            if (c is not HtmlAttributeIntermediateNode) CollectNestedRegions(c, plan);
    }

    static List<IntermediateNode> SlotsIn(IntermediateNode node)
    {
        var slots = new List<IntermediateNode>();
        void Walk(IntermediateNode n)
        {
            if (n is CSharpExpressionIntermediateNode or SetKeyIntermediateNode) { slots.Add(n); return; }
            // A dynamic ATTRIBUTE value on a row's element is a slot of THIS region (decision 152):
            // `class="… @(t.Done ? … : …)"` reads the loop variable, so its expression must compile
            // inside the region method's braces exactly like the row's text slots -- harvested here
            // with the SAME two predicates CollectDynamicAttributes uses at mount level (which then
            // skips anything a region already claimed, so each node is planted in exactly one scope).
            if (n is HtmlAttributeIntermediateNode a)
            {
                if (IsDynamicStringAttribute(a.AttributeName) && ComposableValue(a) is { } parts)
                    slots.AddRange(parts.OfType<CSharpExpressionAttributeValueIntermediateNode>());
                else if (BooleanAttributes.Contains(a.AttributeName) && DynamicValue(a) is { } expr)
                    slots.Add(expr);
                return;
            }
            // Stop at any node that is ITSELF a region container (decision 142) -- including the root,
            // when an outer region's markup item IS one: its slots belong to ITS region method -- the
            // scope its own C# declares (a loop variable, say) -- not to this one's, where they would be
            // planted outside the braces that bind them and CS0103.
            if (n.Children.Any(c => c is CSharpCodeIntermediateNode)) return;
            // A resolved form component's parameters are not ordinary slots inside a region either --
            // the same decision-138 rule Collect() applies outside one: Razor's @bind-Value lowering
            // (ValueChanged binder, ValueExpression accessor) is machinery this compiler REPLACES, and
            // compiling it produces [unsupported-call]s about code the author never wrote.
            var isForm = n is ComponentIntermediateNode { TagName: "InputText" or "EditForm" };
            foreach (var c in n.Children)
                if (!(isForm && c is ComponentAttributeIntermediateNode)) Walk(c);
        }
        Walk(node);
        return slots;
    }

    /// <summary>
    /// Control flow INSIDE control flow. Rows has none, so nothing here would be measured, and
    /// the reassembly would have to splice a region into a region -- i.e. resolve an
    /// expression in two scopes at once. Refused with a location rather than approximated.
    /// </summary>
    /// <summary>
    /// THE INLINE-VS-REFERENCE ARBITRAGE, and it is decision 55's remaining divergence.
    ///
    /// The answer key -- the REFERENCE, decisions 21/51 -- inlines:
    ///     listen(button, 'click', () => { currentCount.value++; });
    /// and emits NO `Increment` binding at all. Phase 2 could not reach that shape:
    /// reading a method's BODY means translating @code, which Phase 2 excluded, so the
    /// gate contradicted the phase's own scope (decision 55) and was committed RED.
    /// Phase 3 translates @code, so the shape is reachable and the contradiction is
    /// resolved by doing the work rather than by moving the threshold.
    ///
    /// THE RULE: a method whose ONLY use in the whole component is one event handler is
    /// inlined into that handler. This is single-use inlining -- a bog-standard compiler
    /// rule, mechanical and checkable -- and the justification is the thesis itself: a
    /// private function with one caller, emitted as a function plus a reference to it, is
    /// an indirection that exists only to have a name. Filament's whole argument is that
    /// it ships no machinery whose only job is to serve machinery.
    ///
    /// A method called from @code as well keeps its function and the handler CALLS it, so
    /// the body is never duplicated.
    ///
    /// RESOLVED (DECISIONS #80). rows.js USED to break this rule: it emitted `function
    /// update()` / `function swapRows()` / `function run()` and referenced them from their
    /// handlers, though each is named by exactly one @onclick and called from nowhere else,
    /// so the two answer keys specified DIFFERENT handler mappings and #68 disclosed that as
    /// the owner's call before the Rows step. The owner made it: rows.js was CORRECTED to
    /// adopt this rule (single-use `run`/`update`/`swapRows` inlined; `clear` kept a function
    /// because `run` also calls it). So the generator emits what the key now specifies, and
    /// the Rows gate is GREEN. This was the answer key adopting the generator's rule, NOT the
    /// generator being re-shaped to the key. DECISIONS #68 (rule), #80 (resolution).
    /// </summary>
    bool ShouldInline(string handler) =>
        _code.IsMethod(handler) &&
        _code.CallsTo(handler) == 0 &&
        _handlers.Count(h => string.Equals(h.Handler, handler, StringComparison.Ordinal)) == 1;

    /// <summary>
    /// The JS handed to listen(). ALWAYS an arrow, never a bare method reference, and
    /// that is a correctness point rather than a shape preference:
    /// addEventListener invokes its listener WITH the DOM Event, so `listen(el,'click',
    /// Increment)` calls a C# method that declares no parameters with one argument. For
    /// `void Increment()` JS ignores the extra argument and nothing breaks; for a method
    /// that does take a parameter it would silently bind a raw DOM Event where the C#
    /// says MouseEventArgs. An arrow passes exactly the arguments the C# declares -- none
    /// -- so the emission cannot depend on the method's arity being lucky.
    ///
    /// Both answer keys agree on this: counter.js emits `() => { ... }` and rows.js emits
    /// `() => batch(clear)` (and `() => batch(() => { ... })` for its inlined handlers).
    /// NEITHER emits `listen(el, 'click', Handler)`.
    ///
    /// batch() iff there is more than one write to coalesce -- see
    /// CSharpFrontEnd.MayWriteMoreThanOnce, which is where both keys' apparently opposite
    /// statements about batch turn out to be the same rule.
    /// </summary>
    string HandlerArrow(string handler, IReadOnlySet<string> inlined, bool preventDefault = false)
    {
        var batched = _code.MayWriteMoreThanOnce(handler);

        // A FORM'S SUBMIT (decision 138). It is the one handler whose arrow takes the event, and it takes
        // one because the DOM requires it: without preventDefault() the browser navigates and the page
        // reloads, which is exactly what Blazor's EditForm suppresses. Emitted as statements rather than
        // by wrapping the no-arg shape below, so the result reads as what it is.
        if (preventDefault)
        {
            var inner = inlined.Contains(handler)
                ? string.Join("\n", _code.InlineBody(handler).Select(l => "  " + l))
                : $"  {_code.MethodJs(handler)}();";
            if (batched)
            {
                _used.Add("batch");
                inner = "  batch(() => {\n" + string.Join("\n", inner.Split('\n').Select(l => "  " + l)) + "\n  });";
            }
            return "(e) => {\n  e.preventDefault();\n" + inner + "\n}";
        }

        // A KEYBOARD handler (decision 159): the other arrow that takes the event -- the DOM
        // provides it, the method declared it. The arrow binds the METHOD's own parameter name so
        // an inlined body reads it unchanged, and batch wraps INSIDE so the event stays in scope
        // (the preventDefault branch above is the same shape for the same reason).
        if (_code.HandlerTakesEvent(handler))
        {
            var p = _code.HandlerEventParamJs(handler);
            var inner = inlined.Contains(handler)
                ? string.Join("\n", _code.InlineBody(handler).Select(l => "  " + l))
                : $"  {_code.MethodJs(handler)}({p});";
            if (batched)
            {
                _used.Add("batch");
                inner = "  batch(() => {\n" + string.Join("\n", inner.Split('\n').Select(l => "  " + l)) + "\n  });";
            }
            return $"({p}) => {{\n" + inner + "\n}";
        }

        string body;
        if (inlined.Contains(handler))
        {
            var lines = _code.InlineBody(handler);
            // An async Task handler inlines to `async () => { … }` so its `await` is legal JS (decision 119).
            var arrow = _code.IsAsyncHandler(handler) ? "async () => {\n" : "() => {\n";
            body = arrow + string.Join("\n", lines.Select(l => "  " + l)) + "\n}";
        }
        else
        {
            body = batched ? _code.MethodJs(handler) : $"() => {_code.MethodJs(handler)}()";
        }

        if (!batched) return body;

        _used.Add("batch");
        return $"() => batch({body})";
    }

    /// <summary>
    /// THE guard: the ONE way a name the template writes may reach the emitted module.
    /// Both the read path (@currentCount) and the event path (@onclick="Increment") call
    /// it -- that is the whole point, because they used to disagree and the event path's
    /// disagreement was a verbatim splice (see this class's header).
    ///
    /// Two conditions, and each one is refused with its own reason so the message tells
    /// the author which mistake they made:
    ///   1. it must be a BARE IDENTIFIER. Anything else -- a lambda, a call, a compound
    ///      expression -- requires deciding which sub-expressions are reactive reads or
    ///      what a body means, and that is C# work, i.e. Phase 3's (decision 54).
    ///   2. it must RESOLVE to something @code binds. Spec 5 admits "calls to methods
    ///      declared in the same component"; a name declared nowhere is not in the subset.
    ///   3. if it is used as a HANDLER it must resolve to a FUNCTION, because spec 5 says
    ///      METHODS and because naming state instead is the blocker's failure mode again:
    ///      addEventListener takes a non-callable object without a murmur and never calls it.
    ///
    /// ORDER MATTERS, AND NOT FOR SAFETY -- FOR THE MESSAGE. Check 2 already subsumes check
    /// 1 (nothing @code declares is anything but a bare identifier, so a lambda can never
    /// resolve), and mutation-testing proved it: disabling check 1 leaves the lambda
    /// fixtures REFUSED, by check 2, with the wrong reason. Check 1 is kept because
    /// "() =&gt; currentCount++ is not a bare identifier" is the truth and "@code does not
    /// declare '() =&gt; currentCount++'" is a riddle.
    ///
    /// PHASE 3 MADE CHECKS 2 AND 3 EXACT. They used to consult a REGEX SCRAPE of the JS
    /// seam, which over-refused (`const a = 1, b = 2;` bound only `a`) and, worse,
    /// over-accepted in the silent direction (`let Foo = () => {}; Foo = {};` stayed
    /// "callable" forever because a regex cannot see a reassignment -- a dead button the
    /// scrape could not catch). They now ask Roslyn for a SYMBOL. Both residuals are gone,
    /// and the reason they are gone is that the question changed from "what does this text
    /// look like" to "what did the compiler bind".
    /// </summary>
    bool NamedByTemplate(string name, string what, SourceSpan? source, bool mustBeCallable = false)
    {
        if (!BareIdentifier.IsMatch(name))
        {
            Diag("compound-expression",
                $"{what} is \"{Trunc(name)}\", which is not a bare identifier. A template may NAME state or a " +
                "method; deciding what a lambda, a call or a compound expression MEANS at a binding site is " +
                "not something this compiler will guess at, and it will not splice it verbatim, because a " +
                "spliced handler compiles cleanly, loads cleanly, renders correctly and then does NOTHING " +
                "for the life of the page -- the silent mis-compile section 10 forbids. Refusing to emit.",
                source);
            return false;
        }

        if (!_code.Declares(name))
        {
            Diag("unresolved-name",
                $"{what} names '{name}', which the @code block does not declare. The subset admits calls to " +
                "methods declared in the SAME component (spec 5), and this compiler resolves them against the " +
                $"names @code declares ({(_code.DeclaredNames.Any() ? "it declares: " + string.Join(", ", _code.DeclaredNames.Order()) : "it declares none")}). " +
                "Refusing to emit rather than emit a reference to a name that does not exist.",
                source);
            return false;
        }

        if (mustBeCallable && !_code.IsMethod(name))
        {
            Diag("handler-is-not-a-method",
                $"{what} names '{name}', which @code declares but NOT as a method. The subset admits methods " +
                "declared in the same component (spec 5); a handler that names STATE compiles, loads, renders " +
                $"and then does nothing -- addEventListener accepts a non-callable '{name}' silently and never " +
                "invokes it (verified in jsdom: neither addEventListener nor dispatchEvent throws). That is the " +
                "same dead button as a spliced lambda, reached by naming state instead of a method. " +
                $"@code declares these methods: {(_code.MethodNames.Any() ? string.Join(", ", _code.MethodNames.Order()) : "none")}. " +
                "Refusing to emit.",
                source);
            return false;
        }

        return true;
    }

    /// <summary>
    /// A node this compiler cannot account for. It refuses, and it says where.
    ///
    /// The lowered declaration-level nodes (@inject's ComponentInjectIntermediateNode,
    /// @page's RouteAttributeExtensionNode) have NO span -- Razor drops it during
    /// lowering, which is why DirectiveSpyPass exists. When the directive gate has
    /// already refused the file with an exact location, repeating the same refusal
    /// with a worse location helps nobody, so this stays quiet THERE and only THERE:
    /// the file is already refused, and it is refused with a location.
    /// </summary>
    void Unaccounted(IntermediateNode node, string where)
    {
        if (Located(node)) return;

        Diag("unaccounted-node",
            $"{node.GetType().Name} {where} is not something this compiler structurally understands. " +
            "Refusing to emit rather than skip it silently -- a skipped node is a module that looks " +
            "right and does less than the source says.",
            node.Source);
    }

    /// <summary>The class itself is not the shape this compiler expects (@inherits/@implements/@typeparam).</summary>
    void ClassShape(ClassDeclarationIntermediateNode cls, string what)
    {
        if (Located(cls)) return;
        Diag("unaccounted-node",
            $"the component class {what}. Refusing to emit rather than drop it silently.",
            cls.Source);
    }

    /// <summary>
    /// True when this node has no span of its own AND the directive gate has already
    /// refused the file at an exact location. Razor drops the span when it lowers a
    /// directive, so repeating that refusal here could only add a WORSE location to a
    /// file that is already refused. It stays quiet THERE and only there: a spanless
    /// node with no directive to explain it is still reported, loudly.
    /// </summary>
    bool Located(IntermediateNode node) =>
        node.Source is null && _diagnostics.Any(d => d.Reason == "unsupported-directive" && d.Source is not null);

    bool IsFromThisFile(SourceSpan? s) =>
        s is { } span && !string.IsNullOrEmpty(span.FilePath) &&
        string.Equals(Path.GetFullPath(span.FilePath), _file, StringComparison.Ordinal);

    /// <summary>Emit one IR node. Returns the JS expression to insert into the parent, or null if it emitted itself.</summary>
    string? EmitNode(IntermediateNode node, string? parent)
    {
        switch (node)
        {
            case MarkupElementIntermediateNode el:
                return EmitElement(el);

            // Static text. Includes the "\n\n" whitespace between siblings -- see
            // Program.cs's header for why those are EMITTED and not stripped.
            case HtmlContentIntermediateNode html:
                return $"document.createTextNode({JsString(RawText(html))})";

            // `@ChildContent` inside a composed child: not a binding but a POSITION -- the composing
            // parent's markup is inlined here, in the PARENT's scope (decision 131).
            case CSharpExpressionIntermediateNode frag when _code.SlotIsFragment(frag):
                EmitFragment(frag, parent);
                return null;

            // @currentCount
            case CSharpExpressionIntermediateNode expr:
                return EmitBinding(expr, parent);

            case MarkupBlockIntermediateNode block:
                throw new GeneratorException(
                    $"FIL-WIRING: an opaque markup block reached the emitter: {Trunc(block.Content)}. " +
                    "ComponentMarkupBlockPass was not removed (decision 52); the IR has no structure to " +
                    "compile and nothing here can be trusted. This is the TOOL being broken, not the input.");

            // Raw C# reaching the EMIT walk means the collect walk did not turn it into a
            // region, i.e. the two walks disagree about the file. Root control flow is a region
            // now (decision 89), emitted via EmitOps, so a code node arriving HERE -- walked
            // individually -- really is the two walks disagreeing about the file.
            //
            // UNLESS a diagnostic already explains it (decision 142): a refused region is deliberately
            // not planned, so its C# reaching this walk is the refusal's expected shadow, and throwing
            // FIL-WIRING here MASKED the real message ("tool broken" instead of the author's answer).
            case CSharpCodeIntermediateNode when _diagnostics.Count > 0:
                return null;
            case CSharpCodeIntermediateNode code:
                throw new GeneratorException(
                    $"FIL-WIRING: raw template C# ({Trunc(RawText(code))}) reached the emitter. The collect " +
                    "walk turns every CSharpCodeIntermediateNode into a region (decision 54's reassembly), so " +
                    "this one was never planned and nothing here can be trusted. This is the TOOL being " +
                    "broken, not the input.");

            // @key is CONSUMED by the list() this row belongs to (see EmitList). Anywhere else
            // it names an identity that nothing reconciles.
            case SetKeyIntermediateNode key when ReferenceEquals(key, _consumedKey):
                return null;

            case SetKeyIntermediateNode key:
                Diag("unsupported-directive",
                    "@key is only meaningful inside a @foreach, where it compiles to list()'s keyOf. On an " +
                    "element that no list reconciles there is no identity for it to be, and nothing would " +
                    "read it. Refusing to emit rather than drop it silently.",
                    key.Source);
                return null;

            // A capture that reached the EMIT walk was not consumed by an element (EmitElement takes the
            // ones it names). @ref on anything else has no node to capture.
            case ReferenceCaptureIntermediateNode r:
                Diag("unsupported-directive",
                    "@ref is admitted on an ELEMENT, where it names the const the element is emitted into " +
                    "(decision 132). Here there is no element for it to name. Refusing to emit.",
                    r.Source);
                return null;

            // <CascadingValue Value="@x"> (decision 134). Razor DOES resolve this one into a component node,
            // because it is a framework tag helper the reference pack describes. It emits no DOM of its own:
            // it puts a value in scope for its children and nothing more.
            case ComponentIntermediateNode c when c.TagName == "CascadingValue":
                EmitCascadingValue(c, parent);
                return null;

            // <EditForm> / <InputText> (decision 138) -- resolved framework components, like <CascadingValue>.
            case ComponentIntermediateNode c when c.TagName == "EditForm":
                return EmitEditForm(c, parent);

            case ComponentIntermediateNode c when c.TagName == "InputText":
                return EmitInputText(c);

            case ComponentIntermediateNode c:
                Diag("component-composition",
                    $"<{c.TagName}> resolved to a framework component, and it is not one of the three the " +
                    "subset admits (<CascadingValue>, <EditForm>, <InputText>). Composition otherwise resolves " +
                    "a child as a sibling .razor file. Validation components in particular are refused rather " +
                    "than ignored: without them every submit IS valid (Blazor's own behaviour), so ignoring " +
                    "one would let an invalid model submit silently. Refusing to emit.",
                    c.Source);
                return null;

            case TagHelperIntermediateNode th:
                Diag("component-composition",
                    $"<{th.TagName}> resolved to a component/tag helper. Component composition is not in " +
                    "Phase 2's subset. Refusing to emit.",
                    th.Source);
                return null;

            default:
                Unaccounted(node, "in the template");
                return null;
        }
    }

    /// <summary>Returns the JS variable holding the element, or null if it was refused.</summary>
    string? EmitElement(MarkupElementIntermediateNode el)
    {
        // COMPONENT COMPOSITION. An upper-case (or dotted) tag is a component reference. Razor left
        // it a plain markup element because this generator has no compilation to resolve it into a
        // ComponentIntermediateNode; EmitComposition resolves it as a same-directory sibling .razor
        // and INLINES it (static-leaf slice, decision 88), or refuses with a clear located reason.
        if (LooksLikeComponent(el.TagName))
            return EmitComposition(el);

        // @ref (decision 132). An element the template captures into a name simply BECOMES that name:
        // Blazor needs an ElementReference because it carries an id across the .NET/JS boundary, but the
        // emitted module already holds the node in a const, so @ref only decides what that const is
        // CALLED. Resolved BEFORE the element is named, because the name is the whole mapping.
        var capture = el.Children.OfType<ReferenceCaptureIntermediateNode>().FirstOrDefault();
        var v = $"_el{_el++}";
        if (capture is not null)
        {
            if (RefTargetJs(capture) is not { } refJs) return null;
            v = refJs;
            _el--;   // the generated name was not used; do not burn the counter on it
        }
        _create.Add($"const {v} = document.createElement({JsString(el.TagName)});");

        // TWO-WAY BINDING (@bind): Razor lowers it to a synthesised value=/onchange pair; TryBind emits
        // the reactive binding and hands back the two attributes it consumed, so the normal loop skips them.
        var boundAttrs = TryBind(el, v);

        // Attributes are handled SEPARATELY from children and never by document order.
        // With the tag helper chain active Razor reorders them -- <button>'s "Click me"
        // content node arrives BEFORE its id/onclick attributes -- so walking children
        // in order and switching on node type is the only correct traversal.
        foreach (var attr in el.Children.OfType<HtmlAttributeIntermediateNode>())
            if (!boundAttrs.Contains(attr))
                EmitAttribute(el, v, attr);

        // A container whose children held C# does not have children in document order any
        // more -- it has a re-parsed STATEMENT LIST (decision 54). Its emission comes from
        // the C# front end, which is the only thing that ever saw the loop.
        if (_regions.Contains(el))
        {
            EmitOps(_code.OpsFor(el), v);
            return v;
        }

        foreach (var child in el.Children)
        {
            if (child is HtmlAttributeIntermediateNode) continue;
            // CONSUMED above: the capture is what named this element, so there is nothing left to emit.
            if (ReferenceEquals(child, capture)) continue;
            var c = EmitNode(child, parent: v);
            if (c is not null) _create.Add($"insert({v}, {c});");
        }
        return v;
    }

    static string AttrCs(HtmlAttributeIntermediateNode attr) =>
        string.Concat(attr.Children.OfType<CSharpExpressionAttributeValueIntermediateNode>()
            .SelectMany(c => c.Children.OfType<IntermediateToken>()).Select(t => t.Content));

    /// <summary>The user's bound field inside a @bind-lowered `value` attribute: its single token that
    /// carries a real source (the `BindConverter.FormatValue(` wrapper tokens are synthesised).</summary>
    static string? BoundField(HtmlAttributeIntermediateNode valueAttr) =>
        valueAttr.Children.OfType<CSharpExpressionAttributeValueIntermediateNode>()
            .SelectMany(c => c.Children.OfType<IntermediateToken>())
            .FirstOrDefault(t => t.Source is not null)?.Content.Trim();

    /// <summary>
    /// TWO-WAY BINDING (@bind). Razor lowers `@bind="text"` on an &lt;input&gt; to a synthesised `value`
    /// attribute (`BindConverter.FormatValue(text)`) plus an `onchange` attribute
    /// (`CreateBinder(this, __value => text = __value, …)`). For a STRING field the converter is identity,
    /// so this compiles to a reactive value-property effect plus a change listener that writes the signal:
    ///     effect(() =&gt; { input.value = text.value; });
    ///     listen(input, 'change', (e) =&gt; { text.value = e.target.value; });
    /// Scoped to a string field that is ALREADY a signal (see IsStringSignal). Returns the two consumed
    /// attributes so the normal attribute loop skips them, or an empty set when this is not a @bind.
    /// </summary>
    HashSet<HtmlAttributeIntermediateNode> TryBind(MarkupElementIntermediateNode el, string v)
    {
        var attrs = el.Children.OfType<HtmlAttributeIntermediateNode>().ToList();
        var changeAttr = attrs.FirstOrDefault(a =>
            string.Equals(a.AttributeName, "onchange", StringComparison.OrdinalIgnoreCase) &&
            AttrCs(a).Contains("CreateBinder"));
        if (changeAttr is null) return [];

        // The bound value attribute is `value` for a text input (string) or `checked` for a checkbox (bool).
        static bool IsFormat(HtmlAttributeIntermediateNode a, string name) =>
            string.Equals(a.AttributeName, name, StringComparison.OrdinalIgnoreCase) &&
            AttrCs(a).Contains("BindConverter.FormatValue");
        var valueAttr = attrs.FirstOrDefault(a => IsFormat(a, "value"));
        var checkedAttr = attrs.FirstOrDefault(a => IsFormat(a, "checked"));
        var bound = valueAttr ?? checkedAttr;
        if (bound is null) return [];

        var field = BoundField(bound);
        var isCheckbox = checkedAttr is not null;
        // The bound field's kind decides the emission: bool (checkbox), string (verbatim), or int (parsed).
        var kind = field is null ? null
            : isCheckbox && _code.IsBoolSignal(field) ? "bool"
            : !isCheckbox && _code.IsStringSignal(field) ? "string"
            : !isCheckbox && _code.IsIntSignal(field) ? "int"
            : null;
        if (kind is null)
        {
            Diag("unsupported-bind",
                $"@bind on <{el.TagName}> binds '{Trunc(field ?? "?")}', which is not a " +
                $"{(isCheckbox ? "bool" : "string or int")} field that is already a signal. @bind binds a STRING " +
                "or INT field (text input) or a BOOL field (checkbox) the component also reads and assigns (so it " +
                "is reactive); other types need their own BindConverter, and a pure @bind-only field needs its " +
                "reactivity marked from the template -- both deferred. Refusing to emit.",
                bound.Source);
            return [bound, changeAttr];   // consumed: the located refusal above is the one diagnostic
        }

        var js = _code.FieldJs(field!);
        _used.Add("effect");
        _used.Add("listen");
        switch (kind)
        {
            case "bool":
                // A checkbox: the .checked PROPERTY is the two-way surface, already a bool -- no parsing.
                _bindings.Add($"effect(() => {{ {v}.checked = {js}.value; }});");
                _events.Add($"listen({v}, 'change', (e) => {{ {js}.value = e.target.checked; }});");
                break;
            case "string":
                _bindings.Add($"effect(() => {{ {v}.value = {js}.value; }});");
                _events.Add($"listen({v}, 'change', (e) => {{ {js}.value = e.target.value; }});");
                break;
            case "int":
                // int/int32: format as a string; parse the change back mirroring int.TryParse (invariant,
                // NumberStyles.Integer) -- regex for the accepted shape, int32 range, and revert-on-invalid
                // so an unparseable/overflowing entry keeps the field and re-renders the old value (Blazor).
                _bindings.Add($"effect(() => {{ {v}.value = String({js}.value); }});");
                _events.Add(
                    $"listen({v}, 'change', (e) => {{\n" +
                    "    const _s = e.target.value;\n" +
                    "    if (/^\\s*[+-]?\\d+\\s*$/.test(_s)) {\n" +
                    "      const _n = parseInt(_s, 10);\n" +
                    $"      if (_n >= -2147483648 && _n <= 2147483647) {{ {js}.value = _n; return; }}\n" +
                    "    }\n" +
                    $"    {v}.value = String({js}.value);\n" +
                    "  });");
                break;
        }
        return [bound, changeAttr];
    }

    /// <summary>
    /// STATIC-LEAF COMPONENT COMPOSITION (decision 88). Resolve &lt;Greeting Name="World" /&gt; as the
    /// sibling Greeting.razor, compile it in its OWN front end with the static parameters bound to
    /// constants, and INLINE its single static root into the parent's tree. No import and no runtime
    /// component instance: a static param folds `@Name` to `'World'`, so the child is static DOM.
    /// Anything outside the slice -- a missing sibling, a bound parameter, a non-string parameter, a
    /// child with state/behaviour, or not exactly one root element -- refuses, loud and located.
    /// </summary>
    /// <summary>
    /// The JS const an @ref names, or null (refused). The target must be an ElementReference FIELD that
    /// @code declares: that is what makes `box` mean this node in the compiled C#, and it is checked
    /// against the compiler's own table rather than against the spelling (decision 57's rule again).
    /// </summary>
    string? RefTargetJs(ReferenceCaptureIntermediateNode capture)
    {
        var name = capture.IdentifierToken?.Content?.Trim();
        if (string.IsNullOrEmpty(name))
            throw new GeneratorException(
                "FIL-WIRING: an @ref arrived with no identifier. This is the TOOL being broken, not the input.");

        if (!_code.IsElementRefField(name!))
        {
            Diag("unsupported-directive",
                $"@ref=\"{name}\" names '{name}', which @code does not declare as an ElementReference field. " +
                "An @ref captures the element into a field the component declares (`private ElementReference " +
                $"{name};`); without one there is nothing for the capture to name, and a capture wired to " +
                "nothing is a handle that silently refers to no node. Refusing to emit.",
                capture.Source);
            return null;
        }

        return _code.FieldJs(name!);
    }

    /// <summary>
    /// The markup a composing parent passed INTO a child (`&lt;Card&gt;…&lt;/Card&gt;`), together with the
    /// parent context it must be compiled in (decision 131). All four fields travel together because the
    /// fragment is written in the PARENT's file and scope: its `@count` names the PARENT's signal, and its
    /// regions were planned by the PARENT's collect walk. Emitting it under the child's context would
    /// resolve those names against the wrong component -- or, worse, silently against nothing.
    /// </summary>
    sealed record Fragment(
        IReadOnlyList<IntermediateNode> Nodes,
        CSharpFrontEnd Code,
        HashSet<IntermediateNode> Regions,
        string File);

    /// <summary>The fragment in scope for the child currently being inlined, or null. Saved/restored
    /// around each composition exactly as _code/_regions/_file are, so a nested composition cannot see
    /// its grandparent's fragment.</summary>
    Fragment? _fragment;

    /// <summary>The cascaded values in scope, keyed by C# TYPE (decision 134). A stack in effect: each
    /// &lt;CascadingValue&gt; adds its entry for the duration of its children and restores on the way out,
    /// so a cascade's reach is exactly its lexical extent -- which, once everything inlines into one
    /// mount(), is all a cascade ever was.</summary>
    Dictionary<string, (string Js, bool Reactive)> _cascades = new(StringComparer.Ordinal);

    /// <summary>
    /// Inline the composing parent's markup at the child's `@ChildContent` (decision 131). The parent
    /// context is restored for the duration, so the fragment compiles in the scope it was WRITTEN in --
    /// which is what makes `<span id="body">@count</span>` a live binding on the parent's signal even
    /// though it renders inside the child's element.
    /// </summary>
    void EmitFragment(CSharpExpressionIntermediateNode slot, string? parent)
    {
        if (_fragment is not { } frag)
        {
            // No parent supplied one. Blazor renders a null RenderFragment as NOTHING, so this emits
            // nothing -- the one case where silence is the faithful answer rather than a dropped node.
            return;
        }

        if (parent is null)
            throw new GeneratorException(
                "FIL-WIRING: a RenderFragment reached the emitter with no container to insert into. " +
                "A fragment slot is always a child of the element the composed child declared it in. " +
                "This is the TOOL being broken, not the input.");

        var savedFile = _file;
        var savedCode = _code;
        var savedRegions = _regions;
        var savedFragment = _fragment;

        _file = frag.File;
        _code = frag.Code;
        _regions = frag.Regions;
        // The fragment's own content may compose further children, but it is not itself inside one:
        // clearing this is what stops a fragment that contains `@ChildContent` from re-inlining itself.
        _fragment = null;

        foreach (var node in frag.Nodes)
        {
            var c = EmitNode(node, parent);
            if (c is not null) _create.Add($"insert({parent}, {c});");
        }

        _file = savedFile;
        _code = savedCode;
        _regions = savedRegions;
        _fragment = savedFragment;
    }

    /// <summary>
    /// &lt;CascadingValue Value="@x"&gt;…&lt;/CascadingValue&gt; (decision 134). Emits NO element: it puts the
    /// parent's translated expression in scope, keyed by its C# TYPE, for the duration of its children.
    /// Because the whole composition inlines into one mount(), a descendant's [CascadingParameter] then
    /// binds to that expression directly -- the cascade IS lexical scope, and it costs nothing at runtime.
    /// </summary>
    void EmitCascadingValue(ComponentIntermediateNode node, string? parent)
    {
        var valueAttr = node.Children.OfType<ComponentAttributeIntermediateNode>()
            .FirstOrDefault(a => a.AttributeName == "Value");
        var expr = valueAttr?.Children.OfType<CSharpExpressionIntermediateNode>().FirstOrDefault();

        if (expr is null)
        {
            Diag("unsupported-cascade",
                "<CascadingValue> needs a Value=\"@…\" expression. A cascade with nothing to cascade would " +
                "put the type's default in scope and let descendants render it as real data. Refusing to emit.",
                node.Source);
            return;
        }

        // Named cascades are matched by NAME instead of by type, which is a second matching rule and a
        // second set of failure modes; not measured, so not admitted.
        if (node.Children.OfType<ComponentAttributeIntermediateNode>().Any(a => a.AttributeName == "Name"))
        {
            Diag("unsupported-cascade",
                "<CascadingValue Name=\"…\"> matches by NAME rather than by type. Only type-matched cascades " +
                "are in the subset. Refusing to emit.",
                node.Source);
            return;
        }

        if (_code.SlotTypeDisplay(expr) is not { } key)
        {
            Diag("unsupported-cascade",
                $"the cascaded expression '{Trunc(RawText(expr))}' has no resolved type to match on. Refusing to emit.",
                node.Source);
            return;
        }

        var saved = _cascades;
        _cascades = new Dictionary<string, (string, bool)>(_cascades, StringComparer.Ordinal)
        {
            [key] = (_code.SlotJs(expr), _code.SlotIsReactive(expr)),
        };

        foreach (var content in node.Children.OfType<ComponentChildContentIntermediateNode>())
            foreach (var child in content.Children)
            {
                var c = EmitNode(child, parent);
                if (c is not null && parent is not null) _create.Add($"insert({parent}, {c});");
            }

        _cascades = saved;
    }

    /// <summary>
    /// &lt;EditForm Model="@m" OnValidSubmit="Save"&gt; -&gt; a &lt;form&gt; whose submit calls Save
    /// (decision 138). preventDefault() is not decoration: without it the browser navigates on submit
    /// and the page reloads, which is precisely what Blazor's EditForm suppresses.
    ///
    /// WITHOUT A VALIDATOR, OnValidSubmit FIRES ON EVERY SUBMIT -- that is Blazor's behaviour, not a
    /// simplification, because "valid" is decided by validator components and there are none. A
    /// &lt;DataAnnotationsValidator /&gt; is therefore REFUSED rather than ignored: ignoring it would make
    /// an invalid model submit silently, which is the wrong answer dressed as the right one.
    /// </summary>
    string? EmitEditForm(ComponentIntermediateNode node, string? parent)
    {
        var attrs = node.Children.OfType<ComponentAttributeIntermediateNode>().ToList();

        // Blazor requires Model (or EditContext); it is the thing a validator would validate. Required
        // here too, so the form a Filament author writes is the form Blazor accepts.
        if (attrs.All(a => a.AttributeName != "Model"))
        {
            Diag("unsupported-form",
                "<EditForm> needs a Model=\"@…\". Blazor requires one (or an EditContext), and it is what " +
                "a validator validates. Refusing to emit.",
                node.Source);
            return null;
        }

        if (attrs.FirstOrDefault(a => a.AttributeName is "OnSubmit" or "OnInvalidSubmit") is { } unsupported)
        {
            Diag("unsupported-form",
                $"<EditForm {unsupported.AttributeName}> is not in the subset. Only OnValidSubmit is, because " +
                "without validator components every submit IS valid; OnSubmit and OnInvalidSubmit only differ " +
                "from it once validation exists. Refusing to emit.",
                node.Source);
            return null;
        }

        var v = $"_el{_el++}";
        _create.Add($"const {v} = document.createElement('form');");

        foreach (var attr in attrs)
        {
            if (attr.AttributeName is "Model") continue;   // consumed: nothing validates it (see above)
            if (attr.AttributeName != "OnValidSubmit")
            {
                Diag("unsupported-form",
                    $"<EditForm> parameter '{attr.AttributeName}' is not in the subset. Refusing to emit.",
                    node.Source);
                return null;
            }

            var handler = string.Concat(attr.Children.OfType<IntermediateToken>().Select(t => t.Content)).Trim();
            if (!NamedByTemplate(handler, $"OnValidSubmit on <{node.TagName}>", attr.Source ?? node.Source,
                    mustBeCallable: true))
                return null;

            _used.Add("listen");
            // Recorded like any other handler, so single-use inlining and batching decide identically.
            _handlers.Add((v, "submit", handler));
            _preventDefault.Add(v);
        }

        foreach (var content in node.Children.OfType<ComponentChildContentIntermediateNode>())
            foreach (var child in content.Children)
            {
                var c = EmitNode(child, parent: v);
                if (c is not null) _create.Add($"insert({v}, {c});");
            }

        return v;
    }

    /// <summary>
    /// &lt;InputText id="x" @bind-Value="model.Name" /&gt; -&gt; an &lt;input&gt; with the SAME two-way shape
    /// decision 104 emits for `@bind` on a field: a value effect and a change listener (decision 138).
    /// The difference is only the target -- a record PROPERTY signal rather than a field signal -- and
    /// that is why the bind had to be marked as a write (MarkTemplateWrites) for it to be a signal at all.
    /// </summary>
    string? EmitInputText(ComponentIntermediateNode node)
    {
        var attrs = node.Children.OfType<ComponentAttributeIntermediateNode>().ToList();
        var value = attrs.FirstOrDefault(a => a.AttributeName == "Value")
            ?.Children.OfType<CSharpExpressionIntermediateNode>().FirstOrDefault();

        if (value is null)
        {
            Diag("unsupported-form",
                "<InputText> needs an @bind-Value. An unbound input displays nothing and stores nothing. " +
                "Refusing to emit.",
                node.Source);
            return null;
        }

        var js = _code.SlotJs(value);
        if (!_code.SlotIsReactive(value))
        {
            Diag("unsupported-form",
                $"@bind-Value=\"{Trunc(RawText(value))}\" does not name reactive state. A two-way bind must " +
                "write somewhere the display can read back; a target that is not a signal would take the " +
                "keystroke and never re-render. Refusing to emit.",
                node.Source);
            return null;
        }

        var v = $"_el{_el++}";
        _create.Add($"const {v} = document.createElement('input');");

        // Any other parameter is a plain DOM attribute (id, class, placeholder…). A CSharp-valued one is
        // out of this slice: it would be a reactive attribute on a component, which nothing measured.
        foreach (var attr in attrs)
        {
            if (attr.AttributeName is "Value" or "ValueChanged" or "ValueExpression") continue;
            var literal = string.Concat(attr.Children.OfType<HtmlContentIntermediateNode>()
                .SelectMany(h => h.Children.OfType<IntermediateToken>()).Select(t => t.Content));
            if (literal.Length == 0)
            {
                Diag("unsupported-form",
                    $"<InputText> parameter '{attr.AttributeName}' is not a literal attribute value. " +
                    "Refusing to emit.",
                    node.Source);
                return null;
            }
            _create.Add(SetAttribute(v, attr.AttributeName, JsString(literal)));
        }

        _used.Add("effect");
        _used.Add("listen");
        _bindings.Add($"effect(() => {{ {v}.value = {js}; }});");
        _events.Add($"listen({v}, 'change', (e) => {{ {AssignTo(js, "e.target.value")} }});");
        return v;
    }

    /// <summary>One static attribute, by the SAME rule the element path uses: a DOM property when the
    /// allowlist names one, setAttr() otherwise -- decision 21/51's mapping reused, not a second one.</summary>
    string SetAttribute(string v, string name, string valueJs)
    {
        if (PropertyAttributes.TryGetValue(name, out var prop)) return $"{v}.{prop} = {valueJs};";
        _used.Add("setAttr");
        return $"setAttr({v}, {JsString(name)}, {valueJs});";
    }

    /// <summary>The JS that writes a translated READ back. A signal read `x.value` writes as `x.value = v`,
    /// which is the same text -- so this is a seam with one case today and a name for the day it has two.</summary>
    static string AssignTo(string readJs, string valueJs) => $"{readJs} = {valueJs};";

    string? EmitComposition(MarkupElementIntermediateNode el)
    {
        var childPath = Path.Combine(Path.GetDirectoryName(_file)!, el.TagName + ".razor");
        if (!File.Exists(childPath))
        {
            Diag("unresolved-component",
                $"<{el.TagName}> resolves to a same-directory component {el.TagName}.razor, which does not " +
                "exist. Composition resolves a child as a sibling .razor file; a framework component such as " +
                "<EditForm> is a spec 3 non-goal and has no sibling here. Refusing to emit.",
                el.Source);
            return null;
        }

        // Two kinds of binding. A STATIC scalar (Name="World") folds to a JS string literal (#88). A
        // BOUND value (Value="@count") is the parent's translated EXPRESSION (decision 90): the parent
        // already compiled it (CollectComponentBindings harvested it into FreeSlots), so SlotJs is its
        // JS and SlotIsReactive says whether it reads a parent signal. The child inlines into the
        // parent's scope, so a reactive binding references the parent's signal directly -- a live @Name.
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        var reactive = new HashSet<string>(StringComparer.Ordinal);
        var handlers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attr in el.Children.OfType<HtmlAttributeIntermediateNode>())
        {
            var bound = attr.Children.OfType<CSharpExpressionAttributeValueIntermediateNode>().FirstOrDefault();
            if (bound is not null)
            {
                // AN EVENT CALLBACK (decision 130): the bound expression NAMES A METHOD rather than
                // computing a value, so the parent is handing the child one of its own methods. There is
                // no signal here and nothing to display, so the reactivity rule below does not apply --
                // what is bound is a NAME, and it is validated by the same guard every other handler
                // passes, HERE, while _code is still the parent that declares it. Downstream the child
                // records the parent's method, so inlining and batching decide exactly as they would
                // have had the parent written the handler on that button itself.
                if (_code.SlotIsMethodGroup(bound))
                {
                    var named = RawText(bound).Trim();
                    if (!NamedByTemplate(named, $"the callback '{attr.AttributeName}' on <{el.TagName}>",
                            bound.Source ?? el.Source, mustBeCallable: true))
                        return null;
                    handlers[attr.AttributeName] = named;
                    continue;
                }

                // The slice is REACTIVE binds: `@count` where count is parent state that changes. A bound
                // value that is NOT reactive (a constant expression, or a never-mutated field) is the
                // BIND-ONCE case -- deferred, refused rather than shipped as a value that silently never
                // tracks. A truly constant display uses a STATIC attribute (Name="World", #88).
                if (!_code.SlotIsReactive(bound))
                {
                    Diag("bound-parameter",
                        $"the parameter '{attr.AttributeName}' on <{el.TagName}> is bound to '{Trunc(RawText(bound))}', " +
                        "which is not reactive parent state. The bound-parameter slice wires a LIVE binding on a parent " +
                        "signal (decision 90); a bind-once capture of a constant is deferred, and a genuinely static " +
                        "value should use a static attribute (Name=\"...\"). Refusing to emit rather than ship a bound " +
                        "parameter that silently never tracks its source.",
                        el.Source);
                    return null;
                }
                bindings[attr.AttributeName] = _code.SlotJs(bound);
                reactive.Add(attr.AttributeName);
                continue;
            }
            // Prefix-aware for the same reason as the static path (decision 151): a multi-token
            // value arrives as several nodes whose spaces are prefixes, and the child must fold
            // the string the parent AUTHORED.
            var value = string.Concat(attr.Children.OfType<HtmlAttributeValueIntermediateNode>()
                .Select(h => h.Prefix + string.Concat(h.Children.OfType<IntermediateToken>().Select(t => t.Content))));
            bindings[attr.AttributeName] = JsString(value);
        }

        var childParse = RazorFrontEnd.Parse(childPath);
        var childCode = new CSharpFrontEnd();
        childCode.BindParameters(bindings, reactive, handlers);
        childCode.BindCascades(_cascades);

        // THE MARKUP THE PARENT PASSED IN (decision 131). Everything that is not an attribute is the
        // child content: `<Card Title="x"><span>…</span></Card>` hands the child that <span>.
        //
        // THIS LOOP CLOSED A SILENT MIS-COMPILE. Before it existed, EmitComposition read only the
        // ATTRIBUTE children, so a composed element's content was neither emitted nor refused -- it was
        // DROPPED, at exit 0, and the module rendered a card with nothing in it. That is precisely the
        // failure section 10 forbids and the reason the node gate exists; it was measured, not guessed
        // (a probe emitted a module with the passed <span> simply absent).
        var fragmentNodes = el.Children.Where(c => c is not HtmlAttributeIntermediateNode).ToList();

        var savedFile = _file;
        var savedCode = _code;
        var savedRegions = _regions;
        var savedFragment = _fragment;
        var diagBefore = _diagnostics.Count;

        _file = childParse.FilePath;
        var (childMethod, childRegions) = PrepareComponent(childParse, childCode);

        string? result = null;
        if (_diagnostics.Count == diagBefore)   // the child @code compiled clean
        {
            var unknown = bindings.Keys.Concat(handlers.Keys)
                .FirstOrDefault(k => !childCode.ParameterNames.Contains(k));
            var roots = childMethod.Children.OfType<MarkupElementIntermediateNode>().ToList();

            if (!childCode.IsLeafDisplay)
                Diag("composition-out-of-subset",
                    $"<{el.TagName}> ({el.TagName}.razor) has state or behaviour. The static-leaf slice " +
                    "composes a LEAF DISPLAY child -- [Parameter] properties only, no fields, methods or " +
                    "events. A stateful child needs its own mounted instance, which is not implemented. " +
                    "Refusing to emit.", el.Source);
            else if (childCode.FirstBoundNonStringParameter() is { } bad)
                Diag("composition-out-of-subset",
                    $"parameter '{bad}' of <{el.TagName}> is not a string. The static-leaf slice folds a " +
                    "STRING attribute value into the child; a numeric or bool parameter would fold a string " +
                    "where a number is meant. Refusing to emit rather than mistranslate.", el.Source);
            else if (unknown is not null)
                Diag("composition-out-of-subset",
                    $"<{el.TagName}> has no parameter '{unknown}'. {el.TagName}.razor declares: " +
                    $"{string.Join(", ", childCode.ParameterNames)}. Refusing to emit.", el.Source);
            // Content passed to a child that declares no RenderFragment has nowhere to go. Blazor calls
            // this out at build time; before decision 131 this compiler DROPPED it silently.
            else if (fragmentNodes.Count > 0 && !childCode.FragmentParameterNames.Any())
                Diag("composition-out-of-subset",
                    $"<{el.TagName}> was given content, but {el.TagName}.razor declares no " +
                    "[Parameter] RenderFragment to place it in, so there is nowhere in the child for it to " +
                    "render. Declare `[Parameter] public RenderFragment? ChildContent { get; set; }` and " +
                    "render it with @ChildContent. Refusing to emit rather than drop the content silently.",
                    el.Source);
            else if (roots.Count != 1)
                Diag("composition-out-of-subset",
                    $"<{el.TagName}> ({el.TagName}.razor) must have exactly ONE root element for the " +
                    $"static-leaf slice; it has {roots.Count}. Refusing to emit.", el.Source);
            else
            {
                // Walk the child's root with _code swapped to the child front end: its create
                // statements splice INTO the parent's shared _create (inline), and @Name folds to
                // the bound constant. Same save/restore idiom EmitBranchFn uses for _create/_bindings.
                _code = childCode;
                _regions = childRegions;
                // The fragment travels with the PARENT context it was written in, so that when the child's
                // @ChildContent is reached the parent's scope can be restored to compile it (decision 131).
                _fragment = fragmentNodes.Count > 0
                    ? new Fragment(fragmentNodes, savedCode, savedRegions, savedFile)
                    : null;
                result = EmitNode(roots[0], parent: null);
            }
        }

        _file = savedFile;
        _code = savedCode;
        _regions = savedRegions;
        _fragment = savedFragment;
        return result;
    }

    /// <summary>Emit one re-parsed region, in the order the C# says.</summary>
    void EmitOps(IReadOnlyList<TemplateOp> ops, string container)
    {
        foreach (var op in ops)
            switch (op)
            {
                case CodeOp code:
                    // A @{ } local declaration: emit the one-time `const x = …;` before the markup that
                    // reads it (ops are in document order, so this lands ahead of the reader).
                    _create.AddRange(code.Lines);
                    break;
                case MarkupOp m:
                    var c = EmitNode(m.Node, parent: container);
                    if (c is not null) _create.Add($"insert({container}, {c});");
                    break;
                case ForEachOp fe:
                    EmitList(fe, container);
                    break;
                case IfOp iff:
                    EmitIf(iff, container);
                    break;
            }
    }

    /// <summary>
    /// `@foreach (Row row in _rows) { &lt;tr @key="row.Id"&gt;...&lt;/tr&gt; }` -> list().
    ///
    /// THE ROW TEMPLATE IS A FUNCTION, and its create/binding split is mount()'s own, one
    /// level down: list() calls it ONCE per key, inside that row's disposal scope and
    /// untracked, so the effect it builds is adopted by the row and dies with it. That is what
    /// stops #run leaking 1000 effects per iteration -- and it is a property of the RUNTIME
    /// (list.ts's scope()), which is why nothing extra is emitted here to get it.
    ///
    /// THE SOURCE reads the version signal and hands reconcile() the LIVE array (rows.js
    /// mapping decision 1). Not a copy: reconcile only reads `items` during the pass and never
    /// retains it, and copying would reintroduce exactly the O(n^2) that decision rejects.
    /// </summary>
    void EmitList(ForEachOp fe, string container)
    {
        // @key -> keyOf, and it must NOT be reactive. reconcile() calls keyOf with the list
        // effect as the ACTIVE subscriber, so a signal read here subscribes the list to every
        // row's key -- 1000 dependency edges whose only possible effect is to re-reconcile the
        // entire table. rows.js's header calls this out as the reason Row.Id is a plain field;
        // this is the same fact, enforced instead of hoped for.
        if (_code.SlotIsReactive(fe.Key))
        {
            Diag("reactive-key",
                $"@key=\"{Trunc(RawText(fe.Key))}\" reads REACTIVE state. @key compiles to list()'s keyOf, " +
                "which reconcile() calls with the list effect as the active subscriber -- so this would " +
                "subscribe the whole list to every row's key, and the only thing those subscriptions can ever " +
                "do is re-reconcile the entire table when one row changes. An identity that changes is not an " +
                "identity. Refusing to emit.",
                fe.Key.Source);
            return;
        }

        var fn = Unique("create" + char.ToUpperInvariant(fe.Var[0]) + fe.Var[1..]);

        // The row's own create/bindings. Saved and restored rather than threaded through every
        // call: the row template IS mount()'s emission shape, one level down. Events too (decision
        // 141): a row listener names row-local consts AND may capture the loop variable, so every
        // listen() this row's emission produces is scooped out of the mount-level section and wired
        // inside the row function, where both exist -- and dies with the row (list()'s disposal scope).
        var outerCreate = _create;
        var outerBindings = _bindings;
        var outerKey = _consumedKey;
        var eventsBefore = _events.Count;
        var handlersBefore = _handlers.Count;
        _create = [];
        _bindings = [];
        _consumedKey = fe.Key;

        var root = EmitNode(fe.Body, parent: null);
        var body = new List<string>();
        body.AddRange(_create);
        body.AddRange(_bindings);
        body.AddRange(_events.GetRange(eventsBefore, _events.Count - eventsBefore));
        _events.RemoveRange(eventsBefore, _events.Count - eventsBefore);

        _create = outerCreate;
        _bindings = outerBindings;
        _consumedKey = outerKey;

        // A NAMED-method handler inside a row records into the DEFERRED mount-level pass (its site
        // count decides inlining), which would emit its listen() where the row's element const does
        // not exist -- broken JS, silently. Refused instead (section 10); the captured-lambda form
        // `() => Method()` is the row idiom and is supported (decision 141).
        if (_handlers.Count > handlersBefore)
        {
            Diag("unsupported-handler",
                "a METHOD-NAMED event handler inside a @foreach row is not mapped: its listen() is wired " +
                "in the deferred mount-level pass, where the row's element does not exist. Use the inline " +
                "lambda form (`@onclick=\"() => TheMethod()\"`), which is wired inside the row (decision 141). " +
                "Refusing to emit.",
                fe.Body.Source);
            _handlers.RemoveRange(handlersBefore, _handlers.Count - handlersBefore);
            return;
        }

        if (root is null) return; // the row template was refused; nothing is emitted for it
        body.Add($"return {root};");

        _bindings.Add($"function {fn}({fe.Var}) {{\n" + string.Join("\n", body.Select(l => "  " + l)) + "\n}");

        _used.Add("list");
        // The source lambda is fixed by the collection's SHAPE (List<T> block vs signal T[]/Dict single
        // expression) and was built once in ForEach(); list() just receives it. keyOf is @key, create is
        // the row function, anchor is null (a list whose parent holds nothing after the rows appends).
        _bindings.Add(
            $"list({container}, {fe.SourceJs}, ({fe.Var}) => {_code.SlotJs(fe.Key)}, {fn}, null);");
    }

    /// <summary>
    /// `@if (cond) { &lt;body&gt; }` -> a conditional list() with a 0/1 source and a comment anchor.
    ///
    /// The anchor is a comment node inserted at the @if's position among its siblings (in _create,
    /// so it lands in source order); list() inserts the body BEFORE it, so the conditional is
    /// positioned correctly no matter what follows. Zero new runtime primitive: document.createComment
    /// is a DOM builtin, and 3-arg insert / list(...anchor) already exist.
    /// </summary>
    void EmitIf(IfOp op, string container)
    {
        var id = _if++;
        var anchor = $"_if{id}";
        _create.Add($"const {anchor} = document.createComment('');");
        _used.Add("insert");
        _create.Add($"insert({container}, {anchor});");
        _used.Add("list");

        // #81 FAST PATH: a plain @if whose body is a single markup node. Byte-for-byte the #81 emission
        // (a constant key and the body function passed directly), so the @if gate and snapshot hold.
        if (op.Branches.Count == 1 && op.Branches[0].Body is [MarkupOp only])
        {
            var fn = Unique("ifBody");
            if (!EmitBranchFn(only.Node, fn)) return; // body refused; nothing emitted
            _bindings.Add($"list({container}, () => ({op.Branches[0].Cond}) ? [0] : [], () => 0, {fn}, {anchor});");
            return;
        }

        // GENERAL: recursively flatten the whole nested @if/@else structure into ONE list(). Every leaf
        // markup node gets a global index + builder (DFS source order); the source is the decision tree
        // over all conditions; the key is the global index. No nesting reproduces #82/#98 bytes exactly.
        var fns = new List<string>();
        var src = IfExpr(op, id, fns);
        if (src is null) return;   // a leaf body was refused; nothing emitted
        _bindings.Add($"list({container}, () => {src}, (i) => i, {IfCreate(fns)}, {anchor});");
    }

    /// <summary>Recursive decision-tree expr for one @if: `(c0) ? &lt;b0&gt; : (c1) ? &lt;b1&gt; : &lt;bN&gt;` — a
    /// trailing @else has no test; a chain with no @else ends `: []`. Fills <paramref name="fns"/> with
    /// leaf builders in DFS source order. Returns null if a leaf body was refused. One markup node per
    /// branch with no nesting reproduces the pre-nesting `(c0) ? [0] : …` form exactly.</summary>
    string? IfExpr(IfOp op, int id, List<string> fns)
    {
        var parts = new List<string>();
        for (var i = 0; i < op.Branches.Count; i++)
        {
            if (BranchExpr(op.Branches[i], id, fns) is not { } b) return null;
            if (op.Branches[i].Cond is { } c) parts.Add($"({c}) ? {b} : ");
            else return string.Concat(parts) + b;   // trailing @else
        }
        return string.Concat(parts) + "[]";
    }

    /// <summary>A branch's active-index expr: its global leaf indices `[i, …]` if markup-only, or the
    /// parenthesized nested decision tree if its sole content is a nested @if.</summary>
    string? BranchExpr(IfBranch branch, int id, List<string> fns)
    {
        if (branch.Body is [IfOp nested])                              // single nested @if (slice 3, byte-identical)
            return IfExpr(nested, id, fns) is { } e ? $"({e})" : null;

        if (branch.Body.All(o => o is MarkupOp))                       // markup-only (slices 1/2, byte-identical)
        {
            var idxs = new List<int>();
            foreach (var op in branch.Body)
            {
                var fn = Unique($"ifBody{id}_{fns.Count}");
                if (!EmitBranchFn(((MarkupOp)op).Node, fn)) return null;
                idxs.Add(fns.Count);
                fns.Add(fn);
            }
            return "[" + string.Join(", ", idxs) + "]";
        }

        // MIXED markup + nested @if (decision 120): a markup node is a constant active leaf `i`; a nested @if
        // SPREADS its own decision-tree active indices `...(…)` into the same conditional-list array. So
        // `<p>@if(c){<span>}` is `[0, ...(c ? [1] : [])]` -- the <p> is always mounted, the <span> iff c.
        var parts = new List<string>();
        foreach (var op in branch.Body)
        {
            switch (op)
            {
                case MarkupOp m:
                    var fn = Unique($"ifBody{id}_{fns.Count}");
                    if (!EmitBranchFn(m.Node, fn)) return null;
                    parts.Add(fns.Count.ToString());
                    fns.Add(fn);
                    break;
                case IfOp io:
                    if (IfExpr(io, id, fns) is not { } e) return null;
                    parts.Add($"...({e})");
                    break;
                default:
                    return null;
            }
        }
        return "[" + string.Join(", ", parts) + "]";
    }

    /// <summary>
    /// Build one branch body into a `function {fnName}() { …; return root; }` binding, exactly as
    /// EmitList builds a row (fresh create/binding pair, adopted key scope). Returns false — nothing
    /// emitted — if the body was refused.
    /// </summary>
    bool EmitBranchFn(IntermediateNode bodyNode, string fnName)
    {
        var outerCreate = _create;
        var outerBindings = _bindings;
        var outerKey = _consumedKey;
        // Same scooping as a row's (decision 141): a listener on a branch-local element must be wired
        // inside the branch function, where its const exists -- not in the mount events section.
        var eventsBefore = _events.Count;
        var handlersBefore = _handlers.Count;
        _create = [];
        _bindings = [];
        _consumedKey = null;

        var root = EmitNode(bodyNode, parent: null);
        var lines = new List<string>();
        lines.AddRange(_create);
        lines.AddRange(_bindings);
        lines.AddRange(_events.GetRange(eventsBefore, _events.Count - eventsBefore));
        _events.RemoveRange(eventsBefore, _events.Count - eventsBefore);

        _create = outerCreate;
        _bindings = outerBindings;
        _consumedKey = outerKey;

        if (_handlers.Count > handlersBefore)
        {
            Diag("unsupported-handler",
                "a METHOD-NAMED event handler inside an @if branch is not mapped: its listen() is wired " +
                "in the deferred mount-level pass, where the branch's element does not exist. Use the inline " +
                "lambda form (`@onclick=\"() => TheMethod()\"`), which is wired inside the branch (decision 141). " +
                "Refusing to emit.",
                bodyNode.Source);
            _handlers.RemoveRange(handlersBefore, _handlers.Count - handlersBefore);
            return false;
        }

        if (root is null) return false;
        lines.Add($"return {root};");
        _bindings.Add($"function {fnName}() {{\n" + string.Join("\n", lines.Select(l => "  " + l)) + "\n}");
        return true;
    }

    /// <summary>`(i) => i === 0 ? f0() : i === 1 ? f1() : fN()` — the last branch needs no test.</summary>
    static string IfCreate(IReadOnlyList<string> fns)
    {
        var parts = new List<string>();
        for (var i = 0; i < fns.Count - 1; i++) parts.Add($"i === {i} ? {fns[i]}() : ");
        return "(i) => " + string.Concat(parts) + $"{fns[^1]}()";
    }

    /// <summary>A binding name mount() does not already hold. @code's bindings share this scope.</summary>
    string Unique(string want)
    {
        var name = want;
        for (var i = 2; _code.IsJsNameTaken(name); i++) name = want + i;
        return name;
    }

    /// <summary>Blazor's own rule: an upper-case initial (or a dotted name) is a component, not an element.</summary>
    static bool LooksLikeComponent(string tag) =>
        tag.Length > 0 && (char.IsUpper(tag[0]) || tag.Contains('.'));

    void EmitAttribute(MarkupElementIntermediateNode el, string v, HtmlAttributeIntermediateNode attr)
    {
        var name = attr.AttributeName;

        // An attribute that still carries its '@' is a Blazor directive attribute that
        // did NOT resolve. Two causes, and the honest message names both rather than
        // asserting the one that happens to be commoner:
        //   - it is a directive attribute outside this subset (@bind, @ref, ...), which
        //     has no descriptor to bind to here;
        //   - or the tag helper descriptors did not resolve at all, in which case even
        //     @onclick lands here as literal text (decision 53) -- the exact silent
        //     mis-compile section 10 forbids. RazorFrontEnd.Parse refuses that case
        //     outright, so this is the backstop, not the first line.
        if (name.StartsWith('@'))
        {
            Diag("unsupported-directive",
                $"attribute '{name}' on <{el.TagName}> still carries its '@': it was NOT resolved to a Blazor " +
                "directive attribute. Either it is a directive attribute outside Phase 2's subset (@bind and " +
                "@ref have no meaning in a Filament module), or the tag helper descriptors are missing " +
                "(decision 53) -- in which case even @onclick would land here and be emitted as a literal HTML " +
                "attribute WHILE APPEARING TO WORK. Refusing to emit either way.",
                attr.Source ?? el.Source);
            return;
        }

        var csharp = attr.Children.OfType<CSharpExpressionAttributeValueIntermediateNode>().ToList();
        var html = attr.Children.OfType<HtmlAttributeValueIntermediateNode>().ToList();

        // EVERY VALUE NODE ACCOUNTED FOR -- and this one was a REAL, SHIPPING, SILENT
        // DROP, not a hypothetical. A third node type exists, CSharpCodeAttributeValue-
        // IntermediateNode (`class="@if (c) { <text>a</text> }"`), and it matched NEITHER
        // list above, so `value` stayed "" and this method emitted `_el0.className = ''`
        // at exit 0 with ZERO diagnostics: the author's conditional simply GONE. Measured
        // on all three attribute paths, and the mixed case is the worst of them --
        // `class="box @if (c) { <text>active</text> }"` emitted `className = 'box'`, the
        // literal half surviving so the output looks like it worked.
        //
        // It is decision 41's pattern one more time and in the same method as the last
        // one: the document walk has Unaccounted() for exactly this shape, and the
        // ATTRIBUTE VALUE walk had no equivalent. A drop is worse than a splice, because
        // a splice is loud. This is the equivalent.
        var unaccounted = attr.Children
            .Where(c => c is not CSharpExpressionAttributeValueIntermediateNode
                     && c is not HtmlAttributeValueIntermediateNode)
            .ToList();

        if (unaccounted.Count > 0)
        {
            foreach (var u in unaccounted)
                Diag("unaccounted-attribute-value",
                    $"the value of attribute '{name}' on <{el.TagName}> contains a {u.GetType().Name} " +
                    $"({Trunc(RawText(u))}), which this compiler does not structurally understand -- C# " +
                    "control flow inside an attribute value is not in the subset. Refusing to emit rather " +
                    "than drop it silently: dropping it emits an attribute that is EMPTY, or that keeps " +
                    "only its literal half, at exit 0 -- a module that renders and lies.",
                    u.Source ?? attr.Source ?? el.Source);
            return;
        }

        if (csharp.Count > 0)
        {
            var tokens = csharp.SelectMany(c => c.Children.OfType<IntermediateToken>()).ToList();
            var expr = string.Concat(tokens.Select(t => t.Content));

            // Razor SYNTHESISES the EventCallback.Factory.Create<...>(this, wrapper and
            // gives those tokens no span; the token in the middle is what the AUTHOR
            // typed, and it carries the author's exact position. Reporting there points
            // at the handler itself rather than at the attribute, which matters most in
            // precisely the case that used to splice: a multi-line lambda.
            var authored = tokens.FirstOrDefault(t => t.Source is not null)?.Source ?? attr.Source ?? el.Source;

            if (TryUnwrapEventCallback(expr, out var handler))
            {
                var domEvent = name.StartsWith("on", StringComparison.Ordinal) ? name[2..] : name;

                // INLINE LAMBDA HANDLER (decision 105). CSharpFrontEnd wrapped `() => currentCount++` as a
                // synthetic method and TRANSLATED its body (currentCount -> currentCount.value, via the
                // semantic model, NOT a splice). Emit the arrow directly -- this is the ONE handler that is
                // not a bare @code method name, and it is safe precisely because the compiler translated it.
                if (_code.LambdaBodyJs(attr) is { } lambdaLines)
                {
                    _used.Add("listen");
                    var arrow = "() => {\n" + string.Join("\n", lambdaLines.Select(l => "  " + l)) + "\n}";
                    if (_code.LambdaBatched(attr)) { _used.Add("batch"); arrow = $"() => batch({arrow})"; }
                    _events.Add($"listen({v}, {JsString(domEvent)}, {arrow});");
                    return;
                }

                // Razor pre-lowers to Blazor semantics: the value is already
                // EventCallback.Factory.Create<MouseEventArgs>(this, Increment).
                // Filament has no EventCallback and no `this`; it has listen().
                //
                // THE GUARD. Without it this line spliced `handler` verbatim into
                // listen(), which is how `@onclick="() => currentCount++"` compiled to a
                // module that renders perfectly and whose button is dead forever.
                // AN EVENTCALLBACK PARAMETER (decision 130). Inside a composed child, `@onclick="OnBump"`
                // names a [Parameter] the PARENT bound to one of its own methods. Resolve the alias to
                // that method's C# NAME right here, before anything is recorded: the callback is not a
                // level of indirection to be preserved, it IS the parent's method, and once the name is
                // the parent's every downstream decision (single-use inlining, batching, MethodJs) reads
                // the parent's own tables and needs no knowledge of composition at all. Already validated
                // at the composition site, by this same guard, against the parent that declares it.
                if (_code.HandlerParamTarget(handler) is { } aliased)
                    handler = aliased;
                else if (!NamedByTemplate(handler, $"the handler for '{name}' on <{el.TagName}>", authored,
                        mustBeCallable: true)) return;

                // A handler that DECLARED the event may only be wired where the DOM provides that
                // event's shape (decision 159): keydown/keyup carry key/code/modifiers; a click
                // handed to it would read `undefined` where the author wrote e.Key -- rendering
                // fine and comparing against nothing, the silent kind. Refused with the fix.
                if (_code.HandlerTakesEvent(handler) && domEvent is not ("keydown" or "keyup"))
                {
                    Diag("unsupported-handler",
                        $"'{handler}' takes KeyboardEventArgs, but '{name}' is not a keyboard event -- only " +
                        "@onkeydown and @onkeyup provide one. Refusing to emit.",
                        attr.Source ?? el.Source);
                    return;
                }

                _used.Add("listen");

                // RECORDED, not emitted: whether this handler's body is inlined depends on
                // how many sites name it, which the walk does not know yet. See _handlers.
                _handlers.Add((v, domEvent, handler));
                return;
            }

            // COMPOSED STRING ATTRIBUTE VALUE (the `class` slice: pure #94/n°13, mixed #96/n°15). An
            // allow-listed string attribute folds its ordered value parts (literals + expressions) into one
            // setAttr. The pure `@expr` case is the degenerate fold (one expression, no literals) and emits
            // byte-identically -- the ReactiveAttr gate proves it. Reactive iff any expression part is
            // reactive; the effect lands in _bindings (before attach), so its first setAttr writes into the
            // detached tree and makes no MutationRecord.
            if (IsDynamicStringAttribute(name) && ComposableValue(attr) is { } parts)
            {
                var (js, reactive) = ComposeAttributeValue(parts);
                _used.Add("setAttr");
                if (reactive)
                {
                    _used.Add("effect");
                    _bindings.Add($"effect(() => setAttr({v}, {JsString(name)}, {js}));");
                }
                else
                {
                    _create.Add($"setAttr({v}, {JsString(name)}, {js});");
                }
                return;
            }

            // BOOLEAN / PRESENT-ABSENT ATTRIBUTE VALUE (the `disabled` slice, BENCH n°14). Same shape as
            // the `class` branch above (disjoint allowlist), but the compiled expression is wrapped in a
            // present/absent ternary: true -> '' -> setAttribute (present, <button disabled="">), false ->
            // null -> removeAttribute (absent, setAttr's own null->remove). Not the naive setAttr of the
            // bool, which would render disabled="true". The effect lands in _bindings (before attach), so
            // its first write goes into the detached tree and makes no MutationRecord.
            if (BooleanAttributes.Contains(name) && DynamicValue(attr) is { } boolNode)
            {
                var js = _code.SlotJs(boolNode);
                // A compound condition must be parenthesised before `? '' : null` -- the conditional
                // is right-associative, so a bare ternary here would swallow the present/absent arm
                // (decision 152's precedence rule; a dotted chain stays bare, snapshot-identical).
                if (!Regex.IsMatch(js, @"^[\w$]+(\.[\w$]+)*$")) js = "(" + js + ")";
                _used.Add("setAttr");
                if (_code.SlotIsReactive(boolNode))
                {
                    _used.Add("effect");
                    _bindings.Add($"effect(() => setAttr({v}, {JsString(name)}, {js} ? '' : null));");
                }
                else
                {
                    _create.Add($"setAttr({v}, {JsString(name)}, {js} ? '' : null);");
                }
                return;
            }

            Diag("dynamic-attribute",
                $"attribute '{name}' on <{el.TagName}> carries the C# expression \"{Trunc(expr)}\". This " +
                "compiler compiles a dynamic value only for ALLOW-LISTED attributes (reactive string: " +
                $"{string.Join(", ", DynamicAttributes.Order())}; boolean present/absent: " +
                $"{string.Join(", ", BooleanAttributes.Order())}); '{name}' is not one of them, and this is " +
                "neither a resolved event handler nor a static value. A dynamic value on an un-measured " +
                "attribute has no measurement covering it. Refusing to emit.",
                attr.Source ?? el.Source);
            return;
        }

        // Razor lowers a multi-token value ("w-1/2 hover:bg-amber-400 …") as SEVERAL
        // HtmlAttributeValue nodes whose leading whitespace is each node's PREFIX, not a token.
        // Concatenating tokens alone welded seven Tailwind utilities into one garbage class
        // (decision 151); the prefix is part of the authored value, exactly as
        // ComposeAttributeValue already treats it on the mixed path.
        var value = string.Concat(html.Select(h =>
            h.Prefix + string.Concat(h.Children.OfType<IntermediateToken>().Select(t => t.Content))));

        if (PropertyAttributes.TryGetValue(name, out var prop))
            _create.Add($"{v}.{prop} = {JsString(value)};");
        else
        {
            _used.Add("setAttr");
            _create.Add($"setAttr({v}, {JsString(name)}, {JsString(value)});");
        }
    }

    /// <summary>
    /// @currentCount / @row.Label -> a Text node owned forever by one effect.
    ///
    /// The text node is created once, empty, and handed to setText for the life of the app.
    /// Writing `span.textContent = v` instead would destroy and rebuild the span's children on
    /// every change: 2 DOM writes where the contract allows 1, and C3 would fail on markup that
    /// looks identical.
    ///
    /// DECISION 57's HOLE IS CLOSED HERE, BY CONSTRUCTION RATHER THAN BY A CHECK. 57 disclosed:
    /// "un @x sur un `let x = 5` ordinaire emettrait `x.value` -- faux. Le detecter exigerait
    /// d'analyser le JS de @code, ce que cette phase ne fait pas." The hole was not a missing
    /// test, it was a missing FRONT END: with an opaque JS seam the only available rule was
    /// "assume everything the template reads is a signal", and that assumption is what was
    /// false. This asks the compiler that did the lifting (SlotIsReactive), so a source that was
    /// NOT lifted compiles to a create-time text write with no effect -- exactly rows.js's
    /// treatment of @row.Id, whose source is non-reactive for the same reason.
    ///
    /// THE BARE-IDENTIFIER RULE IS GONE, AND THAT IS PHASE 3 DOING THE WORK RATHER THAN MOVING
    /// A THRESHOLD. Phase 2 refused anything but a bare name at a binding site because deciding
    /// what an expression MEANT was C# work it did not do; the guard was honest about being a
    /// stand-in for a parser. There is a parser now. `row.Id` is member access on a local record
    /// -- section 5's subset, verbatim -- and it is REFUSED BY THE C# FRONT END if it is not, at
    /// its exact location. What is not negotiable is that nothing is ever SPLICED: the JS here
    /// is what CSharpFrontEnd.Expr translated from a resolved syntax tree, never the author's
    /// text.
    ///
    /// ONE EFFECT PER BINDING POINT, whatever the expression reads. That is the answer to the
    /// question Phase 2 said it would not guess at ("which sub-expressions are reactive reads,
    /// which decides how many effects exist"): the binding point is the unit, and it subscribes
    /// to exactly what it reads. rows.js's @row.Label is that rule with one read; @(a + b) is
    /// the same rule with two.
    /// </summary>
    string? EmitBinding(CSharpExpressionIntermediateNode expr, string? parent)
    {
        if (parent is null)
        {
            Diag("top-level-expression",
                $"@{Trunc(RawText(expr).Trim())} at the top level of the template has no parent element to own " +
                "its text node. Not exercised by either answer key; refusing rather than guessing at the " +
                "attach order.",
                expr.Source);
            return null;
        }

        var js = _code.SlotJs(expr);

        // A FLOAT value is a Math.fround'd double; its bare coercion would print the DOUBLE string, not C#'s
        // float string (0.1f -> "0.1", not "0.10000000149011612"). Format it through the module's __f32 helper
        // (decision 113), which finds the shortest decimal that round-trips through float32 -- exactly what
        // C#'s float.ToString does. The helper is emitted once, in Render, when any float display exists.
        if (_code.SlotIsFloat(expr))
        {
            _needsFloatFormat = true;
            js = $"__f32({js})";
        }

        // A DECIMAL value is a boxed { m, s } object; displaying it renders that object as C#'s decimal string
        // (scale preserved: { m: 110n, s: 2 } -> "1.10") through the emitted __decStr helper (decision 114).
        if (_code.SlotIsDecimal(expr))
        {
            _code.DecimalHelpers.Add("decStr");
            js = $"__decStr({js})";
        }

        // A DATETIME value is a BigInt of ticks; displaying it renders C#'s default DateTime string
        // ("MM/dd/yyyy HH:mm:ss") through the emitted __dtStr helper (decision 115).
        if (_code.SlotIsDateTime(expr))
        {
            _needsDateTimeFormat = true;
            js = $"__dtStr({js})";
        }

        // NOT REACTIVE -> no signal, no effect, no .value: one write, at create time. The
        // source can never change, so an effect around it would be a subscription to nothing --
        // machinery serving machinery, which is the thing this POC refuses. rows.js's @row.Id
        // is this case, and it is why a row costs ONE effect and not two.
        if (!_code.SlotIsReactive(expr))
            return $"document.createTextNode({js})";

        var t = $"_tx{_tx++}";
        _create.Add($"const {t} = document.createTextNode('');");
        _used.Add("setText");
        _used.Add("effect");
        _bindings.Add($"effect(() => setText({t}, {js}));");
        return t;
    }

    /// <summary>
    /// Unwrap Razor's pre-lowered Blazor event binding.
    ///   EventCallback.Factory.Create&lt;MouseEventArgs&gt;(this, Increment)  ->  Increment
    /// </summary>
    static bool TryUnwrapEventCallback(string expr, out string handler)
    {
        handler = "";
        var m = Regex.Match(expr.Trim(),
            @"^(?:global::)?(?:Microsoft\.AspNetCore\.Components\.)?EventCallback\.Factory\.Create(?:Binder)?" +
            @"(?:<[^>]*>)?\s*\(\s*this\s*,\s*(?<h>.*?)\s*\)$", RegexOptions.Singleline);
        if (!m.Success) return false;
        handler = m.Groups["h"].Value.Trim();
        return handler.Length > 0;
    }

    /// <summary>The decimal helper library (decision 114), in emission order. A decimal is { m: BigInt, s: int }.
    /// add/sub align scales then add/subtract mantissas; mul multiplies mantissas and ADDS scales (so 1.0m*1.0m
    /// keeps scale 2 -> "1.00"); cmp scale-aligns then compares; neg flips the mantissa's sign; fromInt boxes an
    /// int at scale 0; str renders the mantissa with the point at `scale`, preserving trailing zeros. These match
    /// System.Decimal for +, -, *, comparison and display; division (28-digit rounding) is refused, not emitted.</summary>
    static readonly (string Name, string Code)[] DecimalHelperSource =
    {
        ("decAdd", "function __decAdd(a, b) { const s = a.s > b.s ? a.s : b.s; return { m: a.m * 10n ** BigInt(s - a.s) + b.m * 10n ** BigInt(s - b.s), s: s }; }"),
        ("decSub", "function __decSub(a, b) { const s = a.s > b.s ? a.s : b.s; return { m: a.m * 10n ** BigInt(s - a.s) - b.m * 10n ** BigInt(s - b.s), s: s }; }"),
        ("decMul", "function __decMul(a, b) { return { m: a.m * b.m, s: a.s + b.s }; }"),
        ("decNeg", "function __decNeg(a) { return { m: -a.m, s: a.s }; }"),
        ("decCmp", "function __decCmp(a, b) { const s = a.s > b.s ? a.s : b.s; const am = a.m * 10n ** BigInt(s - a.s), bm = b.m * 10n ** BigInt(s - b.s); return am < bm ? -1 : am > bm ? 1 : 0; }"),
        ("decFromInt", "function __decFromInt(i) { return { m: BigInt(i), s: 0 }; }"),
        ("decStr", "function __decStr(d) {\n  let neg = d.m < 0n, g = (neg ? -d.m : d.m).toString();\n  if (d.s > 0) { while (g.length <= d.s) g = '0' + g; const i = g.length - d.s; g = g.slice(0, i) + '.' + g.slice(i); }\n  return (neg ? '-' : '') + g;\n}"),
    };

    string Render(List<string> module, List<string> prologue, string runtimeSpecifier, string sourceName)
    {
        _used.Add("insert");
        var imports = RuntimeExports.Where(_used.Contains).ToList();

        var sb = new StringBuilder();
        sb.Append("// GENERATED by Filament.Generator from ").Append(sourceName).Append(". DO NOT EDIT.\n");
        sb.Append("//\n");
        sb.Append("// Compiled from pure .razor: the template AND the @code block (Phase 3).\n");
        sb.Append("// State is lifted -- a private field the template reads and something assigns\n");
        sb.Append("// becomes a Signal -- and no user text is spliced anywhere in this file.\n\n");

        sb.Append("import { ").Append(string.Join(", ", imports)).Append(" } from '").Append(runtimeSpecifier).Append("';\n\n");

        if (_needsFloatFormat)
        {
            // C#'s float.ToString prints the SHORTEST decimal that round-trips through float32 (0.1f -> "0.1",
            // not the double string "0.10000000149011612"). This reproduces it: fround the value, then try
            // increasing precision until a candidate round-trips back to the same float32. Emitted (not a
            // runtime export) so a float display stays generator-only -- the runtime is untouched. Decision 113.
            sb.Append("// -- float display: shortest decimal that round-trips through float32 (C# float.ToString) --\n");
            sb.Append("function __f32(x) {\n");
            sb.Append("  x = Math.fround(x);\n");
            sb.Append("  if (!isFinite(x) || Number.isInteger(x)) return String(x);\n");
            sb.Append("  for (let p = 1; p <= 9; p++) {\n");
            sb.Append("    const s = x.toPrecision(p);\n");
            sb.Append("    if (Math.fround(Number(s)) === x) return Number(s).toString();\n");
            sb.Append("  }\n");
            sb.Append("  return String(x);\n");
            sb.Append("}\n\n");
        }

        if (_code.DecimalHelpers.Count > 0)
        {
            // C#'s `decimal` is a 128-bit base-10 type with tracked scale; JS has no native decimal, so a value
            // is a boxed { m: BigInt mantissa, s: scale } and its arithmetic is exact base-10 on that. Emitted
            // (not runtime exports) so decimal stays generator-only. Only the helpers actually used are emitted,
            // in this fixed order. Decision 114. Division/modulo are refused (System.Decimal's 28-digit rounding).
            sb.Append("// -- decimal: boxed { m, s } (BigInt mantissa + scale), exact base-10 -- C# System.Decimal --\n");
            foreach (var (name, code) in DecimalHelperSource)
                if (_code.DecimalHelpers.Contains(name))
                    sb.Append(code).Append('\n');
            sb.Append('\n');
        }

        if (_needsDateTimeFormat)
        {
            // A DateTime is a BigInt of ticks (100ns since 0001-01-01). C#'s default ToString is
            // "MM/dd/yyyy HH:mm:ss" (invariant). Convert ticks -> ms since the Unix epoch, hand to a UTC Date,
            // and format its parts. Emitted (not a runtime export), so a DateTime display stays generator-only.
            // The epoch offset 621355968000000000 is the ticks at 1970-01-01. Decision 115.
            sb.Append("// -- DateTime display: ticks (BigInt) -> C#'s default \"MM/dd/yyyy HH:mm:ss\" (invariant) --\n");
            sb.Append("function __dtStr(t) {\n");
            sb.Append("  const d = new Date(Number((t - 621355968000000000n) / 10000n));\n");
            sb.Append("  const p = (n, w = 2) => String(n).padStart(w, '0');\n");
            sb.Append("  return p(d.getUTCMonth() + 1) + '/' + p(d.getUTCDate()) + '/' + p(d.getUTCFullYear(), 4) +\n");
            sb.Append("    ' ' + p(d.getUTCHours()) + ':' + p(d.getUTCMinutes()) + ':' + p(d.getUTCSeconds());\n");
            sb.Append("}\n\n");
        }

        if (_code.ClockHelpers.Count > 0)
        {
            // The wall clock as BigInt ticks (decision 145): the SAME clock C# reads. __dtUtcNow is the
            // Unix epoch offset in ticks + Date.now() scaled ms->ticks; Now subtracts the CURRENT local
            // offset (getTimezoneOffset is UTC-local in minutes; one minute = 600,000,000 ticks); Today
            // truncates Now to the local day (BigInt division truncates; ticks are positive). Emitted
            // (not runtime exports) in dependency order, so the clock stays generator-only.
            sb.Append("// -- wall clock: C# DateTime.UtcNow/Now/Today as BigInt ticks (decision 145) --\n");
            sb.Append("function __dtUtcNow() {\n");
            sb.Append("  return 621355968000000000n + BigInt(Date.now()) * 10000n;\n");
            sb.Append("}\n\n");
            if (_code.ClockHelpers.Contains("dtNow"))
            {
                sb.Append("function __dtNow() {\n");
                sb.Append("  return __dtUtcNow() - BigInt(new Date().getTimezoneOffset()) * 600000000n;\n");
                sb.Append("}\n\n");
            }
            if (_code.ClockHelpers.Contains("dtToday"))
            {
                sb.Append("function __dtToday() {\n");
                sb.Append("  return (__dtNow() / 864000000000n) * 864000000000n;\n");
                sb.Append("}\n\n");
            }
        }

        if (_code.RandomHelpers.Count > 0)
        {
            // System.Random (decision 146). SEEDED: the exact .NET Knuth-subtractive generator
            // (Net5CompatSeedImpl -- MSEED 161803398, 55-slot table, 4 shuffle rounds, inext/inextp 0/21,
            // the ==MaxValue decrement, Sample scale 1/int.MaxValue, the two-sample large-range path) --
            // verified against the BCL: Random(42) draws 5,1,1 for Next(1,7) and 1434747710 for Next().
            // UNSEEDED (`null`): Math.random behind the same interface -- C#'s unseeded xoshiro is not
            // reproducible across runs either; range and distribution are the observable contract.
            // Emitted (not a runtime export), so Random stays generator-only.
            sb.Append("// -- Random: C# System.Random -- seeded = the exact .NET Knuth-subtractive sequence --\n");
            sb.Append("function __rnd(seed) {\n");
            sb.Append("  if (seed === null) {\n");
            sb.Append("    return {\n");
            sb.Append("      next: () => Math.floor(Math.random() * 2147483647),\n");
            sb.Append("      nextTo: (max) => Math.floor(Math.random() * max),\n");
            sb.Append("      nextIn: (min, max) => min + Math.floor(Math.random() * (max - min)),\n");
            sb.Append("      nextDouble: () => Math.random(),\n");
            sb.Append("    };\n");
            sb.Append("  }\n");
            sb.Append("  const arr = new Int32Array(56);\n");
            sb.Append("  let mj = 161803398 - (seed === -2147483648 ? 2147483647 : Math.abs(seed));\n");
            sb.Append("  arr[55] = mj;\n");
            sb.Append("  let mk = 1;\n");
            sb.Append("  for (let i = 1; i < 55; i++) {\n");
            sb.Append("    const ii = (21 * i) % 55;\n");
            sb.Append("    arr[ii] = mk;\n");
            sb.Append("    mk = mj - mk;\n");
            sb.Append("    if (mk < 0) mk += 2147483647;\n");
            sb.Append("    mj = arr[ii];\n");
            sb.Append("  }\n");
            sb.Append("  for (let k = 1; k < 5; k++) {\n");
            sb.Append("    for (let i = 1; i < 56; i++) {\n");
            sb.Append("      arr[i] -= arr[1 + (i + 30) % 55];\n");
            sb.Append("      if (arr[i] < 0) arr[i] += 2147483647;\n");
            sb.Append("    }\n");
            sb.Append("  }\n");
            sb.Append("  let inext = 0, inextp = 21;\n");
            sb.Append("  const sample = () => {\n");
            sb.Append("    if (++inext >= 56) inext = 1;\n");
            sb.Append("    if (++inextp >= 56) inextp = 1;\n");
            sb.Append("    let ret = arr[inext] - arr[inextp];\n");
            sb.Append("    if (ret === 2147483647) ret--;\n");
            sb.Append("    if (ret < 0) ret += 2147483647;\n");
            sb.Append("    arr[inext] = ret;\n");
            sb.Append("    return ret;\n");
            sb.Append("  };\n");
            sb.Append("  return {\n");
            sb.Append("    next: sample,\n");
            sb.Append("    nextTo: (max) => Math.trunc(sample() * (1 / 2147483647) * max),\n");
            sb.Append("    nextIn: (min, max) => {\n");
            sb.Append("      const range = max - min;\n");
            sb.Append("      if (range <= 2147483647) return min + Math.trunc(sample() * (1 / 2147483647) * range);\n");
            sb.Append("      let result = sample();\n");
            sb.Append("      if (sample() % 2 === 0) result = -result;\n");
            sb.Append("      return min + Math.trunc(((result + 2147483646) / 4294967293) * range);\n");
            sb.Append("    },\n");
            sb.Append("    nextDouble: () => sample() * (1 / 2147483647),\n");
            sb.Append("  };\n");
            sb.Append("}\n\n");
            if (_code.RandomHelpers.Contains("rndShared"))
            {
                // Random.Shared: ONE module-level unseeded instance -- the same static-singleton lifetime C# gives it.
                sb.Append("const __rndShared = __rnd(null);\n\n");
            }
        }

        if (_code.HttpHelpers.Count > 0)
        {
            // HttpClient erased to fetch (decision 147). __getJson carries GetFromJsonAsync's semantics:
            // throw on a non-success status (EnsureSuccess -- catchable with #110's try/catch), parse the
            // body, then __camel normalizes each key's FIRST character to lower case -- faithful to
            // System.Text.Json's Web defaults (camelCase + case-insensitive) for the Pascal/camel case
            // real APIs use. __postJson serializes with JSON.stringify, which IS Web-defaults output for
            // the admitted shapes (the module's objects are already camelCase). Emitted (not runtime
            // exports), so HTTP stays generator-only.
            sb.Append("// -- HTTP: C# HttpClient erased to fetch (decision 147) --\n");
            if (_code.HttpHelpers.Contains("getJson"))
            {
                sb.Append("function __camel(v) {\n");
                sb.Append("  if (Array.isArray(v)) return v.map(__camel);\n");
                sb.Append("  if (v && typeof v === 'object') {\n");
                sb.Append("    const o = {};\n");
                sb.Append("    for (const k of Object.keys(v)) o[k.charAt(0).toLowerCase() + k.slice(1)] = __camel(v[k]);\n");
                sb.Append("    return o;\n");
                sb.Append("  }\n");
                sb.Append("  return v;\n");
                sb.Append("}\n\n");
                sb.Append("async function __getJson(u) {\n");
                sb.Append("  const r = await fetch(u);\n");
                sb.Append("  if (!r.ok) throw new Error('Response status code does not indicate success: ' + r.status);\n");
                sb.Append("  return __camel(await r.json());\n");
                sb.Append("}\n\n");
            }
            if (_code.HttpHelpers.Contains("getText"))
            {
                sb.Append("async function __getText(u) {\n");
                sb.Append("  const r = await fetch(u);\n");
                sb.Append("  if (!r.ok) throw new Error('Response status code does not indicate success: ' + r.status);\n");
                sb.Append("  return r.text();\n");
                sb.Append("}\n\n");
            }
            if (_code.HttpHelpers.Contains("postJson"))
            {
                sb.Append("function __postJson(u, v) {\n");
                sb.Append("  return fetch(u, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(v) });\n");
                sb.Append("}\n\n");
            }
        }

        if (module.Count > 0)
        {
            // rows.js mapping decision (4): immutable literal lists are inert DATA, and hoisting
            // them is constant folding. The LABELS are not here and must never be -- they are
            // generated per row by the LCG, 3 draws and a concatenation each, exactly as Blazor
            // does them. Hoisting or interning one is the cheat this POC exists not to commit.
            sb.Append("// -- @code: immutable literal data, hoisted to module scope ------------------\n");
            foreach (var line in module) sb.Append(line).Append('\n');
            sb.Append('\n');
        }

        sb.Append("export function mount(target) {\n");

        if (prologue.Count > 0)
        {
            sb.Append("  // -- @code: state and behaviour, compiled from C# ---------------------------\n");
            Emit(sb, prologue);
            sb.Append('\n');
        }

        // INIT LIFECYCLE (decision 156): before create(), so what OnInitialized writes -- and what
        // OnInitializedAsync writes before its first await -- is what the first paint shows.
        if (_code.InitCalls.Count > 0)
        {
            sb.Append("  // -- init: OnInitialized(Async), once, before the first paint ----------------\n");
            Emit(sb, _code.InitCalls.ToList());
            sb.Append('\n');
        }

        sb.Append("  // -- create(): the tree, built detached -------------------------------------\n");
        Emit(sb, _create);

        if (_bindings.Count > 0)
        {
            sb.Append("\n  // -- bindings ---------------------------------------------------------------\n");
            Emit(sb, _bindings);
        }
        if (_events.Count > 0)
        {
            sb.Append("\n  // -- events -----------------------------------------------------------------\n");
            Emit(sb, _events);
        }

        sb.Append("\n  // -- attach: last, so the effects' first run made no MutationRecord ----------\n");
        Emit(sb, _attach);
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>
    /// Emit statements at mount()'s indentation. Each ELEMENT may itself be several lines
    /// (an inlined handler body is), so the indent is applied per LINE and not per element
    /// -- indenting the element would indent only its first line and leave the rest jammed
    /// against the margin.
    /// </summary>
    static void Emit(StringBuilder sb, List<string> statements)
    {
        foreach (var statement in statements)
            foreach (var line in statement.Split('\n'))
                sb.Append(line.Length > 0 ? "  " + line + "\n" : "\n");
    }

    // ---- helpers -----------------------------------------------------------

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

    static string Trunc(string? s, int n = 60)
    {
        s = (s ?? "").Replace("\r", "").Replace("\n", "\\n");
        return s.Length <= n ? s : s[..n] + "...";
    }

    /// <summary>Single-quoted JS string literal, matching the answer key's quoting.</summary>
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
