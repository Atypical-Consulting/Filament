using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// FORMS (decision 138) — &lt;EditForm&gt; + &lt;InputText&gt; + @bind-Value onto a MODEL's property.
///
/// Two things had to be true first, and they are the slice: Razor must RESOLVE the form components (so
/// this compiler reads Blazor's own two-way lowering instead of re-deriving it), and the bound record
/// PROPERTY had to become a signal — which it does because the TEMPLATE's write marks it one, closing
/// decision 104's named deferral.
/// </summary>
public class FormsTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/Forms.Blazor/App.razor is
    /// alpha-equivalent to the hand-written samples/Forms/forms.js (oracle: BENCH n°56).
    /// </summary>
    [Fact]
    public void Gate_GeneratedForms_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.FormsToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.FormsAnswerKey);
        Assert.True(exit == 0,
            "forms gate FAILED. Generated module is NOT alpha-equivalent to samples/Forms/forms.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// THE PREREQUISITE, ASSERTED DIRECTLY: the bound record PROPERTY is a signal. Reactivity is defined
    /// over fields and nothing in @code assigns model.Name — the input is the only writer — so this is
    /// true only because the template's write marks it.
    /// </summary>
    [Fact]
    public void EmittedForms_MakesTheBoundRecordPropertyASignal()
    {
        var js = File.ReadAllText(Generate.FormsToTemp());

        Assert.Contains("const model = { name: signal('') }", js);
        Assert.Contains("model.name.value", js);
    }

    /// <summary>
    /// BOTH DIRECTIONS, and they are separate emissions: a value effect (model → input) and a change
    /// listener (input → model). A form with only the first renders and never accepts input; with only
    /// the second it accepts input and never shows it.
    /// </summary>
    [Fact]
    public void EmittedForms_BindsBothDirections()
    {
        var js = File.ReadAllText(Generate.FormsToTemp());

        Assert.Contains("effect(() => { _el1.value = model.name.value; });", js);
        Assert.Contains("listen(_el1, 'change', (e) => { model.name.value = e.target.value; });", js);
    }

    /// <summary>
    /// preventDefault() IS PART OF THE MAPPING. Without it the browser navigates on submit and the page
    /// reloads — exactly what Blazor's EditForm suppresses — so its absence would be a form that appears
    /// to work once and then throws the app away.
    /// </summary>
    [Fact]
    public void EmittedForms_SubmitsWithoutNavigating()
    {
        var js = File.ReadAllText(Generate.FormsToTemp());

        Assert.Contains("document.createElement('form')", js);
        Assert.Contains("listen(_el0, 'submit'", js);
        Assert.Contains("e.preventDefault();", js);
    }

    /// <summary>
    /// Validation is NOT implemented, and is refused rather than ignored. Ignoring a validator would let
    /// an invalid model submit silently — the wrong answer dressed as the right one.
    /// </summary>
    [Fact]
    public void ValidationComponents_AreRefused_NotIgnored()
    {
        var outPath = Path.Combine(RepoPaths.Root, "samples", "Forms", $".val-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Gate/FormsValidation.razor"), outPath);
            Assert.True(exit != 0, "a validator was COMPILED, not refused");
            Assert.False(File.Exists(outPath));
            Assert.Contains("component-composition", stderr);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    /// <summary>GENERATOR-ONLY, ZERO HELPER: forms reuse the primitives the counter slices already ship.</summary>
    [Fact]
    public void EmittedForms_AddsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(Generate.FormsToTemp());
        Assert.Contains("import { signal, effect, setText, setAttr, listen, insert }", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedFormsJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.FormsToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "Forms.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
