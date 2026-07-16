using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// SECTION 10, MECHANICALLY: "Toute construction hors sous-ensemble doit produire un
/// diagnostic, jamais du JS silencieusement faux."
///
/// Every case here was SILENT before this suite existed. Not "theoretically reachable"
/// -- measured, by running the generator and reading the module it produced:
///
///     @inject  ->  ComponentInjectIntermediateNode  (class child)      DROPPED, clean module emitted
///     @page    ->  RouteAttributeExtensionNode      (namespace child)  DROPPED, clean module emitted
///     @layout  ->  CSharpCodeIntermediateNode       (namespace child)  DROPPED, clean module emitted
///     @using   ->  UsingDirectiveIntermediateNode   (namespace child)  DROPPED, clean module emitted
///     &lt;Widget/&gt; ->  MarkupElementIntermediateNode                     EMITTED as
///                  document.createElement('SomeWidget') -- an unknown element that
///                  renders nothing, and Razor itself reports NOTHING for it.
///
/// The old compiler reached into the class for BuildRenderTree and @code and never
/// looked at the rest of the tree, so all of the above compiled to a module that looked
/// correct and quietly did less than the source said. That is exactly the failure mode
/// section 10 names.
///
/// TWO ASSERTIONS PER CASE, and the second one is the one with teeth:
///   1. it is REFUSED -- exit code non-zero, and NO FILE ON DISK.
///   2. the diagnostic points at the RIGHT LINE. A diagnostic with no location, or with
///      a location of "somewhere in this file", is not actionable, and it is also how
///      you fail to notice that a rule fires for the wrong reason. The line numbers
///      below are the real line numbers in the fixtures next to this file.
/// </summary>
public class DiagnosticTests
{
    // ---- unsupported directives (Phase 2's own code: FIL0003) ---------------

    [Theory]
    // fixture,                 line, col, the reason tag, and what must be named in the text
    [InlineData("Inject.razor", 1, 1, "unsupported-directive", "@inject")]
    [InlineData("Page.razor", 1, 1, "unsupported-directive", "@page")]
    [InlineData("Layout.razor", 1, 1, "unsupported-directive", "@layout")]
    [InlineData("Inherits.razor", 1, 1, "unsupported-directive", "@inherits")]
    [InlineData("Using.razor", 1, 2, "unsupported-directive", "@using")]
    public void UnsupportedDirective_IsRefused_AtItsExactLocation(
        string fixture, int line, int col, string reason, string mentions)
    {
        var d = Refused(fixture);

        Assert.Contains($"{fixture}({line},{col}): FIL0003: [{reason}]", d);
        Assert.Contains(mentions, d);
    }

    /// <summary>
    /// @if and @foreach are Phase 3 per decision 54 -- Razor emits no loop/branch node,
    /// the header is raw C# text with unbalanced braces and the body element is a
    /// SIBLING of it. They must DIAGNOSE, not silently emit nothing: a template whose
    /// entire conditional body vanished is the most expensive kind of quiet.
    /// </summary>
    [Theory]
    [InlineData("If.razor", 2, 2)]
    [InlineData("Foreach.razor", 2, 2)]
    public void ControlFlow_IsRefused_AtItsExactLocation(string fixture, int line, int col)
    {
        var d = Refused(fixture);

        Assert.Contains($"{fixture}({line},{col}): FIL0003: [control-flow]", d);
        Assert.Contains("decision 54", d);
    }

    /// <summary>
    /// COMPONENT COMPOSITION -- the quietest one, and the only one Razor itself is
    /// silent about too (verified: zero Razor diagnostics of ANY severity on this
    /// fixture). This generator has no compilation, so it cannot resolve a sibling
    /// .razor into a component: &lt;SomeWidget /&gt; stays a plain markup element and would
    /// be emitted as document.createElement('SomeWidget').
    /// </summary>
    [Fact]
    public void ComponentComposition_IsRefused_AtItsExactLocation()
    {
        var d = Refused("Component.razor");

        Assert.Contains("Component.razor(2,5): FIL0003: [component-composition]", d);
        Assert.Contains("SomeWidget", d);
    }

