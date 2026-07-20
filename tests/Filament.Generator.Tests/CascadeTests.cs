using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// CASCADING PARAMETERS (decision 134). Blazor needs a cascading VALUE object because a descendant may
/// be arbitrarily deep and is discovered at render time. Here the whole composition inlines into ONE
/// mount(), so an ancestor's expression is literally in scope where the descendant is emitted: a
/// cascade IS lexical scope, and it costs nothing.
/// </summary>
public class CascadeTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/Cascade.Blazor/App.razor is
    /// alpha-equivalent to the hand-written samples/Cascade/cascade.js. The key is the SPEC and the
    /// REFERENCE; the generator is JUDGED (oracle: BENCH n°53).
    /// </summary>
    [Fact]
    public void Gate_GeneratedCascade_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.CascadeToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.CascadeAnswerKey);
        Assert.True(exit == 0,
            "cascade gate FAILED. Generated module is NOT alpha-equivalent to samples/Cascade/cascade.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// THE CLAIM: the cascade is erased into scope. The child's binding is a live effect on the PARENT's
    /// signal, and no context object, dictionary or wrapper element survives.
    /// </summary>
    [Fact]
    public void EmittedCascade_ResolvesToTheAncestorsExpression()
    {
        var js = File.ReadAllText(Generate.CascadeToTemp());

        Assert.Contains("'depth'", js);                    // the child's element
        Assert.Contains("effect(() => setText(", js);      // a LIVE binding, not a mount-time fold
        Assert.Contains("level.value", js);                // ...on the ancestor's own signal
        Assert.DoesNotContain("CascadingValue", js);
        Assert.DoesNotContain("CascadingParameter", js);
        // <CascadingValue> emits no DOM of its own: only #wrap, #depth and #inc are created.
        Assert.Equal(3, js.Split("document.createElement").Length - 1);
    }

    /// <summary>
    /// A [CascadingParameter] with nothing cascading its type is REFUSED, and refused for THAT — not for
    /// the declaration's shape. Bound to no cascade it would silently hold the type's default and render
    /// it as though it were real data.
    /// </summary>
    [Fact]
    public void CascadingParameterWithNothingInScope_IsRefused()
    {
        var outPath = Path.Combine(RepoPaths.Root, "samples", "Cascade", $".casc-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Gate/CascadingParameter.razor"), outPath);
            Assert.True(exit != 0, "an unbound [CascadingParameter] was COMPILED, not refused");
            Assert.False(File.Exists(outPath));
            Assert.Contains("unbound-cascading-parameter", stderr);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    /// <summary>GENERATOR-ONLY, ZERO HELPER: lexical scope needs no runtime primitive.</summary>
    [Fact]
    public void EmittedCascade_AddsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(Generate.CascadeToTemp());
        Assert.Contains("import { signal, effect, setText, listen, insert }", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedCascadeJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.CascadeToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "Cascade.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
