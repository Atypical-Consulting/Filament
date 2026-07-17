using Xunit;

namespace Filament.Generator.Tests;

public class IfTests
{
    /// <summary>
    /// A plain @if nested in an element compiles to a conditional list(): a 0/1 source over the
    /// condition, a constant key, and a comment anchor. The condition field is lifted to a signal
    /// because a read in an @if condition counts as a template read (else the @if renders once).
    /// </summary>
    [Fact]
    public void PlainIf_CompilesToAConditionalList_AndLiftsTheConditionField()
    {
        var js = Compile(
            """
            <div id="wrap"><button id="t" @onclick="Toggle">t</button>@if (show)
            {
                <span id="msg">hi</span>
            }</div>

            @code {
                private bool show = true;
                void Toggle() { show = !show; }
            }
            """);

        Assert.Contains("const show = signal(true);", js);          // lifted: read by @if condition
        Assert.Contains("document.createComment('')", js);          // the anchor node
        Assert.Contains("() => (show.value) ? [0] : []", js);       // reactive 0/1 source
        Assert.Contains("() => 0,", js);                            // constant key
        Assert.Contains("document.createElement('span')", js);      // the body subtree
        Assert.DoesNotContain("when(", js);                         // no new runtime primitive
        Assert.DoesNotContain("[control-flow-not-yet-implemented]", js);
    }

    /// <summary>Compile an inline .razor from samples/If so the runtime specifier resolves.</summary>
    static string Compile(string razor)
    {
        var dir = Path.Combine(RepoPaths.Root, "samples", "If");
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
