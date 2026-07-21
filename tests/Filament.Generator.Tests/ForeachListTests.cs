using Xunit;

namespace Filament.Generator.Tests;

public class ForeachListTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/ForeachList.Blazor/App.razor
    /// is alpha-equivalent to the hand-written samples/ForeachList/foreachlist.js. The key is the SPEC and
    /// the REFERENCE; the generator is JUDGED. foreachlist.js's Blazor-faithfulness is what the DOM-contract
    /// oracle measures (baseline/ForeachList.Blazor rendered vs filament-foreachlist-gen rendered).
    /// </summary>
    [Fact]
    public void Gate_GeneratedForeachList_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.ForeachListToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.ForeachListAnswerKey);
        Assert.True(exit == 0,
            "@foreach-over-reassigned-List gate FAILED. Generated module is NOT alpha-equivalent to samples/ForeachList/foreachlist.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// A @foreach over a REASSIGNED, never-mutated List is a signal source exactly as a reassigned T[]
    /// is (decision 124): the source collapses to the single self-subscribing read `() => items.value`,
    /// NOT the mutated-List two-line `{ version.value; return array }` block (decision 140). Pin the
    /// emission so a regression to the block form -- or back to a refusal -- is caught.
    /// </summary>
    [Fact]
    public void EmittedForeachList_UsesTheCollapsedSignalSource()
    {
        var js = File.ReadAllText(Generate.ForeachListToTemp());
        Assert.Contains("list(_el0, () => items.value, (n) => n, createN, null);", js);
        Assert.DoesNotContain("Version.value", js); // no phantom version signal for a never-mutated List
    }

    /// <summary>The runtime is untouched: only signal/listen/insert/list are imported, all pre-existing.</summary>
    [Fact]
    public void EmittedForeachList_AddsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(Generate.ForeachListToTemp());
        Assert.Contains("import { signal, listen, insert, list }", js);
    }

    /// <summary>
    /// THE BOUNDARY HOLDS: a List that is never written AT ALL (neither mutated nor reassigned) is
    /// still refused -- decision 140 admits the reassigned List, it does not invent reactivity for a
    /// static one. The refusal names the field, so the author is told WHICH list is inert.
    /// </summary>
    [Fact]
    public void NeverWrittenListForeach_IsStillRefused()
    {
        var razor = Path.Combine(RepoPaths.Unsupported, "Gate", "ForeachStatic.razor");
        // Output INSIDE the repo (DiagnosticTests' InRepo pattern) so the runtime specifier resolves and
        // the failure observed is the REFUSAL, not FIL-WIRING about a temp path with no runtime above it.
        var outPath = Path.Combine(RepoPaths.Root, "samples", "Counter", $".diag-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(razor, outPath);
            Assert.NotEqual(0, exit);
            Assert.Contains("unsupported-foreach", stderr);
            Assert.Contains("static tree", stderr);
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedForeachListJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.ForeachListToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "ForeachList.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
