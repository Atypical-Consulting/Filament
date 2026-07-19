using Xunit;

namespace Filament.Generator.Tests;

public class DateTimeCounterTests
{
    /// <summary>THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).</summary>
    [Fact]
    public void Gate_GeneratedDateTimeCounter_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.DateTimeCounterToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.DateTimeCounterAnswerKey);
        Assert.True(exit == 0,
            "PHASE: DateTime gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/DateTimeCounter/datetimecounter.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedDateTimeCounterJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.DateTimeCounterToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "DateTimeCounter.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract (decision 115): `new DateTime(2026, 7, 20)` is computed from its constant args to the exact
    /// tick BigInt at generate-time; `.AddDays(5)` adds 5·TicksPerDay (4320000000000) as tick arithmetic; and a
    /// DateTime display goes through the emitted __dtStr formatter -- never a bare BigInt coercion (which would
    /// print the raw tick number). No runtime Date math for construction; the __dtStr helper is inline.
    /// </summary>
    [Fact]
    public void EmittedDateTimeCounter_ComputesTicks_AndFormatsThroughDtStr()
    {
        var js = File.ReadAllText(Generate.DateTimeCounterToTemp());
        Assert.Contains("signal(639201024000000000n)", js);           // new DateTime(2026,7,20) -> exact ticks
        Assert.Contains("when.value + 4320000000000n", js);           // AddDays(5) -> 5 * TicksPerDay ticks
        Assert.Contains("function __dtStr(t)", js);                   // the formatter is emitted
        Assert.Contains("setText(_tx0, __dtStr(when.value))", js);    // display goes through it
        Assert.DoesNotContain("[unsupported-type]", js);
    }

    /// <summary>Closed-runtime invariant: DateTime adds NO new runtime primitive — __dtStr is emitted into the module.</summary>
    [Fact]
    public void EmittedDateTimeCounter_OnlyImportsClosedRuntimePrimitives_HelperIsInline()
    {
        var js = File.ReadAllText(Generate.DateTimeCounterToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name), $"'{name}' is not a runtime export (the DateTime formatter must be inline).");
    }
}
