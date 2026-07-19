using Xunit;

namespace Filament.Generator.Tests;

public class ListOpsTests
{
    [Fact]
    public void Gate_GeneratedListOps_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.ListOpsToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.ListOpsAnswerKey);
        Assert.True(exit == 0,
            "PHASE: List.Clear() gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/ListOps/listops.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    [Fact]
    public void Snapshot_EmittedListOpsJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.ListOpsToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "ListOps.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>The contract: .Clear() empties the array in place and the version bump re-runs list().</summary>
    [Fact]
    public void EmittedListOps_ClearEmptiesInPlaceAndBumpsVersion()
    {
        var js = File.ReadAllText(Generate.ListOpsToTemp());
        Assert.Contains("items.length = 0;", js);
        Assert.Contains("itemsChanged();", js);
        Assert.DoesNotContain("[unsupported-call]", js);
    }
}
