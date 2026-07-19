using Xunit;

namespace Filament.Generator.Tests;

public class ReactiveAttrTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/ReactiveAttr.Blazor/App.razor
    /// is alpha-equivalent to the hand-written samples/ReactiveAttr/reactiveattr.js. The spec is the
    /// reference; the generator is judged. reactiveattr.js's Blazor-faithfulness is what the DOM-contract
    /// oracle measures (baseline/ReactiveAttr.Blazor vs filament-reactiveattr-gen, BENCH n°13).
    /// </summary>
    [Fact]
    public void Gate_GeneratedReactiveAttr_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.ReactiveAttrToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.ReactiveAttrAnswerKey);
        Assert.True(exit == 0,
            "reactive-attribute gate FAILED. Generated module is NOT alpha-equivalent to samples/ReactiveAttr/reactiveattr.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>The `class` attribute is a live effect over setAttr on the lifted signal, not a splice.</summary>
    [Fact]
    public void EmittedReactiveAttr_BindsClassWithSetAttrEffect()
    {
        var js = File.ReadAllText(Generate.ReactiveAttrToTemp());
        Assert.Contains("effect(", js);
        Assert.Contains("setAttr(", js);
        Assert.Contains("'class'", js);
        Assert.Contains("statusClass.value", js);
        Assert.DoesNotContain("[dynamic-attribute]", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedReactiveAttrJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.ReactiveAttrToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "ReactiveAttr.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
