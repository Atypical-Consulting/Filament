using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// @ref (decision 132) — the first of the spec 3 DIRECTIVE-level non-goals to close. Blazor needs an
/// ElementReference because it carries an opaque id across the .NET/JS boundary; a module that IS JS
/// already holds the node, so @ref reduces to deciding what the element's const is CALLED.
/// </summary>
public class ElemRefTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/ElemRef.Blazor/App.razor is
    /// alpha-equivalent to the hand-written samples/ElemRef/elemref.js. The key is the SPEC and the
    /// REFERENCE; the generator is JUDGED (oracle: BENCH n°51).
    /// </summary>
    [Fact]
    public void Gate_GeneratedElemRef_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.ElemRefToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.ElemRefAnswerKey);
        Assert.True(exit == 0,
            "@ref gate FAILED. Generated module is NOT alpha-equivalent to samples/ElemRef/elemref.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// THE CLAIM: the reference IS the element's name. The captured element is emitted into `const box`,
    /// and there is no ElementReference object and no assignment to it anywhere.
    /// </summary>
    [Fact]
    public void EmittedElemRef_NamesTheElementConstAfterTheRef()
    {
        var js = File.ReadAllText(Generate.ElemRefToTemp());

        Assert.Contains("const box = document.createElement('input')", js);
        Assert.Contains("box.focus()", js);
        Assert.DoesNotContain("ElementReference", js);
        Assert.DoesNotContain("const box = ;", js);   // the invalid JS an un-skipped field declaration emitted
        Assert.DoesNotContain("[unsupported-directive]", js);
    }

    /// <summary>
    /// An @ref that names no ElementReference field is REFUSED, and the message names the field to
    /// declare. A capture wired to nothing is a handle that silently refers to no node.
    /// </summary>
    [Fact]
    public void RefNamingNoElementReferenceField_IsRefused()
    {
        var outPath = Path.Combine(RepoPaths.Root, "samples", "ElemRef", $".ref-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(Path.Combine(RepoPaths.Unsupported, "Ref.razor"), outPath);
            Assert.True(exit != 0, "an @ref with no backing field was COMPILED, not refused");
            Assert.False(File.Exists(outPath));
            Assert.Contains("unsupported-directive", stderr);
            Assert.Contains("ElementReference", stderr);   // the message must say what to declare
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    /// <summary>GENERATOR-ONLY, ZERO HELPER: naming a const needs no runtime primitive. Note this module
    /// imports FEWER primitives than most — it has no signal and no effect at all.</summary>
    [Fact]
    public void EmittedElemRef_AddsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(Generate.ElemRefToTemp());
        Assert.Contains("import { listen, insert }", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedElemRefJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.ElemRefToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "ElemRef.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
