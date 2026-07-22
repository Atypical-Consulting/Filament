using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Razor;

namespace Filament.Generator;

/// <summary>
/// Everything between "a .razor file on disk" and "a structural IR tree".
///
/// This class is three decisions and almost no code. All three are load-bearing and
/// all three are invisible if you get them wrong, which is why each one is asserted
/// from the ARTIFACT rather than trusted.
///
/// DECISION 52 — the package is 6.0.36, and that is not a preference.
///   6.0.36 is the LAST version of Microsoft.AspNetCore.Razor.Language ever
///   published; there is no 7/8/9/10. .NET 10's Razor compiler renamed
///   GetDocumentIntermediateNode to GetDocumentNode and made it internal, and
///   Microsoft.CodeAnalysis.Razor.Compiler 10.0-preview does not restore at all.
///   6.0.36 restores and runs from a net10.0 TFM. Do not "upgrade" this; there is
///   nothing to upgrade to.
///
/// DECISION 52 — the two markup passes must be REMOVED.
///   With ComponentMarkupBlockPass in place, Razor collapses static subtrees into
///   one opaque string: MarkupBlockIntermediateNode Content="&lt;h1 id=\"title\"&gt;...".
///   Re-parsing that string would be the entire project. With it removed we get
///   MarkupElementIntermediateNode / HtmlAttributeIntermediateNode /
///   HtmlContentIntermediateNode -- real structure. The passes are internal types,
///   so they are matched by GetType().Name. That is a string, and a string is
///   breakable in silence by an update -- but the package is dead, so there will
///   be no update. The ugliness and its justification cancel out.
///
/// DECISION 53 — the tag helper chain is not optional and not trusted.
///   WITHOUT CompilationTagHelperFeature fed real Microsoft.AspNetCore.App.Ref
///   assemblies, @onclick="Increment" produces NO DIAGNOSTIC. It silently becomes a
///   literal DOM attribute named '@onclick' carrying an [HTML] token, the button
///   looks entirely static, and a generator emits '@onclick' as a real HTML
///   attribute WHILE APPEARING TO WORK. That is verbatim the failure mode section
///   10 forbids ("jamais du JS silencieusement faux").
///
/// THERE IS EXACTLY ONE ENGINE, AND THAT IS THE POINT.
///   The wiring above used to exist TWICE -- once in Parse(), once in a
///   CountDescriptors() helper that the decision-53 test called. So the test
///   measured a COPY of the wiring instead of the wiring that emits, and deleting
///   CompilationTagHelperFeature from Parse() left that test GREEN. That is
///   decisions 41/46's documented failure mode ("repairing the line a test points
///   at while leaving the identical hole one frame up") reproduced inside the very
///   test that decision 53 asks for. CreateEngine() is now the ONLY place the chain
///   is described, and ParseResult reports what THAT engine resolved, so the
///   invariant is read off the artifact the generator actually used.
/// </summary>
public static class RazorFrontEnd
{
    /// <summary>
    /// The namespace that supplies @onclick and friends. A Blazor app gets this from
    /// its _Imports.razor; a Filament sample should not have to carry a Blazor file
    /// just to make the event descriptors resolve, so the generator supplies it. A
    /// real _Imports.razor next to the component still applies on top of this.
    /// </summary>
    public const string WebImport = "@using Microsoft.AspNetCore.Components.Web";

    /// <summary>
    /// The Forms namespace, so Razor RESOLVES &lt;EditForm&gt;/&lt;InputText&gt; into component nodes with
    /// their own lowering (decision 138). Without it those tags stay plain markup elements and
    /// `@bind-Value` arrives as a raw directive attribute carrying the text `model.Name` -- at which
    /// point implementing forms would mean re-deriving Blazor's binding semantics by hand, which is
    /// decision 53's trap exactly: wiring described twice drifts. Importing it means this compiler READS
    /// Blazor's lowering instead of guessing it, the same way it does for @onclick and @bind.
    /// </summary>
    public const string FormsImport = "@using Microsoft.AspNetCore.Components.Forms";

