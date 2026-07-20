using Xunit;

namespace Filament.Generator.Tests;

public class ElementWriteTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/ElementWrite.Blazor/App.razor is
    /// alpha-equivalent to the hand-written samples/ElementWrite/elementwrite.js. The key is the SPEC and the
    /// REFERENCE; the generator is JUDGED. elementwrite.js's Blazor-faithfulness is what the DOM-contract oracle
    /// measures (baseline/ElementWrite.Blazor rendered vs filament-elementwrite-gen rendered, BENCH n°46).
    /// </summary>
    [Fact]
    public void Gate_GeneratedElementWrite_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.ElementWriteToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.ElementWriteAnswerKey);
        Assert.True(exit == 0,
            "element-write gate FAILED. Generated module is NOT alpha-equivalent to samples/ElementWrite/elementwrite.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// The array/Dict element writes are COPY-ON-WRITE -- .with(i,v) and new Map(d).set(k,v) -- each a new
    /// reference so the signal fires. A plain in-place `xs.value[1] =` (the stale mapping) must NOT appear.
    /// </summary>
    [Fact]
    public void EmittedElementWrite_IsCopyOnWrite()
    {
        var js = File.ReadAllText(Generate.ElementWriteToTemp());
        Assert.Contains("xs.value = xs.value.with(1, xs.value[1] + 5)", js);
        Assert.Contains("scores.value = new Map(scores.value).set('b', scores.value.get('b') + 100)", js);
        Assert.DoesNotContain("xs.value[1] =", js); // the stale in-place write must not appear
    }

    /// <summary>The runtime is untouched: Array.with/Map/.set are JS builtins, only pre-existing primitives imported.</summary>
    [Fact]
    public void EmittedElementWrite_AddsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(Generate.ElementWriteToTemp());
        Assert.Contains("import { signal, effect, batch, setText, listen, insert }", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedElementWriteJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.ElementWriteToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "ElementWrite.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
