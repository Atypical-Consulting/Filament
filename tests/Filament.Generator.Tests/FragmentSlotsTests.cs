using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// A FRAGMENT HOLE HAS A NAME, AND A FRAGMENT CAN BE PASSED ON — decision 168, register defects
/// A5/A6/A7, found by PROBING the eleven spec 3 non-goals ADR 0003 declared closed.
///
/// All three came out of ONE field: `Fragment? _fragment`, single and un-named. A child declaring two
/// fragment [Parameter]s had the parent's bare content inlined at EVERY hole; a named slot lost to a
/// same-named sibling .razor; a fragment forwarded two levels was dropped in silence. Each of the
/// three emitted a module at exit 0 with no diagnostic, which is the one failure mode section 10
/// forbids outright.
///
/// The whole set is measured against real Blazor through the DOM-contract oracle (BENCH n°74).
/// </summary>
public class FragmentSlotsTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/FragmentSlots.Blazor is
    /// alpha-equivalent to the hand-written samples/FragmentSlots/fragmentslots.js. The key is the
    /// SPEC and the generator is JUDGED.
    /// </summary>
    [Fact]
    public void Gate_GeneratedFragmentSlots_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.FragmentSlotsToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.FragmentSlotsAnswerKey);
        Assert.True(exit == 0,
            "fragment-slots gate FAILED. Generated module is NOT alpha-equivalent to samples/FragmentSlots/fragmentslots.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    // ---- A5: bare content binds to ChildContent, and to NOTHING else --------------------------

    /// <summary>
    /// THE CLAIM, AND IT IS A COUNT. Card declares Header AND ChildContent; the parent passes bare
    /// content, which Blazor binds to ChildContent alone (its codegen for this source emits
    /// AddAttribute("ChildContent", …) and no Header attribute at all). The un-named fragment could
    /// not miss, so the same span was emitted at BOTH holes — and "the span is there" passes on the
    /// broken compiler too. Counting the occurrences is the only assert that fails before the fix.
    /// </summary>
    [Fact]
    public void BareContent_FillsChildContentOnly_NotEveryHole()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("Composition/TwoHoles.razor"));

        Assert.Equal(1, js.Split(".id = 'mark';").Length - 1);          // exactly ONE #mark element
        Assert.Equal(1, js.Split("effect(() => setText(").Length - 1);  // ...and exactly ONE effect on it
        Assert.Contains(".id = 'head';", js);                           // the unbound hole's container still exists...
        Assert.DoesNotContain("[composition-out-of-subset]", js);
    }

    /// <summary>
    /// The unbound hole emits NOTHING, which is what Blazor does with a null RenderFragment. Read
    /// structurally: #head must be created and then immediately inserted into the card, with no
    /// child of its own between the two statements.
    /// </summary>
    [Fact]
    public void AnUnboundHole_EmitsNothing()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("Composition/TwoHoles.razor"));

        var head = js.IndexOf(".id = 'head';", StringComparison.Ordinal);
        Assert.True(head >= 0, "#head must still be created -- the hole's CONTAINER is the child's own markup");
        var next = js.IndexOf("insert(", head, StringComparison.Ordinal);
        Assert.True(next > head, "#head must be inserted into the card");
        var between = js[(head + ".id = 'head';".Length)..next];
        Assert.DoesNotContain("createElement", between);
        Assert.DoesNotContain("createTextNode", between);
    }

    // ---- A6: the slot name beats the sibling file --------------------------------------------

    /// <summary>
    /// A NAMED SLOT IS THE CHILD'S HOLE, NOT THE SIBLING COMPONENT. SlotCard declares a `Slot`
    /// fragment and Slot.razor exists next door rendering #decoy. Razor's own codegen for this source
    /// is `AddAttribute(3, "Slot", (RenderFragment)…)` with `OpenComponent&lt;…Slot&gt;` NOWHERE.
    /// Sibling-file resolution used to run first and emit the decoy, at exit 0.
    /// </summary>
    [Fact]
    public void ANamedSlot_FillsTheHole_AndTheSameNamedSiblingIsNotEmitted()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("Composition/SlotClash.razor"));

        Assert.Contains(".id = 'body';", js);        // the slot content rendered...
        Assert.DoesNotContain("decoy", js);          // ...and the sibling component did NOT
        Assert.DoesNotContain("DECOY", js);
        Assert.Contains("effect(() => setText(", js);
        Assert.Contains("count.value", js);          // the slot content kept the PARENT's scope
        Assert.DoesNotContain("[composition-out-of-subset]", js);
    }

    /// <summary>
    /// THE SHARPEST EVIDENCE, and the assert that makes A6 a DEFECT rather than a gap. SlotClash and
    /// SlotClashCfact differ ONLY in which parameter the child declares — and that flips the meaning
    /// completely in Blazor: with `ChildContent` instead of `Slot`, Razor really does emit
    /// `OpenComponent&lt;…Slot&gt;` and the decoy IS instantiated. The compiler's output must depend
    /// on that declaration. Before decision 168 the two modules were BYTE-IDENTICAL.
    /// </summary>
    [Fact]
    public void TheEmission_DependsOnTheDeclarationThatDecidesTheMeaning()
    {
        var slot = File.ReadAllText(Generate.ToTempFixture("Composition/SlotClash.razor"));
        var cfact = File.ReadAllText(Generate.ToTempFixture("Composition/SlotClashCfact.razor"));

        Assert.NotEqual(slot, cfact);
        Assert.DoesNotContain("decoy", slot);        // `Slot` is a hole  -> the slot is filled
        Assert.Contains("decoy", cfact);             // `Slot` is a tag   -> the component is opened
    }

    /// <summary>
    /// THE DEPTH BOUNDARY, which is Blazor's rule too: a fragment [Parameter] name matches only the
    /// component's IMMEDIATE children. `&lt;SlotCard&gt;&lt;Slot&gt;&lt;Slot&gt;…` is the hole, then
    /// the component. Before the fix BOTH were the component and the mis-compile SCALED: two decoys
    /// where Blazor renders one.
    /// </summary>
    [Fact]
    public void TheSlotNameMatches_OnlyImmediateChildren()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("Composition/SlotDepth.razor"));

        Assert.Equal(1, js.Split(".id = 'decoy';").Length - 1);
        Assert.Contains(".id = 'body';", js);
    }

    // ---- A7: a fragment forwarded two levels arrives -------------------------------------------

    /// <summary>
    /// A FORWARD IS A SCOPE, NOT A COPY. Fwd.razor renders `&lt;FwdInner&gt;@ChildContent&lt;/FwdInner&gt;`,
    /// so this parent's span crosses TWO composition boundaries. It used to be dropped in TOTAL
    /// silence — exit 0, no diagnostic, #inner built and left empty. The binding assert is the other
    /// half: content that arrived but lost the scope it was WRITTEN in sits at "0" forever.
    /// </summary>
    [Fact]
    public void AFragmentForwardedTwoLevels_ArrivesAndKeepsItsBinding()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("Composition/Forward.razor"));

        Assert.Contains(".id = 'inner';", js);
        Assert.Contains(".id = 'body';", js);
        Assert.Contains("effect(() => setText(", js);
        Assert.Contains("count.value", js);          // ...on the GRANDPARENT's signal
        Assert.DoesNotContain("[composition-out-of-subset]", js);

        // Structure: the forwarded span must be built INSIDE #inner, i.e. after #inner is created
        // and before #inner is inserted into its own parent.
        var inner = js.IndexOf(".id = 'inner';", StringComparison.Ordinal);
        var body = js.IndexOf(".id = 'body';", StringComparison.Ordinal);
        Assert.True(inner < body, "the forwarded content must be emitted under the element that places it");
    }

    /// <summary>The wrapped variant: the MIDDLE level owns an element the grandparent's markup lands
    /// inside. #hold used to be built and left empty.</summary>
    [Fact]
    public void AForwardedFragment_LandsInsideTheWrapperTheMiddleLevelOwns()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("Composition/FwdWrapped.razor"));

        var inner = js.IndexOf(".id = 'inner';", StringComparison.Ordinal);
        var hold = js.IndexOf(".id = 'hold';", StringComparison.Ordinal);
        var body = js.IndexOf(".id = 'body';", StringComparison.Ordinal);
        Assert.True(inner >= 0 && hold >= 0 && body >= 0, "all three elements must be emitted");
        Assert.True(inner < hold && hold < body, "#body must be built under #hold, which is built under #inner");
    }

    // ---- the refusals, which are the OTHER half of the line ------------------------------------

    /// <summary>
    /// THE BOUNDARY OF THE KEYED MAP, and the one a naive dictionary would have got wrong. The child
    /// declares ONE hole and it is not called ChildContent; the parent passes bare content. The
    /// pre-existing gate only fires when a child declares NO fragment parameter at all, so a
    /// key-miss would have DROPPED this silently — and the Blazor project BUILDS (Razor emits the
    /// AddAttribute for a property that does not exist), so nothing upstream would have said a word.
    /// </summary>
    [Fact]
    public void BareContent_WithNoChildContentParameter_IsRefused_NotDropped()
    {
        var stderr = Refused("FragmentBareNoChildContent.razor");

        Assert.Contains("composition-out-of-subset", stderr);
        Assert.Contains("ChildContent", stderr);           // it must name the key that missed
        Assert.Contains("declares: Header", stderr);       // ...and what the child actually offers
    }

    /// <summary>
    /// D9 STAYS REFUSED, deliberately, and this fixture is what keeps it refused: the standard
    /// MULTI-LINE authoring style. Razor discards the whitespace between child content elements;
    /// this compiler materialises whitespace between siblings as a real text node. Emitting would
    /// build a DOM Blazor does not, so it refuses and names both the disagreement and the spelling
    /// that compiles. The source is VALID Blazor (`Build succeeded. 0 Warning(s) 0 Error(s)`) —
    /// this is a deliberate deferral, not a Razor error being echoed.
    /// </summary>
    [Fact]
    public void NamedSlotsSeparatedByWhitespace_AreRefused_BecauseRazorDiscardsIt()
    {
        var stderr = Refused("FragmentSlotWhitespace.razor");

        Assert.Contains("composition-out-of-subset", stderr);
        Assert.Contains("WHITESPACE", stderr);
        Assert.Contains("DISCARDS", stderr);
    }

    /// <summary>
    /// RZ9996 PARITY. Once one slot is named, ALL of the content must be — Razor rejects the same
    /// source with `error RZ9996: Unrecognized child content inside component 'SlottedCard'`.
    /// Admitting a source Blazor does not compile is the mirror image of the divergence this slice
    /// closes.
    /// </summary>
    [Fact]
    public void NamedSlotsMixedWithLooseContent_AreRefused_AsRazorRefusesThem()
    {
        var stderr = Refused("FragmentSlotLoose.razor");

        Assert.Contains("composition-out-of-subset", stderr);
        Assert.Contains("RZ9996", stderr);
    }

    /// <summary>
    /// RZ9997 PARITY. A slot element is the NAME OF A PARAMETER, not an element, so it carries
    /// nothing — `error RZ9997: Unrecognized attribute 'class' on child content element 'Head'`.
    /// </summary>
    [Fact]
    public void AnAttributeOnASlotElement_IsRefused_AsRazorRefusesIt()
    {
        var stderr = Refused("FragmentSlotAttr.razor");

        Assert.Contains("composition-out-of-subset", stderr);
        Assert.Contains("RZ9997", stderr);
    }

    /// <summary>
    /// THE SAME SLOT NAMED TWICE, and the refusal exists because the fact was MEASURED, not assumed:
    /// Razor ACCEPTS this source (`Build succeeded`), emits the parameter assignment twice, and a real
    /// Blazor app renders the LAST one — read in Chrome as `&lt;div id="card"&gt;&lt;span id="h2"&gt;b&lt;/span&gt;&lt;/div&gt;`,
    /// the earlier content silently gone. Reproducing that is one line (the map is keyed), but a rule
    /// whose whole content is "half of what you wrote is ignored" does not ship without going through
    /// the oracle on both shells. The message must therefore state Blazor's behaviour, not invent a
    /// Razor error code.
    /// </summary>
    [Fact]
    public void TheSameSlotNamedTwice_IsRefused_WithWhatBlazorActuallyDoes()
    {
        var stderr = Refused("FragmentSlotTwice.razor");

        Assert.Contains("composition-out-of-subset", stderr);
        Assert.Contains("more than once", stderr);
        Assert.Contains("renders the LAST one", stderr);
        Assert.DoesNotContain("RZ9995", stderr);   // Razor has no such error here — do not claim one
    }

    // ---- invariants ---------------------------------------------------------------------------

    /// <summary>
    /// GENERATOR-ONLY, ZERO HELPER. Naming the holes and forwarding them are decisions taken at
    /// COMPILE time; the emitted module imports exactly what decision 131's did.
    /// </summary>
    [Fact]
    public void EmittedFragmentSlots_AddsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(Generate.FragmentSlotsToTemp());
        Assert.Contains("import { signal, effect, setText, listen, insert }", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Theory]
    [InlineData("Composition/TwoHoles.razor", "CompositionTwoHoles.approved.js")]
    [InlineData("Composition/SlotClash.razor", "CompositionSlotClash.approved.js")]
    [InlineData("Composition/SlotClashCfact.razor", "CompositionSlotClashCfact.approved.js")]
    [InlineData("Composition/SlotDepth.razor", "CompositionSlotDepth.approved.js")]
    [InlineData("Composition/Forward.razor", "CompositionForward.approved.js")]
    [InlineData("Composition/FwdWrapped.razor", "CompositionFwdWrapped.approved.js")]
    public void Snapshot_EmittedFragmentSlotFixture_MatchesApprovedBytes(string fixture, string approvedName)
    {
        var actual = File.ReadAllText(Generate.ToTempFixture(fixture)).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", approvedName);
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>Section 10: the snapshot of the measured baseline itself.</summary>
    [Fact]
    public void Snapshot_EmittedFragmentSlotsJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.FragmentSlotsToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "FragmentSlots.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    // ---- helper -------------------------------------------------------------------------------

    /// <summary>Compile a fixture that MUST be refused, assert no file was written, hand back stderr.</summary>
    static string Refused(string fixture)
    {
        var outPath = Path.Combine(RepoPaths.Unsupported, "Gate", $".gen-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, stdout, stderr) = Run.Generator(Path.Combine(RepoPaths.Unsupported, "Gate", fixture), outPath);

            Assert.True(exit != 0,
                $"{fixture} was COMPILED, not refused. That is the silent mis-compile section 10 forbids.\n" +
                $"stdout:\n{stdout}\nstderr:\n{stderr}\n" +
                (File.Exists(outPath) ? "it emitted:\n" + File.ReadAllText(outPath) : ""));
            Assert.False(File.Exists(outPath), "the generator refused AND wrote the module anyway");
            return stderr;
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }
}
