using Xunit;

namespace Filament.Generator.Tests;

public class IfElseTests
{
    /// <summary>
    /// An @if / @else if / @else chain compiles to ONE keyed list() whose single item's value is
    /// the active branch index: source is a ternary chain, the key IS the index, and create()
    /// dispatches on it. Every branch condition lifts its field to a signal (read-by-template).
    /// </summary>
    [Fact]
    public void IfElseChain_CompilesToAKeyedList_BranchIndexIsTheKey()
    {
        var js = Compile(
            """
            <div id="wrap"><button id="t" @onclick="Next">t</button>@if (n == 0)
            {
                <span id="a">a</span>
            }
            else if (n == 1)
            {
                <span id="b">b</span>
            }
            else
            {
                <span id="c">c</span>
            }</div>

            @code {
                private int n = 0;
                void Next() { n = (n + 1) % 3; }
            }
            """);

        Assert.Contains("const n = signal(0);", js);                                        // lifted by conditions
        Assert.Contains("document.createComment('')", js);                                  // the anchor
        Assert.Contains("() => (n.value === 0) ? [0] : (n.value === 1) ? [1] : [2]", js);   // ternary source
        Assert.Contains("(i) => i,", js);                                                   // key = branch index
        Assert.Contains("i === 0 ?", js);                                                   // dispatch create
        Assert.Contains("document.createElement('span')", js);                              // branch subtrees
        Assert.DoesNotContain("[else-not-yet-implemented]", js);
        Assert.DoesNotContain("when(", js);                                                 // no new primitive
    }

    /// <summary>Compile an inline .razor from samples/IfElse so the runtime specifier resolves.</summary>
    static string Compile(string razor)
    {
        var dir = Path.Combine(RepoPaths.Root, "samples", "IfElse");
        Directory.CreateDirectory(dir);
        var src = Path.Combine(dir, $".t-{Guid.NewGuid():N}.razor");
        var outPath = Path.Combine(dir, $".t-{Guid.NewGuid():N}.js");
        try
        {
            File.WriteAllText(src, razor);
            var (exit, _, stderr) = Run.Generator(src, outPath);
            Assert.True(exit == 0, $"the generator refused to emit:\n{stderr}");
            return File.ReadAllText(outPath);
        }
        finally
        {
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }
}
