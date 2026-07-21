using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// CONTENT REGIONS (decision 162) — a COMPONENT's child content is a CONTAINER like any other, so when
/// it holds template C# it is a REGION (decision 54's reassembly), and it must be emitted from the C#
/// front end rather than by walking `content.Children`.
///
/// THIS IS HONESTY WORK, NOT SURFACE WORK. It closes ONE bug wearing THREE costumes, and both halves
/// of section 10 were violated at once — measured against the committed tree, not theorised:
///
///   &lt;Card&gt;@if (show) { … }&lt;/Card&gt;              CRASH: "error FIL-WIRING: raw template C#
///   &lt;EditForm …&gt;@if (show) { … }&lt;/EditForm&gt;      (if (show) {) reached the emitter", exit 1,
///   &lt;CascadingValue …&gt;@foreach …&lt;/…&gt;            on three sources Blazor compiles.
///
///   &lt;CascadingValue&gt; at the template ROOT       SILENT DROP: exit 0, module written, the content
///                                                built and inserted NOWHERE — a page that renders
///                                                without it and says nothing.
///
/// Found by probing the eleven §3 non-goals ADR 0003 closed. Nothing had ever put control flow inside
/// a component's tags, so no witness could see either one.
/// </summary>
public class ContentRegionTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/ContentRegion.Blazor/App.razor
    /// is alpha-equivalent to the hand-written samples/ContentRegion/contentregion.js. The key is the SPEC
    /// and the REFERENCE; the generator is JUDGED. Its Blazor-faithfulness is what the DOM-contract oracle
    /// measures (baseline/ContentRegion.Blazor vs filament-contentregion-gen, BENCH n°68).
    /// </summary>
    [Fact]
    public void Gate_GeneratedContentRegion_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.ContentRegionToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.ContentRegionAnswerKey);
        Assert.True(exit == 0,
            "content-region gate FAILED. Generated module is NOT alpha-equivalent to samples/ContentRegion/contentregion.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// COSTUME ONE — a RenderFragment's content. The parent's @if is a region against the CHILD's own
    /// &lt;div id="card"&gt;, anchored on the comment the child's element carries. The anchor is what proves
    /// the region was emitted AGAINST THAT CONTAINER rather than flattened somewhere convenient: a
    /// list() with a null anchor owns its container outright, which a card holding a hole does not.
    /// </summary>
    [Fact]
    public void CardsChildContent_IsARegionAgainstTheChildsOwnElement()
    {
        var js = File.ReadAllText(Generate.ContentRegionToTemp());

        Assert.Contains("_el4.id = 'card';", js);                              // the CHILD's element
        Assert.Contains("insert(_el4, _if0);", js);                            // ...carrying the region's anchor
        Assert.Contains("list(_el4, () => (show.value) ? [0] : [], () => 0, ifBody, _if0);", js);
        Assert.Contains("_el5.id = 'body';", js);                              // the parent's markup, INSIDE the region
        Assert.Contains("effect(() => setText(_tx0, count.value));", js);      // ...with the parent's binding intact
    }

    /// <summary>
    /// COSTUME TWO — an &lt;EditForm&gt;'s content. Same branch, different container: the &lt;form&gt; EditForm
    /// lowers to. The @bind lives INSIDE the region, so the region's own create() is what wires it —
    /// a form whose content compiled but whose binding was left behind would still render #name.
    /// </summary>
    [Fact]
    public void EditFormsChildContent_IsARegionAgainstTheForm_WithItsBindWired()
    {
        var js = File.ReadAllText(Generate.ContentRegionToTemp());

        Assert.Contains("const _el6 = document.createElement('form');", js);
        Assert.Contains("insert(_el6, _if1);", js);
        Assert.Contains("list(_el6, () => (show.value) ? [0, 1] : [], (i) => i,", js);
        Assert.Contains("_el7.id = 'name';", js);
        Assert.Contains("effect(() => { _el7.value = model.name.value; });", js);              // the bind, one way
        Assert.Contains("listen(_el7, 'change', (e) => { model.name.value = e.target.value; });", js);  // ...and the other
    }

    /// <summary>
    /// COSTUME THREE — a &lt;CascadingValue&gt;'s content. The cascade emits NO element, so its content is an
    /// ordinary &lt;ul&gt; whose own @foreach is an ordinary list() against it. Nothing of the cascade survives
    /// (decision 134): no wrapper, no context object, and `level` is a plain const.
    /// </summary>
    [Fact]
    public void CascadesChildContent_IsARegionAgainstItsOwnElement_AndTheCascadeItselfIsErased()
    {
        var js = File.ReadAllText(Generate.ContentRegionToTemp());

        Assert.Contains("_el1.id = 'list';", js);
        Assert.Contains("list(_el1, () => items.value, (n) => n, createN, null);", js);
        Assert.Contains("const level = 1;", js);          // cascaded, and it costs a const
        Assert.DoesNotContain("CascadingValue", js);
        Assert.DoesNotContain("CascadingParameter", js);
    }

    /// <summary>
    /// THE SILENT DROP THIS SLICE CLOSED, and the claim a presence check cannot make. A cascade at the
    /// template ROOT has no parent element, so its children belong to `target` — IN SOURCE ORDER, between
    /// the siblings the author wrote around it. Before the fix the guard was `parent is not null`: the
    /// content was built and inserted NOWHERE, at exit 0. Asserting the ORDER of the three attaches is
    /// what separates "it renders" from "it renders the document the source describes".
    /// </summary>
    [Fact]
    public void RootCascadesContent_IsAttachedToTarget_InSourceOrder()
    {
        var js = File.ReadAllText(Generate.ContentRegionToTemp());

        Assert.Contains("insert(target, _el1);", js);     // the cascade's <ul>, attached at all

        var head = js.IndexOf("insert(target, _el0);", StringComparison.Ordinal);   // #head
        var ul = js.IndexOf("insert(target, _el1);", StringComparison.Ordinal);     // #list, the cascade's content
        var tail = js.IndexOf("insert(target, _el3);", StringComparison.Ordinal);   // #tail

        Assert.True(head >= 0 && ul >= 0 && tail >= 0, "one of #head / #list / #tail is never attached to target");
        Assert.True(head < ul && ul < tail,
            "the root cascade's content must be attached BETWEEN its source siblings; " +
            $"observed head@{head}, list@{ul}, tail@{tail}");
    }

    /// <summary>
    /// NO COMPONENT NAME SURVIVES. Composition is a compile-time inline (decisions 88/131/134/138), so a
    /// component's own identity is gone by emission — and a name surviving in the module is the signature
    /// of a component EMITTED AS AN ELEMENT, the silent failure DiagnosticTests' Component.razor pins.
    /// </summary>
    [Fact]
    public void EmittedContentRegion_KeepsNoComponentName()
    {
        var js = File.ReadAllText(Generate.ContentRegionToTemp());

        Assert.DoesNotContain("Card", js);
        Assert.DoesNotContain("EditForm", js);
        Assert.DoesNotContain("InputText", js);
        Assert.DoesNotContain("ChildContent", js);
        Assert.DoesNotContain("RenderFragment", js);
    }

    /// <summary>
    /// GENERATOR-ONLY, ZERO HELPER: every one of the three costumes reuses the branch EmitElement already
    /// took for an element (decision 54) and Compile already took for the root (decision 89). list() has
    /// shipped since the Rows step; nothing new is imported and the runtime firewall stays empty.
    /// </summary>
    [Fact]
    public void EmittedContentRegion_AddsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(Generate.ContentRegionToTemp());
        Assert.Contains("import { signal, effect, batch, setText, setAttr, listen, insert, list }", js);
    }

    /// <summary>
    /// THE BOUNDARY, AND IT IS A REFUSAL RATHER THAN A REORDERING. A &lt;CascadingValue&gt; at the template
    /// ROOT whose content is control flow DIRECTLY has no container for the region's rows: `target` is
    /// one, but a region's rows are laid down with the BINDINGS, which run BEFORE the attach that inserts
    /// the cascade's root-level siblings. `&lt;button/&gt;&lt;CascadingValue&gt;@foreach…&lt;/&gt;&lt;p/&gt;` would render rows,
    /// button, tail — the source order silently rearranged, which is the failure mode this whole slice
    /// exists to close. Refused, located, with the remedy in the message: wrap the content in ONE element
    /// (which is what the measured baseline writes, and it compiles).
    /// </summary>
    [Fact]
    public void RootCascadeHoldingControlFlowDirectly_IsRefused_NotReordered()
    {
        var fixturePath = Path.Combine(RepoPaths.Unsupported, "Gate/CascadeRootFlow.razor");
        var outPath = Path.Combine(RepoPaths.Root, "samples", "ContentRegion", $".casc-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(fixturePath, outPath);

            Assert.True(exit != 0, "a root <CascadingValue> holding control flow directly was COMPILED, not refused");
            Assert.False(File.Exists(outPath), "the generator refused AND wrote the module anyway");
            Assert.Contains("CascadeRootFlow.razor(16,41): FIL0003: [unsupported-cascade]", stderr);
            Assert.Contains("Wrap the content in an element", stderr);   // the message must say how to fix it
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedContentRegionJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.ContentRegionToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "ContentRegion.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
