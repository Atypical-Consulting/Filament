using System.Diagnostics;
using System.Text;
using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// PHASE 2'S GATE (spec 6): "le JS emis pour Counter et Rows est equivalent au JS
/// ecrit a la main en phase 1, verifie par tests de snapshot".
///
/// Two independent controls, on purpose:
///
///   CanonEquivalence  — decision 51's mechanical definition of "equivalent":
///                       canon(minify(generated)) === canon(minify(hand-written)).
///                       It answers "did we build the right thing".
///   Snapshot          — pins the RAW emitted bytes to a committed file. Section 10:
///                       "Tests de snapshot sur le JS emis, ils sont la seule
///                       protection contre les regressions silencieuses." It answers
///                       "did the generator change behind our back", which the canon
///                       test CANNOT answer -- canon is designed to be blind to
///                       exactly the naming changes a snapshot must catch.
///
/// THE GATE TEST IS CURRENTLY RED, AND THAT IS THE RESULT, NOT A TODO.
/// It is committed asserting equivalence, and it fails. Decision 21/51: the answer
/// key is the REFERENCE and the generator is what is JUDGED, so a disagreement gets
/// REPORTED, never negotiated away by softening this assertion. See the failure
/// message for the remaining divergence and why it is a finding about the spec
/// rather than a bug in the generator.
///
/// IT USED TO FAIL ON TWO DIVERGENCES AND NOW FAILS ON ONE. The second -- the answer
/// key building 3 child nodes where Blazor builds 7 -- was the ANSWER KEY diverging
/// from the shared DOM contract, and the owner ruled it CORRECTED (decision 64). That
/// is not decision 21/51 being bent: 21/51 makes the answer key the reference for the
/// GENERATOR, and this was the reference being corrected against the BASELINE, which
/// outranks it. The gate narrowed as a side effect and STILL FAILS.
/// </summary>
public class GateTests
{
    [Fact]
    public void Gate_GeneratedCounter_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.CounterToTemp();

        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.AnswerKey);

        Assert.True(exit == 0,
            "PHASE 2 GATE: FAILED.\n\n" +
            "The generated module is NOT alpha-equivalent to the Phase 1 answer key\n" +
            "(samples/Counter/counter.js), under decision 51's definition.\n\n" +
            "The comparison:\n" + Indent(stdout) + "\n" +
            (stderr.Length > 0 ? "stderr:\n" + Indent(stderr) + "\n" : "") +
            "\nTHE ONE REMAINING DIVERGENCE -- a finding about the SPEC, not a bug:\n\n" +
            "  THE HANDLER. The answer key emits\n" +
            "      listen(button, 'click', () => { currentCount.value++; })\n" +
            "  i.e. it INLINES the body of `private void Increment()`. Inlining a\n" +
            "  handler's body requires reading that body, and in Phase 2 the body is\n" +
            "  hand-written JS that this phase's own scope says stays hand-written\n" +
            "  (\"la logique @code reste ecrite en JS a la main\"). Compiling the EVENT\n" +
            "  -- which IS in scope -- yields `listen(el, 'click', Increment)`. The\n" +
            "  answer key's shape presupposes Phase 3's C#->JS translation, so Phase 2's\n" +
            "  scope and Phase 2's gate contradict each other on Counter. This is\n" +
            "  decision 54's finding, reached at the @code seam instead of at @foreach.\n" +
            "  It is the OWNER's call to resolve (decision 62c), not an implementer's.\n\n" +
            "VERIFIED CONSTRUCTIVELY: neutralise ONLY the handler indirection on the\n" +
            "generated side and canon reports ALPHA-EQUIVALENT. Nothing else differs;\n" +
            "the template compilation itself is exact to the token.\n\n" +
            "THERE USED TO BE A SECOND DIVERGENCE, AND IT IS GONE -- READ WHY.\n" +
            "The generator emits the two \"\\n\\n\" Text nodes between <h1>/<p>/<button>;\n" +
            "the answer key created neither, so it built 3 child nodes where Blazor\n" +
            "builds 7 and the generator builds 5 (all three measured in-browser). The\n" +
            "GENERATOR was right and the ANSWER KEY diverged from the shared DOM\n" +
            "contract. The owner ruled that the answer key be CORRECTED -- decision 64 --\n" +
            "because a contract that is not actually shared invalidates every C4\n" +
            "comparison, and a reference rendering fewer nodes than the baseline hands\n" +
            "Filament a free create-time advantage. THE MOTIVE WAS THE CONTRACT; the\n" +
            "gate narrowing is a SIDE EFFECT, and note that it did NOT make the gate\n" +
            "pass -- this test is still RED on the handler.\n\n" +
            "DO NOT EDIT samples/Counter/counter.js TO MAKE THIS PASS. Decision 21/51\n" +
            "stands: the answer key is the REFERENCE and the generator is what is JUDGED.\n" +
            "The whitespace correction was NOT an exception to that rule -- it was the\n" +
            "reference being corrected against the BASELINE (Blazor), which is the only\n" +
            "authority above it, on the owner's explicit decision and with the residual\n" +
            "(5 nodes vs Blazor's 7) disclosed rather than banked.\n");
    }

    /// <summary>
    /// PHASE 3's GATE, IN ITS STRONGEST FORM: "les deux apps compilent depuis du .razor
    /// PUR." Not a Filament-flavoured .razor -- THE BASELINE'S OWN App.razor, the exact
    /// file Blazor compiles, byte for byte, comment header and all.
    ///
    /// Why this exists when CounterRazor_CodeBlockIsTheBaselinesCsharp_Verbatim already
    /// compares the two sources: that test compares TEXT, and text comparison is only as
    /// good as the window it compares (it strips the header comment and normalises
    /// indentation). This one removes the sample from the loop entirely. If the generator
    /// can compile baseline/Counter.Blazor/App.razor into the answer key, then "Filament
    /// compiles the same source Blazor compiles" is a fact about an artifact rather than
    /// an argument about two files that are supposed to match.
    ///
    /// It also closes the loophole the other test cannot: someone "fixing" a drift by
    /// editing BOTH files still fails here, because Blazor's file is the input.
    /// </summary>
    [Fact]
    public void PureRazor_TheBaselinesOwnAppRazor_CompilesToTheAnswerKey()
    {
        var appRazor = Path.Combine(RepoPaths.Root, "baseline", "Counter.Blazor", "App.razor");

        // Emitted next to the answer key so the relative runtime specifier resolves, then
        // moved out of the repo -- Generate.CounterToTemp's reasoning.
        var inRepo = Path.Combine(RepoPaths.Root, "samples", "Counter", $".base-{Guid.NewGuid():N}.js");
        var outside = Path.Combine(Path.GetTempPath(), $"filament-base-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(appRazor, inRepo);
            Assert.True(exit == 0,
                "the generator could not compile the BASELINE's own App.razor -- the file Blazor " +
                $"compiles. 'Compiles from pure .razor' is exactly this claim.\n{stderr}");

            File.WriteAllText(outside, File.ReadAllText(inRepo));

            var (canonExit, stdout, canonErr) = Run.Node(RepoPaths.Canon, outside, RepoPaths.AnswerKey);
            Assert.True(canonExit == 0,
                "Blazor's own App.razor compiled, but not to the answer key:\n" +
                Indent(stdout) + (canonErr.Length > 0 ? "\nstderr:\n" + Indent(canonErr) : ""));
        }
        finally
        {
            if (File.Exists(inRepo)) File.Delete(inRepo);
            if (File.Exists(outside)) File.Delete(outside);
        }
    }

    [Fact]
    public void Snapshot_EmittedJs_MatchesApprovedBytes()
    {
        var generated = Generate.CounterToTemp();
        var actual = Norm(File.ReadAllText(generated));

        var approvedPath = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "Counter.approved.js");

        if (!File.Exists(approvedPath))
        {
            File.WriteAllText(approvedPath, actual);
            Assert.Fail($"No approved snapshot existed; wrote one to {approvedPath}. Review it and re-run.");
        }

        var approved = Norm(File.ReadAllText(approvedPath));
        if (approved == actual) return;

        var receivedPath = Path.ChangeExtension(approvedPath, ".received.js");
        File.WriteAllText(receivedPath, actual);
        Assert.Fail(
            "The emitted JS changed and the snapshot did not.\n\n" +
            "Section 10: snapshots are the ONLY protection against silent generator\n" +
            "regressions, so this is a wall, not a formality. Diff them, and only then\n" +
            "decide whether the generator or the snapshot is wrong:\n\n" +
            $"  approved: {approvedPath}\n" +
            $"  received: {receivedPath}\n\n" +
            FirstDiff(approved, actual));
    }

    /// <summary>
    /// The snapshot is only a regression wall if it pins the SHARED DOM CONTRACT and
    /// not merely some bytes. These assertions say what the contract is, so that a
    /// blanket re-approval of a wrong snapshot still fails here.
    /// </summary>
    [Fact]
    public void EmittedJs_HonoursTheSharedDomContract()
    {
        var js = File.ReadAllText(Generate.CounterToTemp());

        Assert.Contains("document.createElement('h1')", js);
        Assert.Contains("_el0.id = 'title'", js);
        Assert.Contains("document.createElement('p')", js);
        Assert.Contains("document.createElement('span')", js);
        Assert.Contains(".id = 'counter-value'", js);
        Assert.Contains("document.createElement('button')", js);
        Assert.Contains(".id = 'increment'", js);
        Assert.Contains("'click'", js);
        Assert.Contains("document.createTextNode('Click me')", js);
        Assert.Contains("document.createTextNode('Current count: ')", js);

        // The binding point: one Text node, created empty, owned by one effect forever.
        Assert.Contains("document.createTextNode('')", js);
        Assert.Contains("effect(() => setText(_tx0, currentCount.value))", js);

        // Never textContent: that would destroy and rebuild the span's children on
        // every increment -- 2 DOM writes where C3 allows 1, on markup that looks identical.
        Assert.DoesNotContain("textContent", js);

        // The '@' must be gone. If it survived, the tag helper descriptors did not
        // resolve and the button silently compiled to a static element (decision 53).
        Assert.DoesNotContain("'@onclick'", js);

        // No render tree, no diffing, no component instance. The thesis.
        Assert.DoesNotContain("innerHTML", js);
    }

    [Fact]
    public void EmittedJs_OnlyCallsClosedRuntimePrimitives()
    {
        var js = File.ReadAllText(Generate.CounterToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));

        string[] allowed = ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        var imported = import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var name in imported)
            Assert.True(allowed.Contains(name),
                $"'{name}' is not one of the runtime's exports. The runtime is CLOSED (it sits at ~1955 B " +
                "against a 2048 B budget); a generator that needs a new primitive is a FINDING to report, " +
                "not headroom to spend.");

        Assert.Equal("import { signal, effect, setText, listen, insert } from '../../src/filament-runtime/src/index.ts';", import);
    }

    // ---- helpers -----------------------------------------------------------

    static string Norm(string s) => s.Replace("\r\n", "\n").TrimEnd() + "\n";

    static string Indent(string s) =>
        string.Join('\n', s.Replace("\r\n", "\n").TrimEnd().Split('\n').Select(l => "    " + l));

    static string FirstDiff(string a, string b)
    {
        var la = a.Split('\n');
        var lb = b.Split('\n');
        for (var i = 0; i < Math.Max(la.Length, lb.Length); i++)
        {
            var x = i < la.Length ? la[i] : "<end of file>";
            var y = i < lb.Length ? lb[i] : "<end of file>";
            if (x != y)
                return new StringBuilder()
                    .Append("first differing line ").Append(i + 1).Append(":\n")
                    .Append("  approved: ").Append(x).Append('\n')
                    .Append("  received: ").Append(y).Append('\n').ToString();
        }
        return "(files differ only in trailing whitespace)";
    }
}

