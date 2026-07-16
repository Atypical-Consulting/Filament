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
    /// THE ONE description of the chain. Both the parse and every invariant that
    /// reports on the parse come through here, so a mutation to the wiring cannot hit
    /// one copy and miss the other.
    /// </summary>
    static RazorProjectEngine CreateEngine(string dir, DirectiveSpyPass spy)
    {
        var refs = LoadReferenceAssemblies();
        var fs = RazorProjectFileSystem.Create(dir);

        return RazorProjectEngine.Create(RazorConfiguration.Default, fs, b =>
        {
            // public API: registers the component passes and the tag helper providers
            CompilerFeatures.Register(b);
            b.Features.Add(new DefaultMetadataReferenceFeature { References = refs });
            b.Features.Add(new CompilationTagHelperFeature());
            b.AddDefaultImports(WebImport);
            b.Features.Add(spy);

            foreach (var name in new[] { "ComponentMarkupBlockPass", "ComponentMarkupEncodingPass" })
            {
                var f = b.Features.FirstOrDefault(x => x.GetType().Name == name);
                if (f is not null) b.Features.Remove(f);
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

    /// <summary>
    /// Locate the reference assemblies the tag helper discovery needs. Discovered from
    /// the running runtime rather than hardcoded, so this survives a machine that is
    /// not the author's -- but it is still a filesystem probe, and it FAILS LOUDLY
    /// rather than returning an empty list, because an empty list is exactly the
    /// silent mis-parse of decision 53.
    /// </summary>
    static List<MetadataReference> LoadReferenceAssemblies()
    {
        // .../shared/Microsoft.NETCore.App/10.0.9/  ->  .../
        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var dotnetRoot = Path.GetFullPath(Path.Combine(runtimeDir, "..", "..", ".."));
        var packs = Path.Combine(dotnetRoot, "packs");

        if (!Directory.Exists(packs))
            throw new GeneratorException(
                $"FIL-WIRING: no reference packs under '{packs}'. Tag helper discovery cannot run, and " +
                "without it @onclick mis-parses in silence (decision 53). Refusing to emit.");

        var netRef = NewestRefDir(Path.Combine(packs, "Microsoft.NETCore.App.Ref"));
        var aspRef = NewestRefDir(Path.Combine(packs, "Microsoft.AspNetCore.App.Ref"));

        var files = Directory.GetFiles(netRef, "*.dll").Concat(Directory.GetFiles(aspRef, "*.dll"));
        var refs = files.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();

        if (refs.Count == 0)
            throw new GeneratorException("FIL-WIRING: reference packs resolved to zero assemblies. Refusing to emit.");
        return refs;
    }

    static string NewestRefDir(string packRoot)
    {
        if (!Directory.Exists(packRoot))
            throw new GeneratorException($"FIL-WIRING: reference pack '{packRoot}' not found. Refusing to emit.");

        var best = Directory.GetDirectories(packRoot)
            .Select(d => (dir: d, ver: ParseVersion(Path.GetFileName(d))))
            .Where(x => x.ver is not null)
            .OrderByDescending(x => x.ver)
            .Select(x => x.dir)
            .FirstOrDefault()
            ?? throw new GeneratorException($"FIL-WIRING: no versioned directory under '{packRoot}'.");

        var refDir = Directory.GetDirectories(Path.Combine(best, "ref")).OrderByDescending(x => x).FirstOrDefault()
            ?? throw new GeneratorException($"FIL-WIRING: no ref/<tfm> directory under '{best}'.");
        return refDir;
    }

    static Version? ParseVersion(string s) => Version.TryParse(s.Split('-')[0], out var v) ? v : null;
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
public readonly record struct DirectiveSite(string Name, SourceSpan? Source);

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
            _directives.Add(new DirectiveSite(d.DirectiveName, d.Source));
    }
}

public sealed class GeneratorException(string message) : Exception(message);
