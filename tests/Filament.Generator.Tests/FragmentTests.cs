using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// RENDERFRAGMENT / CHILDCONTENT — the STRUCTURAL half of composition (decision 131). #88/#90 passed a
/// VALUE down and #130 passed an EVENT up; this passes MARKUP. It also closed a SILENT MIS-COMPILE:
/// before it, content passed to a composed child was neither emitted nor refused, it was DROPPED at
/// exit 0 (see ContentPassedToAChildThatCannotPlaceIt_IsRefused_NotDropped).
/// </summary>
public class FragmentTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/Fragment.Blazor/App.razor
    /// is alpha-equivalent to the hand-written samples/Fragment/fragment.js. The key is the SPEC and the
    /// REFERENCE; the generator is JUDGED. fragment.js's Blazor-faithfulness is what the DOM-contract
    /// oracle measures (baseline/Fragment.Blazor vs filament-fragment-gen, BENCH n°50).
    /// </summary>
    [Fact]
    public void Gate_GeneratedFragment_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.FragmentToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.FragmentAnswerKey);
        Assert.True(exit == 0,
            "render-fragment gate FAILED. Generated module is NOT alpha-equivalent to samples/Fragment/fragment.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// THE CLAIM: the parent's markup lands inside the CHILD's element, in the position the CHILD chose,
    /// and it keeps the binding it was written with. A fragment that rendered but lost its binding would
    /// still produce #body — and would sit at "0" forever. Asserting the effect is asserting the part
    /// that is easy to get wrong.
    /// </summary>
    [Fact]
    public void EmittedFragment_InlinesTheParentsMarkupIntoTheChild_WithItsBindingIntact()
    {
        var js = File.ReadAllText(Generate.FragmentToTemp());

        Assert.Contains("'card'", js);                    // the child's own element
        Assert.Contains("'body'", js);                    // the PARENT's markup, emitted
        Assert.Contains("effect(() => setText(", js);     // ...still a live binding...
        Assert.Contains("count.value", js);               // ...on the PARENT's signal
        Assert.DoesNotContain("RenderFragment", js);
        Assert.DoesNotContain("ChildContent", js);
        Assert.DoesNotContain("[composition-out-of-subset]", js);
    }

    /// <summary>
    /// ORDER IS THE CHILD'S. Card.razor writes `<h3>@Title</h3>` and THEN `@ChildContent`, so #title is
    /// created before #body. The parent supplies content; it does not choose placement.
    /// </summary>
    [Fact]
    public void EmittedFragment_PlacesTheContentWhereTheChildAskedForIt()
    {
        var js = File.ReadAllText(Generate.FragmentToTemp());
        Assert.True(js.IndexOf("'title'", StringComparison.Ordinal) < js.IndexOf("'body'", StringComparison.Ordinal),
            "the child's own markup must precede the inlined fragment — Card.razor renders @Title before @ChildContent");
    }

    /// <summary>
    /// THE REGRESSION THIS SLICE EXISTS TO PREVENT. Content handed to a child with no RenderFragment has
    /// nowhere to render. This USED TO COMPILE, at exit 0, with the content simply absent from the module
    /// — the silent mis-compile section 10 forbids, and the exact failure the node gate was built for.
    /// </summary>
    [Fact]
    public void ContentPassedToAChildThatCannotPlaceIt_IsRefused_NotDropped()
    {
        var fixturePath = Path.Combine(RepoPaths.Unsupported, "Gate/FragmentUnplaceable.razor");
        var outPath = Path.Combine(RepoPaths.Root, "samples", "Fragment", $".drop-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(fixturePath, outPath);

            Assert.True(exit != 0, "content passed to a child that cannot place it was COMPILED, not refused");
            Assert.False(File.Exists(outPath), "the generator refused AND wrote the module anyway");
            Assert.Contains("composition-out-of-subset", stderr);
            Assert.Contains("RenderFragment", stderr);   // the message must say how to fix it
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    /// <summary>
    /// GENERATOR-ONLY, ZERO HELPER: a fragment is a compile-time splice of one subtree into another's
    /// position, so it needs no runtime primitive to carry or replay it.
    /// </summary>
    [Fact]
    public void EmittedFragment_AddsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(Generate.FragmentToTemp());
        Assert.Contains("import { signal, effect, setText, listen, insert }", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedFragmentJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.FragmentToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "Fragment.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
