using Xunit;

namespace Filament.Generator.Tests;

public class IfNestedMixedTests
{
    /// <summary>THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).</summary>
    [Fact]
    public void Gate_GeneratedIfNestedMixed_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.IfNestedMixedToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.IfNestedMixedAnswerKey);
        Assert.True(exit == 0,
            "PHASE: mixed-@if gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/IfNestedMixed/ifnestedmixed.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedIfNestedMixedJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.IfNestedMixedToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "IfNestedMixed.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract (decision 120): a branch mixing markup with a nested @if flattens to ONE list() whose source
    /// SPREADS the nested @if's active indices beside the constant markup leaf. The pure markup-only and
    /// pure-nested cases (#98–#100) are untouched -- this is a THIRD BranchExpr arm.
    /// </summary>
    [Fact]
    public void EmittedIfNestedMixed_SpreadsTheNestedIndicesBesideTheMarkupLeaf()
    {
        var js = File.ReadAllText(Generate.IfNestedMixedToTemp());
        Assert.Contains("(show.value) ? [0, ...((other.value) ? [1] : [])] : []", js);
        Assert.DoesNotContain("[unsupported-if-body]", js);
    }

    /// <summary>Closed-runtime invariant: the mixed @if adds NO new runtime primitive (list() already ships).</summary>
    [Fact]
    public void EmittedIfNestedMixed_OnlyCallsClosedRuntimePrimitives_NoNewExport()
    {
        var js = File.ReadAllText(Generate.IfNestedMixedToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name), $"'{name}' is not a runtime export.");
    }
}
