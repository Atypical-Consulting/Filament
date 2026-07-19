using Xunit;

namespace Filament.Generator.Tests;

public class DivideIntTests
{
    /// <summary>
    /// THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).
    /// samples/DivideInt/divideint.js is the Blazor-faithful reference; the generator is judged.
    /// </summary>
    [Fact]
    public void Gate_GeneratedDivideInt_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.DivideIntToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.DivideIntAnswerKey);
        Assert.True(exit == 0,
            "PHASE: integer-division gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/DivideInt/divideint.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedDivideIntJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.DivideIntToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "DivideInt.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract: integer division lowers to Math.trunc(a / b), NOT a bare `/`. A bare `/` would
    /// render 3.5 where C# renders 3 -- the silently-wrong number spec 10 refuses.
    /// </summary>
    [Fact]
    public void EmittedDivideInt_LowersToMathTrunc_NotBareSlash()
    {
        var js = File.ReadAllText(Generate.DivideIntToTemp());
        Assert.Contains("value.value = Math.trunc(value.value / 2)", js);
        Assert.DoesNotContain("[unsupported-expression]", js);
    }

    /// <summary>Closed-runtime invariant: integer division adds NO new runtime primitive (Math.trunc is a JS builtin).</summary>
    [Fact]
    public void EmittedDivideInt_OnlyCallsClosedRuntimePrimitives_NoNewExport()
    {
        var js = File.ReadAllText(Generate.DivideIntToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name),
                $"'{name}' is not a runtime export. Integer division must add NO new primitive (Math.trunc is a JS builtin).");
    }
}
