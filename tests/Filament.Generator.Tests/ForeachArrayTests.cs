using Xunit;

namespace Filament.Generator.Tests;

public class ForeachArrayTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/ForeachArray.Blazor/App.razor
    /// is alpha-equivalent to the hand-written samples/ForeachArray/foreacharray.js. The key is the SPEC and
    /// the REFERENCE; the generator is JUDGED. foreacharray.js's Blazor-faithfulness is what the DOM-contract
    /// oracle measures (baseline/ForeachArray.Blazor rendered vs filament-foreacharray-gen rendered, BENCH n°43).
    /// </summary>
    [Fact]
    public void Gate_GeneratedForeachArray_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.ForeachArrayToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.ForeachArrayAnswerKey);
        Assert.True(exit == 0,
            "@foreach-over-array gate FAILED. Generated module is NOT alpha-equivalent to samples/ForeachArray/foreacharray.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// A @foreach over a REASSIGNED array is a signal source, so list()'s source collapses to the single
    /// self-subscribing read `() => items.value` -- NOT the List<T> two-line `{ version.value; return array }`
    /// block. This is the whole of decision 124's emission; pin it so a regression to the block form is caught.
    /// </summary>
    [Fact]
    public void EmittedForeachArray_UsesTheCollapsedSignalSource()
    {
        var js = File.ReadAllText(Generate.ForeachArrayToTemp());
        Assert.Contains("list(_el0, () => items.value, (n) => n, createN, null);", js);
        Assert.DoesNotContain("return items.value;", js); // the redundant block-form double-read must be gone
    }

    /// <summary>The runtime is untouched: only signal/listen/insert/list are imported, all pre-existing.</summary>
    [Fact]
    public void EmittedForeachArray_AddsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(Generate.ForeachArrayToTemp());
        Assert.Contains("import { signal, listen, insert, list }", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedForeachArrayJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.ForeachArrayToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "ForeachArray.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
