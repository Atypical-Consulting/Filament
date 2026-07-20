using Xunit;

namespace Filament.Generator.Tests;

public class GroupByTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/GroupBy.Blazor/App.razor is
    /// alpha-equivalent to the hand-written samples/GroupBy/groupby.js. The key is the SPEC and the REFERENCE;
    /// the generator is JUDGED. groupby.js's Blazor-faithfulness is what the DOM-contract oracle measures
    /// (baseline/GroupBy.Blazor rendered vs filament-groupby-gen rendered, BENCH n°47).
    /// </summary>
    [Fact]
    public void Gate_GeneratedGroupBy_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.GroupByToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.GroupByAnswerKey);
        Assert.True(exit == 0,
            "GroupBy gate FAILED. Generated module is NOT alpha-equivalent to samples/GroupBy/groupby.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// GroupBy reduces into a Map&lt;K, group&gt; where each group is an array-with-.key, and g.Key is g.key --
    /// the whole of decision 128. Every sequence op on a group (g.Count() -> .length) flows through the array path.
    /// </summary>
    [Fact]
    public void EmittedGroupBy_ReducesIntoKeyedGroups()
    {
        var js = File.ReadAllText(Generate.GroupByToTemp());
        Assert.Contains("__g = []; __g.key = __k; __m.set(__k, __g);", js);   // fresh keyed group per first-seen key
        Assert.Contains(".values()][0].key", js);                            // g.Key -> g.key
        Assert.Contains(".values()][0].length", js);                         // g.Count() -> .length (group is an array)
    }

    /// <summary>The runtime is untouched: reduce/Map/spread are JS builtins, only pre-existing primitives imported.</summary>
    [Fact]
    public void EmittedGroupBy_AddsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(Generate.GroupByToTemp());
        Assert.Contains("import { signal, effect, batch, setText, listen, insert }", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedGroupByJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.GroupByToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "GroupBy.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
