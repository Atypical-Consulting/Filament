using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// PHASE 3'S GATE, THIRD TERM, VERBATIM: "une suite de 20 cas hors sous-ensemble produit
/// 20 diagnostics corrects."
///
/// This file IS that suite. It is drawn from the spec's OWN non-goals (section 3) and its
/// OWN subset boundary (section 5), one case per named construct: 26 out-of-subset cases,
/// covering the 20 the gate names, plus 1 case that is not out-of-subset but NOT C# at all
/// (Gate/NotCSharp.razor), disclosed separately below rather than counted as a 27th construct.
///
/// WHAT "CORRECT" MEANS HERE, AND WHY IT IS FOUR ASSERTIONS AND NOT ONE.
/// "Un faux positif silencieux est un bug bloquant, une erreur claire ne l'est pas." So the
/// claim under test is NOT "20 errors appear" -- a compiler that refuses every input passes
/// that trivially and is worthless. It is:
///     1. REFUSED            exit != 0
///     2. NO FILE            downstream always believes the file
///     3. RIGHT CODE + PLACE file(line,col) and one of the spec's three codes
///     4. RIGHT REASON       the tag names the rule that actually fired
/// and, in NegativeControls below, that NOTHING IN SUBSET IS REJECTED. The fifth assertion
/// is the one that gives the other four meaning.
///
/// EVERY LINE, COLUMN, CODE AND REASON BELOW WAS READ OFF THE GENERATOR, NEVER REASONED
/// ABOUT. This repo has twice recorded a rule that fired for a reason nobody measured.
///
/// THIS SUITE FOUND FIVE REAL DEFECTS IN THE COMPILER IT WAS WRITTEN TO TEST -- which is the
/// whole argument for writing it before believing anything. Every one of them is decision 41's
/// pattern: a guard that existed on the path some repro pointed at, with the identical hole one
/// frame over.
///
///   1. USER-DEFINED GENERICS WERE NOT GATED AT ALL (fixed: CSharpFrontEnd, [unsupported-
///      generic]). Method() checked the return type and the parameter types, so `T Echo&lt;T&gt;(T)`
///      was refused -- and `void Noop&lt;T&gt;() {}` called as `Noop&lt;System.DateTime&gt;()` was NOT.
///      Measured: exit 0, module written, `function noop() {}` + `noop()`, the type argument
///      ERASED IN SILENCE, with System.DateTime -- a type section 5 does not admit -- never
///      reaching CheckType. Generics are a section 3 NON-GOAL. Fixed on the CONSTRUCT (the type
///      parameter list), not on the types it happens to mention. GenericErasure.razor is that
///      defect's regression test.
///
///   2. ROOT-LEVEL TEMPLATE C# WORE FIL-WIRING (fixed: TemplateCompiler, first [template-code-at-
///      root], now COMPILES onto target -- decision 89, #77's third false positive closed).
///      See NegativeControls.TemplateControlFlowAtRoot_NowCompiles_AttachingToTarget.
///
///   3. EVERY C# ATTRIBUTE WAS SILENTLY ERASED, ON ALL THREE MEMBER PATHS (fixed:
///      CheckNoAttributes, [unsupported-attribute]). `[CascadingParameter] public int Depth = 0;`
///      -> exit 0, `const depth = 0;`. AND IT WAS THIS SUITE'S OWN BLIND SPOT, not just the
///      compiler's: Gate/CascadingParameter.razor was GREEN throughout, because it declares the
///      parameter as a PROPERTY and Member() refuses properties. The case passed for a reason
///      that had nothing to do with cascading, so a mandated spec 3 non-goal was covered in name
///      only. Gate/CascadingParameterField.razor is the field form the guard never saw.
///
///   4. SEMANTIC C# ERRORS WERE NEVER ASKED FOR (fixed: CheckSemantics, [not-csharp]).
///      `private int currentCount = "a string";` -> exit 0, module written. Compile() checked
///      Roslyn's SYNTAX diagnostics under a comment reading "a block that does not parse must
///      never be compiled past", and never asked for its SEMANTIC ones. Gate/NotCSharp.razor.
///
///   5. A DIAGNOSTIC THAT LIED (fixed: Invocation, [not-csharp]). See
///      ACallNamingADeclaredMethod_IsNotReportedAsUndeclared.
///
/// 3 and 4 are the ones this suite existed to catch and did not, until it was pointed at its own
/// coverage instead of at its case count. Both were found by asking, of each mandated case, "what
/// rule ACTUALLY fired here?" rather than "is it green?".
/// </summary>
public class GateSubsetTests
{
    /// <summary>
    /// THE CASES. Every row names the spec clause it is drawn from, the fixture, and the
    /// (code, line, col, reason) the generator ACTUALLY produced.
    ///
    /// THE REASON COLUMN IS DELIBERATE DISCLOSURE, AND IT IS WHERE DEFECT 3 WAS HIDING. 27 cases
    /// do not exercise 27 rules -- they exercise 12. Five different section 3 non-goals all land
    /// on [unsupported-directive], and five statements all land on [unsupported-statement]. That
    /// is not padding and it is not hidden: a suite whose case count is inflated by cases that
    /// collapse onto one rule looks broader than it is, so the collapse is written where a reader
    /// can count it.
    ///
    /// It is also the STRONGEST TEST IN THE FILE, and the reason is worth stating: reading this
    /// column construct-by-construct and asking "is THAT the rule that should have fired?" is what
    /// exposed the cascading-parameter case as coverage in name only -- it was refused for being a
    /// property. Green told nobody anything. The reason column did.
    /// </summary>
    [Theory]
    // ---- spec 3 NON-GOALS ---------------------------------------------------
    [InlineData("Gate/AsyncTask.razor", 6, 13, "FIL0001", "unsupported-modifier")]
    [InlineData("Gate/Linq.razor", 9, 24, "FIL0001", "unsupported-call")]
    [InlineData("Gate/GenericMethod.razor", 6, 19, "FIL0001", "unsupported-generic")]
    [InlineData("Gate/GenericErasure.razor", 7, 22, "FIL0001", "unsupported-generic")]
    [InlineData("Inherits.razor", 1, 1, "FIL0003", "unsupported-directive")]
    [InlineData("Gate/Implements.razor", 1, 1, "FIL0003", "unsupported-directive")]
    [InlineData("Inject.razor", 1, 1, "FIL0003", "unsupported-directive")]
    [InlineData("Page.razor", 1, 1, "FIL0003", "unsupported-directive")]
    [InlineData("Gate/Typeparam.razor", 1, 1, "FIL0003", "unsupported-directive")]
    [InlineData("Gate/Forms.razor", 1, 1, "FIL0003", "unresolved-component")]
    [InlineData("Gate/EventCallback.razor", 5, 13, "FIL0002", "unsupported-type")]
    [InlineData("Gate/RenderFragment.razor", 5, 13, "FIL0002", "unsupported-type")]
    // THE TWO CASCADING-PARAMETER ROWS ARE REFUSED BY DIFFERENT RULES, AND THAT IS THE WHOLE
    // POINT OF HAVING BOTH. The property form is caught for being a PROPERTY -- a verdict with
    // nothing to do with cascading -- and while it was the suite's only coverage of this spec 3
    // non-goal, the FIELD form compiled at exit 0 to `const depth = 0;`. See CheckNoAttributes.
    //
    // The property form now raises TWO diagnostics, both true and both at (6,5), because both
    // rules genuinely fire on it. This row asserts one of the two; the pair is pinned in full by
    // TheCascadingParameterProperty_RaisesBothTrueDiagnostics, so neither can go unnoticed.
    [InlineData("Gate/CascadingParameter.razor", 6, 5, "FIL0001", "unsupported-member")]
    [InlineData("Gate/CascadingParameterField.razor", 15, 5, "FIL0001", "unsupported-attribute")]
    [InlineData("Gate/JsInterop.razor", 5, 13, "FIL0002", "unsupported-type")]
    // ---- spec 5's TYPE list -------------------------------------------------
    [InlineData("Code/TypeDateTime.razor", 5, 13, "FIL0002", "unsupported-type")]
    // ---- spec 5's STATEMENT list --------------------------------------------
    [InlineData("Code/While.razor", 8, 9, "FIL0001", "unsupported-statement")]
    [InlineData("Gate/DoWhile.razor", 8, 9, "FIL0001", "unsupported-statement")]
    [InlineData("Code/Switch.razor", 8, 9, "FIL0001", "unsupported-statement")]
    [InlineData("Code/Goto.razor", 8, 9, "FIL0001", "unsupported-statement")]
    [InlineData("Code/TryCatch.razor", 8, 9, "FIL0001", "unsupported-statement")]
    // ---- spec 5's EXPRESSION list -------------------------------------------
    //
    // The two lambda rows are refused by DIFFERENT rules and that is the point of having
    // both: the template handler is caught by the binding gate (a handler must NAME a
    // method), the @code local is caught by its declared TYPE (System.Func<int>) before the
    // lambda body is ever reached. Asserted as measured, not as first guessed.
    [InlineData("HandlerLambda.razor", 1, 34, "FIL0003", "compound-expression")]
    [InlineData("Code/Lambda.razor", 8, 9, "FIL0002", "unsupported-type")]
    [InlineData("BindingUnresolved.razor", 1, 30, "FIL0001", "unresolved-name")]
    [InlineData("Code/ConsoleCall.razor", 8, 9, "FIL0001", "unsupported-call")]
    // List<T> outside {indexing, .Count, .Add, .RemoveAt}. `_items.Clear()` is refused by
    // ListMutation's own switch, NOT by Invocation's -- a different rule from ConsoleCall
    // above, which is why both are here.
    [InlineData("Gate/ListOp.razor", 9, 9, "FIL0001", "unsupported-call")]
    // ---- NOT out-of-subset C#: NOT C# ---------------------------------------
    //
    // DISCLOSED AS A DIFFERENT CATEGORY, not counted as a 21st construct. Section 5 is a
    // subset OF C#, so "is this C#" is a question underneath it -- and it was unasked: every
    // rule above is an allowlist over syntax, and an allowlist cannot notice that `int x =
    // "s"` type-checks nowhere. It is here because it produced the same forbidden outcome as
    // the 20 -- a plausible module at exit 0 -- by the one route none of them watches.
    [InlineData("Gate/NotCSharp.razor", 16, 32, "FIL0001", "not-csharp")]
    public void OutOfSubset_ProducesOneCorrectLocatedDiagnostic_AndWritesNoFile(
        string fixture, int line, int col, string code, string reason)
    {
        var outPath = InRepo();
        try
        {
            var (exit, _, stderr) = Run.Generator(Path.Combine(RepoPaths.Unsupported, fixture), outPath);

            // 1. REFUSED.
            Assert.True(exit != 0,
                $"{fixture} was COMPILED, not refused -- the silent mis-compile section 10 forbids.\n" +
                (File.Exists(outPath) ? "it emitted:\n" + File.ReadAllText(outPath) : ""));

            // 2. NO FILE. A generator that reports an error and still writes the module
            //    leaves the build to decide whether to believe the exit code, and something
            //    downstream always believes the file.
            Assert.False(File.Exists(outPath),
                "the generator refused AND wrote the module anyway; downstream always believes the file");

            // 3 + 4. RIGHT CODE, RIGHT PLACE, RIGHT REASON.
            Assert.Contains($"{Path.GetFileName(fixture)}({line},{col}): {code}: [{reason}]", stderr);

            // The tool's own failures must not wear a spec code, and -- the mirror, which
            // this suite caught for real -- the author's input must not wear the TOOL's.
            Assert.DoesNotContain("FIL-WIRING", stderr);
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    /// <summary>
    /// EVERY refusal in the suite carries a real location and one of the SPEC's three codes.
    /// Quantified over the whole set rather than named per construct, so a rule added later
    /// with a null span or an invented code fails here the day it is added.
    /// </summary>
    [Theory]
    [InlineData("Gate/AsyncTask.razor")]
    [InlineData("Gate/Linq.razor")]
    [InlineData("Gate/GenericMethod.razor")]
    [InlineData("Gate/GenericErasure.razor")]
    [InlineData("Gate/Implements.razor")]
    [InlineData("Gate/Typeparam.razor")]
    [InlineData("Gate/Forms.razor")]
    [InlineData("Gate/EventCallback.razor")]
    [InlineData("Gate/RenderFragment.razor")]
    [InlineData("Gate/CascadingParameter.razor")]
    [InlineData("Gate/CascadingParameterField.razor")]
    [InlineData("Gate/NotCSharp.razor")]
    [InlineData("Gate/JsInterop.razor")]
    [InlineData("Gate/DoWhile.razor")]
    [InlineData("Gate/ListOp.razor")]
    public void EveryGateDiagnostic_IsLocated_AndCarriesOneOfTheSpecsThreeCodes(string fixture)
    {
        var outPath = InRepo();
        try
        {
            var (_, _, stderr) = Run.Generator(Path.Combine(RepoPaths.Unsupported, fixture), outPath);

            var errors = stderr.Split('\n').Where(l => l.TrimStart().StartsWith("error ")).ToList();
            Assert.NotEmpty(errors);

            foreach (var line in errors)
            {
                Assert.DoesNotContain("<no source span>", line);
                Assert.Matches(
                    $@"{System.Text.RegularExpressions.Regex.Escape(Path.GetFileName(fixture))}\(\d+,\d+\): FIL000[123]: \[",
                    line);
                Assert.DoesNotContain("FIL-WIRING", line);
            }
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    /// <summary>
    /// `[CascadingParameter] public int Depth { get; set; }` breaks TWO of section 5's rules at
    /// once -- it carries an attribute and it is a property -- and it now says so twice, at the
    /// same (6,5), because a PropertyDeclaration's span STARTS at its attribute list.
    ///
    /// Pinned rather than tidied away. Two true diagnostics for one declaration is noise, and
    /// noise is a cost; a suite that asserts Contains() on one of them and never mentions the
    /// other is how a second rule's behaviour goes unrecorded until someone is surprised by it.
    /// Deduplicating by location would be the wrong fix -- it would drop a TRUE statement about
    /// a DIFFERENT rule, and the two rules are independent by design.
    /// </summary>
    [Fact]
    public void TheCascadingParameterProperty_RaisesBothTrueDiagnostics()
    {
        var outPath = InRepo();
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Gate/CascadingParameter.razor"), outPath);

            Assert.NotEqual(0, exit);
            Assert.False(File.Exists(outPath));

            var errors = stderr.Split('\n').Where(l => l.TrimStart().StartsWith("error ")).ToList();
            Assert.Equal(2, errors.Count);
            Assert.Contains("CascadingParameter.razor(6,5): FIL0001: [unsupported-attribute]", stderr);
            Assert.Contains("CascadingParameter.razor(6,5): FIL0001: [unsupported-member]", stderr);
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    static string InRepo() =>
        Path.Combine(RepoPaths.Root, "samples", "Counter", $".gate-{Guid.NewGuid():N}.js");
}

/// <summary>
/// THE HALF THAT GIVES THE OTHER HALF ITS MEANING.
///
/// "A diagnostic suite that rejects everything trivially passes '20 diagnostics' and is
/// worthless." So section 5's subset is exercised construct by construct, and each of these
/// MUST compile clean. One construct per test, so a false positive is attributable to it
/// rather than to a soup.
/// </summary>
public class NegativeControls
{
    /// <summary>Section 5's four scalar types, declared, read by the template, and assigned.</summary>
    [Fact]
    public void Section5_ScalarTypes_CompileClean()
    {
        var js = Compiles(
            """
            <p><span id="a">@count</span><span id="b">@ratio</span><span id="c">@flag</span><span id="d">@name</span></p>
            <button id="go" @onclick="Go">go</button>

            @code {
                private int count = 0;
                private double ratio = 1.5;
                private bool flag = true;
                private string name = "x";

                private void Go()
                {
                    count = count + 1;
                    ratio = ratio * 2.0;
                    flag = !flag;
                    name = "y";
                }
            }
            """);
        Assert.Contains("signal(0)", js);
        Assert.Contains("signal(1.5)", js);
        Assert.Contains("signal(true)", js);
        Assert.Contains("signal('x')", js);
    }

    /// <summary>
    /// Section 5's expressions: arithmetic and comparison operators, &amp;&amp;, ||, !,
    /// ternary, string interpolation. Double division is covered by
    /// Section5_DoubleDivision_CompilesClean; integer division by
    /// IntegerDivision_CompilesToMathTrunc (both in §5, decision 101).
    /// </summary>
    [Fact]
    public void Section5_Operators_CompileClean()
    {
        var js = Compiles(
            """
            <p><span id="a">@label</span></p>
            <button id="go" @onclick="Go">go</button>

            @code {
                private int n = 0;
                private string label = "";

                private void Go()
                {
                    int a = n + 1 - 2 * 3 % 4;
                    bool b = a < 1 && a <= 2 || a > 3 && a >= 4;
                    bool c = !(a == 1) && a != 2;
                    int d = b ? 1 : 0;
                    label = $"a={a} b={b} c={c} d={d}";
                    n = a + d;
                }
            }
            """);
        // C#'s == on the subset's types is value equality with no coercion; === is that.
        Assert.Contains("===", js);
        Assert.Contains("!==", js);
        Assert.Contains("`a=${", js);
    }

    /// <summary>
    /// SECTION 5 ADMITS "arithmetic operators" AND DOUBLE DIVISION IS NOW ONE OF THEM.
    /// C#'s double `/` and JS's `/` are the same IEEE-754 op, so `r / 2.0` compiles and emits `/`.
    /// The divergent-input measurement (baseline/Divide.Blazor: 7.0/2.0 = 3.5, a value integer
    /// division could never produce) is what proves the emitted `/` renders Blazor's number; this
    /// control only pins that it IS emitted, not refused.
    /// </summary>
    [Fact]
    public void Section5_DoubleDivision_CompilesClean()
    {
        var js = Compiles(
            """
            <p><span id="a">@r</span></p>
            <button id="go" @onclick="Go">go</button>

            @code {
                private double r = 7.0;
                private void Go() { r = r / 2.0; }
            }
            """);
        // The division is emitted as faithful JS `/` on the lifted signal, not refused. The `2.0`
        // literal normalises to `2`; JS `/` is float division either way (7 / 2 === 3.5).
        Assert.Contains("r.value / 2", js);
        Assert.DoesNotContain("Math.trunc", js);   // NOT integer division
    }

    /// <summary>
    /// SECTION 5 ADMITS "component composition with scalar parameters" AND A [Parameter] SCALAR
    /// PROPERTY IS THE PARAMETER DECLARATION. A component declaring one compiles clean (it emits
    /// nothing on its own -- the value comes from a parent at the composition site). This is the
    /// narrow carve-out from §5's no-properties (#85) / no-attributes (#77) rules.
    /// </summary>
    [Fact]
    public void Section5_ComponentParameterProperty_CompilesClean()
        => Compiles(
            """
            <p><span id="a">@count</span></p>

            @code {
                [Parameter] public string Label { get; set; } = "";
                private int count = 0;
            }
            """);

    /// <summary>Section 5's List&lt;T&gt;: indexing, .Count, .Add, .RemoveAt -- all four.</summary>
    [Fact]
    public void Section5_ListOperations_CompileClean()
    {
        var js = Compiles(
            """
            <p><span id="a">@total</span></p>
            <button id="go" @onclick="Go">go</button>

            @code {
                private int total = 0;
                private List<int> _xs = new List<int>();

                private void Go()
                {
                    _xs.Add(1);
                    total = _xs.Count + _xs[0];
                    _xs.RemoveAt(0);
                }
            }
            """);
        Assert.Contains("_xs.push(1);", js);
        Assert.Contains("_xs.length", js);
        Assert.Contains("_xs.splice(0, 1);", js);
        Assert.Contains("_xs[0]", js);
    }

    /// <summary>
    /// Section 5's statements: local declaration, assignment, compound assignment, if/else,
    /// for, foreach, and a call to a method declared in the same component.
    /// </summary>
    [Fact]
    public void Section5_Statements_CompileClean()
    {
        var js = Compiles(
            """
            <p><span id="a">@total</span></p>
            <button id="go" @onclick="Go">go</button>

            @code {
                private int total = 0;
                private List<int> _xs = new List<int>();

                private int Twice(int v)
                {
                    return v * 2;
                }

                private void Go()
                {
                    int s = 0;
                    s += 1;
                    s -= 2;
                    s *= 3;
                    s %= 4;
                    if (s > 0) { s = 1; } else { s = 2; }
                    for (int i = 0; i < 3; i++) { s += Twice(i); }
                    foreach (int x in _xs) { s += x; }
                    total = s;
                }
            }
            """);
        Assert.Contains("function twice(v)", js);
        Assert.Contains("for (let i = 0; i < 3; i++)", js);
        Assert.Contains("for (const x of _xs)", js);
        Assert.Contains("} else {", js);
    }

    /// <summary>Section 5's Razor: @foreach + @key over a List&lt;T&gt; of a local record.</summary>
    [Fact]
    public void Section5_ForeachKeyOverALocalRecord_CompilesClean()
    {
        var js = Compiles(
            """
            <button id="go" @onclick="Go">go</button>
            <div id="wrap">
            @foreach (Row row in _rows)
            {
                <span @key="row.Id">@row.Label</span>
            }
            </div>

            @code {
                record Row
                {
                    public int Id { get; set; }
                    public string Label { get; set; } = "";
                }

                private List<Row> _rows = new List<Row>();

                private void Go()
                {
                    Row row = new Row();
                    row.Id = 1;
                    row.Label = "a";
                    _rows.Add(row);
                }
            }
            """);
        Assert.Contains("list(", js);
    }

    /// <summary>Section 5's Razor: @oninput, alongside @onclick.</summary>
    [Fact]
    public void Section5_OnInput_CompilesClean()
    {
        var js = Compiles(
            """
            <input id="i" @oninput="Go" />
            <p><span id="a">@count</span></p>

            @code {
                private int count = 0;
                private void Go() { count++; }
            }
            """);
        Assert.Contains("'input'", js);
    }

    /// <summary>
    /// THE TWO REAL APPS, which are the only negative control that cannot be accused of
    /// being written to pass. If the suite's guards rejected either of these, the guards
    /// would be wrong and the whole gate would be worthless.
    /// </summary>
    [Fact]
    public void TheTwoRealApps_StillCompileFromPureRazor()
    {
        foreach (var app in new[] { RepoPaths.CounterRazor, RepoPaths.RowsRazor })
        {
            var outPath = Path.Combine(RepoPaths.Root, "samples", "Counter", $".neg-{Guid.NewGuid():N}.js");
            try
            {
                var (exit, _, stderr) = Run.Generator(app, outPath);
                Assert.True(exit == 0, $"{app} -- the file Blazor compiles -- was REFUSED:\n{stderr}");
                Assert.True(File.Exists(outPath), "the generator exited 0 and wrote nothing");
            }
            finally
            {
                if (File.Exists(outPath)) File.Delete(outPath);
            }
        }
    }

    /// <summary>
    /// A DIAGNOSTIC MUST NOT SAY SOMETHING FALSE, EVEN WHEN ITS VERDICT IS RIGHT.
    ///
    /// `private int seed = Compute();` with Compute() declared THREE LINES BELOW is invalid C#
    /// (CS0236: a field initializer cannot reference an instance method), so refusing it is the
    /// right verdict. But the reason measured was "'Compute()' is not a call to a method declared
    /// in this component" -- and it IS declared in this component. Roslyn returns Symbol == null
    /// for a call it bound and rejected, and Invocation() read that as "not ours".
    ///
    /// Decision 69's third defect in its second form: there the author was blamed for a `using`
    /// the compiler omitted; here for a rule they had obeyed. The verdict was never the bug.
    /// </summary>
    [Fact]
    public void ACallNamingADeclaredMethod_IsNotReportedAsUndeclared()
    {
        var (exit, stderr, wrote) = Compile(
            """
            <p><span id="a">@seed</span></p>

            @code {
                private int seed = Compute();

                private int Compute()
                {
                    return 7;
                }
            }
            """);

        Assert.NotEqual(0, exit);
        Assert.False(wrote, "a refusal must write no file");

        // The lie, pinned so it cannot come back.
        Assert.DoesNotContain("is not a call to a method declared in this component", stderr);
        // The truth: C#'s own verdict, carried verbatim.
        Assert.Contains("[not-csharp]", stderr);
        Assert.Contains("CS0236", stderr);
        Assert.Matches(@"\(\d+,\d+\): FIL0001", stderr);
    }

    /// <summary>
    /// THE CONTROL FOR THE ONE ABOVE, and the reason it is not a licence to stop refusing calls:
    /// a call to something this component really does NOT declare must keep the original reason.
    /// Without this, "do not say 'not declared here'" would be satisfied by never saying it.
    /// </summary>
    [Fact]
    public void ACallToAMethodOnAnotherClass_StillSaysItIsNotDeclaredHere()
    {
        var (exit, stderr, wrote) = Compile(
            """
            <p><span id="a">@count</span></p>
            <button id="go" @onclick="Go">go</button>

            @code {
                private int count = 0;
                private void Go() { System.Console.WriteLine(count); }
            }
            """);

        Assert.NotEqual(0, exit);
        Assert.False(wrote, "a refusal must write no file");
        Assert.Contains("FIL0001: [unsupported-call]", stderr);
        Assert.Contains("is not a call to a method declared in this component", stderr);
    }

    // ---- DISCLOSED FALSE POSITIVES -----------------------------------------
    //
    // These are in-subset constructs the generator REJECTS. They are recorded here as
    // tests rather than left to be rediscovered, because "si un critere devient
    // inatteignable, le dire immediatement plutot que de deplacer le seuil". Each asserts
    // the CURRENT behaviour, so closing one of them goes RED and is closed DELIBERATELY.
    //
    // None of them is a SILENT false positive: every one is a loud, located refusal that
    // writes no file -- "une erreur claire", which the working rules permit. They are
    // reported, not banked.

    /// <summary>
    /// INTEGER DIVISION ENTERED §5 (decision 101, closing #87's deferral). C#'s int/int truncates toward
    /// zero (7/2 == 3) where JS's `/` yields 3.5, so the FAITHFUL lowering is Math.trunc(a / b) -- which
    /// restores the truncation instead of emitting the silently-wrong bare `/`. `n = n / 2` compiles to
    /// `n.value = Math.trunc(n.value / 2)`, NOT `n.value = n.value / 2`.
    ///
    /// DOUBLE division (Section5_DoubleDivision_CompilesClean) still maps to `/` verbatim (same IEEE-754
    /// op). Both numeric divisions are now in §5; long/decimal division stay refused (types not in §5).
    /// </summary>
    [Fact]
    public void IntegerDivision_CompilesToMathTrunc()
    {
        var js = Compiles(
            """
            <p><span id="a">@n</span></p>
            <button id="go" @onclick="Go">go</button>

            @code {
                private int n = 0;
                private void Go() { n = n / 2; }
            }
            """);

        Assert.Contains("Math.trunc(n.value / 2)", js);     // faithful truncation, not bare `/`
        Assert.DoesNotContain("[unsupported-expression]", js);
    }

    /// <summary>
    /// STATIC-LEAF COMPOSITION IS NOW IN §5 (decision 88; see ComposeTests). What stays refused is
    /// composition the slice does NOT cover -- here &lt;MyWidget Count="3" /&gt; has no sibling
    /// MyWidget.razor to resolve, so it refuses [unresolved-component], loud and located, no file.
    /// (Were the sibling present, Count="3" would ALSO be out of slice: an int parameter is deferred,
    /// the slice folds STRING params only.) The old blanket "all composition is refused" is gone.
    /// </summary>
    [Fact]
    public void UnresolvedComponent_IsRefused_LoudAndLocated()
    {
        var (exit, stderr, wrote) = Compile(
            """
            <p><span id="a">@count</span></p>
            <MyWidget Count="3" />

            @code {
                private int count = 0;
            }
            """);

        Assert.NotEqual(0, exit);
        Assert.False(wrote, "a refusal must write no file");
        Assert.Contains("FIL0003: [unresolved-component]", stderr);
        Assert.Matches(@"\(\d+,\d+\): FIL0003", stderr);
    }

    /// <summary>
    /// SECTION 5 ADMITS @if AND @foreach, AND WHETHER THEY COMPILE DEPENDS ON WHETHER THEY
    /// ARE WRAPPED IN AN ELEMENT. This one was a REAL DEFECT, found by this suite, and the
    /// diagnostic half of it is FIXED; the mapping half is an open owner call.
    ///
    /// MEASURED, before the fix -- the SAME loop, differing only by a wrapping &lt;div&gt;:
    ///     &lt;div&gt;@foreach (int x in _xs) { &lt;span @key="x"&gt;@x&lt;/span&gt; }&lt;/div&gt;
    ///         -> exit 0, compiles.
    ///     @foreach (int x in _xs) { &lt;span @key="x"&gt;@x&lt;/span&gt; }        (at the root)
    ///         -> "FIL-WIRING: ... This is the TOOL being broken, not the input."
    ///            NO location. NO spec code. For ordinary, in-subset Razor.
    ///
    /// Cause: Collect() is called on each CHILD of the render method and looks for C# among
    /// THAT child's kids, so the method's own child list is never a container and root-level
    /// C# is never planned into a region. The emitter's `case CSharpCodeIntermediateNode`
    /// then fired a throw whose comment asserted an invariant -- "every CSharpCodeIntermediate-
    /// Node is a region item by construction" -- that the code did not have. Neither answer
    /// key has root-level control flow (Counter has none; Rows' @foreach is inside &lt;tbody&gt;),
    /// so it never showed.
    ///
    /// NOW CLOSED (decision 89, #77's THIRD and last false positive). It WAS a located FIL0003
    /// [template-code-at-root] with the mapping deliberately not invented; that mapping has since
    /// been decided and MEASURED (BENCH n°11): when the root itself holds template C#, the METHOD
    /// is the region container and its control flow maps onto mount()'s target -- the same
    /// `list(target, ...)` an in-element @foreach/@if emits, only against the mount point. The
    /// SAME snippet that used to refuse now COMPILES, and this control flipped with it. The
    /// still-refused root cases (bare code blocks, not @foreach/@if) carry the more specific
    /// [unsupported-template-statement] from RegionOps -- see RootControlFlowTests.
    /// </summary>
    [Fact]
    public void TemplateControlFlowAtRoot_NowCompiles_AttachingToTarget()
    {
        var (exit, stderr, emitted) = CompileEmitting(
            """
            <button id="go" @onclick="Go">go</button>
            @foreach (int x in _xs)
            {
                <span @key="x">@x</span>
            }

            @code {
                private List<int> _xs = new List<int>();
                private void Go() { _xs.Add(1); }
            }
            """);

        Assert.True(exit == 0, $"root control flow should compile now (decision 89):\n{stderr}");

        // The author's own Razor is neither tool-blamed nor refused; its list reconciles
        // against target, the mount point, not a created wrapper element.
        Assert.DoesNotContain("FIL-WIRING", stderr);
        Assert.DoesNotContain("[template-code-at-root]", stderr);
        Assert.Contains("list(target,", emitted);
    }

    /// <summary>
    /// THE CONTROL FOR THE ONE ABOVE: the identical loop, nested one element deep, still
    /// compiles. Without this, "root-level @foreach is refused" would be satisfied by a
    /// compiler that refuses @foreach everywhere.
    /// </summary>
    [Fact]
    public void TheSameForeach_NestedOneElementDeep_StillCompilesClean()
    {
        var js = Compiles(
            """
            <button id="go" @onclick="Go">go</button>
            <div id="wrap">
            @foreach (int x in _xs)
            {
                <span @key="x">@x</span>
            }
            </div>

            @code {
                private List<int> _xs = new List<int>();
                private void Go() { _xs.Add(1); }
            }
            """);
        Assert.Contains("list(", js);
    }

    // ---- helpers -----------------------------------------------------------

    /// <summary>Compile inline .razor that MUST succeed, and hand back the emitted module.</summary>
    static string Compiles(string razor)
    {
        var dir = Path.Combine(RepoPaths.Root, "samples", "Counter");
        var src = Path.Combine(dir, $".n-{Guid.NewGuid():N}.razor");
        var outPath = Path.Combine(dir, $".n-{Guid.NewGuid():N}.js");
        try
        {
            File.WriteAllText(src, razor);
            var (exit, _, stderr) = Run.Generator(src, outPath);
            Assert.True(exit == 0,
                "IN-SUBSET CODE WAS REJECTED. Section 5 admits this construct; refusing it is a false " +
                "positive, and a suite that rejects everything passes '20 diagnostics' and is worthless.\n" +
                stderr);
            return File.ReadAllText(outPath);
        }
        finally
        {
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    /// <summary>Compile inline .razor whose outcome is under test.</summary>
    static (int exit, string stderr, bool wrote) Compile(string razor)
    {
        var dir = Path.Combine(RepoPaths.Root, "samples", "Counter");
        var src = Path.Combine(dir, $".n-{Guid.NewGuid():N}.razor");
        var outPath = Path.Combine(dir, $".n-{Guid.NewGuid():N}.js");
        try
        {
            File.WriteAllText(src, razor);
            var (exit, _, stderr) = Run.Generator(src, outPath);
            return (exit, stderr, File.Exists(outPath));
        }
        finally
        {
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    /// <summary>Like Compile, but hands back the EMITTED JS (for a positive control that must
    /// assert the emission shape, e.g. root control flow attaching to target). In-repo output so
    /// the runtime specifier resolves.</summary>
    static (int exit, string stderr, string emitted) CompileEmitting(string razor)
    {
        var dir = Path.Combine(RepoPaths.Root, "samples", "Counter");
        var src = Path.Combine(dir, $".n-{Guid.NewGuid():N}.razor");
        var outPath = Path.Combine(dir, $".n-{Guid.NewGuid():N}.js");
        try
        {
            File.WriteAllText(src, razor);
            var (exit, _, stderr) = Run.Generator(src, outPath);
            return (exit, stderr, File.Exists(outPath) ? File.ReadAllText(outPath) : "");
        }
        finally
        {
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }
}
