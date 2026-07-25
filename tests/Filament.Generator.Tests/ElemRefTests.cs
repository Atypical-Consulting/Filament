using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// @ref (decision 132) — the first of the spec 3 DIRECTIVE-level non-goals to close. Blazor needs an
/// ElementReference because it carries an opaque id across the .NET/JS boundary; a module that IS JS
/// already holds the node, so @ref reduces to deciding what the element's const is CALLED.
/// </summary>
public class ElemRefTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/ElemRef.Blazor/App.razor is
    /// alpha-equivalent to the hand-written samples/ElemRef/elemref.js. The key is the SPEC and the
    /// REFERENCE; the generator is JUDGED (oracle: BENCH n°51).
    /// </summary>
    [Fact]
    public void Gate_GeneratedElemRef_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.ElemRefToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.ElemRefAnswerKey);
        Assert.True(exit == 0,
            "@ref gate FAILED. Generated module is NOT alpha-equivalent to samples/ElemRef/elemref.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// THE CLAIM: the reference IS the element's name. The captured element is emitted into `const box`,
    /// and there is no ElementReference object and no assignment to it anywhere.
    /// </summary>
    [Fact]
    public void EmittedElemRef_NamesTheElementConstAfterTheRef()
    {
        var js = File.ReadAllText(Generate.ElemRefToTemp());

        Assert.Contains("const box = document.createElement('input')", js);
        Assert.Contains("box.focus()", js);
        Assert.DoesNotContain("ElementReference", js);
        Assert.DoesNotContain("const box = ;", js);   // the invalid JS an un-skipped field declaration emitted
        Assert.DoesNotContain("[unsupported-directive]", js);
    }

    /// <summary>
    /// An @ref that names no ElementReference field is REFUSED, and the message names the field to
    /// declare. A capture wired to nothing is a handle that silently refers to no node.
    /// </summary>
    [Fact]
    public void RefNamingNoElementReferenceField_IsRefused()
    {
        var outPath = Path.Combine(RepoPaths.Root, "samples", "ElemRef", $".ref-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(Path.Combine(RepoPaths.Unsupported, "Ref.razor"), outPath);
            Assert.True(exit != 0, "an @ref with no backing field was COMPILED, not refused");
            Assert.False(File.Exists(outPath));
            Assert.Contains("unsupported-directive", stderr);
            Assert.Contains("ElementReference", stderr);   // the message must say what to declare
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    /// <summary>
    /// AN @ref UNDER A @foreach ROW IS REFUSED (decision 168, register defect A8). Found by PROBING the
    /// eleven spec 3 non-goals ADR 0003 declared closed: this shape used to compile at exit 0 and emit
    /// `const row` inside the row's local function while a mount-level handler read a FREE VARIABLE —
    /// measured `ReferenceError: row is not defined`. The refusal is located ON THE CAPTURED NAME.
    /// </summary>
    [Fact]
    public void RefInsideAForeachRow_IsRefused()
    {
        var outPath = Path.Combine(RepoPaths.Unsupported, $".ref-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Gate/RefInRow.razor"), outPath);

            Assert.True(exit != 0, "@ref inside a @foreach row was COMPILED, not refused");
            Assert.False(File.Exists(outPath), "the generator refused AND wrote the module anyway");
            // (31,27) is `row` inside @ref="row" on the row's <li> — the captured NAME, as for Ref.razor.
            Assert.Contains("RefInRow.razor(31,27): FIL0003: [ref-under-region]", stderr);
            Assert.Contains("a @foreach row", stderr);                 // WHICH region, by name
            Assert.Contains("free variable", stderr);                  // WHY, in one phrase
            Assert.Contains("OUTSIDE the @foreach/@if", stderr);       // the remedy, in the message
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    /// <summary>
    /// A REGION IS NOT ONLY A @foreach: the same refusal covers an @if body, whose branch function is
    /// built by the same save/restore idiom (EmitBranchFn). The witness deliberately does NOT name the
    /// element after the field — an `id="box"` element would let the free variable resolve through HTML
    /// named window access and make a broken mapping look like a working one.
    /// </summary>
    [Fact]
    public void RefInsideAnIfBranch_IsRefused()
    {
        var outPath = Path.Combine(RepoPaths.Unsupported, $".ref-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Gate/RefInBranch.razor"), outPath);

            Assert.True(exit != 0, "@ref inside an @if branch was COMPILED, not refused");
            Assert.False(File.Exists(outPath), "the generator refused AND wrote the module anyway");
            // (26,29) is `box` inside @ref="box" on the branch's <input>.
            Assert.Contains("RefInBranch.razor(26,29): FIL0003: [ref-under-region]", stderr);
            Assert.Contains("an @if branch", stderr);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    /// <summary>
    /// THE CONTROL THAT ISOLATES THE REFUSAL, and the workaround the diagnostic names.
    /// Supported/Gate/RefOutsideRow.razor is the refused row fixture with the @ref moved onto an &lt;li&gt;
    /// OUTSIDE the @foreach: same field, same list, same @key, same async handler. It compiles, and the
    /// name lands at MOUNT scope — textually before the row function — which is exactly the property the
    /// refused shape does not have. So the refusal is about the region and about nothing else.
    /// </summary>
    [Fact]
    public void TheSameRefOutsideTheRow_CompilesClean_AndNamesTheElementAtMountScope()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("Gate/RefOutsideRow.razor"));

        var decl = js.IndexOf("const row = document.createElement('li');", StringComparison.Ordinal);
        var rowFn = js.IndexOf("function createR(", StringComparison.Ordinal);
        Assert.True(decl >= 0, "the captured element was not emitted into `const row`");
        Assert.True(rowFn >= 0, "the @foreach did not compile to a row function");
        Assert.True(decl < rowFn, "`const row` was emitted INSIDE the row function, not at mount scope");

        Assert.Contains("await row.focus();", js);
        Assert.DoesNotContain("ref-under-region", js);
        Assert.DoesNotContain("ElementReference", js);
    }

    /// <summary>GENERATOR-ONLY, ZERO HELPER: naming a const needs no runtime primitive. Note this module
    /// imports FEWER primitives than most — it has no signal and no effect at all.</summary>
    [Fact]
    public void EmittedElemRef_AddsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(Generate.ElemRefToTemp());
        Assert.Contains("import { listen, insert }", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedElemRefJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.ElemRefToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "ElemRef.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
