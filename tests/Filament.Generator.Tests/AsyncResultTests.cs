using Xunit;

namespace Filament.Generator.Tests;

public class AsyncResultTests
{
    /// <summary>THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).</summary>
    [Fact]
    public void Gate_GeneratedAsyncResult_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.AsyncResultToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.AsyncResultAnswerKey);
        Assert.True(exit == 0,
            "PHASE: async-Task<T> gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/AsyncResult/asyncresult.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedAsyncResultJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.AsyncResultToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "AsyncResult.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract (decision 123): an `async Task<int>` method emits an `async function` that RETURNS its value,
    /// and `count = await Compute()` becomes `count.value = await compute()` (await unwraps the Promise).
    /// </summary>
    [Fact]
    public void EmittedAsyncResult_EmitsAnAsyncFunctionReturningAValue_AndAwaitsIt()
    {
        var js = File.ReadAllText(Generate.AsyncResultToTemp());
        Assert.Contains("async function compute()", js);   // async Task<int> -> async function
        Assert.Contains("return count.value + 42", js);    // returns a value
        Assert.Contains("count.value = await compute()", js);   // await unwraps it
        Assert.DoesNotContain("[unsupported-type]", js);
    }

    /// <summary>Closed-runtime invariant: async Task<T> adds NO new runtime primitive — all JS builtins.</summary>
    [Fact]
    public void EmittedAsyncResult_OnlyImportsClosedRuntimePrimitives_NoHelper()
    {
        var js = File.ReadAllText(Generate.AsyncResultToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name), $"'{name}' is not a runtime export.");
    }
}
