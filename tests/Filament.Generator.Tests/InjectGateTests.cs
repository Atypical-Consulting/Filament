using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// THE @inject GATES TELL THE TRUTH (decision 166) — found by PROBING the eleven §3 non-goals ADR 0003
/// declared closed, not by reading the code. Three defects, one directive:
///
///   A11 (SOUNDNESS). The gate was `typeName.EndsWith("HttpClient")`, so `@inject WrapperHttpClient Api`
///        — a typed client registered with `AddHttpClient&lt;WrapperHttpClient&gt;(c =&gt; c.BaseAddress = …)`,
///        valid Blazor — was silently ADMITTED and its `GetStringAsync("weather")` erased to
///        `fetch('weather')`, a DOCUMENT-relative URL. Blazor asks the client's BaseAddress: measured on
///        the wire, `http://127.0.0.1:8791/api/weather`. A different request, and not one word about it.
///        Same hole for every name merely ENDING in `IJSRuntime`.
///
///   C1   The refusal's caret always pointed at the FIRST `@inject` in the file, which is usually an
///        ADMITTED one, and the diagnostics came out in reverse source order.
///
///   C7   A bare read of an injected name was reported as `[unresolved-name] … no injected services to
///        reach for` when an `@inject` was right there and the name had bound.
///
/// REFUSAL WORK, so there is nothing new for a browser to render. The evidence is the located diagnostic,
/// the boundary that stays admitted (InjectQualified below), and the byte-identity of the two baselines
/// that use the admitted spellings — JsInterop.approved.js and HttpJson.approved.js, unchanged.
/// </summary>
public class InjectGateTests
{
    /// <summary>
    /// A11, THE HTTP HALF. Two characters of a name are not a type. `WrapperHttpClient` is a typed client:
    /// it TAKES an HttpClient, its BaseAddress is the API's, and its relative URLs resolve against THAT.
    /// Filament cannot see the .cs file it is declared in, so the only honest answer is a refusal at the
    /// directive — never a fetch against a different origin.
    /// </summary>
    [Fact]
    public void ATypedHttpClient_IsRefusedAtTheDirective_NotSilentlyRewrittenToADocumentRelativeFetch()
    {
        var (stderr, emitted) = Refused("Gate/InjectTypedHttpClient.razor");

        Assert.Contains("InjectTypedHttpClient.razor(1,1): FIL0003: [unsupported-directive]", stderr);
        Assert.Contains("@inject WrapperHttpClient is not in the subset", stderr);

        // THE CLAIM, stated as the absence it is: no module, therefore no fetch of the wrong URL.
        Assert.Null(emitted);
    }

    /// <summary>A11, THE JS HALF. `MyIJSRuntime` is a user's own service; it is not Microsoft.JSInterop's,
    /// and its `InvokeVoidAsync` is not a JS call to erase. It used to compile to
    /// `await localStorage.setItem('fil', 'hello')`.</summary>
    [Fact]
    public void AServiceMerelyNamedLikeIJSRuntime_IsRefused_NotErasedIntoADirectJsCall()
    {
        var (stderr, emitted) = Refused("Gate/InjectSuffixJsRuntime.razor");

        Assert.Contains("InjectSuffixJsRuntime.razor(1,1): FIL0003: [unsupported-directive]", stderr);
        Assert.Contains("@inject MyIJSRuntime is not in the subset", stderr);
        Assert.Null(emitted);
    }

    /// <summary>
    /// C1: THREE injects, the refused one LAST, and the first two ADMITTED — the exact shape that made the
    /// old caret land on an @inject the compiler had no complaint about.
    ///
    /// PAIRING BY INDEX WOULD NOT FIX IT, and that refutation is why this test exists in this shape: the
    /// two lists are ANTI-PARALLEL (DirectiveSpyPass records document order, the lowered inject nodes
    /// arrive reversed), so the Nth of one is not the Nth of the other. The span comes from matching the
    /// directive's own TOKENS.
    /// </summary>
    [Fact]
    public void WithThreeInjects_TheCaretIsOnTheRefusedOne_NotOnTheFirst()
    {
        var (stderr, _) = Refused("Gate/InjectThreeRefusedLast.razor");

        Assert.Contains("InjectThreeRefusedLast.razor(4,1): FIL0003: [unsupported-directive]", stderr);
        Assert.Contains("@inject NavigationManager is not in the subset", stderr);

        // The two admitted injects are on lines 2 and 3. Neither may be blamed for the third.
        Assert.DoesNotContain("InjectThreeRefusedLast.razor(2,1)", stderr);
        Assert.DoesNotContain("InjectThreeRefusedLast.razor(3,1)", stderr);
    }

