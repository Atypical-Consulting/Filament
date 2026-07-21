using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// System.Random (decision 146) -- TWO regimes behind one emitted factory. SEEDED `new Random(s)` is
/// the exact .NET Knuth-subtractive sequence (Net5CompatSeedImpl, stable by compat guarantee),
/// reimplemented in the emitted __rnd(seed) -- so the oracle's byte-equality against Blazor IS the
/// faithfulness proof against the real BCL. UNSEEDED / Random.Shared is arbitrary on both sides:
/// Math.random behind the same interface, measured by range.
/// </summary>
public class RandTests
{
    /// <summary>THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).</summary>
    [Fact]
    public void Gate_GeneratedRand_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.RandToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.RandAnswerKey);
        Assert.True(exit == 0,
            "PHASE: Random gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/Rand/rand.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedRandJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.RandToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "Rand.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract (decision 146): `new Random(42)` -> `__rnd(42)` (a stateful generator held in a
    /// const -- not a signal: it is never read by the template); `.Next(1, 7)` -> `.nextIn(1, 7)`;
    /// `Random.Shared` -> ONE module-level unseeded instance; `.Next(10)` -> `.nextTo(10)`.
    /// </summary>
    [Fact]
    public void EmittedRand_SeededFactory_SharedInstance_MethodMapping()
    {
        var js = File.ReadAllText(Generate.RandToTemp());
        Assert.Contains("function __rnd(seed)", js);              // the factory is emitted
        Assert.Contains("161803398", js);                          // Knuth MSEED -- the REAL algorithm, not a stub
        Assert.Contains("const rng = __rnd(42)", js);              // seeded construction
        Assert.Contains("rng.nextIn(1, 7)", js);                   // Next(min, max)
        Assert.Contains("const __rndShared = __rnd(null)", js);    // Random.Shared, once, module-level
        Assert.Contains("__rndShared.nextTo(10)", js);             // Next(max)
    }

    /// <summary>The exact seeded sequence, executed: the emitted factory must reproduce the BCL's
    /// Random(42) -- draws 5, 1, 1 for Next(1,7) and 1434747710 for Next() -- values read off
    /// `dotnet run`, never computed by hand. Run through node on the GENERATED module's own bytes.</summary>
    [Fact]
    public void EmittedRandFactory_ReproducesTheBclSequence_ExecutedInNode()
    {
        var js = File.ReadAllText(Generate.RandToTemp());
        var start = js.IndexOf("function __rnd(seed)");
        Assert.True(start >= 0, "no __rnd factory in the emitted module");
        var end = js.IndexOf("\n}", start);
        var factory = js[start..(end + 2)];
        var probe = factory + "\n" +
            "const a = __rnd(42);\n" +
            "console.log([a.nextIn(1, 7), a.nextIn(1, 7), a.nextIn(1, 7), __rnd(42).next()].join(' '));\n";
        var probePath = Path.Combine(Path.GetTempPath(), $"filament-rnd-{Guid.NewGuid():N}.mjs");
        try
        {
            File.WriteAllText(probePath, probe);
            var (exit, stdout, stderr) = Run.Node(probePath);
            Assert.True(exit == 0, $"node failed:\n{stderr}");
            Assert.Equal("5 1 1 1434747710", stdout.Trim());
        }
        finally
        {
            File.Delete(probePath);
        }
    }

    /// <summary>Closed-runtime invariant: Random adds NO new runtime primitive -- the factory is emitted into the module.</summary>
    [Fact]
    public void EmittedRand_OnlyImportsClosedRuntimePrimitives_FactoryIsInline()
    {
        var js = File.ReadAllText(Generate.RandToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name), $"'{name}' is not a runtime export (the Random factory must be inline).");
    }

    /// <summary>THE BOUNDARY: NextBytes fills a byte[], which is not in the subset; refused with wording
    /// that says so, never silently emitted (section 10).</summary>
    [Fact]
    public void RandomNextBytes_IsRefused_ByteArrayIsNotInTheSubset()
    {
        var outPath = Path.Combine(RepoPaths.Root, "samples", "Rand", $".bytes-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Code", "RandomNextBytes.razor"), outPath);
            Assert.True(exit != 0, "Random.NextBytes was COMPILED, not refused.");
            Assert.False(File.Exists(outPath), "refused AND wrote the module anyway");
            Assert.Contains("NextBytes", stderr);
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    /// <summary>THE OTHER BOUNDARY: displaying a Random itself (`@rng`) would render "[object Object]"
    /// where C# renders "System.Random" -- a silent wrong render, so it is refused at the slot.</summary>
    [Fact]
    public void RandomDisplay_IsRefused_NoFaithfulDisplay()
    {
        var outPath = Path.Combine(RepoPaths.Root, "samples", "Rand", $".disp-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Code", "RandomDisplay.razor"), outPath);
            Assert.True(exit != 0, "a Random-typed slot was COMPILED, not refused -- '[object Object]' vs 'System.Random'.");
            Assert.False(File.Exists(outPath), "refused AND wrote the module anyway");
            Assert.Contains("display", stderr);
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }
}
