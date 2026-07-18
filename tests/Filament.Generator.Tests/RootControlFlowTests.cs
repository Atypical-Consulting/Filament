using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// #77's THIRD and last disclosed false positive: @foreach/@if at the template ROOT.
/// The mapping (decided by the old refusal's own message) is "a root region attaches to
/// mount()'s target". These prove the emission SHAPE; RootForeachTests/RootIfTests MEASURE
/// it against Blazor via the DOM-contract oracle (decision 89, BENCH n°11).
/// </summary>
public class RootControlFlowTests
{
    // A root @foreach compiles to list() whose PARENT is target (not a created element).
    [Fact]
    public void RootForeach_AttachesTheListToTarget()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("RootForeachInline.razor"));
        Assert.Contains("list(target,", js);
        Assert.DoesNotContain("[template-code-at-root]", js);
    }

    // A root @if compiles to a comment anchor inserted INTO target and a conditional list(target, ...).
    [Fact]
    public void RootIf_AnchorsAndListsAgainstTarget()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("RootIfInline.razor"));
        Assert.Contains("insert(target,", js);       // the anchor lands in target
        Assert.Contains("list(target,", js);          // the conditional attaches to target
        Assert.DoesNotContain("[template-code-at-root]", js);
    }

    // Root C# that is NOT control flow is STILL refused -- now by RegionOps' shared re-parse,
    // with a more specific message than the old blanket root-code guard. Emitted IN-REPO: the
    // CLI computes the runtime specifier from the output path BEFORE compiling, so a temp output
    // would throw FIL-WIRING (bad specifier) and mask the real refusal.
    [Fact]
    public void RootBareCodeBlock_IsStillRefused_ButAsUnsupportedStatement()
    {
        var outPath = Path.Combine(RepoPaths.Unsupported, $".gen-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(Path.Combine(RepoPaths.Unsupported, "RootCodeBlock.razor"), outPath);
            Assert.NotEqual(0, exit);
            Assert.Contains("[unsupported-template-statement]", stderr);
            Assert.DoesNotContain("[template-code-at-root]", stderr);   // the old guard is gone
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }
}
