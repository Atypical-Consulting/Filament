using Xunit;

namespace Filament.Generator.Tests;

public class BoundComposeTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/BoundCompose.Blazor/App.razor
    /// is alpha-equivalent to the hand-written samples/BoundCompose/boundcompose.js. The key is the SPEC and
    /// the REFERENCE; the generator is JUDGED. boundcompose.js's Blazor-faithfulness is what the DOM-contract
    /// oracle measures (baseline/BoundCompose.Blazor vs filament-boundcompose-gen, BENCH n°12).
    /// </summary>
    [Fact]
    public void Gate_GeneratedBoundCompose_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.BoundComposeToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.BoundComposeAnswerKey);
        Assert.True(exit == 0,
            "bound-composition gate FAILED. Generated module is NOT alpha-equivalent to samples/BoundCompose/boundcompose.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>The child's @Value is a LIVE effect on the parent's lifted signal, not a folded constant.</summary>
    [Fact]
    public void EmittedBoundCompose_BindsTheChildToTheParentSignal()
    {
        var js = File.ReadAllText(Generate.BoundComposeToTemp());
        Assert.Contains("effect(", js);
        Assert.Contains("count.value", js);
        Assert.DoesNotContain("[bound-parameter]", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedBoundComposeJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.BoundComposeToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "BoundCompose.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