/// <summary>Runs the generator the way the build script does: as a process, on the real input.</summary>
public static class Generate
{
    /// <summary>
    /// Emit NEXT TO the answer key, then move the file out of the repo.
    ///
    /// Emitting there is deliberate: it is the only way the generator's relative
    /// runtime specifier gets resolved from the location the shipped module would
    /// actually occupy, so the emitted `from '../../src/filament-runtime/src/index.ts'`
    /// is exercised rather than asserted. Moving it out afterwards is equally
    /// deliberate: a generated file left in samples/ is a file someone will eventually
    /// edit by hand, next to an answer key that must never be edited at all.
    /// </summary>
    public static string RowsToTemp() => ToTemp(RepoPaths.RowsRazor, "Rows");

    public static string CounterToTemp() => ToTemp(RepoPaths.CounterRazor, "Counter");

    public static string IfToTemp() => ToTemp(RepoPaths.IfRazor, "If");

    public static string IfElseToTemp() => ToTemp(RepoPaths.IfElseRazor, "IfElse");

    public static string DivideToTemp() => ToTemp(RepoPaths.DivideRazor, "Divide");

    public static string DivideIntToTemp() => ToTemp(RepoPaths.DivideIntRazor, "DivideInt");

    public static string LoopsToTemp() => ToTemp(RepoPaths.LoopsRazor, "Loops");

