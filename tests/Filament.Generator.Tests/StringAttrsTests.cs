using Xunit;

namespace Filament.Generator.Tests;

public class StringAttrsTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/StringAttrs.Blazor/App.razor
    /// is alpha-equivalent to the hand-written samples/StringAttrs/stringattrs.js. title/href/aria-label
    /// compile to the same composed setAttr as class; the emission is what the DOM-contract oracle
    /// measures (baseline/StringAttrs.Blazor vs filament-stringattrs-gen, BENCH n°16).
    /// </summary>
    [Fact]
    public void Gate_GeneratedStringAttrs_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.StringAttrsToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.StringAttrsAnswerKey);
        Assert.True(exit == 0,
            "string-attribute-names gate FAILED. Generated module is NOT alpha-equivalent to samples/StringAttrs/stringattrs.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Each of title/href/aria-label is a live effect over setAttr on the lifted signal.</summary>
    [Fact]
    public void EmittedStringAttrs_BindsEachNameWithSetAttrEffect()
    {
        var js = File.ReadAllText(Generate.StringAttrsToTemp());
        Assert.Contains("effect(", js);
        Assert.Contains("setAttr(", js);
        Assert.Contains("'href'", js);
        Assert.Contains("'title'", js);
        Assert.Contains("'aria-label'", js);
        Assert.Contains("url.value", js);
        Assert.Contains("tip.value", js);
        Assert.Contains("label.value", js);
        Assert.DoesNotContain("[dynamic-attribute]", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedStringAttrsJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.StringAttrsToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "StringAttrs.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
