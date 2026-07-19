using Xunit;

namespace Filament.Generator.Tests;

public class LambdaHandlerTests
{
    /// <summary>THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).</summary>
    [Fact]
    public void Gate_GeneratedLambdaHandler_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.LambdaHandlerToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.LambdaHandlerAnswerKey);
        Assert.True(exit == 0,
            "PHASE: lambda-handler gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/LambdaHandler/lambdahandler.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedLambdaHandlerJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.LambdaHandlerToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "LambdaHandler.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract: the inline lambda is TRANSLATED (count -> count.value) and emitted as an arrow in
    /// listen(), never spliced. A verbatim splice would leave `() => count++` (a dead button, #68).
    /// </summary>
    [Fact]
    public void EmittedLambdaHandler_TranslatesTheBodyAndEmitsAnArrow()
    {
        var js = File.ReadAllText(Generate.LambdaHandlerToTemp());
        Assert.Contains("listen(_el2, 'click', () => {", js);
        Assert.Contains("count.value++;", js);            // translated body
        Assert.DoesNotContain("() => count++", js);       // NOT spliced verbatim
        Assert.DoesNotContain("[compound-expression]", js);
    }

    /// <summary>Closed-runtime invariant: a lambda handler adds NO new runtime primitive (listen already ships).</summary>
    [Fact]
    public void EmittedLambdaHandler_OnlyCallsClosedRuntimePrimitives_NoNewExport()
    {
        var js = File.ReadAllText(Generate.LambdaHandlerToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name), $"'{name}' is not a runtime export.");
    }
}
