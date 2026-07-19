using Xunit;

namespace Filament.Generator.Tests;

public class IfMultiBodyTests
{
    /// <summary>
    /// THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).
    /// samples/IfMultiBody/ifmulti.js is the Blazor-faithful reference; the generator is judged.
    /// </summary>
    [Fact]
    public void Gate_GeneratedIfMultiBody_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.IfMultiBodyToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.IfMultiBodyAnswerKey);
        Assert.True(exit == 0,
            "PHASE: @if multi-node body gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/IfMultiBody/ifmulti.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedIfMultiBodyJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.IfMultiBodyToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "IfMultiBody.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract: a comment anchor, a reactive [0, 1] source over the condition, an identity key,
    /// and TWO span subtrees. This is the multi-node lowering, pinned.
    /// </summary>
    [Fact]
    public void EmittedIfMultiBody_HonoursTheContract()
    {
        var js = File.ReadAllText(Generate.IfMultiBodyToTemp());
        Assert.Contains("document.createComment('')", js);
        Assert.Contains("() => (show.value) ? [0, 1] : []", js);   // one item per body node
        Assert.Contains("(i) => i", js);                            // identity key
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(js, @"document\.createElement\('span'\)").Count);
        Assert.DoesNotContain("innerHTML", js);
        Assert.DoesNotContain("[unsupported-if-body]", js);
    }

    /// <summary>Closed-runtime invariant: multi-node @if adds NO new runtime primitive (reuses list()).</summary>
    [Fact]
    public void EmittedIfMultiBody_OnlyCallsClosedRuntimePrimitives_NoNewExport()
    {
        var js = File.ReadAllText(Generate.IfMultiBodyToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name),
                $"'{name}' is not a runtime export. Multi-node @if must add NO new primitive (reuse list()).");
        Assert.Contains("document.createComment(''", js);
    }
}
