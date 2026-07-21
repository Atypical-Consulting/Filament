using Xunit;

namespace Filament.Generator.Tests;

public class RowActionsTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/RowActions.Blazor/App.razor
    /// is alpha-equivalent to the hand-written samples/RowActions/rowactions.js. The key is the SPEC and
    /// the REFERENCE; the generator is JUDGED. rowactions.js's Blazor-faithfulness is what the DOM-contract
    /// oracle measures (baseline/RowActions.Blazor rendered vs filament-rowactions-gen rendered).
    /// </summary>
    [Fact]
    public void Gate_GeneratedRowActions_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.RowActionsToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.RowActionsAnswerKey);
        Assert.True(exit == 0,
            "per-row-handler gate FAILED. Generated module is NOT alpha-equivalent to samples/RowActions/rowactions.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// THE SCOPE IS THE SLICE (decision 141): the row lambda captures the LOOP VARIABLE, so its
    /// listener must be wired INSIDE the row create function -- between the function's declaration
    /// and list()'s registration -- never in the mount-level events section, where the row's element
    /// consts do not exist and the loop variable never did.
    /// </summary>
    [Fact]
    public void EmittedRowActions_WiresTheRowListenerInsideTheRowCreateFunction()
    {
        var js = File.ReadAllText(Generate.RowActionsToTemp());
        var createAt = js.IndexOf("function createR(r)", StringComparison.Ordinal);
        var arrowAt = js.IndexOf("del(r.id)", StringComparison.Ordinal);
        var listAt = js.IndexOf("list(", StringComparison.Ordinal);
        Assert.True(createAt >= 0, "row create function createR(r) not found");
        Assert.True(arrowAt > createAt, "the captured-lambda arrow del(r.id) must be inside createR");
        Assert.True(listAt > arrowAt, "the row listener must be wired before list() registers the template");
    }

    /// <summary>The runtime is untouched: signal/batch/setAttr/listen/insert/list, all pre-existing.</summary>
    [Fact]
    public void EmittedRowActions_AddsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(Generate.RowActionsToTemp());
        Assert.Contains("import { signal, batch, setAttr, listen, insert, list }", js);
    }

    /// <summary>
    /// THE BOUNDARY HOLDS: `e => …` (the event-object lambda) stays refused -- decision 141 admits the
    /// no-argument captured lambda, it does not open the event object (deferred, as under decision 105).
    /// </summary>
    [Fact]
    public void EventArgLambdaInRow_IsStillRefused()
    {
        var razor = Path.Combine(RepoPaths.Unsupported, "Gate", "RowEventArg.razor");
        var outPath = Path.Combine(RepoPaths.Root, "samples", "Counter", $".diag-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(razor, outPath);
            Assert.NotEqual(0, exit);
            // FIL0003 compound-expression: the e-lambda is not the no-arg form the harvest admits, so it
            // falls through to the binding-site guard -- refused, never spliced (decision 105's boundary).
            Assert.Contains("FIL0003", stderr);
            Assert.Contains("e => Del(n)", stderr);
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedRowActionsJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.RowActionsToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "RowActions.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
