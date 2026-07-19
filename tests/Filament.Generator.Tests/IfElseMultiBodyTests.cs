using Xunit;

namespace Filament.Generator.Tests;

public class IfElseMultiBodyTests
{
    /// <summary>
    /// THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).
    /// samples/IfElseMultiBody/ifelsemulti.js is the Blazor-faithful reference; the generator is judged.
    /// </summary>
    [Fact]
    public void Gate_GeneratedIfElseMultiBody_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.IfElseMultiBodyToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.IfElseMultiBodyAnswerKey);
        Assert.True(exit == 0,
            "PHASE: @if/@else multi-node body gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/IfElseMultiBody/ifelsemulti.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedIfElseMultiBodyJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.IfElseMultiBodyToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "IfElseMultiBody.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract: the @if branch is the range [0], the @else branch is the range [1, 2], keyed by
    /// identity, over THREE span subtrees. This is the global-index range lowering, pinned.
    /// </summary>
    [Fact]
    public void EmittedIfElseMultiBody_HonoursTheContract()
    {
        var js = File.ReadAllText(Generate.IfElseMultiBodyToTemp());
        Assert.Contains("document.createComment('')", js);
        Assert.Contains("() => (show.value) ? [0] : [1, 2]", js);   // branch ranges: [0] then [1, 2]
        Assert.Contains("(i) => i", js);                            // identity key
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(js, @"document\.createElement\('span'\)").Count);
        Assert.DoesNotContain("innerHTML", js);
        Assert.DoesNotContain("[unsupported-if-body]", js);
    }

    /// <summary>Closed-runtime invariant: multi-node @else adds NO new runtime primitive (reuses list()).</summary>
    [Fact]
    public void EmittedIfElseMultiBody_OnlyCallsClosedRuntimePrimitives_NoNewExport()
    {
        var js = File.ReadAllText(Generate.IfElseMultiBodyToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name),
                $"'{name}' is not a runtime export. Multi-node @else must add NO new primitive (reuse list()).");
        Assert.Contains("document.createComment(''", js);
    }
}
