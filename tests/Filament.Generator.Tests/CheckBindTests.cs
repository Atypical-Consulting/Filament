using Xunit;

namespace Filament.Generator.Tests;

public class CheckBindTests
{
    [Fact]
    public void Gate_GeneratedCheckBind_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.CheckBindToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.CheckBindAnswerKey);
        Assert.True(exit == 0,
            "PHASE: checkbox @bind gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/CheckBind/checkbind.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    [Fact]
    public void Snapshot_EmittedCheckBindJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.CheckBindToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "CheckBind.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>The contract: the checkbox binds the .checked PROPERTY both ways, no BindConverter, no parsing.</summary>
    [Fact]
    public void EmittedCheckBind_BindsTheCheckedProperty_NoConverter()
    {
        var js = File.ReadAllText(Generate.CheckBindToTemp());
        Assert.Contains("effect(() => { _el0.checked = on.value; })", js);
        Assert.Contains("listen(_el0, 'change', (e) => { on.value = e.target.checked; })", js);
        Assert.DoesNotContain("BindConverter", js);
        Assert.DoesNotContain("[unsupported-bind]", js);
    }
}
