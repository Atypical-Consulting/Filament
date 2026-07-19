using Xunit;

namespace Filament.Generator.Tests;

public class BindTests
{
    /// <summary>THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).</summary>
    [Fact]
    public void Gate_GeneratedBind_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.BindToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.BindAnswerKey);
        Assert.True(exit == 0,
            "PHASE: @bind gate FAILED. Generated module is NOT alpha-equivalent to samples/Bind/bind.js.\n" +
            stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedBindJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.BindToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "Bind.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract: @bind on a string signal lowers to a reactive value-PROPERTY effect plus a change
    /// listener that writes the signal. Both directions, from one directive.
    /// </summary>
    [Fact]
    public void EmittedBind_LowersToValueEffectAndChangeListener()
    {
        var js = File.ReadAllText(Generate.BindToTemp());
        Assert.Contains("effect(() => { _el0.value = text.value; })", js);            // reactive value binding
        Assert.Contains("listen(_el0, 'change', (e) => { text.value = e.target.value; })", js);  // write-back
        Assert.DoesNotContain("BindConverter", js);                                    // the wrapper is gone
        Assert.DoesNotContain("[dynamic-attribute]", js);
        Assert.DoesNotContain("[unsupported-bind]", js);
    }

    /// <summary>Closed-runtime invariant: @bind adds NO new runtime primitive (effect/listen already ship; .value is a DOM builtin).</summary>
    [Fact]
    public void EmittedBind_OnlyCallsClosedRuntimePrimitives_NoNewExport()
    {
        var js = File.ReadAllText(Generate.BindToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name), $"'{name}' is not a runtime export.");
    }
}
