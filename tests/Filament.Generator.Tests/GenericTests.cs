using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// GENERIC COMPONENTS — @typeparam (decision 135). Generics erase, and they erase for free: a type
/// parameter is a COMPILE-TIME constraint, and this compiler resolves every composition at compile time
/// into a scope where the value is the parent's own expression. There is not even monomorphisation to
/// do, because the child is INLINED at each use site.
/// </summary>
public class GenericTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/Generic.Blazor/App.razor is
    /// alpha-equivalent to the hand-written samples/Generic/generic.js (oracle: BENCH n°54).
    /// </summary>
    [Fact]
    public void Gate_GeneratedGeneric_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.GenericToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.GenericAnswerKey);
        Assert.True(exit == 0,
            "generics gate FAILED. Generated module is NOT alpha-equivalent to samples/Generic/generic.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// THE CLAIM: the type is gone and the BINDING survives. An erased generic that folded its value at
    /// mount would render "1" forever — which is why the effect, not just the element, is asserted.
    /// </summary>
    [Fact]
    public void EmittedGeneric_ErasesTheTypeAndKeepsTheBindingLive()
    {
        var js = File.ReadAllText(Generate.GenericToTemp());

        Assert.Contains("effect(() => setText(", js);
        Assert.Contains("count.value", js);
        Assert.DoesNotContain("typeparam", js);
        Assert.DoesNotContain("<T>", js);
        // Inlined at the use site, so there is exactly ONE mount() and no per-type instantiation.
        Assert.Equal(1, js.Split("export function mount").Length - 1);
    }

    /// <summary>
    /// A generic parameter that no parent bound REACTIVELY is refused. A type parameter carries no type
    /// of its own; what makes it faithful is that the PARENT's expression is already type-correct
    /// (decision 90's exemption), so with no such parent there is nothing to substitute.
    /// </summary>
    [Fact]
    public void UnboundGenericParameter_IsRefused()
    {
        var outPath = Path.Combine(RepoPaths.Root, "samples", "Generic", $".gen-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Gate/GenericParamUnbound.razor"), outPath);
            Assert.True(exit != 0, "an unbound generic parameter was COMPILED, not refused");
            Assert.False(File.Exists(outPath));
            Assert.Contains("unbound-generic-parameter", stderr);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    /// <summary>GENERATOR-ONLY, ZERO HELPER: an erased type needs no runtime primitive.</summary>
    [Fact]
    public void EmittedGeneric_AddsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(Generate.GenericToTemp());
        Assert.Contains("import { signal, effect, setText, listen, insert }", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedGenericJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.GenericToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "Generic.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
