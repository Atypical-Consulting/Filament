using Xunit;

namespace Filament.Generator.Tests;

public class SizedArrayTests
{
    /// <summary>THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).</summary>
    [Fact]
    public void Gate_GeneratedSizedArray_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.SizedArrayToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.SizedArrayAnswerKey);
        Assert.True(exit == 0,
            "PHASE: sized-array gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/SizedArray/sizedarray.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedSizedArrayJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.SizedArrayToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "SizedArray.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>The contract (decision 122): `new int[3]` -> `new Array(3).fill(0)` (C#'s n-defaults array).</summary>
    [Fact]
    public void EmittedSizedArray_LowersSizedNewToArrayFill()
    {
        var js = File.ReadAllText(Generate.SizedArrayToTemp());
        Assert.Contains("new Array(3).fill(0)", js);   // new int[3] -> new Array(3).fill(0)
        Assert.Contains("xs.value = [7, 8, 9, 10]", js);   // reassignment to a literal array
        Assert.DoesNotContain("[unsupported-expression]", js);
    }

    /// <summary>Closed-runtime invariant: a sized array adds NO new runtime primitive — Array/.fill are JS builtins.</summary>
    [Fact]
    public void EmittedSizedArray_OnlyImportsClosedRuntimePrimitives_NoHelper()
    {
        var js = File.ReadAllText(Generate.SizedArrayToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name), $"'{name}' is not a runtime export.");
    }
}
