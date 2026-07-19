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

    // A root @{ } code block declaring a LOCAL now COMPILES (decision 109): a local declaration runs ONCE
    // in mount() (where the tree is built), so it is a one-time `const x = 5;`, not "a statement with no
    // place to run". The template read @x resolves to the local. Emitted IN-REPO: the CLI computes the
    // runtime specifier from the output path BEFORE compiling, so a temp output would throw FIL-WIRING.
    [Fact]
    public void RootBareCodeBlock_NowCompiles_ToAOneTimeLocal()
    {
        var outPath = Path.Combine(RepoPaths.Unsupported, $".gen-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(Path.Combine(RepoPaths.Unsupported, "RootCodeBlock.razor"), outPath);
            Assert.True(exit == 0, $"a root @{{ }} local declaration should compile now (decision 109):\n{stderr}");
            var js = File.ReadAllText(outPath);
            Assert.Contains("const x = 5;", js);                        // the one-time local
            Assert.Contains("createTextNode(x)", js);                   // @x reads it (static)
            Assert.DoesNotContain("[unsupported-template-statement]", js);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    // A NON-declaration statement at the root (a bare expression/call) STAYS refused -- it would need a
    // re-render to matter, which a Filament module has no place for.
    [Fact]
    public void RootBareExpressionStatement_IsStillRefused()
    {
        var outPath = Path.Combine(RepoPaths.Unsupported, $".gen-{Guid.NewGuid():N}.js");
        var src = Path.Combine(RepoPaths.Unsupported, $".stmt-{Guid.NewGuid():N}.razor");
        try
        {
            File.WriteAllText(src, "@{ System.Console.WriteLine(1); }\n<p id=\"p\">hi</p>\n");
            var (exit, _, stderr) = Run.Generator(src, outPath);
            Assert.NotEqual(0, exit);
            Assert.Contains("[unsupported-template-statement]", stderr);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); if (File.Exists(src)) File.Delete(src); }
    }
}
