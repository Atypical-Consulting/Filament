using Xunit;

namespace Filament.Generator.Tests;

public class IfElseTests
{
    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the emitted module is alpha-equivalent to the
    /// hand-written samples/IfElse/ifelse.js. The key is the SPEC and REFERENCE; the generator is
    /// JUDGED. Never edit the key to make this pass except to correct it against the BASELINE.
    /// </summary>
    [Fact]
    public void Gate_GeneratedIfElse_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.IfElseToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.IfElseAnswerKey);
        Assert.True(exit == 0,
            "PHASE: @else gate FAILED. Generated module is NOT alpha-equivalent to samples/IfElse/ifelse.js.\n" +
            stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// An @if / @else if / @else chain compiles to ONE keyed list() whose single item's value is
    /// the active branch index: source is a ternary chain, the key IS the index, and create()
    /// dispatches on it. Every branch condition lifts its field to a signal (read-by-template).
    /// </summary>
    [Fact]
    public void IfElseChain_CompilesToAKeyedList_BranchIndexIsTheKey()
    {
        var js = Compile(
            """
            <div id="wrap"><button id="t" @onclick="Next">t</button>@if (n == 0)
            {
                <span id="a">a</span>
            }
            else if (n == 1)
            {
                <span id="b">b</span>
            }
            else
            {
                <span id="c">c</span>
            }</div>

            @code {
                private int n = 0;
                void Next() { n = (n + 1) % 3; }
            }
            """);

        Assert.Contains("const n = signal(0);", js);                                        // lifted by conditions
        Assert.Contains("document.createComment('')", js);                                  // the anchor
        Assert.Contains("() => (n.value === 0) ? [0] : (n.value === 1) ? [1] : [2]", js);   // ternary source
        Assert.Contains("(i) => i,", js);                                                   // key = branch index
        Assert.Contains("i === 0 ?", js);                                                   // dispatch create
        Assert.Contains("document.createElement('span')", js);                              // branch subtrees
        Assert.DoesNotContain("[else-not-yet-implemented]", js);
        Assert.DoesNotContain("when(", js);                                                 // no new primitive
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedIfElseJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.IfElseToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "IfElse.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>The snapshot is only a wall if it pins the actual contract: one anchor, the ternary
    /// index source, the index key, and the dispatch create.</summary>
    [Fact]
    public void EmittedIfElse_HonoursTheContract()
    {
        var js = File.ReadAllText(Generate.IfElseToTemp());
        Assert.Contains("document.createComment('')", js);
        Assert.Contains("() => (n.value === 0) ? [0] : (n.value === 1) ? [1] : [2]", js);
        Assert.Contains("(i) => i,", js);
        Assert.Contains("i === 0 ?", js);
        Assert.DoesNotContain("innerHTML", js);
        Assert.DoesNotContain("'@onclick'", js);   // descriptors resolved (decision 53)
    }

    /// <summary>Closed-runtime invariant: @else emits NO new runtime primitive (reuses list()).
    /// The anchor is a DOM builtin (document.createComment), not a runtime import.</summary>
    [Fact]
    public void EmittedIfElse_OnlyCallsClosedRuntimePrimitives_NoNewExport()
    {
        var js = File.ReadAllText(Generate.IfElseToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));

        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name),
                $"'{name}' is not a runtime export. @else must add NO new primitive (decision: reuse list()).");

        Assert.Contains("document.createComment(''", js);
        Assert.DoesNotContain("when", import);
    }

    /// <summary>
    /// A field read ONLY in an `else if` condition (never in the @if condition or a binding) must
    /// still lift to a signal — otherwise that branch never reacts. Locks MarkConditionReads walking
    /// the whole else-chain (DescendantNodes over the method body already does this).
    /// </summary>
    [Fact]
    public void EveryBranchCondition_LiftsItsField_NotJustTheFirst()
    {
        var js = Compile(
            """
            <div id="wrap"><button id="t" @onclick="Flip">t</button>@if (a)
            {
                <span id="x">x</span>
            }
            else if (b)
            {
                <span id="y">y</span>
            }</div>

            @code {
                private bool a = true;
                private bool b = false;
                void Flip() { a = !a; b = !b; }
            }
            """);

        Assert.Contains("const a = signal(true);", js);    // read by the @if condition
        Assert.Contains("const b = signal(false);", js);   // read ONLY by the else-if condition
        Assert.Contains("(b.value) ? [1] : []", js);       // no trailing @else -> [] when none match
    }

    /// <summary>Compile an inline .razor from samples/IfElse so the runtime specifier resolves.</summary>
    static string Compile(string razor)
    {
        var dir = Path.Combine(RepoPaths.Root, "samples", "IfElse");
        Directory.CreateDirectory(dir);
        var src = Path.Combine(dir, $".t-{Guid.NewGuid():N}.razor");
        var outPath = Path.Combine(dir, $".t-{Guid.NewGuid():N}.js");
        try
        {
            File.WriteAllText(src, razor);
            var (exit, _, stderr) = Run.Generator(src, outPath);
            Assert.True(exit == 0, $"the generator refused to emit:\n{stderr}");
            return File.ReadAllText(outPath);
        }
        finally
        {
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }
}
