using Xunit;

namespace Filament.Generator.Tests;

public class LongCounterTests
{
    /// <summary>THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).</summary>
    [Fact]
    public void Gate_GeneratedLongCounter_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.LongCounterToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.LongCounterAnswerKey);
        Assert.True(exit == 0,
            "PHASE: long/BigInt gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/LongCounter/longcounter.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedLongCounterJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.LongCounterToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "LongCounter.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract (decision 112): a `long` field is a BigInt signal, and an integer literal in a long
    /// context is a BigInt literal (`5` -> `5n`). The value 9007199254740990 is emitted verbatim with an `n`
    /// suffix, and the increment is `+ 3n` -- NOT a `number`, which would lose precision the moment the sum
    /// passes 2^53. The DOM coerces the BigInt on setText, so no new runtime primitive is needed.
    /// </summary>
    [Fact]
    public void EmittedLongCounter_UsesBigIntLiterals_NotNumbers()
    {
        var js = File.ReadAllText(Generate.LongCounterToTemp());
        Assert.Contains("signal(9007199254740990n)", js);          // the long seed -> a BigInt signal
        Assert.Contains("total.value = total.value + 3n", js);     // the `3` in a long context -> `3n`
        Assert.DoesNotContain("9007199254740990)", js);            // NOT a bare number seed (would be lossy)
        Assert.DoesNotContain("[unsupported-type]", js);
    }

    /// <summary>Closed-runtime invariant: long adds NO new runtime primitive — the DOM coerces the BigInt itself.</summary>
    [Fact]
    public void EmittedLongCounter_OnlyCallsClosedRuntimePrimitives_NoNewExport()
    {
        var js = File.ReadAllText(Generate.LongCounterToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name), $"'{name}' is not a runtime export.");
    }
}