    /// <summary>
    /// THE ONE description of the chain. Both the parse and every invariant that
    /// reports on the parse come through here, so a mutation to the wiring cannot hit
    /// one copy and miss the other.
    /// </summary>
    static RazorProjectEngine CreateEngine(string dir, DirectiveSpyPass spy)
    {
        var refs = ReferenceAssemblies.All();
        var fs = RazorProjectFileSystem.Create(dir);

        return RazorProjectEngine.Create(RazorConfiguration.Default, fs, b =>
        {
            // public API: registers the component passes and the tag helper providers
            CompilerFeatures.Register(b);
            b.Features.Add(new DefaultMetadataReferenceFeature { References = refs });
            b.Features.Add(new CompilationTagHelperFeature());
            b.AddDefaultImports(WebImport, FormsImport);
            b.Features.Add(spy);

            // DECISION 52 debt #1, HARDENED (docs/adr/0001-eol-razor-mitigation.md). These two passes
            // are matched by GetType().Name -- a string, silent to break if a Razor bump ever renames
            // them. 6.0.36 is pinned and dead, so both ARE registered; if one is ever NOT found, that
            // is the EOL-Razor migration trigger firing, and it must be LOUD -- leaving the pass in
            // would collapse static subtrees into opaque markup and silently change every emitted
            // module. Fail fast at the seam rather than mis-compile in silence.
            foreach (var name in new[] { "ComponentMarkupBlockPass", "ComponentMarkupEncodingPass" })
            {
                var f = b.Features.FirstOrDefault(x => x.GetType().Name == name)
                    ?? throw new InvalidOperationException(
                        $"Razor pass '{name}' was not found to remove. The pinned Razor.Language 6.0.36 " +
                        "always registers it, so its absence means the toolchain changed underfoot -- the " +
                        "EOL-Razor migration trigger (docs/adr/0001-eol-razor-mitigation.md). Refusing to " +
                        "parse: leaving it in collapses static markup into opaque strings and silently " +
                        "changes every emitted module.");
                b.Features.Remove(f);
            }
        });
    }

    public static ParseResult Parse(string razorPath)
    {
        var full = Path.GetFullPath(razorPath);
        var dir = Path.GetDirectoryName(full)!;
        var file = Path.GetFileName(full);

        var spy = new DirectiveSpyPass();
        var engine = CreateEngine(dir, spy);

        // Decision 53: verify the chain from the artifact, never from the call you
        // believe you made. No descriptors => @onclick mis-parses in silence.
        var thFeature = engine.Engine.Features.OfType<ITagHelperFeature>().FirstOrDefault()
            ?? throw new GeneratorException(
                "FIL-WIRING: no ITagHelperFeature is registered. @onclick would silently become a " +
                "literal DOM attribute named '@onclick' with no diagnostic (decision 53). Refusing to emit.");

        var descriptors = thFeature.GetDescriptors();
        if (descriptors.Count == 0)
            throw new GeneratorException(
                "FIL-WIRING: the tag helper chain resolved ZERO descriptors. @onclick would silently " +
                "become a literal DOM attribute named '@onclick' with no diagnostic (decision 53). " +
                "This usually means the Microsoft.AspNetCore.App.Ref assemblies were not found. " +
                "Refusing to emit.");

        var document = engine.Process(engine.FileSystem.GetItem(file, FileKinds.Component));
        var ir = document.GetDocumentIntermediateNode();

        return new ParseResult(ir, document, spy.Directives, descriptors.Count, full);
    }

