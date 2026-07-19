using Xunit;

namespace Filament.Generator.Tests;

public class MixedAttrTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/MixedAttr.Blazor/App.razor is
    /// alpha-equivalent to the hand-written samples/MixedAttr/mixedattr.js. The spec is the reference; the
    /// generator is judged. mixedattr.js's Blazor-faithfulness is what the DOM-contract oracle measures
    /// (baseline/MixedAttr.Blazor vs filament-mixedattr-gen, BENCH n°15).
    /// </summary>
    [Fact]
    public void Gate_GeneratedMixedAttr_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.MixedAttrToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.MixedAttrAnswerKey);
        Assert.True(exit == 0,
            "mixed-attribute gate FAILED. Generated module is NOT alpha-equivalent to samples/MixedAttr/mixedattr.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>The `class` value is a live effect over setAttr whose argument is the COMPOSED string:
    /// the literal terms survive around the reactive expression, in order.</summary>
    [Fact]
    public void EmittedMixedAttr_ComposesLiteralsAroundExpression()
    {
        var js = File.ReadAllText(Generate.MixedAttrToTemp());
        Assert.Contains("effect(", js);
        Assert.Contains("setAttr(", js);
        Assert.Contains("'class'", js);
        Assert.Contains("'badge '", js);          // leading literal
        Assert.Contains("statusClass.value", js); // the reactive expression
        Assert.Contains("' rounded'", js);         // trailing literal
        Assert.DoesNotContain("[dynamic-attribute]", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedMixedAttrJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.MixedAttrToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "MixedAttr.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
