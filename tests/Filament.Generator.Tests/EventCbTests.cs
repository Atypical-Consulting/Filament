using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// EVENTCALLBACK — the child→parent half of composition (decision 130). #88/#90 pushed data DOWN
/// into a child; this carries an EVENT back UP. ADR 0002's Bucket B audit named it the highest-value
/// genuine gap, and its fix reuses the inline-into-parent model rather than adding a subsystem.
/// </summary>
public class EventCbTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/EventCb.Blazor/App.razor
    /// is alpha-equivalent to the hand-written samples/EventCb/eventcb.js. The key is the SPEC and the
    /// REFERENCE; the generator is JUDGED. eventcb.js's Blazor-faithfulness is what the DOM-contract
    /// oracle measures (baseline/EventCb.Blazor vs filament-eventcb-gen, BENCH n°49).
    /// </summary>
    [Fact]
    public void Gate_GeneratedEventCb_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.EventCbToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.EventCbAnswerKey);
        Assert.True(exit == 0,
            "event-callback gate FAILED. Generated module is NOT alpha-equivalent to samples/EventCb/eventcb.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// THE CLAIM, stated as an assertion: the callback is ERASED. The child's button listens, and what
    /// it runs is the PARENT's write to the PARENT's signal — no delegate, no InvokeAsync, no
    /// subscription list, nothing named OnBump anywhere in the module. If any of that appeared, the
    /// feature would have cost runtime weight, which is precisely what this slice claims it need not.
    /// </summary>
    [Fact]
    public void EmittedEventCb_ErasesTheCallbackIntoTheParentsOwnHandler()
    {
        var js = File.ReadAllText(Generate.EventCbToTemp());

        Assert.Contains("listen(", js);
        Assert.Contains("count.value++", js);      // the PARENT's method body, run by the CHILD's button
        Assert.DoesNotContain("OnBump", js);       // the parameter name survives nowhere
        Assert.DoesNotContain("EventCallback", js);
        Assert.DoesNotContain("InvokeAsync", js);
        Assert.DoesNotContain("[unresolved-name]", js);
        Assert.DoesNotContain("[composition-out-of-subset]", js);
    }

    /// <summary>
    /// The alias resolves to the parent's method BEFORE the handler is recorded, so the handler rules
    /// decide on the parent's own tables: Inc is named once and called nowhere else, so decision 68's
    /// single-use inlining folds its body in, and its single write takes no batch(). Composition
    /// changes neither — that is what makes the alias faithful rather than merely convenient.
    /// </summary>
    [Fact]
    public void EmittedEventCb_AppliesTheParentsOwnInliningAndBatchingRules()
    {
        var js = File.ReadAllText(Generate.EventCbToTemp());

        Assert.DoesNotContain("function inc", js);   // single-use: inlined, not emitted as a function
        Assert.DoesNotContain("batch(", js);         // one write: no batch to coalesce (decision 68)
    }

    /// <summary>
    /// GENERATOR-ONLY, ZERO HELPER: the module imports exactly the primitives the counter slices
    /// already ship. An EventCallback that needed a runtime primitive would show up here.
    /// </summary>
    [Fact]
    public void EmittedEventCb_AddsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(Generate.EventCbToTemp());
        Assert.Contains("import { signal, effect, setText, listen, insert }", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedEventCbJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.EventCbToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "EventCb.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
