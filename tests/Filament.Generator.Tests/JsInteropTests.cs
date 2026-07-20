using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// JS INTEROP + the one honest form of @inject (decision 133) — two spec 3 non-goals closed at once,
/// and closed NARROWLY. Blazor needs IJSRuntime to be a service because calling JavaScript from .NET
/// crosses a boundary; a module that IS JavaScript has no boundary, so the bridge is ERASED.
/// </summary>
public class JsInteropTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/JsInterop.Blazor/App.razor
    /// is alpha-equivalent to the hand-written samples/JsInterop/jsinterop.js. The key is the SPEC and
    /// the REFERENCE; the generator is JUDGED (oracle: BENCH n°52).
    /// </summary>
    [Fact]
    public void Gate_GeneratedJsInterop_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.JsInteropToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.JsInteropAnswerKey);
        Assert.True(exit == 0,
            "JS-interop gate FAILED. Generated module is NOT alpha-equivalent to samples/JsInterop/jsinterop.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// THE CLAIM: the bridge is gone. The dotted identifier is resolved at COMPILE time into the same
    /// path — legal JS exactly as written — and neither the runtime service nor the invoke call survives.
    /// </summary>
    [Fact]
    public void EmittedJsInterop_ErasesTheBridgeIntoADirectCall()
    {
        var js = File.ReadAllText(Generate.JsInteropToTemp());

        Assert.Contains("localStorage.setItem('fil', 'hello')", js);
        Assert.Contains("localStorage.getItem('fil')", js);
        Assert.DoesNotContain("IJSRuntime", js);
        Assert.DoesNotContain("InvokeVoidAsync", js);
        Assert.DoesNotContain("InvokeAsync", js);
        Assert.DoesNotContain("JS.", js);
    }

    /// <summary>
    /// @inject IS NARROW, ON PURPOSE. Any service other than IJSRuntime is refused: a general container
    /// resolves an implementation at RUNTIME and a static module has none to ask. The refusal must SAY
    /// that, rather than reading as "directives are unsupported".
    /// </summary>
    [Fact]
    public void InjectingAnythingButIJSRuntime_IsRefused_WithAReasonThatExplains()
    {
        var outPath = Path.Combine(RepoPaths.Root, "samples", "JsInterop", $".inj-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(Path.Combine(RepoPaths.Unsupported, "Inject.razor"), outPath);
            Assert.True(exit != 0, "@inject of an arbitrary service was COMPILED, not refused");
            Assert.False(File.Exists(outPath));
            Assert.Contains("unsupported-directive", stderr);
            Assert.Contains("IJSRuntime", stderr);   // it must name what IS allowed, and why
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    /// <summary>
    /// A COMPUTED identifier is refused. Resolving one would mean walking the global scope at runtime —
    /// shipping the very bridge this slice claims to have removed.
    /// </summary>
    [Fact]
    public void ComputedJsIdentifier_IsRefused()
    {
        var outPath = Path.Combine(RepoPaths.Root, "samples", "JsInterop", $".dyn-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Gate/JsIdentifierComputed.razor"), outPath);
            Assert.True(exit != 0, "a computed JS identifier was COMPILED, not refused");
            Assert.False(File.Exists(outPath));
            Assert.Contains("unsupported-call", stderr);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    /// <summary>GENERATOR-ONLY, ZERO HELPER: an erased bridge needs no runtime primitive.</summary>
    [Fact]
    public void EmittedJsInterop_AddsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(Generate.JsInteropToTemp());
        Assert.Contains("import { signal, effect, setText, listen, insert }", js);
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedJsInteropJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.JsInteropToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "JsInterop.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
