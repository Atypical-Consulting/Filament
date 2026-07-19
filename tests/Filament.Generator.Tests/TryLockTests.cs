using Xunit;

namespace Filament.Generator.Tests;

public class TryLockTests
{
    /// <summary>THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).</summary>
    [Fact]
    public void Gate_GeneratedTryLock_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.TryLockToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.TryLockAnswerKey);
        Assert.True(exit == 0,
            "PHASE: try/catch/throw/lock gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/TryLock/trylock.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedTryLockJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.TryLockToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "TryLock.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract (decision 110): try/catch/finally maps to the JS namesake, `throw new Exception(msg)`
    /// to `throw new Error(msg)`, and `lock (x) { … }` to a BARE block (the lock target is dropped -- JS is
    /// single-threaded). All three are STATEMENTS in a translated method body, never spliced verbatim.
    /// </summary>
    [Fact]
    public void EmittedTryLock_MapsTryThrowLock_ToTheirJsForms()
    {
        var js = File.ReadAllText(Generate.TryLockToTemp());
        Assert.Contains("try {", js);
        Assert.Contains("} catch {", js);
        Assert.Contains("throw new Error('boom');", js);      // Exception(msg) -> Error(msg)
        Assert.Contains("count.value = count.value + 5;", js);// caught -> +5, translated (count is a signal)
        Assert.Contains("count.value = count.value + 1;", js);// lock body -> +1
        Assert.DoesNotContain("lock (this)", js);             // the lock target is dropped, replaced by a bare block
        Assert.DoesNotContain("new System.Exception", js);    // NOT spliced verbatim
        Assert.DoesNotContain("[unsupported-statement]", js);
    }

    /// <summary>Closed-runtime invariant: try/catch/throw/lock add NO new runtime primitive (all are plain JS).</summary>
    [Fact]
    public void EmittedTryLock_OnlyCallsClosedRuntimePrimitives_NoNewExport()
    {
        var js = File.ReadAllText(Generate.TryLockToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name), $"'{name}' is not a runtime export.");
    }
}
