using Xunit;

namespace Filament.Generator.Tests;

public class IfTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): "le JS emis ... est equivalent au JS ecrit a la main",
    /// under decision 51's mechanical definition -- canon(minify(generated)) === canon(minify(key)).
    /// samples/If/if.js is the SPEC and the REFERENCE; the generator is JUDGED. Never edit the
    /// answer key to make this pass except to correct it against the BASELINE (decision 64's move),
    /// which is recorded in if.js's own header, not performed silently here.
    /// </summary>
    [Fact]
    public void Gate_GeneratedIf_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.IfToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.IfAnswerKey);
        Assert.True(exit == 0,
            "PHASE: @if gate FAILED. Generated module is NOT alpha-equivalent to samples/If/if.js.\n" +
            stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// Section 10: snapshots are the only protection against silent generator regressions -- the
    /// canon gate is deliberately blind to naming, so it cannot answer "did the generator change
    /// behind our back". This can.
    /// </summary>
    [Fact]
    public void Snapshot_EmittedIfJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.IfToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "If.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The snapshot is only a regression wall if it pins the actual contract, not merely some
    /// bytes: the comment anchor, the reactive 0/1 source, and the resolved descriptors.
    /// </summary>
    [Fact]
    public void EmittedIf_HonoursTheContract()
    {
        var js = File.ReadAllText(Generate.IfToTemp());
        Assert.Contains("document.createComment('')", js);
        Assert.Contains("() => (show.value) ? [0] : []", js);
        Assert.DoesNotContain("innerHTML", js);
        Assert.DoesNotContain("textContent", js);
        Assert.DoesNotContain("'@onclick'", js);   // descriptors resolved (decision 53)
    }

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
