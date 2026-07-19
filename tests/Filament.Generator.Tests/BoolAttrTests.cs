using Xunit;

namespace Filament.Generator.Tests;

public class BoolAttrTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/BoolAttr.Blazor/App.razor is
    /// alpha-equivalent to the hand-written samples/BoolAttr/boolattr.js. The spec is the reference; the
    /// generator is judged. boolattr.js's Blazor-faithfulness is what the DOM-contract oracle measures
    /// (baseline/BoolAttr.Blazor vs filament-boolattr-gen, BENCH n°14).
    /// </summary>
    [Fact]
    public void Gate_GeneratedBoolAttr_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.BoolAttrToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.BoolAttrAnswerKey);
        Assert.True(exit == 0,
            "boolean-attribute gate FAILED. Generated module is NOT alpha-equivalent to samples/BoolAttr/boolattr.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>The `disabled` attribute is a live effect that maps the bool to present/absent via
    /// setAttr's null->remove -- never the naive `setAttr(el,'disabled',true)` that yields disabled="true".</summary>
    [Fact]
    public void EmittedBoolAttr_BindsDisabledPresentAbsent()
    {
        var js = File.ReadAllText(Generate.BoolAttrToTemp());
        Assert.Contains("effect(", js);
        Assert.Contains("setAttr(", js);
        Assert.Contains("'disabled'", js);
        Assert.Contains("? '' : null", js);          // the present/absent ternary
        Assert.DoesNotContain("[dynamic-attribute]", js);
        Assert.DoesNotContain("'disabled', true", js); // the naive-boolean trap
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedBoolAttrJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.BoolAttrToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "BoolAttr.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
