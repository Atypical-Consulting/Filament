using Xunit;

namespace Filament.Generator.Tests;

public class LinqAggregateTests
{
    /// <summary>THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).</summary>
    [Fact]
    public void Gate_GeneratedLinqAggregate_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.LinqAggregateToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.LinqAggregateAnswerKey);
        Assert.True(exit == 0,
            "PHASE: LINQ-aggregate gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/LinqAggregate/linqaggregate.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedLinqAggregateJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.LinqAggregateToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "LinqAggregate.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract (decision 121): the number aggregates over a List map to JS array reductions --
    /// `.Where(x => x > 3).Sum()` -> `.filter(x => x > 3).reduce((a, b) => a + b, 0)`, and `.Max()` -> `Math.max(...arr)`.
    /// </summary>
    [Fact]
    public void EmittedLinqAggregate_LowersSumToReduce_AndMaxToMathMax()
    {
        var js = File.ReadAllText(Generate.LinqAggregateToTemp());
        Assert.Contains("_nums.filter(x => x > 3).reduce((a, b) => a + b, 0)", js);   // Where(...).Sum()
        Assert.Contains("Math.max(..._nums)", js);                                   // Max()
        Assert.DoesNotContain("[unsupported-call]", js);
    }

    /// <summary>Closed-runtime invariant: the aggregates add NO new runtime primitive — reduce/Math are JS builtins.</summary>
    [Fact]
    public void EmittedLinqAggregate_OnlyImportsClosedRuntimePrimitives_NoHelper()
    {
        var js = File.ReadAllText(Generate.LinqAggregateToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name), $"'{name}' is not a runtime export.");
    }
}
