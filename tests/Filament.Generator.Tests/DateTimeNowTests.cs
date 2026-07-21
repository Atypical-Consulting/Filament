using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// DateTime.UtcNow / Now / Today (decision 145) -- the FIRST admitted non-deterministic value.
/// The ticks model (decision 115) already represents every DateTime as a BigInt; what was refused
/// was the SOURCE. The wall clock is the same wall clock on both sides: __dtUtcNow() derives the
/// tick count from Date.now(), and .Ticks is the IDENTITY on the representation (a long, #112).
/// </summary>
public class DateTimeNowTests
{
    /// <summary>THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).</summary>
    [Fact]
    public void Gate_GeneratedDateTimeNow_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.DateTimeNowToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.DateTimeNowAnswerKey);
        Assert.True(exit == 0,
            "PHASE: DateTime.UtcNow gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/DateTimeNow/datetimenow.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedDateTimeNowJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.DateTimeNowToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "DateTimeNow.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract (decision 145): DateTime.UtcNow is the emitted __dtUtcNow() -- Unix epoch offset in
    /// ticks + Date.now() scaled ms->ticks -- and `.Ticks` is the identity on the ticks representation,
    /// so the display is the bare BigInt (a long, #112), never __dtStr.
    /// </summary>
    [Fact]
    public void EmittedDateTimeNow_ReadsTheClockThroughDtUtcNow_AndTicksIsIdentity()
    {
        var js = File.ReadAllText(Generate.DateTimeNowToTemp());
        Assert.Contains("function __dtUtcNow()", js);                          // the clock helper is emitted
        Assert.Contains("621355968000000000n + BigInt(Date.now()) * 10000n", js); // epoch offset + ms->ticks
        Assert.Contains("stamp.value = __dtUtcNow()", js);                     // UtcNow IS the helper call
        Assert.Contains("setText(_tx0, stamp.value)", js);                     // .Ticks = identity, bare BigInt display
        Assert.DoesNotContain("__dtStr", js);                                  // no formatter on this path
        Assert.DoesNotContain("__dtNow", js.Replace("__dtUtcNow", ""));        // unused helpers NOT emitted
    }

    /// <summary>Now and Today ride the same clock (decision 145): Now subtracts the CURRENT local offset
    /// (getTimezoneOffset is UTC-local in minutes; one minute = 600,000,000 ticks), Today truncates Now to
    /// the local day. Pinned here at emission level; the MEASURED witness is UtcNow (no timezone in play).</summary>
    [Fact]
    public void NowAndToday_EmitTheOffsetChain_HelpersInDependencyOrder()
    {
        var outPath = Path.Combine(RepoPaths.Root, "samples", "DateTimeNow", $".nowtoday-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Supported, "Code", "DateTimeNowToday.razor"), outPath);
            Assert.True(exit == 0, $"Now/Today fixture refused:\n{stderr}");
            var js = File.ReadAllText(outPath);
            Assert.Contains("function __dtUtcNow()", js);   // dependency emitted first
            Assert.Contains("function __dtNow()", js);
            Assert.Contains("__dtUtcNow() - BigInt(new Date().getTimezoneOffset()) * 600000000n", js);
            Assert.Contains("function __dtToday()", js);
            Assert.Contains("(__dtNow() / 864000000000n) * 864000000000n", js);
            Assert.Contains("when.value = __dtNow()", js);
            Assert.Contains("today.value = __dtToday()", js);
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    /// <summary>Closed-runtime invariant: the clock adds NO new runtime primitive -- helpers are emitted into the module.</summary>
    [Fact]
    public void EmittedDateTimeNow_OnlyImportsClosedRuntimePrimitives_HelperIsInline()
    {
        var js = File.ReadAllText(Generate.DateTimeNowToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name), $"'{name}' is not a runtime export (the clock must be inline).");
    }

    /// <summary>
    /// THE BOUNDARY: .Kind does not survive the ticks erasure -- a tick count has no Kind. Refused at its
    /// exact location with wording that names the erasure, never silently emitted (section 10).
    /// </summary>
    [Fact]
    public void DateTimeKind_IsRefused_TheTicksModelHasNoKind()
    {
        var outPath = Path.Combine(RepoPaths.Root, "samples", "DateTimeNow", $".kind-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Code", "DateTimeKind.razor"), outPath);
            Assert.True(exit != 0, "DateTime.Kind was COMPILED, not refused -- the ticks model has no Kind to read.");
            Assert.False(File.Exists(outPath), "refused AND wrote the module anyway");
            Assert.Contains("[unsupported-member]", stderr);
            Assert.Contains("Kind", stderr);
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }
}
