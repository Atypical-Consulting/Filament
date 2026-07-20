using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// @inherits (decision 136). Inheritance is a COMPILE-TIME question about where a member's text lives:
/// the base's members are merged into the derived component's compilation BEFORE state lifting, so an
/// inherited field is lifted exactly as though it had been written in the derived file. A Filament
/// module has no base class, no vtable and no `this`.
/// </summary>
public class InheritsTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/Inherits.Blazor/App.razor is
    /// alpha-equivalent to the hand-written samples/Inherits/inherits.js (oracle: BENCH n°55).
    /// </summary>
    [Fact]
    public void Gate_GeneratedInherits_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.InheritsToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.InheritsAnswerKey);
        Assert.True(exit == 0,
            "@inherits gate FAILED. Generated module is NOT alpha-equivalent to samples/Inherits/inherits.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// THE CLAIM: the BASE's field is lifted to a signal in the DERIVED module, and the BASE's method is
    /// the click handler — with nothing named after the base surviving.
    /// </summary>
    [Fact]
    public void EmittedInherits_LiftsTheBasesMembersAsIfTheyWereLocal()
    {
        var js = File.ReadAllText(Generate.InheritsToTemp());

        Assert.Contains("const count = signal(0)", js);   // the BASE's field, lifted
        Assert.Contains("count.value++", js);             // the BASE's method body, inlined
        Assert.DoesNotContain("CounterBase", js);
        Assert.DoesNotContain("this.", js);
    }

    /// <summary>
    /// A base this compiler cannot READ is refused. The only C# it ever reads is a sibling .razor; a base
    /// in a .cs file would contribute nothing, and inheriting nothing silently would leave the module
    /// missing exactly the state the author put in the base.
    /// </summary>
    [Fact]
    public void InheritsFromANonSiblingBase_IsRefused()
    {
        var outPath = Path.Combine(RepoPaths.Root, "samples", "Inherits", $".inh-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(Path.Combine(RepoPaths.Unsupported, "Inherits.razor"), outPath);
            Assert.True(exit != 0, "@inherits of an unresolvable base was COMPILED, not refused");
            Assert.False(File.Exists(outPath));
            Assert.Contains("unsupported-directive", stderr);
            Assert.Contains(".razor", stderr);   // the message must say what it looked for
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    /// <summary>GENERATOR-ONLY, ZERO HELPER: merged text needs no runtime primitive.</summary>
    [Fact]
    public void EmittedInherits_AddsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(Generate.InheritsToTemp());
        Assert.Contains("import { signal, effect, setText, listen, insert }", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedInheritsJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.InheritsToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "Inherits.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
