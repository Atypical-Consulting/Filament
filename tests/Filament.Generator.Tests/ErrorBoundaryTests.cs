using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// &lt;ErrorBoundary&gt; (decision 164). Blazor needs a component with instance state because it
/// discovers the throwing descendant at RENDER time. Here everything inlines into one mount(), so
/// what survives of a boundary is a LATCH plus the conditional the latch drives — a `signal(null)`
/// and one `list()` over a comment anchor, which is `@if`/`@else`'s own shape (decisions 81/82).
///
/// WHAT IT CATCHES WAS MEASURED FIRST, against the real Blazor renderer and not the documentation
/// (`tools/error-boundary-oracle`, which drives Renderer.HandleExceptionViaErrorBoundary headless —
/// no browser, so it runs where Playwright cannot be installed):
///
///   W1  a throw from an event handler the PARENT owns, inside the boundary   NOT caught by Blazor
///   W2  a throw from a CHILD COMPONENT's handler                                        caught
///   W3  a throw from a CHILD COMPONENT's OnInitialized                                  caught
///   W4  a throw raised while the parent EVALUATES the content                           caught
///
/// W1 is the shape authors expect and Blazor does not catch it, so neither does this. W2/W3 need a
/// stateful child, refused by [composition-out-of-subset], so they are unreachable. W4 is the whole
/// of what a Filament boundary can faithfully catch, and it is what these tests pin.
/// </summary>
public class ErrorBoundaryTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the emitted module is alpha-equivalent to the
    /// hand-written samples/ErrorBoundary/errorboundary.js. The key is the SPEC and the REFERENCE;
    /// the generator is JUDGED, and the key is never edited to make this pass.
    /// </summary>
    [Fact]
    public void Gate_GeneratedErrorBoundary_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.ErrorBoundaryToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.ErrorBoundaryAnswerKey);
        Assert.True(exit == 0,
            "ErrorBoundary gate FAILED. Generated module is NOT alpha-equivalent to "
            + "samples/ErrorBoundary/errorboundary.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// THE CLAIM, IN THE BYTES: a boundary is a latch plus a conditional. No component survives, the
    /// latch is STICKY (`??=`, which is what makes ErrorContent keep showing the FIRST exception the
    /// way Blazor's CurrentException does), and the swap is keyed on the latch being null.
    /// </summary>
    [Fact]
    public void EmittedErrorBoundary_IsALatchAndAConditional()
    {
        var js = File.ReadAllText(Generate.ErrorBoundaryToTemp());

        Assert.Contains("signal(null)", js);                       // the latch — Blazor's CurrentException
        Assert.Contains("document.createComment('')", js);         // positioned among its siblings
        Assert.Contains(".value === null ? [0] : [1]", js);        // content while null, error UI after
        Assert.Contains("??=", js);                                // STICKY: first exception wins
        Assert.DoesNotContain("innerHTML", js);

        // The component itself is ERASED. Asked of the CODE and not of the whole file: the generated
        // header names the source .razor, and matching that would pass for the wrong reason.
        var code = js[js.IndexOf("export function mount", StringComparison.Ordinal)..];
        Assert.DoesNotContain("ErrorBoundary", code);
        Assert.DoesNotContain("ErrorContent", code);
        Assert.DoesNotContain("ChildContent", code);
    }

    /// <summary>
    /// GENERATOR-ONLY, ZERO NEW PRIMITIVE — the answer this slice owed the non-goals register's S16,
    /// the one slice flagged as possibly needing a change to the frozen runtime. It did not need one:
    /// a fragment is N top-level nodes while a list() row owns exactly ONE, and each top-level node
    /// becomes its own leaf KEY in the same list rather than the row contract being widened.
    /// </summary>
    [Fact]
    public void EmittedErrorBoundary_AddsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(Generate.ErrorBoundaryToTemp());
        Assert.Contains("import { signal, insert, list }", js);
    }

    /// <summary>
    /// THE BUILD IS GUARDED INSIDE THE BRANCH FUNCTION, not around it. The throw happens while
    /// list()'s reconcile is calling create(), midway through rebuilding its row array; letting it
    /// escape would corrupt the list. Catching inside keeps create()'s contract — it ALWAYS returns
    /// a node.
    /// </summary>
    [Fact]
    public void EmittedErrorBoundary_GuardsTheBuildAndAlwaysReturnsANode()
    {
        var js = File.ReadAllText(Generate.ErrorBoundaryToTemp());
        Assert.Contains("try {", js);
        Assert.Contains("catch (_e) {", js);
        Assert.Contains("return document.createTextNode('');", js);
    }

    /// <summary>
    /// A THROW ROUTED THROUGH A COMPUTED IS REFUSED, and this is the defect that shaped the slice.
    /// A computed is refreshed by checkDirty() from INSIDE flush() — before the binding that reads
    /// it is entered — so it passes NO guard wrapped around that binding and is re-thrown at the
    /// write site (decision 38). Measured: `flush -> checkDirty -> refresh -> recompute -> throw`,
    /// latch still null. Blazor CATCHES that case, so admitting it would ship a boundary that looks
    /// like a guard and silently is not one — section 10's silent mis-compile, in the one construct
    /// whose entire purpose is to be trustworthy when something goes wrong.
    /// </summary>
    [Fact]
    public void AComputedInsideTheBoundary_IsRefused_NotSilentlyUnguarded()
    {
        AssertRefused("EbComputedContent.razor", "unsupported-boundary", "computed");
    }

    [Fact]
    public void ANestedBoundary_IsRefused() =>
        AssertRefused("EbNested.razor", "unsupported-boundary", "nested");

    [Fact]
    public void ABoundaryAtTheTemplateRoot_IsRefused_NotReordered() =>
        AssertRefused("EbAtRoot.razor", "unsupported-boundary", "ROOT");

    [Fact]
    public void MaximumErrorCount_IsRefused_NotAcceptedAndIgnored() =>
        AssertRefused("EbMaximumErrorCount.razor", "unsupported-boundary", "MaximumErrorCount");

    /// <summary>Blazor renders a bare `@context` as Exception.ToString() — CLR type name, message and
    /// CLR stack — where a JS Error stringifies to "Error: boom". Different text, so refused.</summary>
    [Fact]
    public void ABareContext_IsRefused_BecauseToStringHasNoJsTwin() =>
        AssertRefused("EbBareContext.razor", "unsupported-expression", "@context.Message");

    [Fact]
    public void ContextStackTrace_IsRefused_OnlyMessageHasAJsTwin() =>
        AssertRefused("EbStackTrace.razor", "unsupported-expression", "StackTrace");

    /// <summary>An author member named `context` and the ErrorContent's caught exception are ONE name
    /// in the scope every slot is compiled in, so the boundary would read the member instead of the
    /// exception — silently, and only there. Refused at the author's declaration, which is where a
    /// reader can act on it. Same rule as decision 163's route-parameter collision.</summary>
    [Fact]
    public void AnAuthorMemberNamedContext_IsRefused_NotSilentlyShadowed() =>
        AssertRefused("EbContextCollision.razor", "name-collision", "context");

    /// <summary>
    /// THE SECOND GATE, AND IT IS NOT REDUNDANT WITH THE FIRST. canon decides the BYTES; it cannot
    /// decide what they DO, and every claim decision 164 makes is a claim about behaviour — that the
    /// throw is CAUGHT rather than propagated, that ErrorContent carries the message, that markup
    /// outside the boundary survives. error-boundary-contract runs the emitted module in a DOM and
    /// asks. Each of its steps carries a control that must BREAK it; a step whose control cannot
    /// fail is reported as inapplicable rather than counted as evidence.
    /// </summary>
    [Fact]
    public void Contract_EmittedBytes_ActuallyCatch()
    {
        var outPath = Path.Combine(RepoPaths.Root, "samples", "ErrorBoundary", $".c-{Guid.NewGuid():N}.g.js");
        try
        {
            var (gen, _, genErr) = Run.Generator(RepoPaths.ErrorBoundaryRazor, outPath);
            Assert.True(gen == 0, "the sample did not compile:\n" + genErr);

            var (exit, stdout, stderr) = Run.Node(RepoPaths.ErrorBoundaryContract, outPath);
            Assert.True(exit == 0,
                "ErrorBoundary BEHAVIOURAL contract FAILED — the bytes are alpha-equivalent but do not "
                + "behave as decision 164 claims.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    /// <summary>
    /// THE ORACLE, PINNED. Decision 164's whole mapping rests on what BLAZOR does — in particular on
    /// W1, where a boundary does NOT catch a throw from a handler the parent owns, which is why this
    /// compiler deliberately lets that one propagate too. If a framework update changes any of those
    /// answers, the mapping has to be re-argued rather than silently drifting, and this is what says
    /// so. It hosts the real Renderer, so it needs no browser: it runs where Playwright cannot be
    /// installed, which is the reserve BENCH n°69 disclosed.
    /// </summary>
    [Fact]
    public void Oracle_BlazorStillBehavesAsDecision164Recorded()
    {
        var (exit, stdout, stderr) = Run.DotnetRun(RepoPaths.ErrorBoundaryOracle);
        Assert.True(exit == 0,
            "the Blazor oracle DIVERGED from decision 164's record.\n" + stdout
            + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedErrorBoundaryJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.ErrorBoundaryToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests",
            "Snapshots", "ErrorBoundary.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>A refusal is located, names its reason, and writes NO file — the rule every other
    /// gate in this repo holds (decision 163's `--router` fix is the same rule one level up).</summary>
    static void AssertRefused(string witness, string reason, string mustMention)
    {
        var outPath = Path.Combine(RepoPaths.Root, "samples", "ErrorBoundary", $".eb-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Gate", witness), outPath);
            Assert.True(exit != 0, $"{witness} was COMPILED, not refused");
            Assert.False(File.Exists(outPath), "a refusal wrote a file");
            Assert.Contains(reason, stderr);
            Assert.Contains(mustMention, stderr);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }
}
