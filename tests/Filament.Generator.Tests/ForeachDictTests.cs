using Xunit;

namespace Filament.Generator.Tests;

public class ForeachDictTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/ForeachDict.Blazor/App.razor is
    /// alpha-equivalent to the hand-written samples/ForeachDict/foreachdict.js. The key is the SPEC and the
    /// REFERENCE; the generator is JUDGED. foreachdict.js's Blazor-faithfulness is what the DOM-contract oracle
    /// measures (baseline/ForeachDict.Blazor rendered vs filament-foreachdict-gen rendered, BENCH n°44).
    /// </summary>
    [Fact]
    public void Gate_GeneratedForeachDict_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.ForeachDictToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.ForeachDictAnswerKey);
        Assert.True(exit == 0,
            "@foreach-over-Dict gate FAILED. Generated module is NOT alpha-equivalent to samples/ForeachDict/foreachdict.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// The Dict source SPREADS the Map to the [k,v][] array list() reconciles, and @kvp.Value is the REACTIVE
    /// lookup -- reading the Dict signal so a reused key's value refreshes. Both are the whole of decision 125's
    /// emission; a frozen kvp[1] (the wrong mapping) would appear here as `document.createTextNode(kvp[1])`.
    /// </summary>
    [Fact]
    public void EmittedForeachDict_SpreadsTheMapAndLooksTheValueUpReactively()
    {
        var js = File.ReadAllText(Generate.ForeachDictToTemp());
        Assert.Contains("list(_el0, () => [...scores.value], (kvp) => kvp[0], createKvp, null);", js);
        Assert.Contains("effect(() => setText(_tx0, scores.value.get(kvp[0])));", js);
        Assert.DoesNotContain("createTextNode(kvp[1])", js); // the stale static-value mapping must NOT appear
    }

    /// <summary>The runtime is untouched: only pre-existing primitives are imported.</summary>
    [Fact]
    public void EmittedForeachDict_AddsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(Generate.ForeachDictToTemp());
        Assert.Contains("import { signal, effect, setText, listen, insert, list }", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedForeachDictJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.ForeachDictToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "ForeachDict.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
