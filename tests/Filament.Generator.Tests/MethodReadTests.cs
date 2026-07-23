using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// A FIELD A TEMPLATE-CALLED METHOD READS IS STILL STATE (register defect A14; the S10 slice).
///
/// Signal promotion needed a TEMPLATE read: decision 67's conjunction lifts a field the template reads
/// AND something assigns. But the template read was only ever counted when the field was NAMED in a
/// slot, an @if condition or a @foreach source -- never when the template CALLED a method that read it.
/// So `<span>@Format()</span>` where `Format()` reads `count` left `count` a plain `let`, the display a
/// one-shot insert, and the click's `count++` moved nothing: a silent frozen render, measured against
/// Blazor (which re-renders after every handler).
///
/// The fix propagates template reads through the phase-3 CALL GRAPH into phase-2 promotion -- exactly
/// the transitivity decision 160 already applies to a computed()'s dependencies, one hop further out.
/// A field a template-called method (transitively) reads is marked read-by-template, and a slot that
/// calls a method reading a signal becomes a live effect. GENERATOR-ONLY; the change is ADDITIVE, so
/// every directly-read witness stays byte-identical.
///
/// Measured browser-free by tools/method-read-oracle: the real ComponentBase + Renderer under bUnit
/// (the interactive analogue of text-format-oracle's HtmlRenderer) vs the emitted module in happy-dom.
/// Both shells: {"n_before":"n=0","d_before":"d=0","n_after":"n=1","d_after":"d=1"} (BENCH n°78).
/// </summary>
public class MethodReadTests
{
    static string Emit(string fixture) => File.ReadAllText(Generate.ToTempFixture("Code/" + fixture + ".razor"));

    /// <summary>
    /// C_methodread: a field read ONLY through `@Format()` and written in the handler now lifts to a
    /// signal, the method reads `.value`, and the slot is a live effect that re-runs on the write.
    /// </summary>
    [Fact]
    public void MethodRead_PromotesTheFieldToASignalAndTheSlotToAnEffect()
    {
        var js = Emit("MethodRead");

        Assert.Contains("const count = signal(0);", js);
        Assert.Contains("return `n=${count.value}`;", js);
        Assert.Contains("effect(() => setText(_tx0, format()));", js);
        Assert.Contains("count.value++;", js);
        // The bug was a plain `let` + a one-shot insert; both must be gone.
        Assert.DoesNotContain("let count", js);
        Assert.DoesNotContain("createTextNode(format())", js);
    }

    /// <summary>
    /// E_asyncmethodread: the async twin -- the write lands in an `await` continuation. Promotion is
    /// decided by read+write, not by where the write sits, so the same signal + effect are emitted and
    /// the handler is an async arrow whose continuation fires the effect.
    /// </summary>
    [Fact]
    public void AsyncMethodRead_PromotesAcrossTheAwaitContinuation()
    {
        var js = Emit("AsyncMethodRead");

        Assert.Contains("const count = signal(0);", js);
        Assert.Contains("effect(() => setText(_tx0, format()));", js);
        Assert.Contains("listen(_el2, 'click', async () => {", js);
        Assert.Contains("await new Promise((resolve) => setTimeout(resolve, 1));", js);
        Assert.Contains("count.value++;", js);
        Assert.DoesNotContain("let count", js);
    }

    /// <summary>
    /// D_asyncnowrite -- THE CONTROL the register names. The template reads `msg` DIRECTLY, and an async
    /// method writes it; this lifted to signal + effect before the slice and must STAY there. The fix
    /// only ever marks MORE fields read, so a directly-read field is untouched.
    /// </summary>
    [Fact]
    public void AsyncNoWrite_DirectReadControl_StaysASignalAndEffect()
    {
        var js = Emit("AsyncNoWrite");

        Assert.Contains("const msg = signal('idle');", js);
        Assert.Contains("effect(() => setText(_tx0, msg.value));", js);
    }

    /// <summary>
    /// THE BOUNDARY the fix does not cross. A template-called method reads `caption`, but `caption` is
    /// never written outside its initialiser -- conjunction 67 is read AND write, so propagating the
    /// read alone must NOT promote it. `caption` stays a plain binding and `@Label()` a one-shot insert.
    /// This proves the slice promotes reachability, not every field a reachable method touches.
    /// </summary>
    [Fact]
    public void MethodReadNoWrite_UnwrittenField_IsNotPromoted()
    {
        var js = Emit("MethodReadNoWrite");

        Assert.Contains("const caption = 'hello';", js);
        Assert.Contains("insert(_el1, document.createTextNode(label()));", js);
        // Never written, so never a signal and never an effect.
        Assert.DoesNotContain("signal(", js);
        Assert.DoesNotContain("effect(", js);
        Assert.DoesNotContain("caption.value", js);
    }

    /// <summary>Section 10: the snapshot is the wall against silent generator regressions the name-blind
    /// canon gate cannot see. Pins the two fix witnesses' emitted bytes.</summary>
    [Theory]
    [InlineData("Code/MethodRead.razor", "MethodRead.approved.js")]
    [InlineData("Code/AsyncMethodRead.razor", "AsyncMethodRead.approved.js")]
    public void Snapshot_EmittedJs_MatchesApprovedBytes(string fixture, string approvedName)
    {
        var actual = File.ReadAllText(Generate.ToTempFixture(fixture)).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", approvedName);
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
