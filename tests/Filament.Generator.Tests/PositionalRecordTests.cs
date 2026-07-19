using Xunit;

namespace Filament.Generator.Tests;

public class PositionalRecordTests
{
    /// <summary>THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).</summary>
    [Fact]
    public void Gate_GeneratedPositionalRecord_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.PositionalRecordToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.PositionalRecordAnswerKey);
        Assert.True(exit == 0,
            "PHASE: positional-record gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/PositionalRecord/positionalrecord.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedPositionalRecordJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.PositionalRecordToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "PositionalRecord.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract (decision 111): a positional `record Item(string Name, int Rank)` compiles to the SAME
    /// object literal a body record does, and INLINE construction maps the positional args to the props by
    /// constructor order -- `new Item("alpha", 1)` -> `{ name: 'alpha', rank: 1 }` in the seed list literal,
    /// `new Item("beta", 2)` -> `{ name: 'beta', rank: 2 }` inside the .Add -> push on click. Never spliced.
    /// </summary>
    [Fact]
    public void EmittedPositionalRecord_MapsConstructionToAnObjectLiteral_ByConstructorOrder()
    {
        var js = File.ReadAllText(Generate.PositionalRecordToTemp());
        Assert.Contains("{ name: 'alpha', rank: 1 }", js);   // seed, built inline in the list literal
        Assert.Contains("{ name: 'beta', rank: 2 }", js);    // appended inline in .Add -> push
        Assert.Contains("(item) => item.name", js);          // @key -> keyOf
        Assert.DoesNotContain("new Item", js);               // NOT spliced verbatim
        Assert.DoesNotContain("[unsupported-member]", js);
        Assert.DoesNotContain("[unsupported-expression]", js);
    }

    /// <summary>Closed-runtime invariant: a positional record adds NO new runtime primitive (list already ships).</summary>
    [Fact]
    public void EmittedPositionalRecord_OnlyCallsClosedRuntimePrimitives_NoNewExport()
    {
        var js = File.ReadAllText(Generate.PositionalRecordToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name), $"'{name}' is not a runtime export.");
    }
}
