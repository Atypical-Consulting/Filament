using Xunit;

namespace Filament.Generator.Tests;

public class LinqTests
{
    /// <summary>THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).</summary>
    [Fact]
    public void Gate_GeneratedLinq_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.LinqToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.LinqAnswerKey);
        Assert.True(exit == 0,
            "PHASE: LINQ gate FAILED. Generated module is NOT alpha-equivalent to samples/Linq/linq.js.\n" +
            stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedLinqJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.LinqToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "Linq.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract (decision 116): the LINQ chain `_nums.Where(x => x > 0).Count()` lowers to
    /// `_nums.filter(x => x > 0).length` -- Where -> filter, Count -> length, and the predicate lambda is emitted
    /// as a plain arrow (its parameter is an ordinary local). The source List is already a JS array, so no
    /// conversion and no runtime helper are needed.
    /// </summary>
    [Fact]
    public void EmittedLinq_LowersWhereCount_ToFilterLength()
    {
        var js = File.ReadAllText(Generate.LinqToTemp());
        Assert.Contains("_nums.filter(x => x > 0).length", js);   // Where -> filter, Count -> length, lambda translated
        Assert.DoesNotContain(".Where(", js);                     // NOT spliced verbatim
        Assert.DoesNotContain("[unsupported-call]", js);
    }

    /// <summary>Closed-runtime invariant: LINQ adds NO new runtime primitive — it is pure JS array methods.</summary>
    [Fact]
    public void EmittedLinq_OnlyImportsClosedRuntimePrimitives_NoHelper()
    {
        var js = File.ReadAllText(Generate.LinqToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name), $"'{name}' is not a runtime export.");
    }
}
