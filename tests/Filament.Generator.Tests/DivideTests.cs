using Xunit;

namespace Filament.Generator.Tests;

public class DivideTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/Divide.Blazor/App.razor
    /// is alpha-equivalent to the hand-written samples/Divide/divide.js. The key is the SPEC and the
    /// REFERENCE; the generator is JUDGED. divide.js's Blazor-faithfulness is what the DOM-contract
    /// oracle measures (baseline/Divide.Blazor rendered vs filament-divide-gen rendered).
    /// </summary>
    [Fact]
    public void Gate_GeneratedDivide_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.DivideToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.DivideAnswerKey);
        Assert.True(exit == 0,
            "double-division gate FAILED. Generated module is NOT alpha-equivalent to samples/Divide/divide.js.\n" +
            stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>The emitted division is faithful JS `/` on a lifted signal -- NOT Math.trunc and NOT
    /// refused. The `2.0` literal normalises to `2`; JS `/` is float division either way (7 / 2 === 3.5).</summary>
    [Fact]
    public void EmittedDivide_HalvesADoubleSignalWithFaithfulSlash()
    {
        var js = File.ReadAllText(Generate.DivideToTemp());
        Assert.Contains("value.value = value.value / 2;", js);   // faithful `/`, on the signal
        Assert.DoesNotContain("Math.trunc", js);                  // NOT integer division
        Assert.DoesNotContain("[unsupported-expression]", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedDivideJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.DivideToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "Divide.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
