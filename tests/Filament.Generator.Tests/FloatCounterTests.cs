using Xunit;

namespace Filament.Generator.Tests;

public class FloatCounterTests
{
    /// <summary>THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).</summary>
    [Fact]
    public void Gate_GeneratedFloatCounter_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.FloatCounterToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.FloatCounterAnswerKey);
        Assert.True(exit == 0,
            "PHASE: float gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/FloatCounter/floatcounter.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedFloatCounterJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.FloatCounterToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "FloatCounter.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract (decision 113): a float literal is Math.fround'd (`0.1f` -> `Math.fround(0.1)`), every float
    /// ARITHMETIC op is wrapped in Math.fround (single-precision rounding at each step), and a float DISPLAY
    /// goes through the emitted __f32 helper -- never the bare double coercion, which would print the double
    /// string ("0.10000000149011612") instead of C#'s float string ("0.1").
    /// </summary>
    [Fact]
    public void EmittedFloatCounter_FroundsArithmetic_AndFormatsTheDisplay()
    {
        var js = File.ReadAllText(Generate.FloatCounterToTemp());
        Assert.Contains("signal(Math.fround(0.1))", js);                         // the 0.1f seed -> frounded
        Assert.Contains("Math.fround(total.value + Math.fround(0.2))", js);      // per-op fround (literal + sum)
        Assert.Contains("function __f32(x)", js);                               // the display formatter is emitted
        Assert.Contains("setText(_tx0, __f32(total.value))", js);               // the display goes through it
        Assert.DoesNotContain("[unsupported-type]", js);
    }

    /// <summary>Closed-runtime invariant: float adds NO new runtime primitive — __f32 is emitted into the module.</summary>
    [Fact]
    public void EmittedFloatCounter_OnlyImportsClosedRuntimePrimitives_HelperIsInline()
    {
        var js = File.ReadAllText(Generate.FloatCounterToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name), $"'{name}' is not a runtime export (the float helper must be inline, not imported).");
    }
}
