using Xunit;

namespace Filament.Generator.Tests;

public class LinqOrderTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/LinqOrder.Blazor/App.razor is
    /// alpha-equivalent to the hand-written samples/LinqOrder/linqorder.js. The key is the SPEC and the REFERENCE;
    /// the generator is JUDGED. linqorder.js's Blazor-faithfulness is what the DOM-contract oracle measures
    /// (baseline/LinqOrder.Blazor rendered vs filament-linqorder-gen rendered, BENCH n°45).
    /// </summary>
    [Fact]
    public void Gate_GeneratedLinqOrder_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.LinqOrderToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.LinqOrderAnswerKey);
        Assert.True(exit == 0,
            "LINQ-ordering gate FAILED. Generated module is NOT alpha-equivalent to samples/LinqOrder/linqorder.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// OrderBy sorts a COPY by the numeric key selector (stable, ascending), OrderByDescending flips the operands,
    /// Skip/Take are slices, First/Last are index terminals -- the whole of decision 126's emission.
    /// </summary>
    [Fact]
    public void EmittedLinqOrder_SortsACopyAndSlices()
    {
        var js = File.ReadAllText(Generate.LinqOrderToTemp());
        Assert.Contains("[..._nums].sort((__a, __b) => (x => x)(__a) - (x => x)(__b)).slice(1)[0]", js);
        Assert.Contains("[..._nums].sort((__a, __b) => (x => x)(__b) - (x => x)(__a)).slice(0, 2).at(-1)", js);
    }

    /// <summary>The runtime is untouched: sort/slice/spread are JS builtins, only pre-existing primitives imported.</summary>
    [Fact]
    public void EmittedLinqOrder_AddsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(Generate.LinqOrderToTemp());
        Assert.Contains("import { signal, effect, batch, setText, listen, insert }", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedLinqOrderJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.LinqOrderToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "LinqOrder.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