    /// <summary>
    /// @bind lowers into value= + onchange= carrying BindConverter, so it arrives as a
    /// dynamic attribute value rather than as a surviving '@bind'. Refused either way;
    /// this test pins WHICH way, because a rule that fires for a reason you did not
    /// measure is a rule you do not have.
    /// </summary>
    [Fact]
    public void Bind_IsRefused_AtItsExactLocation()
    {
        var d = Refused("Bind.razor");

        Assert.Contains("Bind.razor(1,24): FIL0003: [dynamic-attribute]", d);
        Assert.Contains("BindConverter", d);
    }

    /// <summary>
    /// DECISION 53's backstop, from the input side. An attribute that still carries its
    /// '@' never resolved to a directive attribute. The cause is either an
    /// out-of-subset directive attribute or -- and this is the one that matters -- the
    /// missing tag helper descriptors, in which case @onclick ITSELF lands here and a
    /// generator would emit it as a literal HTML attribute while appearing to work.
    /// </summary>
    [Fact]
    public void UnresolvedDirectiveAttribute_IsRefused_AndNamesBothCauses()
    {
        var d = Refused("DirectiveAttribute.razor");

        // col 15 is the whitespace immediately before '@wat': that is where Razor puts
        // the attribute's span, and the number is read off the generator rather than
        // reasoned about. Being wrong about it is how you discover a rule fires from a
        // node you did not think it fired from.
        Assert.Contains("DirectiveAttribute.razor(1,15): FIL0003: [unsupported-directive]", d);
        Assert.Contains("'@wat'", d);
        Assert.Contains("decision 53", d);
    }

    /// <summary>
    /// THE NODE GATE'S OWN TEST, and the reason it exists is worth writing down.
    ///
    /// Every other fixture here is caught by the DIRECTIVE gate, so no-oping the node
    /// gate left the whole suite GREEN -- an untested backstop, i.e. a claim rather than
    /// an invariant. @attributes is the case only the node gate can see: it lowers to a
    /// SplatIntermediateNode, which is not spelled as a directive (the spy sees nothing)
    /// and is not a node the emitter handles, so it falls to the default branch and
    /// nothing else in the compiler has an opinion about it.
    ///
    /// MUTATION-TESTED: no-op Unaccounted() -> RED (and green before this fixture
    /// existed, which is exactly why it exists).
    /// </summary>
    [Fact]
    public void Splat_IsRefused_ByTheNodeGate_AtItsExactLocation()
    {
        var d = Refused("Splat.razor");

        Assert.Contains("Splat.razor(1,26): FIL0003: [unaccounted-node]", d);
        Assert.Contains("SplatIntermediateNode", d);
    }

    /// <summary>@ref resolves (the descriptors are live) to a capture whose span is the captured name.</summary>
    [Fact]
    public void Ref_IsRefused_AtItsExactLocation()
    {
        var d = Refused("Ref.razor");

        Assert.Contains("Ref.razor(1,22): FIL0003: [unsupported-directive]", d);
        Assert.Contains("@ref", d);
    }

