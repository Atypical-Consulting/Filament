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

    // ---- decision 173: @inherits is a CHAIN, and it says what it did (register A3/A4/C3/C4/C6) --------

    /// <summary>
    /// A4: a THREE-level chain (App : Mid : Grand) lifts the GRANDPARENT's field and method as if written
    /// in the derived. The merge walks the chain base-first; nothing about inheritance survives. Before
    /// decision 173 the base merge tested BaseType once, on the derived, so Grand vanished silently.
    /// </summary>
    [Fact]
    public void ChainThreeLevelsDeep_LiftsTheGrandparentsMembers()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("Inheritance/ChainApp.razor"));

        Assert.Contains("const count = signal(0)", js);              // the GRANDPARENT's field, lifted
        Assert.Contains("count.value++", js);                        // the GRANDPARENT's method, inlined
        Assert.Contains("effect(() => setText(_tx0, count.value))", js);
        Assert.DoesNotContain("ChainMid", js);
        Assert.DoesNotContain("ChainGrand", js);
        Assert.DoesNotContain("this.", js);
    }

    /// <summary>Section 10: the chain's emitted bytes are pinned. A 3-level chain collapses to the SAME
    /// lean module a single-level base does -- inheritance is a compile-time question, not a runtime one.</summary>
    [Fact]
    public void Snapshot_EmittedChainJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.ToTempFixture("Inheritance/ChainApp.razor")).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "InheritsChain.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// A3: a code-behind partial base (Base.razor + Base.razor.cs) is REFUSED with a located diagnostic
    /// that NAMES the .cs half. The sibling .razor satisfies File.Exists, but this compiler reads only the
    /// .razor, so the .cs members would be silently absent. One diagnostic -- and it is the located one,
    /// not a misdirected [unresolved-name] blaming the derived for `count` (register C6).
    /// </summary>
    [Fact]
    public void CodeBehindPartialBase_IsRefused_NamingTheCsHalf()
    {
        var (exit, stderr) = RefuseInheritance("CodeBehind.razor");

        Assert.NotEqual(0, exit);
        Assert.Contains("unsupported-directive", stderr);
        Assert.Contains("CodeBehindBase.razor.cs", stderr);   // it must name what it could not read
        Assert.Contains("code-behind partial", stderr);
        // The located refusal is the SOLE diagnostic: the .razor half is still merged so `count` resolves,
        // which is what keeps the misdirected secondary [unresolved-name] from escaping (register C6).
        Assert.DoesNotContain("[unresolved-name]", stderr);
    }

    /// <summary>
    /// A4: a broken link DEEPER in the chain (App : Mid : MissingGrand) is REFUSED, located at the link's
    /// OWN @inherits (Mid.razor), not vanished and not misdirected onto the derived. The chain walk is what
    /// makes the existing missing-sibling refusal guard every link.
    /// </summary>
    [Fact]
    public void BrokenLinkInChain_IsRefused_LocatedAtTheLink()
    {
        var (exit, stderr) = RefuseInheritance("BrokenChain.razor");

        Assert.NotEqual(0, exit);
        Assert.Contains("unsupported-directive", stderr);
        Assert.Contains("BrokenMid.razor(", stderr);          // located at the LINK, not at BrokenChain
        Assert.Contains("BrokenGrand.razor, which does", stderr);
    }

    /// <summary>
    /// C4: `@inherits ComponentBase` written explicitly is the framework default -- a no-op. Its emitted
    /// module is byte-identical (banner line aside) to the same component with no @inherits at all. Before
    /// decision 173 the unqualified spelling fell into sibling resolution and was refused.
    /// </summary>
    [Fact]
    public void ExplicitComponentBase_IsTheFrameworkDefault_ByteIdenticalToNoInherits()
    {
        var withDirective = Body(File.ReadAllText(Generate.ToTempFixture("Inheritance/ExplicitComponentBase.razor")));
        var without = Body(File.ReadAllText(Generate.ToTempFixture("Inheritance/PlainCounter.razor")));

        Assert.Equal(without, withDirective);
        Assert.Contains("const currentCount = signal(5)", withDirective);
    }

    /// <summary>
    /// C4's other arm: a sibling ComponentBase.razor legally SHADOWS the framework base, so the default
    /// fallback must fire only AFTER the sibling-existence check -- never in place of it. ShadowApp
    /// compiles because the sibling was MERGED; had the fallback wrongly fired, `currentCount` would be
    /// unresolved and it would refuse.
    /// </summary>
    [Fact]
    public void SiblingComponentBaseShadows_AndIsMerged()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("InheritShadow/ShadowApp.razor"));

        Assert.Contains("currentCount", js);        // merged from the sibling ComponentBase.razor
        Assert.DoesNotContain("ComponentBase", js); // the base name does not survive into the module
    }

    /// <summary>The module body with its first line -- the `from <name>.razor` banner -- removed, so two
    /// modules compiled from different filenames can be compared for structural byte-identity.</summary>
    static string Body(string js)
    {
        var normalized = js.Replace("\r\n", "\n");
        var nl = normalized.IndexOf('\n');
        return nl < 0 ? normalized : normalized[(nl + 1)..].TrimEnd();
    }

    /// <summary>Run a refused Inheritance fixture and hand back exit code + stderr, asserting no file was
    /// written. Emitted in-repo so the relative runtime specifier resolves.</summary>
    static (int exit, string stderr) RefuseInheritance(string name)
    {
        var outPath = Path.Combine(RepoPaths.Unsupported, $".inh-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Inheritance", name), outPath);
            Assert.False(File.Exists(outPath), "the generator refused AND wrote the module anyway");
            return (exit, stderr);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }
}
