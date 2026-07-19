using Xunit;

namespace Filament.Generator.Tests;

public class IfNestedTests
{
    /// <summary>
    /// THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).
    /// samples/IfNested/ifnested.js is the Blazor-faithful reference; the generator is judged.
    /// </summary>
    [Fact]
    public void Gate_GeneratedIfNested_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.IfNestedToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.IfNestedAnswerKey);
        Assert.True(exit == 0,
            "PHASE: nested @if gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/IfNested/ifnested.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedIfNestedJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.IfNestedToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "IfNested.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract: a decision-tree source `(show.value) ? ((other.value) ? [0] : []) : []`, an identity
    /// key, and ONE leaf span builder. This is the nested-@if flattening, pinned.
    /// </summary>
    [Fact]
    public void EmittedIfNested_HonoursTheContract()
    {
        var js = File.ReadAllText(Generate.IfNestedToTemp());
        Assert.Contains("document.createComment('')", js);
        Assert.Contains("() => (show.value) ? ((other.value) ? [0] : []) : []", js);  // decision tree
        Assert.Contains("(i) => i", js);                                              // identity key
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(js, @"document\.createElement\('span'\)").Count);
        Assert.DoesNotContain("innerHTML", js);
        Assert.DoesNotContain("[unsupported-if-body]", js);
    }

    /// <summary>Closed-runtime invariant: nested @if adds NO new runtime primitive (reuses list()).</summary>
    [Fact]
    public void EmittedIfNested_OnlyCallsClosedRuntimePrimitives_NoNewExport()
    {
        var js = File.ReadAllText(Generate.IfNestedToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name),
                $"'{name}' is not a runtime export. Nested @if must add NO new primitive (reuse list()).");
        Assert.Contains("document.createComment(''", js);
    }
}