    /// <summary>
    /// C7: the refusal was RIGHT and the reason was a lie. `@JS` in text position has no faithful mapping —
    /// the service is erased, so the emitted module holds no binding for it — but the name is declared, by
    /// the author, one line up. The message now names the service, says it is admitted only as a call
    /// receiver, and names the calls that erase.
    /// </summary>
    [Fact]
    public void AnInjectedServiceReadAsAValue_IsNotReportedAsAnUndeclaredName()
    {
        var (stderr, _) = Refused("Gate/InjectServiceAsValue.razor");

        Assert.Contains("InjectServiceAsValue.razor(4,14): FIL0001: [unsupported-expression]", stderr);
        Assert.Contains("'JS' is an @inject'd IJSRuntime read as a VALUE", stderr);
        Assert.Contains("InvokeVoidAsync", stderr);   // the position it IS admitted in

        // The sentence that blamed the author for a directive they had written.
        Assert.DoesNotContain("no injected services to reach for", stderr);
        Assert.DoesNotContain("[unresolved-name]", stderr);
    }

    /// <summary>
    /// ONE NAME PER SERVICE, and the SECOND is refused rather than dropped. Blazor takes two
    /// `@inject HttpClient` under two names without blinking — it holds two references to one object.
    /// Filament holds a NAME, because the service is erased and the name is all that survives of it, so a
    /// second binding overwrote the first and left a name that still LOOKED declared while resolving to
    /// nothing: measured on this very fixture at HEAD, exit 0 and a module emitted, with `Backup` dead.
    /// Use both names and HEAD blamed the author for it somewhere else entirely
    /// (`[unsupported-call] 'Backup.GetStringAsync(…)' is not a call to a method declared in this
    /// component`), never mentioning the directive. Which of the two got dropped also depended on the walk
    /// order this decision just corrected, so refusing is what keeps that correction from being a silent
    /// behaviour change.
    /// </summary>
    [Fact]
    public void ASecondInjectOfTheSameService_IsRefusedAtTheDuplicate_NotSilentlyDropped()
    {
        var (stderr, emitted) = Refused("Gate/InjectTwiceSameService.razor");

        Assert.Contains("InjectTwiceSameService.razor(2,1): FIL0003: [unsupported-directive]", stderr);
        Assert.Contains("is a SECOND @inject of HttpClient", stderr);
        Assert.Contains("Inject it once and use that one name", stderr);   // the workaround, named
        Assert.Null(emitted);
    }

    /// <summary>
    /// THE OTHER SIDE OF THE LINE. Exact matching must not become over-refusal: the fully-qualified and
    /// the alias-qualified spellings denote the same two framework types and stay admitted, erasing
    /// exactly as the bare spellings do. The bare spellings themselves are pinned byte-for-byte by
    /// JsInterop.approved.js and HttpJson.approved.js, which this slice did not touch.
    /// </summary>
    [Fact]
    public void QualifiedAndAliasQualifiedSpellings_StillCompile_AndStillErase()
    {
        var outPath = InRepo();
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Supported, "Gate", "InjectQualified.razor"), outPath);

            Assert.True(exit == 0, $"an exactly-named service was refused:\n{stderr}");

            var js = File.ReadAllText(outPath);
            Assert.Contains("__getText('data/hello.txt')", js);            // System.Net.Http.HttpClient
            Assert.Contains("localStorage.setItem('fil', msg.value)", js); // global::…IJSRuntime
            Assert.DoesNotContain("IJSRuntime", js);
            Assert.DoesNotContain("InvokeVoidAsync", js);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    /// <summary>The refusal, plus what it did NOT write. A refusal that leaves a module behind is not one.</summary>
    static (string Stderr, string? Emitted) Refused(string fixture)
    {
        var outPath = InRepo();
        try
        {
            var (exit, stdout, stderr) = Run.Generator(Path.Combine(RepoPaths.Unsupported, fixture), outPath);
            var emitted = File.Exists(outPath) ? File.ReadAllText(outPath) : null;

            Assert.True(exit != 0,
                $"{fixture} was COMPILED, not refused -- the silent mis-compile section 10 forbids.\n" +
                $"stdout:\n{stdout}\nstderr:\n{stderr}\n" +
                (emitted is null ? "" : "it emitted:\n" + emitted));

            return (stderr, emitted);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    static string InRepo() =>
        Path.Combine(RepoPaths.Root, "samples", "Counter", $".inject-{Guid.NewGuid():N}.js");
}