    // The reference-assembly probe lives in ReferenceAssemblies: the C# front end needs
    // the same set to resolve the types in @code, and decision 53's lesson is that the
    // wiring gets described ONCE or the two copies drift.
    /// <summary>
    /// The route an `@page "/about"` declares, or null if the file has none (decision 139).
    ///
    /// A route is METADATA, not code: the page's own module is identical with or without it, and the only
    /// consumer is the generated router. It is read from the directive TOKENS (captured by
    /// DirectiveSpyPass) rather than from the lowered route-attribute node, because the lowered node has
    /// no span and a malformed route has to be reportable at the place the author wrote it.
    /// </summary>
    public static string? RouteOf(ParseResult parse)
    {
        var page = parse.Directives.FirstOrDefault(d => d.Name == "page");
        if (page.Name is null) return null;

        // Razor hands the route back as a C# string literal, quotes included.
        var raw = page.Tokens.FirstOrDefault()?.Trim();
        if (raw is null || raw.Length < 2 || raw[0] != '"' || raw[^1] != '"') return null;
        return raw[1..^1];
    }

    /// <summary>
    /// A refusal ABOUT the route, located at the `@page` directive the author wrote (decision 163).
    ///
    /// The span is available for exactly the reason DirectiveSpyPass exists: Razor lowers `@page` into a
    /// route-attribute node that keeps the value and LOSES the source, and a diagnostic without a
    /// location is one the author cannot act on. FIL0003 — this is an out-of-subset Razor construct, and
    /// the route template is Razor.
    /// </summary>
    public static Diagnostic RouteDiagnostic(ParseResult parse, string message)
    {
        var page = parse.Directives.FirstOrDefault(d => d.Name == "page");
        return new Diagnostic("FIL0003", "unsupported-route", message, page.Source);
    }

    /// <summary>An `@inject T Name` site, read back off the lowered IR (decision 133).</summary>
    public readonly record struct InjectSite(string TypeName, string MemberName);

    /// <summary>
    /// The @inject sites on the component class.
    ///
    /// REFLECTION, AND CONTAINED HERE ON PURPOSE. `ComponentInjectIntermediateNode` is INTERNAL to
    /// Microsoft.AspNetCore.Razor.Language, so it cannot be named in a type test the way every other node
    /// in this compiler is. That is exactly the EOL-Razor exposure ADR 0001 describes, so it lives in this
    /// file with the rest of the seam rather than leaking a reflective lookup into TemplateCompiler.
    ///
    /// It FAILS LOUD, like the pass removal above: if the node is present but its TypeName/MemberName
    /// properties are not where this expects them, that is the pinned Razor version having moved under us,
    /// and a silently empty result would mean an @inject the author wrote being dropped without a word.
    /// </summary>
    public static IReadOnlyList<InjectSite> Injects(IntermediateNode cls)
    {
        var sites = new List<InjectSite>();
        foreach (var child in cls.Children)
        {
            if (child.GetType().Name != "ComponentInjectIntermediateNode") continue;

            var t = child.GetType();
            var typeName = t.GetProperty("TypeName")?.GetValue(child) as string;
            var memberName = t.GetProperty("MemberName")?.GetValue(child) as string;

            if (typeName is null || memberName is null)
                throw new GeneratorException(
                    "FIL-WIRING: a ComponentInjectIntermediateNode has no TypeName/MemberName. The pinned " +
                    "Razor version (6.0.36) has changed shape under this seam (ADR 0001). Refusing to emit " +
                    "rather than drop an @inject the author wrote.");

            sites.Add(new InjectSite(typeName, memberName));
        }
        return sites;
    }

}

/// <summary>What one parse produced, all of it read off the engine that did the parsing.</summary>
/// <param name="Ir">the structural tree the compiler consumes</param>
/// <param name="Document">the Razor document, for its own diagnostics</param>
/// <param name="Directives">every directive Razor recognised, WITH its source span (see DirectiveSpyPass)</param>
/// <param name="DescriptorCount">how many tag helper descriptors THIS engine resolved -- decision 53's invariant</param>
/// <param name="FilePath">the full path of the component, used to tell a user's node from a synthesised one</param>
public sealed record ParseResult(
    DocumentIntermediateNode Ir,
    RazorCodeDocument Document,
    IReadOnlyList<DirectiveSite> Directives,
    int DescriptorCount,
    string FilePath);

