using Xunit;

namespace Filament.Generator.Tests;

public class LoopsTests
{
    /// <summary>
    /// THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).
    /// samples/Loops/loops.js is the Blazor-faithful reference; the generator is judged.
    /// </summary>
    [Fact]
    public void Gate_GeneratedLoops_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.LoopsToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.LoopsAnswerKey);
        Assert.True(exit == 0,
            "PHASE: loop/switch-statement gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/Loops/loops.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedLoopsJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.LoopsToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "Loops.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract: each of the three statements lowers to its JS namesake. A missing case would
    /// silently drop a construct (spec 10) or refuse the whole module.
    /// </summary>
    [Fact]
    public void EmittedLoops_LowersEachStatementToItsJsNamesake()
    {
        var js = File.ReadAllText(Generate.LoopsToTemp());
        Assert.Contains("while (n.value < 5)", js);        // while loop
        Assert.Contains("switch (n.value)", js);           // switch statement
        Assert.Contains("case 5:", js);                    // constant case label
        Assert.Contains("break;", js);                     // break inside switch
        Assert.Contains("do {", js);                       // do-while loop
        Assert.Contains("} while (n.value < 3);", js);
        Assert.DoesNotContain("[unsupported-statement]", js);
    }

    /// <summary>Closed-runtime invariant: loop/switch statements add NO new runtime primitive (JS keywords).</summary>
    [Fact]
    public void EmittedLoops_OnlyCallsClosedRuntimePrimitives_NoNewExport()
    {
        var js = File.ReadAllText(Generate.LoopsToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name),
                $"'{name}' is not a runtime export. Loop/switch statements must add NO new primitive (JS keywords).");
    }
}