    /// <summary>
    /// DECISION 54, on the real file. Rows is the case the phase was cut around, so it
    /// is checked against baseline/Rows.Blazor/RowsApp.razor itself rather than a
    /// stand-in: @foreach at line 12, @key at line 14.
    /// </summary>
    [Fact]
    public void Rows_IsRefused_WithLocatedDiagnostics_NotCompiledIntoGarbage()
    {
        var outPath = InRepo();
        try
        {
            var (exit, _, stderr) = Compile(RepoPaths.RowsRazor, outPath);

            Assert.NotEqual(0, exit);
            Assert.False(File.Exists(outPath), "the generator wrote a file for a component it cannot compile");
            Assert.Contains("RowsApp.razor(12,14): FIL0003: [control-flow]", stderr);
            Assert.Contains("RowsApp.razor(14,27): FIL0003: [unsupported-directive]", stderr);
            Assert.Contains("@key", stderr);
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    // ---- the properties that hold for EVERY refusal -------------------------

    /// <summary>
    /// EVERY diagnostic carries a real location. This is the assertion that stops a
    /// future rule from being added with a null span: it does not name a construct, it
    /// quantifies over all of them, so a new fixture is covered the day it is added.
    /// </summary>
    [Theory]
    [InlineData("Inject.razor")]
    [InlineData("Page.razor")]
    [InlineData("Layout.razor")]
    [InlineData("Inherits.razor")]
    [InlineData("Using.razor")]
    [InlineData("If.razor")]
    [InlineData("Foreach.razor")]
    [InlineData("Bind.razor")]
    [InlineData("Component.razor")]
    [InlineData("DirectiveAttribute.razor")]
    [InlineData("Ref.razor")]
    [InlineData("Splat.razor")]
    public void EveryDiagnostic_CarriesAnExactLocation_AndPhase2sOwnCode(string fixture)
    {
        var d = Refused(fixture);

        foreach (var line in d.Split('\n').Where(l => l.TrimStart().StartsWith("error ")))
        {
            Assert.DoesNotContain("<no source span>", line);
            Assert.Matches($@"{System.Text.RegularExpressions.Regex.Escape(fixture)}\(\d+,\d+\): FIL0003:", line);

            // FIL0001/FIL0002 are the C# subset's, i.e. Phase 3's. Phase 2 emits FIL0003
            // and nothing else; a generator must not squat the namespace it reports in.
            Assert.DoesNotContain("FIL0001", line);
            Assert.DoesNotContain("FIL0002", line);
        }
    }

    /// <summary>
    /// The refusal is a REFUSAL: no file. A generator that reports an error and still
    /// writes the module leaves the build to decide whether to believe the exit code,
    /// and something downstream always believes the file.
    /// </summary>
    [Theory]
    [InlineData("Inject.razor")]
    [InlineData("Page.razor")]
    [InlineData("Component.razor")]
    [InlineData("If.razor")]
    public void ARefusalWritesNoFile(string fixture)
    {
        var outPath = InRepo();
        try
        {
            var (exit, _, _) = Compile(Path.Combine(RepoPaths.Unsupported, fixture), outPath);

            Assert.NotEqual(0, exit);
            Assert.False(File.Exists(outPath),
                "the generator refused AND wrote the module anyway; downstream will believe the file");
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    // ---- helpers -----------------------------------------------------------

    /// <summary>Compile a fixture that MUST be refused, and hand back stderr.</summary>
    static string Refused(string fixture)
    {
        var outPath = InRepo();
        try
        {
            var (exit, stdout, stderr) = Compile(Path.Combine(RepoPaths.Unsupported, fixture), outPath);

            Assert.True(exit != 0,
                $"{fixture} was COMPILED, not refused. That is the silent mis-compile section 10 forbids.\n" +
                $"stdout:\n{stdout}\nstderr:\n{stderr}\n" +
                (File.Exists(outPath) ? "it emitted:\n" + File.ReadAllText(outPath) : ""));
            return stderr;
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    static (int exit, string stdout, string stderr) Compile(string input, string? output = null)
    {
        var outPath = output ?? InRepo();
        try
        {
            return Run.Generator(input, outPath);
        }
        finally
        {
            if (output is null && File.Exists(outPath)) File.Delete(outPath);
        }
    }

    /// <summary>
    /// An output path inside the repo, so the generator's relative runtime specifier
    /// resolves the way it does in a real build rather than failing for an unrelated
    /// reason and looking like a refusal. (It would: ResolveRuntimeSpecifier throws
    /// when it cannot find the runtime above the output, which is a non-zero exit that
    /// has nothing to do with the diagnostic under test.)
    /// </summary>
    static string InRepo() =>
        Path.Combine(RepoPaths.Root, "samples", "Counter", $".diag-{Guid.NewGuid():N}.js");
}
