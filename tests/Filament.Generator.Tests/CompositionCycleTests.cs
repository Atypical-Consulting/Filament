using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// THE COMPOSITION CYCLE GUARD (decision 169) — found by PROBING the eleven §3 non-goals ADR 0003
/// declared closed (register defect B4). This is honesty work, not surface work.
///
/// A component that renders itself, or two that render each other, used to ABORT THE PROCESS:
///
///   EXIT=134 (SIGABRT), stderr 1 271 340 bytes beginning "Stack overflow.", no out.js, no
///   diagnostic, no location — 1 218 EmitComposition frames deep.
///
/// Both sources are valid Blazor and both RENDER there: HtmlRenderer produces
/// <c>&lt;span class="node"&gt;x&lt;span class="node"&gt;done&lt;/span&gt;&lt;/span&gt;</c> and
/// <c>&lt;span class="alpha"&gt;x&lt;em class="beta"&gt;b&lt;/em&gt;&lt;/span&gt;</c>. Blazor
/// instantiates a child per level and evaluates the guard at RUN time; composition here is
/// compile-time inlining (decision 88) with no instance and no run time, and an @if compiles to a
/// list() whose body is walked whatever the condition says (decision 81). So the recursion has no
/// finite expansion and the honest answer is a LOCATED refusal that names the cycle.
///
/// REFUSAL-ONLY: nothing new renders, so there is nothing for a browser to observe. The evidence is
/// the located diagnostic plus the byte-identity of the controls below.
/// </summary>
public class CompositionCycleTests
{
    /// <summary>
    /// SELF-RECURSION. The refusal must be LOCATED at the recursive use inside the child — the edge
    /// the author can cut — and it must name the cycle rather than merely say "recursive".
    /// </summary>
    [Fact]
    public void SelfRecursiveComponent_IsRefused_NotAStackOverflow()
    {
        var (exit, stderr) = RefuseFixture("CycleSelf");

        Assert.True(exit == 1, $"expected a refusal at exit 1, got {exit} (134 is the old SIGABRT)");
        Assert.Contains("CycleNode.razor(9,47): FIL0003: [composition-cycle]", stderr);
        Assert.Contains("CycleNode.razor -> CycleNode.razor", stderr);
        Assert.DoesNotContain("Stack overflow", stderr);
    }

    /// <summary>
    /// MUTUAL RECURSION — the shape a self-check cannot see, since no file names itself. The whole
    /// path must appear, because "CycleAlpha is recursive" would not tell the author which of the
    /// two edges to cut.
    /// </summary>
    [Fact]
    public void MutuallyRecursiveComponents_AreRefused_WithTheWholePathNamed()
    {
        var (exit, stderr) = RefuseFixture("CycleMutual");

        Assert.True(exit == 1, $"expected a refusal at exit 1, got {exit} (134 is the old SIGABRT)");
        Assert.Contains("CycleBeta.razor(4,50): FIL0003: [composition-cycle]", stderr);
        Assert.Contains("CycleAlpha.razor -> CycleBeta.razor -> CycleAlpha.razor", stderr);
        Assert.DoesNotContain("Stack overflow", stderr);
    }

    /// <summary>
    /// A CYCLE THROUGH A CONTAINER'S OWN MARKUP. CycleCard.razor nests itself, so every level renders
    /// another level — Blazor does not terminate on this one either (HtmlRenderer still running at
    /// 60 s, 0 bytes of output). Its twin NestedSame.razor below is the same two tags nested by the
    /// PARENT, and that one compiles: the guard distinguishes them, it does not blanket-refuse.
    /// </summary>
    [Fact]
    public void ComponentThatNestsItselfInItsOwnContent_IsRefused()
    {
        var (exit, stderr) = RefuseFixture("CycleContent");

        Assert.True(exit == 1, $"expected a refusal at exit 1, got {exit}");
        Assert.Contains("CycleCard.razor(7,19): FIL0003: [composition-cycle]", stderr);
        Assert.Contains("CycleCard.razor -> CycleCard.razor", stderr);
    }