    public static string ComposeToTemp() => ToTemp(RepoPaths.ComposeRazor, "Compose");

    public static string RootForeachToTemp() => ToTemp(RepoPaths.RootForeachRazor, "RootForeach");

    public static string RootIfToTemp() => ToTemp(RepoPaths.RootIfRazor, "RootIf");

    public static string IfMultiBodyToTemp() => ToTemp(RepoPaths.IfMultiBodyRazor, "IfMultiBody");

    public static string IfElseMultiBodyToTemp() => ToTemp(RepoPaths.IfElseMultiBodyRazor, "IfElseMultiBody");

    public static string IfNestedToTemp() => ToTemp(RepoPaths.IfNestedRazor, "IfNested");

    public static string BoundComposeToTemp() => ToTemp(RepoPaths.BoundComposeRazor, "BoundCompose");

    public static string ReactiveAttrToTemp() => ToTemp(RepoPaths.ReactiveAttrRazor, "ReactiveAttr");

    public static string BoolAttrToTemp() => ToTemp(RepoPaths.BoolAttrRazor, "BoolAttr");

    public static string MixedAttrToTemp() => ToTemp(RepoPaths.MixedAttrRazor, "MixedAttr");

    public static string StringAttrsToTemp() => ToTemp(RepoPaths.StringAttrsRazor, "StringAttrs");

