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
/// WHAT IS DELIBERATELY NOT HERE: anything that needs to understand C#. Per decision
/// 54, @foreach/@if arrive as RAW C# TEXT with unbalanced braces and the element is
/// a SIBLING of the loop header, not a child -- Razor never structures control flow
/// because Blazor never needs it to. Translating that is Phase 3.
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
    /// The ONLY directive Phase 2 accepts. "la logique @code reste ecrite en JS a la
    /// main" (spec 6) -- @code is the seam, and decision 57 pins that Razor hands it
    /// back as one opaque verbatim token. Every other directive Razor recognises --
    /// @page, @inject, @layout, @inherits, @implements, @typeparam, @attribute,
    /// @namespace, @preservewhitespace, ... -- is out of subset and must SAY SO.
    /// Driving this off Razor's own directive table (rather than off a list of node
    /// types this compiler happens to know) is what makes the gate complete: a
    /// directive nobody here has heard of is refused too.
    /// </summary>
    static readonly HashSet<string> AllowedDirectives = new(StringComparer.Ordinal) { "code" };

    /// <summary>
    /// The base class Razor gives EVERY component whether or not @inherits was written.
    /// It is the default, so it is the thing to compare against; treating a non-null
    /// BaseType as evidence of @inherits refuses Counter itself.
    /// </summary>
    const string ComponentBaseType = "Microsoft.AspNetCore.Components.ComponentBase";

    /// <summary>
    /// Static attributes written as DOM PROPERTIES rather than via setAttr(). The answer
    /// key writes `h1.id = 'title'`, and a property write is one less runtime call and
    /// one less string lookup than setAttribute. The map is an ALLOWLIST because the
    /// attribute->property correspondence is not general (`class`->`className`, and
    /// plenty of attributes have no property at all); anything not named here goes
    /// through setAttr, which is always correct.
    /// </summary>
    static readonly Dictionary<string, string> PropertyAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["id"] = "id",
        ["class"] = "className",
    };

    readonly List<string> _create = [];
    readonly List<string> _bindings = [];
    readonly List<string> _events = [];
    readonly List<string> _attach = [];
    readonly HashSet<string> _used = [];
    readonly List<Diagnostic> _diagnostics = [];
    string _file = "";
    int _el, _tx;

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    /// <summary>One refusal: the code, why, and WHERE. All three are required.</summary>
    public sealed record Diagnostic(string Code, string Reason, string Message, SourceSpan? Source)
    {
        /// <summary>"file(line,col)" -- 1-based, the way every compiler on earth prints it.</summary>
        public string Location => Source is { } s
            ? $"{Path.GetFileName(s.FilePath)}({s.LineIndex + 1},{s.CharacterIndex + 1})"
            : "<no source span>";

        public override string ToString() => $"{Location}: {Code}: [{Reason}] {Message}";
    }

    void Diag(string reason, string message, SourceSpan? source) =>
        _diagnostics.Add(new Diagnostic("FIL0003", reason, message, source));

    public string Compile(ParseResult parse, string runtimeSpecifier, string sourceName)
    {
        _file = parse.FilePath;

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

        // @code arrives as ONE opaque CSharpCodeIntermediateNode holding the whole block
        // verbatim, as a sibling of BuildRenderTree. Razor lexes it but does not
        // interpret it, which is exactly what lets JS ride through it untouched.
        var codeBlocks = cls.Children.OfType<CSharpCodeIntermediateNode>()
            .Select(RawText)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        // --- the template -----------------------------------------------------
        foreach (var child in method.Children)
        {
            var v = EmitNode(child, parent: null);
            if (v is not null) _attach.Add($"insert(target, {v});");
        }

        var prologue = string.Join("\n", codeBlocks.Select(Dedent));
        foreach (var p in RuntimeExports)
            if (Regex.IsMatch(prologue, $@"\b{Regex.Escape(p)}\b"))
                _used.Add(p);

        return Render(prologue, runtimeSpecifier, sourceName);
    }

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

                // Razor SYNTHESISES the default @usings (they have no file path). A
                // @using the AUTHOR wrote carries this component's own path, and in a
                // .razor file its job is to bring COMPONENTS into scope -- which is
                // component composition, which is out of subset. Telling the two apart
                // by the span's file is measured, not guessed: see the probe in
                // DiagnosticTests.
                case UsingDirectiveIntermediateNode u when IsFromThisFile(u.Source):
                    Diag("unsupported-directive",
                        "@using in a component brings COMPONENTS into scope, and component composition is " +
                        "not in Phase 2's subset. Refusing to emit rather than drop it silently.",
                        u.Source);
                    break;

                case UsingDirectiveIntermediateNode:
                    break; // synthesised by Razor; not the author's, nothing to compile

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
        if (!string.Equals(cls.BaseType, ComponentBaseType, StringComparison.Ordinal))
            ClassShape(cls, $"declares base type '{cls.BaseType}' (Razor's default is {ComponentBaseType}); a Filament module has no base class");
        if (cls.Interfaces is { Count: > 0 })
            ClassShape(cls, "declares interfaces (@implements); a Filament module implements none");
        if (cls.TypeParameters is { Count: > 0 })
            ClassShape(cls, "is generic (@typeparam); a Filament module is not");

        foreach (var child in cls.Children)
        {
            switch (child)
            {
                case MethodDeclarationIntermediateNode m when m.MethodName == "BuildRenderTree":
                case CSharpCodeIntermediateNode: // the @code seam -- spliced verbatim
                    break;
                default:
                    Unaccounted(child, "inside the component class");
                    break;
            }
        }

        return cls;
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

            // @currentCount
            case CSharpExpressionIntermediateNode expr:
                return EmitBinding(expr, parent);

            case MarkupBlockIntermediateNode block:
                throw new GeneratorException(
                    $"FIL-WIRING: an opaque markup block reached the emitter: {Trunc(block.Content)}. " +
                    "ComponentMarkupBlockPass was not removed (decision 52); the IR has no structure to " +
                    "compile and nothing here can be trusted. This is the TOOL being broken, not the input.");

            // @foreach / @if arrive here, as raw C# text with unbalanced braces.
            case CSharpCodeIntermediateNode code:
                Diag("control-flow",
                    $"C# in the template ({Trunc(RawText(code))}) is out of Phase 2's reach. Razor emits no " +
                    "loop/branch node -- control flow is raw C# text whose braces do not balance and whose " +
                    "body elements are SIBLINGS of the header (decision 54). Translating it is Phase 3's " +
                    "C#->JS work. Refusing to emit.",
                    code.Source);
                return null;

            case SetKeyIntermediateNode key:
                Diag("unsupported-directive",
                    "@key is only meaningful inside a list, and lists need @foreach, which is Phase 3 " +
                    "(decision 54). Refusing to emit.",
                    key.Source);
                return null;

            case ReferenceCaptureIntermediateNode r:
                Diag("unsupported-directive", "@ref is not in the v0 subset. Refusing to emit.", r.Source);
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
        // COMPONENT COMPOSITION, and it is the quietest failure of the lot.
        // This generator has no compilation, so it cannot resolve sibling .razor files
        // into components: <Counter /> does NOT become a ComponentIntermediateNode, it
        // stays a markup element named "Counter" -- and Razor emits NO diagnostic for
        // it (verified: zero diagnostics of any severity on the probe). Emitted, that
        // is document.createElement('Counter'): a valid unknown element that renders
        // NOTHING. Blazor's own rule is that an upper-case initial means a component,
        // so that is the rule used here.
        if (LooksLikeComponent(el.TagName))
        {
            Diag("component-composition",
                $"<{el.TagName}> is a component reference (an upper-case initial, or a dotted name). " +
                "Component composition is not in Phase 2's subset, and this generator has no compilation " +
                "to resolve it against, so Razor left it as a plain markup element and said nothing. " +
                $"Emitting it would produce document.createElement('{el.TagName}') -- an unknown element " +
                "that renders nothing. Refusing to emit.",
                el.Source);
            return null;
        }

        var v = $"_el{_el++}";
        _create.Add($"const {v} = document.createElement({JsString(el.TagName)});");

        // Attributes are handled SEPARATELY from children and never by document order.
        // With the tag helper chain active Razor reorders them -- <button>'s "Click me"
        // content node arrives BEFORE its id/onclick attributes -- so walking children
        // in order and switching on node type is the only correct traversal.
        foreach (var attr in el.Children.OfType<HtmlAttributeIntermediateNode>())
            EmitAttribute(el, v, attr);

        foreach (var child in el.Children)
        {
            if (child is HtmlAttributeIntermediateNode) continue;
            var c = EmitNode(child, parent: v);
            if (c is not null) _create.Add($"insert({v}, {c});");
        }
        return v;
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

        if (csharp.Count > 0)
        {
            var expr = string.Concat(csharp.SelectMany(c => c.Children.OfType<IntermediateToken>()).Select(t => t.Content));

            if (TryUnwrapEventCallback(expr, out var handler))
            {
                // Razor pre-lowers to Blazor semantics: the value is already
                // EventCallback.Factory.Create<MouseEventArgs>(this, Increment).
                // Filament has no EventCallback and no `this`; it has listen().
                var domEvent = name.StartsWith("on", StringComparison.Ordinal) ? name[2..] : name;
                _used.Add("listen");
                _events.Add($"listen({v}, {JsString(domEvent)}, {handler});");
                return;
            }

            Diag("dynamic-attribute",
                $"attribute '{name}' on <{el.TagName}> carries the C# expression \"{Trunc(expr)}\", which is " +
                "neither a resolved event handler nor a static value. Dynamic attribute values are in Phase 2's " +
                "declared scope but are NOT exercised by Counter, so this compiler refuses them rather than ship " +
                "an emission path no measurement covers.",
                attr.Source ?? el.Source);
            return;
        }

        var value = string.Concat(html.SelectMany(h => h.Children.OfType<IntermediateToken>()).Select(t => t.Content));

        if (PropertyAttributes.TryGetValue(name, out var prop))
            _create.Add($"{v}.{prop} = {JsString(value)};");
        else
        {
            _used.Add("setAttr");
            _create.Add($"setAttr({v}, {JsString(name)}, {JsString(value)});");
        }
    }

    /// <summary>
    /// @currentCount -> a Text node owned forever by one effect.
    ///
    /// The text node is created once, empty, and handed to setText for the life of the
    /// app. Writing `span.textContent = v` instead would destroy and rebuild the span's
    /// children on every change: 2 DOM writes where the contract allows 1, and C3 would
    /// fail on markup that looks identical.
    /// </summary>
    string? EmitBinding(CSharpExpressionIntermediateNode expr, string? parent)
    {
        var text = RawText(expr).Trim();

        // The seam's rule: the template reads STATE, and this phase's state is declared
        // by the hand-written JS in @code as a signal. The runtime's read protocol is a
        // property access (decision 22: `s.Value` in C# maps to `s.value` in JS,
        // character for character), so a template read of `x` is `x.value`.
        //
        // Only a bare identifier is accepted. Anything else -- @(a + b), @Foo.Bar(),
        // @(cond ? x : y) -- needs to know which sub-expressions are signal reads, and
        // that is a C# (Phase 3) question this compiler will not guess at.
        if (!Regex.IsMatch(text, @"^[A-Za-z_$][A-Za-z0-9_$]*$"))
        {
            Diag("compound-expression",
                $"@({Trunc(text)}) is not a bare identifier. Deciding which parts of a compound expression are " +
                "reactive reads is Phase 3's C# work (decision 54). Refusing to emit.",
                expr.Source);
            return null;
        }

        if (parent is null)
        {
            Diag("top-level-expression",
                $"@{text} at the top level of the template has no parent element to own its text node. " +
                "Not exercised by Counter; refusing rather than guessing at the attach order.",
                expr.Source);
            return null;
        }

        var t = $"_tx{_tx++}";
        _create.Add($"const {t} = document.createTextNode('');");
        _used.Add("setText");
        _used.Add("effect");
        _bindings.Add($"effect(() => setText({t}, {text}.value));");
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

    string Render(string prologue, string runtimeSpecifier, string sourceName)
    {
        _used.Add("insert");
        var imports = RuntimeExports.Where(_used.Contains).ToList();

        var sb = new StringBuilder();
        sb.Append("// GENERATED by Filament.Generator from ").Append(sourceName).Append(". DO NOT EDIT.\n");
        sb.Append("//\n");
        sb.Append("// The template is compiled; the @code block is hand-written JS, spliced verbatim\n");
        sb.Append("// (Phase 2: \"la logique @code reste ecrite en JS a la main\").\n\n");

        sb.Append("import { ").Append(string.Join(", ", imports)).Append(" } from '").Append(runtimeSpecifier).Append("';\n\n");
        sb.Append("export function mount(target) {\n");

        if (prologue.Length > 0)
        {
            sb.Append("  // -- @code ------------------------------------------------------------------\n");
            foreach (var line in prologue.Split('\n'))
                sb.Append(line.Length > 0 ? "  " + line + "\n" : "\n");
            sb.Append('\n');
        }

        sb.Append("  // -- create(): the tree, built detached -------------------------------------\n");
        foreach (var l in _create) sb.Append("  ").Append(l).Append('\n');

        if (_bindings.Count > 0)
        {
            sb.Append("\n  // -- bindings ---------------------------------------------------------------\n");
            foreach (var l in _bindings) sb.Append("  ").Append(l).Append('\n');
        }
        if (_events.Count > 0)
        {
            sb.Append("\n  // -- events -----------------------------------------------------------------\n");
            foreach (var l in _events) sb.Append("  ").Append(l).Append('\n');
        }

        sb.Append("\n  // -- attach: last, so the effects' first run made no MutationRecord ----------\n");
        foreach (var l in _attach) sb.Append("  ").Append(l).Append('\n');
        sb.Append("}\n");
        return sb.ToString();
    }

    // ---- helpers -----------------------------------------------------------

    static string RawText(IntermediateNode n) =>
        string.Concat(n.FindDescendantNodes<IntermediateToken>().Select(t => t.Content));

    static string Dedent(string s)
    {
        var lines = s.Replace("\r\n", "\n").Trim('\n').Split('\n');
        var indent = lines.Where(l => l.Trim().Length > 0)
            .Select(l => l.Length - l.TrimStart().Length)
            .DefaultIfEmpty(0).Min();
        return string.Join("\n", lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart())).Trim('\n');
    }

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
