using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// THE SUBMIT CONTRACT (decision 165) — a submit event with a registered handler has its browser
/// default suppressed, ALWAYS.
///
/// THIS IS HONESTY WORK, NOT SURFACE WORK. It closes TWO class-A silent divergences found by PROBING
/// the eleven §3 non-goals ADR 0003 declared closed — measured against the committed tree, both at
/// exit 0 with no diagnostic, on sources Blazor compiles and runs without moving:
///
///   &lt;form @onsubmit="Save"&gt;                  emitted `listen(_el0,'submit',() =&gt; {…})` — no
///                                              preventDefault, so the browser NAVIGATED  (register A1)
///
///   &lt;EditForm Model&gt; with no OnValidSubmit    emitted NO submit listener at all — same reload,
///                                              same thrown-away application            (register A2)
///
/// Blazor's authority is one line of its own shipped dispatcher: `_={submit:!0}` with
/// `hasOwnProperty.call(_,t.type)&amp;&amp;t.preventDefault()`. A table with ONE entry, keyed on the EVENT
/// TYPE, reached whenever a handler is registered — no `:preventDefault` modifier, no dependence on
/// which component owns the form, no dependence on a callback existing. Filament had read that table
/// inside EmitEditForm only.
///
/// A reload silently resets everything, which is why the browser measurement (BENCH n°71) plants a
/// marker on `window` before each click and asserts it SURVIVES: a DOM assert alone would pass while
/// the page was already navigating.
/// </summary>
public class SubmitTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/Submit.Blazor/App.razor is
    /// alpha-equivalent to the hand-written samples/Submit/submit.js. The key is the SPEC and the
    /// REFERENCE; the generator is JUDGED. Its Blazor-faithfulness is what the DOM-contract oracle
    /// measures (baseline/Submit.Blazor vs filament-submit-gen, BENCH n°71).
    /// </summary>
    [Fact]
    public void Gate_GeneratedSubmit_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.SubmitToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.SubmitAnswerKey);
        Assert.True(exit == 0,
            "submit gate FAILED. Generated module is NOT alpha-equivalent to samples/Submit/submit.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// REGISTER DEFECT A1, PINNED. A plain &lt;form @onsubmit="Save"&gt; — no framework component anywhere —
    /// suppresses the default before it runs the author's statement. The emitted arrow now TAKES the
    /// event, which the previous one did not, so the shape of the listener is itself the evidence.
    /// </summary>
    [Fact]
    public void PlainFormSubmit_SuppressesTheBrowsersDefault()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("Gate/PlainFormSubmit.razor"));

        Assert.Contains("document.createElement('form')", js);
        Assert.Contains("listen(_el0, 'submit', (e) => {\n    e.preventDefault();\n    saved.value = name.value;\n  });", js);
    }

    /// <summary>
    /// REGISTER DEFECT A2, PINNED. An &lt;EditForm Model&gt; with NO OnValidSubmit emitted no submit listener
    /// at all; the only listener in the whole module was the input's change. Blazor's EditForm wires
    /// `onsubmit` unconditionally in its own render tree — the callback only decides what runs AFTER the
    /// default is dead — so the listener is unconditional here too, and with no callback its entire body
    /// IS the suppression.
    ///
    /// The @bind-Value is asserted alongside it on purpose: the value a reload destroys is the one the
    /// author just typed, and this fixture keeps a live one across the submit.
    /// </summary>
    [Fact]
    public void CallbacklessEditForm_StillRegistersItsSubmit()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("Gate/FormNoCallback.razor"));

        Assert.Contains("listen(_el0, 'submit', (e) => { e.preventDefault(); });", js);
        Assert.Contains("listen(_el1, 'change', (e) => { model.name.value = e.target.value; });", js);
        Assert.DoesNotContain("EditForm", js);
    }

    /// <summary>
    /// THE SECOND EMISSION PATH. An inline lambda handler (decision 105) is emitted DURING the template
    /// walk; a @code method name is RECORDED and rendered after it. Two sites, no shared line — so the
    /// rule was applied on neither, and repairing only the recorded one would have left
    /// `&lt;form @onsubmit="() =&gt; …"&gt;` navigating while its neighbour did not. They share SuppressingArrow.
    /// </summary>
    [Fact]
    public void LambdaSubmitHandler_SuppressesTheBrowsersDefault()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("Gate/LambdaFormSubmit.razor"));

        Assert.Contains("listen(_el0, 'submit', (e) => {\n    e.preventDefault();\n    hits.value++;\n  });", js);
    }

    /// <summary>
    /// THE KEY IS THE EVENT, NOT THE ELEMENT — and this fixture is the reason the distinction is not
    /// cosmetic. ONE &lt;form&gt; carries @onsubmit AND @onkeydown, so both listeners land on _el0. The rule
    /// began as a set of ELEMENT names; adding this element to it would have suppressed the KEYDOWN as
    /// well (Blazor's table is submit-only) and handed the keydown arrow to a method that DECLARED a
    /// KeyboardEventArgs while passing it nothing — `e.Key` reading undefined, rendering fine, comparing
    /// against nothing. Blazor keys on the event type; so does this.
    ///
    /// Asserted as a CONTRAST on the same element: one arrow suppresses, the other is byte-for-byte the
    /// arrow decision 159 already emitted.
    /// </summary>
    [Fact]
    public void SubmitAndKeydownOnOneElement_SuppressOnlyTheSubmit()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("Gate/SubmitPlusKeydown.razor"));

        Assert.Contains("listen(_el0, 'submit', (e) => {\n    e.preventDefault();\n    saved.value = name.value;\n  });", js);
        Assert.Contains("listen(_el0, 'keydown', (e) => {\n    lastKey.value = e.key;\n  });", js);
        // Exactly one suppression in the module — the keydown's arrow must not have grown one.
        Assert.Equal(1, js.Split("e.preventDefault();").Length - 1);
    }

    /// <summary>
    /// THE BOUNDARY. Now that a callback-less &lt;EditForm&gt; registers its submit, OnSubmit looks like it
    /// ought to be the callback for it — and it is not the same callback. Blazor's OnSubmit means "I will
    /// handle EVERY submit myself, validation included", so admitting it as an alias for OnValidSubmit
    /// would silently answer a different question than the author asked. Refused, located, naming the
    /// parameter that IS in the subset.
    /// </summary>
    [Fact]
    public void EditFormOnSubmit_IsRefused_NotAliasedToOnValidSubmit()
    {
        var outPath = Path.Combine(RepoPaths.Root, "samples", "Submit", $".onsubmit-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Gate/FormOnSubmit.razor"), outPath);

            Assert.True(exit != 0, "<EditForm OnSubmit> was COMPILED, not refused");
            Assert.False(File.Exists(outPath), "the generator refused AND wrote the module anyway");
            Assert.Contains("FormOnSubmit.razor(11,1): FIL0003: [unsupported-form]", stderr);
            Assert.Contains("Only OnValidSubmit is", stderr);   // the message must name what IS supported
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    /// <summary>
    /// GENERATOR-ONLY, ZERO HELPER. preventDefault() is a DOM method; suppressing a default costs the
    /// runtime nothing, and listen() has shipped since the counter. The firewall
    /// (`git diff -- src/filament-runtime`) is empty for this slice and the runtime stays at 1,943 B.
    /// </summary>
    [Fact]
    public void EmittedSubmit_AddsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(Generate.SubmitToTemp());
        Assert.Contains("import { signal, effect, setText, setAttr, listen, insert }", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedSubmitJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.SubmitToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "Submit.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
