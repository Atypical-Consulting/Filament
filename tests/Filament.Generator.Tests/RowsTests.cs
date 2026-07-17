using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// ROWS — the hard half of the POC, and the half decision 54 said was out of reach.
///
/// Everything here is asserted against the module the generator emits from
/// baseline/Rows.Blazor/RowsApp.razor — THE FILE BLAZOR COMPILES, not a Filament-flavoured
/// stand-in. samples/Rows/rows.js is the SPEC (decisions 21/51) and its header states FOUR
/// MAPPING DECISIONS; each one has a test below, because "it compiles" and "it compiles to
/// the thing the key specifies" are different claims and only the second one is the gate.
///
/// THE GATE TEST IS NOW GREEN (DECISIONS #80). It was RED for a phase on three shape
/// divergences between the generator and the key — none a translation bug — which #68/#76
/// disclosed as the OWNER's call before this step. The owner made the call: samples/Rows/rows.js
/// was CORRECTED to the rules the generator already applies (single-use handler inlining, `+=`
/// verbatim, and the four whitespace Text nodes Blazor ships). That is the answer key adopting
/// the generator's rule — decision 64's move, the baseline outranking the key — NOT the
/// generator being re-shaped to pass. canon now reports ALPHA-EQUIVALENT at 2309 B / 3480 B /
/// 920 tokens on both sides.
/// </summary>
public class RowsTests
{
    /// <summary>
    /// PHASE 3's GATE ON ROWS: "le JS emis pour Counter et Rows est equivalent au JS ecrit a
    /// la main en phase 1" (spec 6), under decision 51's mechanical definition.
    ///
    /// IT PASSES (DECISIONS #80). canon reports ALPHA-EQUIVALENT at 2309 B / 3480 B / 920
    /// tokens on both sides. It was RED for a phase on three shape divergences, each MEASURED
    /// by neutralising it alone (the table below is the record of that RED state):
    ///
    ///   #  what                                       minified   first canon token
    ///   -  ----------------------------------------   --------   -----------------
    ///      generated, as it stands                       2309 B   diverged at #342
    ///   1  handler inlining (decision 68)                2336 B   diverged at #399
    ///   2  compound assignment on a signal               2352 B   diverged at #505
    ///   3  the 4 whitespace Text nodes                   2200 B   ALPHA-EQUIVALENT
    ///      samples/Rows/rows.js (old, 3-divergence)      2200 B   (887 tokens)
    ///
    /// The translation itself was EXACT TO THE TOKEN throughout — the reassembly, the record,
    /// the escape analysis, list(), @key, the LCG, batch, the method order, the hoisting. The
    /// three disagreements were all shape, and all the OWNER's call. The owner ruled: correct
    /// the KEY, not the generator, so the resolution went toward BLAZOR (bigger, 2309 B), not
    /// toward the old key (2200 B). What changed in samples/Rows/rows.js:
    ///
    /// 1. THE HANDLER. rows.js used to emit `function run()`/`update()`/`swapRows()` and
    ///    reference them, though each is named by exactly one @onclick and called nowhere else.
    ///    The two answer keys specified DIFFERENT handler mappings; decision 68 disclosed that
    ///    in advance and left it to the owner. rows.js now INLINES the three single-use handlers
    ///    (decision 68's rule) and keeps `clear` a function because `run` also calls it.
    ///
    /// 2. COMPOUND ASSIGNMENT. rows.js used to expand `_rows[i].Label += " !!!"` to
    ///    `_rows[i].label.value = _rows[i].label.value + ' !!!'`, evaluating `_rows[i]` TWICE
    ///    where the C# evaluates it once. It now emits `_rows[i].label.value += ' !!!'` —
    ///    decision 68's "no syntactic desugaring", verbatim.
    ///
    /// 3. THE WHITESPACE TEXT NODES — decision 64's situation exactly: the ANSWER KEY had
    ///    diverged from BLAZOR. RowsApp.razor puts each &lt;button&gt; on its own line, and Razor
    ///    turns the newline+indent between siblings into a real text node — VERIFIED FROM
    ///    BLAZOR'S OWN GENERATED CODE:
    ///        __builder.AddMarkupContent(6,  "\n    ");
    ///        __builder.AddMarkupContent(11, "\n    ");
    ///        __builder.AddMarkupContent(16, "\n    ");
    ///        __builder.AddMarkupContent(21, "\n    ");
    ///    (obj/.../RowsApp_razor.g.cs, built from baseline/Rows.Blazor). Blazor ships four; the
    ///    old key built none. rows.js now builds all four. NOTE WHICH WAY THAT CUT: it makes the
    ///    module 152 B BIGGER and builds four DOM nodes — the correction costs Filament, which
    ///    is precisely why decision 21/51 forbade an IMPLEMENTER doing it and #64/#80 make it the
    ///    OWNER's to rule on the baseline's authority.
    ///
    /// STILL DO NOT EDIT samples/Rows/rows.js TO MAKE THIS PASS. Decision 21/51 stands: the key
    /// is the REFERENCE and the generator is what is JUDGED. #80 was the OWNER correcting the key
    /// against the BASELINE (as #64 did counter.js), not an implementer softening a gate — and
    /// the disclosed cost (a heavier hand-written filament-rows bundle, pending re-measurement)
    /// is what a correction against one's own interest looks like.
    /// </summary>
    [Fact]
    public void Gate_GeneratedRows_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.RowsToTemp();

        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.RowsAnswerKey);

        Assert.True(exit == 0,
            "PHASE 3 GATE (Rows): REGRESSED.\n\n" +
            "This gate is GREEN as of decision 80: the generated module was alpha-equivalent\n" +
            "to samples/Rows/rows.js. It no longer is, so either the generator changed or the\n" +
            "answer key was edited. Diff them and find out which.\n\n" +
            "The comparison:\n" + Indent(stdout) +
            (stderr.Length > 0 ? "\nstderr:\n" + Indent(stderr) : "") +
            "\n\nThis gate was RED for a phase on three OWNER-level divergences, all resolved in\n" +
            "decision 80 by CORRECTING THE KEY toward Blazor (see this test's doc comment):\n\n" +
            "  1. THE HANDLER      single-use `run`/`update`/`swapRows` inlined (decision 68);\n" +
            "                      `clear` kept a function because `run` calls it too.\n" +
            "  2. `+=` ON A SIGNAL `_rows[i].label.value += ' !!!'`, no syntactic desugaring.\n" +
            "  3. THE WHITESPACE   the four '\\n    ' Text nodes between the buttons that BLAZOR\n" +
            "                      SHIPS (AddMarkupContent(6/11/16/21, \"\\n    \")) — decision 64.\n\n" +
            "If you are re-opening one of those, that is an OWNER decision (21/51), not a fix.\n");
    }

    /// <summary>
    /// DECISION 54, ANSWERED. "Razor emits NO loop node: no scope, no balanced tree ... text
    /// meant to be spliced verbatim into a C# method body." That is still true of the IR; what
    /// changed is that the spans are REASSEMBLED and RE-PARSED, so this compiler sees a loop.
    ///
    /// The assertions are what a splice could never produce: a list() call whose source reads a
    /// version signal, a keyOf built from @key, and a row template that is a real function.
    /// </summary>
    [Fact]
    public void Foreach_CompilesToList_NotToASplice()
    {
        var js = File.ReadAllText(Generate.RowsToTemp());

        // list() -- the runtime half of @foreach, and one of the CLOSED runtime's exports.
        Assert.Contains("list(_el6, () => {", js);
        Assert.Contains("_rowsVersion.value;", js);
        Assert.Contains("return _rows;", js);
        Assert.Contains("createRow, null);", js);

        // @key -> keyOf, a PLAIN read (see KeyIsAPlainRead_NeverASignal).
        Assert.Contains("(row) => row.id", js);

        // the row template: called once per key, returns the row's root
        Assert.Contains("function createRow(row) {", js);
        Assert.Contains("return _el7;", js);

        // NOTHING of the C# survives as text. A splice is exactly what this would look like.
        var code = js[js.IndexOf("export function mount", StringComparison.Ordinal)..];
        Assert.DoesNotContain("foreach", code);
        Assert.DoesNotContain("Row row", code);
        Assert.DoesNotContain("_rows.Count", code);
        Assert.DoesNotContain("@key", code);
    }

    /// <summary>
    /// MAPPING DECISION (1): `List&lt;Row&gt; _rows` -> a MUTABLE ARRAY + a VERSION SIGNAL.
    ///
    /// The tempting mapping is Signal&lt;Row[]&gt; with copy-on-write, and rows.js's header rejects
    /// it with a number: Run() is Clear() + 1000 Add(), so copy-on-write turns 1000 amortised
    /// O(1) Adds into 1000 array copies -- ~500k element copies per #run against a C# List that
    /// does none. THAT is what these assertions are guarding, which is why the negative half
    /// matters as much as the positive: a compiler that emitted `_rows.value = [..._rows.value,
    /// row]` would pass "it renders" and hand C4's headline an asymptotic handicap Blazor never
    /// pays.
    /// </summary>
    [Fact]
    public void ListField_IsAMutableArrayPlusAVersionSignal_NeverCopyOnWrite()
    {
        var js = File.ReadAllText(Generate.RowsToTemp());

        Assert.Contains("const _rows = [];", js);
        Assert.Contains("const _rowsVersion = signal(0);", js);
        Assert.Contains("function _rowsChanged() {", js);
        Assert.Contains("_rowsVersion.value++;", js);

        // Add -> push, RemoveAt -> splice: the array's own O(1) tail operations, as in C#.
        Assert.Contains("_rows.push(row);", js);
        Assert.Contains("_rows.splice(i, 1);", js);

        // THE COPY-ON-WRITE MAPPING, REFUSED. No spread, no slice, no concat, no rebind.
        Assert.DoesNotContain("..._rows", js);
        Assert.DoesNotContain("_rows.slice", js);
        Assert.DoesNotContain("_rows.concat", js);
        Assert.DoesNotContain("signal([])", js);
        Assert.DoesNotContain("_rows.value", js);

        // `const _rows = []` is the ONE binding of that name: the array is never rebound, so
        // there is no copy for anything to be written to.
        Assert.Equal(1, Occurrences(js, "_rows ="));
    }

    /// <summary>
    /// MAPPING DECISION (2): `Row.Id` is a PLAIN field; `Row.Label` is a Signal. "A property is
    /// reactive iff it is assigned anywhere other than its object's construction site."
    ///
    /// THIS TEST IS THE ESCAPE ANALYSIS, and the analysis is load-bearing rather than tidy.
    /// AddRow's C# is
    ///     Row row = new Row(); row.Id = _nextId; row.Label = NextLabel();
    /// so a compiler that counts `row.Id = _nextId` as a write makes Id REACTIVE -- and @key
    /// compiles to keyOf, which reconcile() calls with the list effect ACTIVE, so that would
    /// subscribe the list to all 1000 row ids. rows.js's header calls the mapping FORCED for
    /// exactly this reason. The three assertions are the three quadrants, together, because
    /// each alone is satisfiable by a wrong compiler: "everything is a signal" passes the Label
    /// half, "nothing is a signal" passes the Id half.
    /// </summary>
    [Fact]
    public void RecordProperty_IsReactive_OnlyWhenAssignedOutsideItsConstructionSite()
    {
        var js = File.ReadAllText(Generate.RowsToTemp());

        // The construction site, FOLDED into one object literal -- which is what makes
        // `row.Id = _nextId` construction rather than a write. Field order is preserved: Id is
        // read from _nextId, THEN Label is drawn (3 LCG draws), THEN _nextId advances.
        Assert.Contains("const row = { id: _nextId, label: signal(nextLabel()) };", js);
        Assert.Contains("_nextId += 1;", js);

        // Id: assigned ONLY at the construction site -> PLAIN. No signal, no .value, and its
        // binding is a create-time write with no effect.
        Assert.Contains("insert(_el8, document.createTextNode(row.id));", js);
        Assert.DoesNotContain("row.id.value", js);
        Assert.DoesNotContain("signal(_nextId)", js);

        // Label: assigned by Update() outside any construction site -> SIGNAL, with the one
        // effect a row is allowed to cost.
        Assert.Contains("effect(() => setText(_tx0, row.label.value));", js);
        Assert.Contains("_rows[i].label.value", js);
    }

    /// <summary>
    /// The other half of decision (2), and the one that costs 1000 dependency edges to get
    /// wrong. @key compiles to list()'s keyOf; reconcile() calls keyOf with the list effect as
    /// the ACTIVE subscriber. A signal read there subscribes the whole list to every row's key.
    ///
    /// Asserted on the ARTIFACT (`(row) => row.id`, no `.value`) AND on the compiler's refusal,
    /// because the artifact alone only says this file is fine today.
    /// </summary>
    [Fact]
    public void KeyIsAPlainRead_NeverASignal_AndAReactiveKeyIsRefused()
    {
        Assert.Contains("(row) => row.id, createRow, null);", File.ReadAllText(Generate.RowsToTemp()));

        // A @key on a property Update() writes: the same shape, one field different.
        var (exit, _, stderr) = CompileSource(
            """
            <table><tbody id="tbody">
            @foreach (Row r in _rows)
            {
                <tr @key="r.Label"><td>@r.Label</td></tr>
            }
            </tbody></table>
            <button id="b" @onclick="Go">go</button>

            @code {
                record Row { public int Id { get; set; } public string Label { get; set; } = ""; }
                List<Row> _rows = new List<Row>();
                // The RemoveAt is load-bearing in this fixture: a list nothing mutates has no
                // version signal, so it is refused one check earlier and this test would then
                // pass without ever reaching the rule it is about.
                void Go() { _rows.RemoveAt(0); _rows[0].Label += "!"; }
            }
            """);

        Assert.NotEqual(0, exit);
        Assert.Contains("FIL0003: [reactive-key]", stderr);
        Assert.Contains("re-reconcile the entire table", stderr);
    }

    /// <summary>
    /// MAPPING DECISION (3): every @onclick body runs inside batch() -- Blazor's one-render-per-
    /// handler semantic. Without it Run() is quadratic a second way: each of the 1001 version
    /// bumps flushes its own full reconcile.
    ///
    /// All four handlers, because "it batches somewhere" is not the claim. The rule that
    /// produces this is CSharpFrontEnd.MayWriteMoreThanOnce -- batch iff there is more than one
    /// write to coalesce -- and it is the same rule that gives Counter NO batch. Both
    /// directions are pinned: CodeTests.Batch_IsEmittedOnlyWhenThereIsMoreThanOneWriteToCoalesce.
    /// </summary>
    [Fact]
    public void EveryHandler_RunsInsideBatch()
    {
        var js = File.ReadAllText(Generate.RowsToTemp());

        var listens = js.Split('\n').Where(l => l.TrimStart().StartsWith("listen(")).ToList();
        Assert.Equal(4, listens.Count);
        foreach (var l in listens)
            Assert.Contains("batch(", l);

        // A handler is ALWAYS an arrow, never a bare reference (decision 68): addEventListener
        // invokes its listener WITH the DOM Event, so `listen(el,'click',Handler)` calls a
        // zero-parameter C# method with one argument. Both answer keys agree on this.
        foreach (var l in listens)
            Assert.Contains("'click', () =>", l);
    }

    /// <summary>
    /// MAPPING DECISION (4): the word lists are module consts; THE LABELS ARE NOT.
    ///
    /// "Hoisting a label, interning a string, or reusing a previous run's stream is the cheat
    /// this whole POC exists to not commit." So this test asserts the cheat is ABSENT: the
    /// labels are produced by three LCG draws and a three-part concatenation, per row, every
    /// time -- 3000 + 1000 per #run, exactly as Blazor does them.
    ///
    /// The LCG stays in DOUBLE arithmetic, and that is not style: 16807 * 2^31 ~= 3.6e13 &lt; 2^53,
    /// so every intermediate product is exactly representable in BOTH languages and the two
    /// label streams are byte-identical. `| 0` or Math.imul would overflow differently and break
    /// the cross-language parity that is the entire point. MEASURED IN CHROME against
    /// bench/harness/expected-labels.json -- the golden C#-derived fixture -- on the generated
    /// module: first5, row1000 and the FIRST IDs of run 1 AND run 2 all byte-exact, and the
    /// second run's stream DIFFERS from the first (draws 3001..6000), which is what refuses an
    /// app that generates the stream once and reuses the interned strings.
    /// </summary>
    [Fact]
    public void WordListsAreHoisted_ButLabelsAreGeneratedPerRow()
    {
        var js = File.ReadAllText(Generate.RowsToTemp());
        var mount = js.IndexOf("export function mount", StringComparison.Ordinal);

        // The three word lists: module scope, inert data, ABOVE mount().
        foreach (var name in new[] { "_adjectives", "_colours", "_nouns" })
        {
            var at = js.IndexOf($"const {name} = [", StringComparison.Ordinal);
            Assert.True(at >= 0 && at < mount, $"{name} is not a module-scope const");
        }

        // The LCG: DOUBLE arithmetic, and (int) is Math.trunc -- the cast's real semantic.
        Assert.Contains("_seed = (_seed * 16807) % 2147483647;", js);
        Assert.Contains("Math.trunc((next() % 25))", js);
        Assert.Contains("Math.trunc((next() % 11))", js);
        Assert.Contains("Math.trunc((next() % 13))", js);
        Assert.DoesNotContain("| 0", js);
        Assert.DoesNotContain("Math.imul", js);
        Assert.DoesNotContain("Math.floor", js);

        // The label: three draws and a three-part concatenation, PER ROW, every time.
        Assert.Contains("return _adjectives[a] + ' ' + _colours[c] + ' ' + _nouns[n];", js);
        Assert.Contains("label: signal(nextLabel())", js);

        // THE CHEAT, ABSENT. _seed is seeded ONCE at field-initialiser time -- i.e. once per
        // page load -- and NOTHING re-seeds it per #run. If `_seed = 42` appeared anywhere but
        // its declaration, every #run would replay the same 1000 labels and #create-warm would
        // be timing a different workload from Blazor's.
        Assert.Equal(1, Occurrences(js, "_seed = 42"));
        Assert.Contains("let _seed = 42;", js);
    }

    /// <summary>
    /// The runtime is CLOSED (it sits at 1943 B against a 2048 B budget). Rows is the app that
    /// would justify a new primitive if anything did -- it is the one that needs keyed
    /// reconciliation -- so this is where the claim is worth pinning: it needs list(), which
    /// already exists, and nothing else.
    /// </summary>
    [Fact]
    public void EmittedJs_OnlyCallsClosedRuntimePrimitives()
    {
        var js = File.ReadAllText(Generate.RowsToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));

        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name),
                $"'{name}' is not one of the runtime's exports. A generator that needs a new primitive is a " +
                "FINDING to report, not headroom to spend (decision 42).");

        Assert.Equal(
            "import { signal, effect, batch, setText, setAttr, listen, insert, list } " +
            "from '../../src/filament-runtime/src/index.ts';",
            import);
    }

    /// <summary>
    /// THE SHARED DOM CONTRACT, exactly. rows.js: "Exactly two &lt;td&gt; children of &lt;tr&gt;, no stray
    /// text nodes between them, and the label inside a real &lt;a class="lbl"&gt;. Blazor builds those
    /// 1000 &lt;a&gt; elements and 2000 class attributes; skipping them would be a ~3000-node-per-#run
    /// discount on the exact number C4 is decided by."
    ///
    /// AND the four whitespace Text nodes between the buttons, which BLAZOR SHIPS
    /// (AddMarkupContent(6/11/16/21, "\n    "), read off its own generated BuildRenderTree) and
    /// which the answer key now ALSO builds (decision 80 corrected it — this WAS the gate's
    /// third divergence). They are asserted PRESENT here on purpose: they make Filament bigger,
    /// and pinning them stops them being quietly "fixed" into the free create-time advantage
    /// decision 20 lists as an open debt.
    /// </summary>
    [Fact]
    public void EmittedJs_HonoursTheSharedDomContract()
    {
        var js = File.ReadAllText(Generate.RowsToTemp());

        Assert.Contains("document.createElement('tr')", js);
        Assert.Contains("setAttr(_el8, 'class', 'col-md-1')", js);
        Assert.Contains("setAttr(_el9, 'class', 'col-md-4')", js);
        Assert.Contains("document.createElement('a')", js);
        Assert.Contains("setAttr(_el10, 'class', 'lbl')", js);
        Assert.Equal(2, js.Split("document.createElement('td')").Length - 1);

        // The buttons, and the table.
        foreach (var id in new[] { "run", "update", "swaprows", "clear", "tbody", "main" })
            Assert.Contains($".id = '{id}'", js);

        // Blazor's four "\n    " text nodes between the buttons. See the doc comment.
        Assert.Equal(4, js.Split(@"document.createTextNode('\n    ')").Length - 1);

        // Never textContent: that would destroy and rebuild children on every change.
        Assert.DoesNotContain("textContent", js);
        Assert.DoesNotContain("innerHTML", js);
        // The '@' must be gone, or the descriptors did not resolve (decision 53).
        Assert.DoesNotContain("'@onclick'", js);
    }

    /// <summary>
    /// Section 10: "Tests de snapshot sur le JS emis, ils sont la seule protection contre les
    /// regressions silencieuses." The canon gate is deliberately BLIND to naming, so it cannot
    /// answer "did the generator change behind our back". This can.
    /// </summary>
    [Fact]
    public void Snapshot_EmittedJs_MatchesApprovedBytes()
    {
        var actual = Norm(File.ReadAllText(Generate.RowsToTemp()));
        var approvedPath = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "Rows.approved.js");

        if (!File.Exists(approvedPath))
        {
            File.WriteAllText(approvedPath, actual);
            Assert.Fail($"No approved snapshot existed; wrote one to {approvedPath}. Review it and re-run.");
        }

        var approved = Norm(File.ReadAllText(approvedPath));
        if (approved == actual) return;

        var receivedPath = Path.ChangeExtension(approvedPath, ".received.js");
        File.WriteAllText(receivedPath, actual);

        var la = approved.Split('\n');
        var lb = actual.Split('\n');
        var first = "(files differ only in trailing whitespace)";
        for (var i = 0; i < Math.Max(la.Length, lb.Length); i++)
        {
            var x = i < la.Length ? la[i] : "<end of file>";
            var y = i < lb.Length ? lb[i] : "<end of file>";
            if (x == y) continue;
            first = $"first differing line {i + 1}:\n  approved: {x}\n  received: {y}\n";
            break;
        }

        Assert.Fail(
            "The emitted JS for Rows changed and the snapshot did not.\n\n" +
            "Section 10: snapshots are the ONLY protection against silent generator\n" +
            "regressions, so this is a wall, not a formality. Diff them, and only then\n" +
            "decide whether the generator or the snapshot is wrong:\n\n" +
            $"  approved: {approvedPath}\n" +
            $"  received: {receivedPath}\n\n" + first);
    }

    // ---- helpers -----------------------------------------------------------

    static int Occurrences(string haystack, string needle) => haystack.Split(needle).Length - 1;

    static string Norm(string s) => s.Replace("\r\n", "\n").TrimEnd() + "\n";

    static string Indent(string s) =>
        string.Join('\n', s.Replace("\r\n", "\n").TrimEnd().Split('\n').Select(l => "    " + l));

    /// <summary>Compile a .razor written inline, from the Rows sample dir so the specifier resolves.</summary>
    static (int exit, string stdout, string stderr) CompileSource(string razor)
    {
        var dir = Path.Combine(RepoPaths.Root, "samples", "Rows");
        var src = Path.Combine(dir, $".t-{Guid.NewGuid():N}.razor");
        var outPath = Path.Combine(dir, $".t-{Guid.NewGuid():N}.js");
        try
        {
            File.WriteAllText(src, razor);
            return Run.Generator(src, outPath);
        }
        finally
        {
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }
}