    public static string MoreAttrsToTemp() => ToTemp(RepoPaths.MoreAttrsRazor, "MoreAttrs");

    public static string BindToTemp() => ToTemp(RepoPaths.BindRazor, "Bind");

    public static string LambdaHandlerToTemp() => ToTemp(RepoPaths.LambdaHandlerRazor, "LambdaHandler");

    public static string ListOpsToTemp() => ToTemp(RepoPaths.ListOpsRazor, "ListOps");

    public static string CheckBindToTemp() => ToTemp(RepoPaths.CheckBindRazor, "CheckBind");

    public static string IntBindToTemp() => ToTemp(RepoPaths.IntBindRazor, "IntBind");

    public static string CodeBlockToTemp() => ToTemp(RepoPaths.CodeBlockRazor, "CodeBlock");

    /// <summary>
    /// Emit a fixture from the Unsupported dir (some of which now COMPILE -- e.g. root control
    /// flow, decision 89) and hand back a temp copy. Emits IN-REPO first, like ToTemp, so the
    /// relative runtime specifier resolves (a temp output would fail FIL-WIRING on a clean emit).
    /// </summary>
    public static string ToTempFixture(string fixture)
    {
        var inRepo = Path.Combine(RepoPaths.Unsupported, $".gen-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(Path.Combine(RepoPaths.Unsupported, fixture), inRepo);
            Assert.True(exit == 0, $"the generator refused to emit {fixture}:\n{stderr}");

            var outside = Path.Combine(Path.GetTempPath(), $"filament-gen-{Guid.NewGuid():N}.js");
            File.WriteAllText(outside, File.ReadAllText(inRepo));
            return outside;
        }
        finally
        {
            if (File.Exists(inRepo)) File.Delete(inRepo);
        }
    }

    static string ToTemp(string razor, string sampleDir)
    {
        var inRepo = Path.Combine(RepoPaths.Root, "samples", sampleDir, $".gen-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(razor, inRepo);
            Assert.True(exit == 0, $"the generator refused to emit:\n{stderr}");

            var outside = Path.Combine(Path.GetTempPath(), $"filament-gen-{Guid.NewGuid():N}.js");
            File.WriteAllText(outside, File.ReadAllText(inRepo));
            return outside;
        }
        finally
        {
            if (File.Exists(inRepo)) File.Delete(inRepo);
        }
    }
}

public static class Run
{
    public static (int exit, string stdout, string stderr) Generator(string input, string output)
    {
        var dll = Path.Combine(RepoPaths.Root, "src", "Filament.Generator", "bin",
            Configuration, "net10.0", "Filament.Generator.dll");
        Assert.True(File.Exists(dll), $"generator not built at {dll}");
        return Exec("dotnet", [dll, input, output]);
    }

    public static (int exit, string stdout, string stderr) Node(params string[] args) => Exec("node", args);

    const string Configuration =
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    static (int, string, string) Exec(string file, string[] args)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = RepoPaths.Root };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd();
        var e = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, o, e);
    }
}
