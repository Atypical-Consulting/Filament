using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// BUCKET B, banked by an empirical audit (docs/adr/0002-bucket-b-framework-roadmap.md). Two
/// composition capabilities were assumed to be gaps and turned out ALREADY SUPPORTED — the repo's
/// recurring lesson ("measure, don't pre-defer"): EmitComposition's attribute loop never limited
/// the parameter count, and it inlines a child recursively, so multi-parameter fan-out and nested
/// composition both compile. They were unpinned; these tests pin them. Coverage-widening of #88
/// (static leaf) + #90 (single bound parameter) — same machinery, N params and N levels.
/// </summary>
public class CompositionFanoutTests
{
    /// <summary>
    /// A leaf child receives TWO parameters at once: a STATIC string folds to a text-node literal,
    /// a REACTIVE int wires a live effect on the PARENT's lifted signal across the composition
    /// boundary. Both, from one EmitComposition attribute loop.
    /// </summary>
    [Fact]
    public void MultiParameterComposition_FoldsStaticAndBindsReactive()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("Composition/MultiParam.razor"));

        Assert.Contains("document.createTextNode('hits')", js);          // static Label="hits" folded
        Assert.Contains("effect(() => setText(", js);                    // reactive Count="@n" is a live effect
        Assert.Contains("n.value", js);                                  // ...on the PARENT's signal
        Assert.DoesNotContain("[composition-out-of-subset]", js);
        Assert.DoesNotContain("[bound-parameter]", js);
    }

    /// <summary>
    /// Parent composes Mid, which composes Grand — three levels, all inlined into ONE mount(). The
    /// reactive @V is threaded through each boundary and lands as a single effect on the parent's
    /// signal; no child gets its own mounted instance.
    /// </summary>
    [Fact]
    public void NestedComposition_FlattensThreeLevelsIntoOneMount()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("Composition/Nested.razor"));

        Assert.Contains("'mid'", js);                                    // Mid's root element inlined
        Assert.Contains("'gc'", js);                                     // Grand's root element inlined (level 3)
        Assert.Contains("effect(() => setText(", js);                    // one live binding...
        Assert.Contains("x.value", js);                                  // ...on the parent's signal, threaded through both
        // one mount(), one export — the children did NOT each get their own module.
        Assert.Equal(1, js.Split("export function mount").Length - 1);
        Assert.DoesNotContain("[composition-out-of-subset]", js);
    }

    /// <summary>Section 10: snapshots are the wall against silent generator regressions the
    /// name-blind canon gate cannot see. One per capability.</summary>
    [Theory]
    [InlineData("Composition/MultiParam.razor", "CompositionMultiParam.approved.js")]
    [InlineData("Composition/Nested.razor", "CompositionNested.approved.js")]
    public void Snapshot_EmittedComposition_MatchesApprovedBytes(string fixture, string approvedName)
    {
        var actual = File.ReadAllText(Generate.ToTempFixture(fixture)).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", approvedName);
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
