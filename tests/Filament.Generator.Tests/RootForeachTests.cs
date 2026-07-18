using Xunit;

namespace Filament.Generator.Tests;

public class RootForeachTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/RootForeach.Blazor/App.razor
    /// is alpha-equivalent to the hand-written samples/RootForeach/rootforeach.js. The key is the SPEC and
    /// the REFERENCE; the generator is JUDGED. rootforeach.js's Blazor-faithfulness is what the DOM-contract
    /// oracle measures (baseline/RootForeach.Blazor rendered vs filament-rootforeach-gen rendered, BENCH n°11).
    /// </summary>
    [Fact]
    public void Gate_GeneratedRootForeach_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.RootForeachToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.RootForeachAnswerKey);
        Assert.True(exit == 0,
            "root-@foreach gate FAILED. Generated module is NOT alpha-equivalent to samples/RootForeach/rootforeach.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>The root list reconciles against target, not against a created wrapper element.</summary>
    [Fact]
    public void EmittedRootForeach_ListsAgainstTarget()
    {
        var js = File.ReadAllText(Generate.RootForeachToTemp());
        Assert.Contains("list(target,", js);
        Assert.DoesNotContain("[template-code-at-root]", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedRootForeachJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.RootForeachToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "RootForeach.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
