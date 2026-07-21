using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// HttpClient -> fetch (decision 147): the network enters the subset on decision 133's exact
/// argument -- Blazor WASM's HttpClient is implemented ON TOP of fetch, so the bridge erases.
/// The honesty core is the JSON type gate: GetFromJsonAsync&lt;T&gt; admits T only where the JSON
/// shape and the Filament shape COINCIDE. Also covers the @using widening the slice needed: a
/// RESOLVING @using is pure name resolution and is admitted; an unresolvable one still refuses.
/// </summary>
public class HttpJsonTests
{
    /// <summary>THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).</summary>
    [Fact]
    public void Gate_GeneratedHttpJson_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.HttpJsonToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.HttpJsonAnswerKey);
        Assert.True(exit == 0,
            "PHASE: HttpClient gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/HttpJson/httpjson.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedHttpJsonJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.HttpJsonToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "HttpJson.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract (decision 147): the call becomes `await __getJson(url)`; __getJson throws on !ok
    /// (GetFromJsonAsync's EnsureSuccess semantics) and normalizes keys' leading case (__camel, the Web
    /// defaults' Pascal-to-camel); the null test survives as the same reference test.
    /// </summary>
    [Fact]
    public void EmittedHttpJson_ErasesTheClientIntoGetJson()
    {
        var js = File.ReadAllText(Generate.HttpJsonToTemp());
        Assert.Contains("async function __getJson(u)", js);
        Assert.Contains("function __camel(v)", js);
        Assert.Contains("if (!r.ok) throw new Error(", js);
        Assert.Contains("await __getJson('data/items.json')", js);
        Assert.Contains("data !== null", js);
        Assert.DoesNotContain("__getText", js);   // unused helpers NOT emitted
        Assert.DoesNotContain("__postJson", js);
    }

    /// <summary>Closed-runtime invariant: HTTP adds NO new runtime primitive -- fetch is the platform's own.</summary>
    [Fact]
    public void EmittedHttpJson_OnlyImportsClosedRuntimePrimitives_HelpersAreInline()
    {
        var js = File.ReadAllText(Generate.HttpJsonToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name), $"'{name}' is not a runtime export (the HTTP helpers must be inline).");
    }

    /// <summary>
    /// THE TYPE GATE (the honesty core): a record with a `long` member is refused -- a JSON number
    /// deserializes to a JS number, not the BigInt `long` maps to. Never silently emitted (section 10).
    /// </summary>
    [Fact]
    public void GetFromJson_WithLongMember_IsRefused_JsonShapeIsNotFilamentShape()
    {
        var outPath = Path.Combine(RepoPaths.Root, "samples", "HttpJson", $".long-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Code", "HttpJsonLong.razor"), outPath);
            Assert.True(exit != 0, "a long-membered T was COMPILED, not refused -- a silent BigInt/number mismatch.");
            Assert.False(File.Exists(outPath), "refused AND wrote the module anyway");
            Assert.Contains("[unsupported-type]", stderr);
            Assert.Contains("long", stderr);
            Assert.Contains("BigInt", stderr);
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    /// <summary>THE OTHER BOUNDARY: the rest of HttpClient (DeleteAsync here) stays refused with guidance.</summary>
    [Fact]
    public void HttpDelete_IsRefused_OnlyTheFaithfulShapesAreAdmitted()
    {
        var outPath = Path.Combine(RepoPaths.Root, "samples", "HttpJson", $".del-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Code", "HttpDelete.razor"), outPath);
            Assert.True(exit != 0, "DeleteAsync was COMPILED, not refused.");
            Assert.False(File.Exists(outPath), "refused AND wrote the module anyway");
            Assert.Contains("[unsupported-call]", stderr);
            Assert.Contains("GetFromJsonAsync", stderr);   // the guidance names what IS admitted
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    /// <summary>The @using widening's OWN boundary is already pinned by DiagnosticTests (Using.razor: an
    /// unresolvable namespace still refuses at its span, as Blazor's CS0246 would). This pins the admitted
    /// half: the witness's `@using System.Net.Http.Json` resolves and the fixture compiles -- proven by
    /// every other test in this class reaching emission.</summary>
    [Fact]
    public void ResolvingAuthorUsing_IsAdmitted_TheWitnessCompiles()
    {
        var outPath = Path.Combine(RepoPaths.Root, "samples", "HttpJson", $".using-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(RepoPaths.HttpJsonRazor, outPath);
            Assert.True(exit == 0, $"the witness (with @using System.Net.Http.Json) was refused:\n{stderr}");
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }
}
