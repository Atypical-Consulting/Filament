using Xunit;

namespace Filament.Generator.Tests;

public class RootIfTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/RootIf.Blazor/App.razor
    /// is alpha-equivalent to the hand-written samples/RootIf/rootif.js. The key is the SPEC and the
    /// REFERENCE; the generator is JUDGED. rootif.js's Blazor-faithfulness is what the DOM-contract
    /// oracle measures (baseline/RootIf.Blazor rendered vs filament-rootif-gen rendered, BENCH n°11).
    /// </summary>
    [Fact]
    public void Gate_GeneratedRootIf_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.RootIfToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.RootIfAnswerKey);
        Assert.True(exit == 0,
            "root-@if gate FAILED. Generated module is NOT alpha-equivalent to samples/RootIf/rootif.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>The root conditional anchors AND lists against target, not a created wrapper element.</summary>
    [Fact]
    public void EmittedRootIf_AnchorsAndListsAgainstTarget()
    {
        var js = File.ReadAllText(Generate.RootIfToTemp());
        Assert.Contains("insert(target,", js);   // the comment anchor lands in target
        Assert.Contains("list(target,", js);      // the conditional attaches to target
        Assert.DoesNotContain("[template-code-at-root]", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedRootIfJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.RootIfToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "RootIf.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
