using Xunit;

namespace Filament.Generator.Tests;

public class MoreAttrsTests
{
    /// <summary>THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).</summary>
    [Fact]
    public void Gate_GeneratedMoreAttrs_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.MoreAttrsToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.MoreAttrsAnswerKey);
        Assert.True(exit == 0,
            "PHASE: attribute-allowlist gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/MoreAttrs/moreattrs.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedMoreAttrsJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.MoreAttrsToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "MoreAttrs.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract: boolean `hidden` emits the present/absent ternary; `role`/`style`/`data-count` emit
    /// the composed string setAttr. The data-* name proves the prefix admission.
    /// </summary>
    [Fact]
    public void EmittedMoreAttrs_HonoursBothAllowlists()
    {
        var js = File.ReadAllText(Generate.MoreAttrsToTemp());
        Assert.Contains("setAttr(_el0, 'hidden', hid.value ? '' : null)", js);   // boolean present/absent
        Assert.Contains("setAttr(_el0, 'role', r.value)", js);                   // string name
        Assert.Contains("setAttr(_el0, 'style', st.value)", js);                 // string name
        Assert.Contains("setAttr(_el0, 'data-count', d.value)", js);             // data-* prefix
        Assert.DoesNotContain("[dynamic-attribute]", js);
    }

    /// <summary>Closed-runtime invariant: no new primitive (setAttr already shipped).</summary>
    [Fact]
    public void EmittedMoreAttrs_OnlyCallsClosedRuntimePrimitives_NoNewExport()
    {
        var js = File.ReadAllText(Generate.MoreAttrsToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name), $"'{name}' is not a runtime export.");
    }
}
