using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// A FRAGMENT HOLE UNDER A REGION IS A LOCATED REFUSAL, NOT A CRASH (decision 174) — found by PROBING
/// the eleven §3 non-goals ADR 0003 declared closed (register defect B3). Honesty work, not surface.
///
/// `@ChildContent` placed BARE inside a region in the child — `@if (Title == "t") { @ChildContent }` —
/// used to reach the emitter as a fragment with no container and ABORT:
///
///   error FIL-WIRING: a RenderFragment reached the emitter with no container to insert into. … This
///   is the TOOL being broken, not the input.
///
/// exit 1, NO location, no file — on a source Blazor compiles (`Build succeeded. 0 Error(s)`). A crash
/// is a broken tool; a located refusal is honest. The CAPABILITY (a templated fragment re-invoked per
/// render / per item, register defect D6) is BLOCKED on a runtime-freeze decision the owner reserved:
/// both obvious mappings were MEASURED to render wrong DOM, and a faithful one needs list() rows that
/// own N nodes (a runtime change) or a wrapper element Blazor does not render. This slice is only the
/// honest floor — the crash becomes a located FIL0003 — and it does NOT touch the frozen runtime.
///
/// REFUSAL-ONLY: nothing new renders, so there is nothing for a browser to observe. The evidence is
/// the located diagnostic replacing the crash, plus the byte-identity of the wrapped control below,
/// which sits one element away on the SUPPORTED side of the line and must not move.
/// </summary>
public class FragmentRegionTests
{
    /// <summary>
    /// THE CRASH IS GONE, AND IN ITS PLACE A LOCATED FIL0003. The refusal must point at the
    /// @ChildContent inside the child's region — the position the author can fix — not at the parent,
    /// and it must not carry the FIL-WIRING "tool broken" text any more.
    /// </summary>
    [Fact]
    public void BareChildContentInsideARegion_IsRefused_NotCrashed()
    {
        var (exit, stderr) = RefuseFixture("RegionHole");

        Assert.True(exit == 1, $"expected a refusal at exit 1, got {exit}");
        Assert.Contains("RegionHoleCard.razor(9,38): FIL0003: [fragment-under-region]", stderr);
        Assert.DoesNotContain("FIL-WIRING", stderr);
        Assert.DoesNotContain("This is the TOOL being broken", stderr);
    }

    /// <summary>
    /// The message must carry the MECHANISM (a fragment is N nodes and the region re-runs, so there is
    /// no stable container) and BOTH remedies (wrap the hole, or lift it out) — a located code alone
    /// would leave the author no way forward.
    /// </summary>
    [Fact]
    public void TheRefusal_NamesTheMechanismAndBothRemedies()
    {
        var (_, stderr) = RefuseFixture("RegionHole");

        Assert.Contains("an @if branch", stderr);                 // it names WHICH region
        Assert.Contains("N top-level nodes", stderr);             // ...and WHY a hole cannot live there
        Assert.Contains("region re-runs", stderr);
        Assert.Contains("Wrap the hole in one element", stderr);  // remedy one
        Assert.Contains("lift the @ChildContent OUT", stderr);    // remedy two
    }

    /// <summary>
    /// THE CONTROL, ONE ELEMENT AWAY, ON THE SUPPORTED SIDE. Wrapping the hole in an element the CHILD
    /// declares (`@if (Title == "t") { <div id="hole">@ChildContent</div> }`) gives the region's body a
    /// container, and it compiles — the fragment lands inside #hole with the PARENT's binding intact.
    /// The fix touches only the parent-is-null arm this case never reaches, so it must stay unmoved.
    /// </summary>
    [Fact]
    public void AWrappedHoleInsideARegion_StillCompiles()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("Composition/RegionHoleWrapped.razor"));

        Assert.DoesNotContain("fragment-under-region", js);
        Assert.DoesNotContain("FIL-WIRING", js);

        Assert.Contains("_el2.id = 'hole';", js);                              // the element the child owns...
        Assert.Contains("_el3.id = 'body';", js);                             // ...holding the parent's markup
        Assert.Contains("effect(() => setText(_tx0, count.value));", js);     // ...with the parent's binding live
        Assert.Contains("list(_el1, () => ('t' === 't') ? [0] : [], () => 0, ifBody, _if0);", js);

        var hole = js.IndexOf("_el2.id = 'hole';", StringComparison.Ordinal);
        var body = js.IndexOf("_el3.id = 'body';", StringComparison.Ordinal);
        Assert.True(hole >= 0 && body >= 0 && hole < body, "#body must be built inside the child's #hole");
    }

    /// <summary>
    /// GENERATOR-ONLY, ZERO PRIMITIVE. The refusal emits nothing; the wrapped control reuses the region
    /// machinery (decision 162's content region + list()) that already ships, so its import is unchanged
    /// and the runtime firewall stays empty.
    /// </summary>
    [Fact]
    public void TheWrappedControl_AddsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("Composition/RegionHoleWrapped.razor"));
        Assert.Contains("import { signal, effect, setText, listen, insert, list }", js);
    }

    /// <summary>Section 10: the snapshot is the wall against silent generator regressions the
    /// name-blind canon gate cannot see. The wrapped control, pinned to the byte.</summary>
    [Fact]
    public void Snapshot_EmittedWrappedControl_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.ToTempFixture("Composition/RegionHoleWrapped.razor"))
            .Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots",
            "CompositionRegionHoleWrapped.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>Run a refused fixture from Unsupported/Gate and hand back exit code + stderr, asserting
    /// nothing was written. Emitted in-repo so the relative runtime specifier resolves.</summary>
    static (int exit, string stderr) RefuseFixture(string name)
    {
        var outPath = Path.Combine(RepoPaths.Unsupported, $".region-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Gate", name + ".razor"), outPath);
            Assert.False(File.Exists(outPath), "the generator refused AND wrote the module anyway");
            return (exit, stderr);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }
}
