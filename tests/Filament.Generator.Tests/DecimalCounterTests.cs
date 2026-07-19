using Xunit;

namespace Filament.Generator.Tests;

public class DecimalCounterTests
{
    /// <summary>THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).</summary>
    [Fact]
    public void Gate_GeneratedDecimalCounter_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.DecimalCounterToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.DecimalCounterAnswerKey);
        Assert.True(exit == 0,
            "PHASE: decimal gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/DecimalCounter/decimalcounter.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedDecimalCounterJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.DecimalCounterToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "DecimalCounter.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract (decision 114): a decimal literal is a boxed { m: <mantissa>n, s: <scale> } (1.10m keeps its
    /// trailing zero as scale 2 -> { m: 110n, s: 2 }); decimal `+` is __decAdd (never the `+` operator, which
    /// would add two objects to NaN); and a decimal DISPLAY goes through the emitted __decStr formatter. Only the
    /// helpers actually used (__decAdd, __decStr) are emitted -- not the whole library.
    /// </summary>
    [Fact]
    public void EmittedDecimalCounter_BoxesTheDecimal_AndUsesTheDecHelpers()
    {
        var js = File.ReadAllText(Generate.DecimalCounterToTemp());
        Assert.Contains("signal({ m: 110n, s: 2 })", js);                        // 1.10m -> boxed, trailing zero kept
        Assert.Contains("__decAdd(total.value, { m: 105n, s: 2 })", js);         // decimal + -> __decAdd, not `+`
        Assert.Contains("setText(_tx0, __decStr(total.value))", js);            // display -> __decStr
        Assert.Contains("function __decAdd", js);                               // the used helpers are emitted
        Assert.Contains("function __decStr", js);
        Assert.DoesNotContain("function __decMul", js);                         // unused helpers are NOT emitted
        Assert.DoesNotContain("[unsupported-type]", js);
    }

    /// <summary>Closed-runtime invariant: decimal adds NO new runtime primitive — the __dec helpers are inline.</summary>
    [Fact]
    public void EmittedDecimalCounter_OnlyImportsClosedRuntimePrimitives_HelpersAreInline()
    {
        var js = File.ReadAllText(Generate.DecimalCounterToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name), $"'{name}' is not a runtime export (the decimal helpers must be inline).");
    }
}
