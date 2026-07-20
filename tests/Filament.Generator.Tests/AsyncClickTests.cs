using Xunit;

namespace Filament.Generator.Tests;

public class AsyncClickTests
{
    /// <summary>THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).</summary>
    [Fact]
    public void Gate_GeneratedAsyncClick_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.AsyncClickToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.AsyncClickAnswerKey);
        Assert.True(exit == 0,
            "PHASE: async gate FAILED. Generated module is NOT alpha-equivalent to samples/AsyncClick/asyncclick.js.\n" +
            stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedAsyncClickJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.AsyncClickToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "AsyncClick.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract (decision 119): an `async Task` handler emits an `async function`/`async () =>`, `await` maps to
    /// JS await, and `Task.Delay(1)` to `new Promise((resolve) => setTimeout(resolve, 1))`.
    /// </summary>
    [Fact]
    public void EmittedAsyncClick_EmitsAnAsyncArrow_AwaitAndPromise()
    {
        var js = File.ReadAllText(Generate.AsyncClickToTemp());
        Assert.Contains("async () => {", js);                                    // async handler arrow
        Assert.Contains("await new Promise((resolve) => setTimeout(resolve, 1))", js);  // await Task.Delay(1)
        Assert.Contains("count.value++", js);                                    // the post-await write
        Assert.DoesNotContain("[unsupported-modifier]", js);
    }

    /// <summary>Closed-runtime invariant: async adds NO new runtime primitive — Promise/setTimeout/await are JS builtins.</summary>
    [Fact]
    public void EmittedAsyncClick_OnlyImportsClosedRuntimePrimitives_NoHelper()
    {
        var js = File.ReadAllText(Generate.AsyncClickToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name), $"'{name}' is not a runtime export.");
    }
}
