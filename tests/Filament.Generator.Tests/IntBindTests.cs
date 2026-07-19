using Xunit;

namespace Filament.Generator.Tests;

public class IntBindTests
{
    [Fact]
    public void Gate_GeneratedIntBind_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.IntBindToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.IntBindAnswerKey);
        Assert.True(exit == 0,
            "PHASE: int @bind gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/IntBind/intbind.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    [Fact]
    public void Snapshot_EmittedIntBindJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.IntBindToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "IntBind.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>The contract: int @bind formats via String() and parses the change with the int.TryParse-mirroring
    /// regex + int32 range + revert, so an invalid entry keeps the field.</summary>
    [Fact]
    public void EmittedIntBind_FormatsAndParsesWithRevert()
    {
        var js = File.ReadAllText(Generate.IntBindToTemp());
        Assert.Contains("_el0.value = String(count.value)", js);        // format
        Assert.Contains(@"/^\s*[+-]?\d+\s*$/.test(_s)", js);            // int.TryParse-mirroring shape
        Assert.Contains("_n >= -2147483648 && _n <= 2147483647", js);   // int32 range
        Assert.DoesNotContain("[unsupported-bind]", js);
    }
}