    /// <summary>The message must carry the mechanism and the remedy, not just a code.</summary>
    [Fact]
    public void TheCycleDiagnostic_NamesTheMechanismAndTheRemedy()
    {
        var (_, stderr) = RefuseFixture("CycleSelf");

        Assert.Contains("re-enters a component that is already being inlined", stderr);
        Assert.Contains("COMPILE-TIME INLINING", stderr);
        Assert.Contains("Cut one edge of the cycle", stderr);
    }

    /// <summary>
    /// THE CONTROL THAT KEEPS THE GUARD HONEST: "used more than once" is NOT a cycle. DiamondLeaf is
    /// reached four times — through DiamondLeft, through DiamondRight, and twice directly — and the
    /// file compiles, emitting all four copies inlined. A visited set instead of a path would refuse
    /// this, which would be a worse defect than the crash.
    ///
    /// The emitted tree is Blazor's own: HtmlRenderer gives
    /// &lt;span class="left"&gt;&lt;b class="leaf"&gt;l&lt;/b&gt;&lt;/span&gt;&lt;em class="right"&gt;&lt;b
    /// class="leaf"&gt;r&lt;/b&gt;&lt;/em&gt;&lt;b class="leaf"&gt;c&lt;/b&gt;&lt;b class="leaf"&gt;d&lt;/b&gt;.
    /// </summary>
    [Fact]
    public void ADiamondAndARepeatedChild_StillCompile()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("Composition/Diamond.razor"));

        Assert.DoesNotContain("composition-cycle", js);
        Assert.Equal(4, js.Split("document.createElement('b')").Length - 1);   // DiamondLeaf, four times
        Assert.Contains("document.createTextNode('l')", js);
        Assert.Contains("document.createTextNode('r')", js);
        Assert.Contains("document.createTextNode('c')", js);
        Assert.Contains("document.createTextNode('d')", js);
        // Everything inlines into ONE mount(): no child got a module of its own.
        Assert.Equal(1, js.Split("export function mount").Length - 1);
    }

    /// <summary>
    /// THE COUNTERFACTUAL CONTROL. A card nested inside a card's CONTENT terminates, and it compiles.
    /// It is here because the first cut of the guard REFUSED it: a fragment is compiled with the
    /// parent's context restored (decision 131) and the parent's composition chain has to be restored
    /// with it, or the content reads as re-entering the child that places it. Measured by commenting
    /// the restore out and re-running this exact file.
    /// </summary>
    [Fact]
    public void AComponentNestedInsideItsOwnContent_StillCompiles()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("Composition/NestedSame.razor"));

        Assert.DoesNotContain("composition-cycle", js);
        Assert.Equal(2, js.Split("setAttr(_el").Length - 1);                             // two cards, nested
        Assert.Contains("insert(_el2, _el3);", js);                                      // <b> inside the inner card
        Assert.Contains("insert(_el1, _el2);", js);                                      // inner card inside the outer
        Assert.Contains("document.createTextNode('x')", js);
    }

    /// <summary>Section 10: snapshots are the wall against silent generator regressions the
    /// name-blind canon gate cannot see. Both controls, pinned to the byte.</summary>
    [Theory]
    [InlineData("Composition/Diamond.razor", "CompositionDiamond.approved.js")]
    [InlineData("Composition/NestedSame.razor", "CompositionNestedSame.approved.js")]
    public void Snapshot_EmittedControl_MatchesApprovedBytes(string fixture, string approvedName)
    {
        var actual = File.ReadAllText(Generate.ToTempFixture(fixture)).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", approvedName);
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>Run a refused fixture from Unsupported/Gate and hand back exit code + stderr, asserting
    /// nothing was written. Emitted in-repo so the relative runtime specifier resolves.</summary>
    static (int exit, string stderr) RefuseFixture(string name)
    {
        var outPath = Path.Combine(RepoPaths.Unsupported, $".cycle-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Gate", name + ".razor"), outPath);
            Assert.False(File.Exists(outPath), "the generator refused AND wrote the module anyway");
            return (exit, stderr);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }
}