/// <summary>A directive as WRITTEN: its name and the exact place it was written.</summary>
/// <param name="Name">the directive's name, e.g. "page"</param>
/// <param name="Source">where the author wrote it — Razor drops this during lowering, which is why
///   DirectiveSpyPass exists</param>
/// <param name="Tokens">the directive's own tokens, e.g. the route string of `@page "/about"`
///   (decision 139). Captured here because the lowered node keeps the value and loses the span, and a
///   router needs the VALUE while every diagnostic needs the span.</param>
public readonly record struct DirectiveSite(string Name, SourceSpan? Source, IReadOnlyList<string> Tokens);

/// <summary>
/// Captures every directive Razor recognised, WITH ITS SOURCE SPAN, before Razor
/// lowers it into a node that has lost it.
///
/// WHY THIS EXISTS. Section 10 requires that an out-of-subset construct produce a
/// diagnostic, and a diagnostic without a location is a diagnostic you cannot act on.
/// But Razor's directive-classifier passes REPLACE the located DirectiveIntermediateNode
/// with an unlocated lowered node -- verified, not assumed:
///
///     @inject IServiceProvider S  ->  ComponentInjectIntermediateNode   Source = null
///     @page "/counter"            ->  RouteAttributeExtensionNode       Source = null
///     @layout MainLayout          ->  CSharpCodeIntermediateNode        Source = null
///
/// So by the time the emitter walks the tree, the location is GONE and the best a
/// diagnostic could say is "somewhere in this file". This pass reads the directives
/// while they still exist and writes down what it saw. Measured on the probes:
/// inject/page/layout/inherits/implements/typeparam/attribute/code all arrive here
/// with an exact (line, character).
///
/// WHERE THE DIRECTIVE ACTUALLY DIES -- measured with one spy per phase, because the
/// obvious guess was wrong. It is NOT the directive-classifier passes that remove it:
///
///     directive-classifier, Order=MinValue    sees: [inject@(1,1)]
///     directive-classifier, Order=MaxValue    sees: [inject@(1,1)]
///     optimization,         Order=MinValue    sees: [inject@(1,1)]
///     optimization,         Order=MaxValue    sees: []
///     FINAL IR (what the emitter walks)       sees: []
///
/// The node survives the WHOLE classifier phase and dies in a late OPTIMIZATION pass.
/// So Order = int.MinValue is DEFENSIVE, NOT LOAD-BEARING: any directive-classifier
/// pass at any Order would see the same thing, and setting it to int.MaxValue changes
/// nothing (verified -- the mutation is green, and the comment says so rather than
/// letting the next reader believe the Order is what makes this work). What IS
/// load-bearing is that this pass runs AT ALL: dropping it from the feature list turns
/// every declaration-level diagnostic into "&lt;no source span&gt;", and that mutation is
/// RED.
///
/// It is a READER. It mutates nothing, so it cannot change an emitted byte.
/// </summary>
public sealed class DirectiveSpyPass : IntermediateNodePassBase, IRazorDirectiveClassifierPass
{
    readonly List<DirectiveSite> _directives = [];

    public IReadOnlyList<DirectiveSite> Directives => _directives;

    public override int Order => int.MinValue;

    protected override void ExecuteCore(RazorCodeDocument codeDocument, DocumentIntermediateNode documentNode)
    {
        foreach (var d in documentNode.FindDescendantNodes<DirectiveIntermediateNode>())
            _directives.Add(new DirectiveSite(d.DirectiveName, d.Source,
                d.Tokens.Select(t => t.Content).ToList()));
    }

}

public sealed class GeneratorException(string message) : Exception(message);
