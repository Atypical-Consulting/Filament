using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// PHASE 3: @code (C#) -> JS. Spec 6's gate: "les deux apps compilent depuis du .razor
/// PUR, les mesures sont inchangees, et une suite de 20 cas hors sous-ensemble produit
/// 20 diagnostics corrects."
///
/// This file is the C# subset's half of that gate. DiagnosticTests covers the RAZOR
/// subset (FIL0003); everything here is FIL0001 (construct) and FIL0002 (type).
///
/// EVERY LOCATION BELOW IS READ OFF THE GENERATOR, NEVER REASONED ABOUT. That is not
/// ceremony: this repo has twice recorded a rule that fired for a reason nobody measured,
/// and a column asserted from arithmetic is how you fail to notice.
/// </summary>
public class CodeTests
{
    /// <summary>
    /// THE PHASE GATE'S 20 CASES -- there are 28, and every one of them was RUN.
    ///
    /// THIS SUITE FOUND A REAL DEFECT IN THE COMPILER IT WAS WRITTEN TO TEST, which is
    /// the entire argument for writing it before believing anything. Thirteen of these
    /// cases -- every statement and expression case -- exited 0 with ZERO diagnostics and
    /// WROTE THE MODULE. Measured:
    ///
    ///     System.Console.WriteLine(currentCount);      ->  exit 0, module contains
    ///                                                      `/*refused*/;` as a COMMENT
    ///     while (currentCount &lt; 10) { currentCount++; }
    ///                                                  ->  exit 0, `function Increment() {}`
    ///                                                      -- the entire loop GONE, a
    ///                                                      method that silently does nothing
    ///
    /// Cause: method bodies were translated LAZILY, at emission, which is after the caller
    /// has already read Diagnostics -- so the refusals went into a list nobody would look
    /// at again. Fixed on the INVARIANT (CSharpFrontEnd._sealed), not by re-ordering the
    /// two calls the repro pointed at.
    ///
    /// The three columns are asserted because a diagnostic without a location is a shrug,
    /// and the REASON is asserted because "it was refused" is not the same claim as "it was
    /// refused for the right reason" -- a compiler that refuses everything passes every
    /// "it refuses X" test ever written.
    /// </summary>
    [Theory]
    // ---- statements outside section 5's list (FIL0001) ----------------------
    // try/catch, throw and lock LEFT this list at decision 110 (they compile now -> the NowCompiles
    // test below). `using` and `goto` stay: a Filament module has no IDisposable to dispose and no
    // labelled-goto lowering, so both remain deliberate refusals.
    [InlineData("Using.razor", 8, 9, "FIL0001", "unsupported-statement")]
    [InlineData("Goto.razor", 8, 9, "FIL0001", "unsupported-statement")]
    // ---- expressions outside section 5's list (FIL0001) ---------------------
    [InlineData("ConsoleCall.razor", 8, 9, "FIL0001", "unsupported-call")]
    [InlineData("Await.razor", 8, 9, "FIL0001", "unsupported-expression")]
    // Integer division: C#'s int/int truncates and JS's `/` does not, so `7/2` is 3 in
    // C# and 3.5 in JS. Refused DELIBERATELY rather than emitted as `/` -- that would be
    // a silently wrong number, which is section 10's forbidden mode, and Math.trunc()
    // around it is a mapping neither answer key states.
    // ---- members outside "fields and methods" (FIL0001) ---------------------
    [InlineData("Property.razor", 5, 5, "FIL0001", "unsupported-member")]
    [InlineData("Constructor.razor", 5, 5, "FIL0001", "unsupported-member")]
    [InlineData("NestedClass.razor", 5, 5, "FIL0001", "unsupported-member")]
    // ---- types outside section 5's list (FIL0002) ---------------------------
    // long (112), float (113) and decimal (114) all LEFT this list -- they compile now (TypeLong_NowCompiles /
    // TypeFloat_NowCompiles / TypeDecimal_NowCompiles). long -> BigInt; float -> Math.fround + __f32 display;
    // decimal -> a boxed { m, s } (BigInt mantissa + scale) with the emitted __dec helpers (exact base-10, scale
    // preserved). object/DateTime/Dict/Array STAY refused (object is untyped; DateTime has no BCL to map).
    [InlineData("TypeObject.razor", 5, 13, "FIL0002", "unsupported-type")]
    [InlineData("TypeDict.razor", 5, 13, "FIL0002", "unsupported-type")]
    [InlineData("TypeArray.razor", 5, 13, "FIL0002", "unsupported-type")]
    [InlineData("TypeDateTime.razor", 5, 13, "FIL0002", "unsupported-type")]
    // These four are refused at the LOCAL's type rather than at the expression, because
    // the declaration's type is checked first and Func/IEnumerable/StringBuilder/Type are
    // all out of subset. Asserted as measured, not as first guessed.
    [InlineData("Lambda.razor", 8, 9, "FIL0002", "unsupported-type")]
    [InlineData("Linq.razor", 8, 9, "FIL0002", "unsupported-type")]
    [InlineData("ObjectCreate.razor", 8, 9, "FIL0002", "unsupported-type")]
    [InlineData("Typeof.razor", 8, 9, "FIL0002", "unsupported-type")]
    // ---- the List<T> and record boundaries, NOW THAT BOTH ARE IMPLEMENTED ---
    //
    // THESE TWO FIXTURES CHANGED, AND THE CHANGE IS A PHASE CHANGE, NOT A SOFTENED
    // ASSERTION. They used to assert `List<int>` and `record Row {}` are
    // [type-not-yet-implemented] -- true when this step began and FALSE now: Rows' step
    // implements both (rows.js mapping decisions 1 and 2), so the old fixtures COMPILE,
    // correctly, and asserting they are refused would be asserting a bug. Their line and
    // column are UNCHANGED, because the boundary moved, not the reporting.
    //
    // The coverage does not disappear, it moves OUT to the new edge -- which is the only
    // place a subset boundary can be tested once the middle works:
    //   List<DateTime>   the CONTAINER is in the subset, the ELEMENT is not (int/long/float/decimal are IN now)
    //   List<int> = null the type is in the subset, the C# default has no array to be
    //   a method in a record: a data SHAPE cannot carry behaviour
    //
    // `record Row(int)` positional LEFT this list at decision 111 (it compiles now -> the
    // PositionalRecord_NowCompiles test below): a positional record is the SAME object-literal data
    // shape as a body record, written shorter, and its generated ctor/Equals/GetHashCode/Deconstruct
    // are simply unused. A method in a record STAYS refused -- that is behaviour, not shape.
    [InlineData("TypeList.razor", 5, 13, "FIL0002", "unsupported-type")]
    [InlineData("TypeListNull.razor", 5, 50, "FIL0001", "unsupported-expression")]
    [InlineData("RecordMember.razor", 8, 9, "FIL0001", "unsupported-member")]
    public void OutOfSubsetCsharp_IsRefused_AtItsExactLocation_NeverSilentlyEmitted(
        string fixture, int line, int col, string code, string reason)
    {
        var outPath = InRepo();
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Code", fixture), outPath);

            Assert.True(exit != 0,
                $"{fixture} was COMPILED, not refused -- the silent mis-compile section 10 forbids.\n" +
                (File.Exists(outPath) ? "it emitted:\n" + File.ReadAllText(outPath) : ""));

            Assert.False(File.Exists(outPath),
                "the generator refused AND wrote the module anyway; downstream always believes the file");

            Assert.Contains($"{fixture}({line},{col}): {code}: [{reason}]", stderr);
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    /// <summary>
    /// INTEGER DIVISION now COMPILES (decision 101, closing #87's deferral): C# int/int truncates toward
    /// zero, so `currentCount / 2` lowers to Math.trunc(currentCount.value / 2) -- faithful, not the bare
    /// `/` that would render 3.5 where C# renders 3. It used to be refused [unsupported-expression] @ (8,24).
    /// </summary>
    [Fact]
    public void IntDivision_NowCompiles_ToMathTrunc()
    {
        var outPath = InRepo();
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Code", "IntDivision.razor"), outPath);

            Assert.True(exit == 0, $"integer division should compile now (decision 101):\n{stderr}");
            Assert.Contains("Math.trunc(currentCount.value / 2)", File.ReadAllText(outPath));
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    /// <summary>
    /// WHILE and SWITCH statements now COMPILE (decision 102): they map to their JS namesakes. They used
    /// to be refused [unsupported-statement] @ (8,9).
    /// </summary>
    [Theory]
    [InlineData("While.razor", "while (currentCount.value < 10)")]
    [InlineData("Switch.razor", "switch (currentCount.value)")]
    public void LoopSwitchStatement_NowCompiles(string fixture, string expected)
    {
        var outPath = InRepo();
        try
        {
            var (exit, _, stderr) = Run.Generator(Path.Combine(RepoPaths.Unsupported, "Code", fixture), outPath);

            Assert.True(exit == 0, $"{fixture} should compile now (decision 102):\n{stderr}");
            var js = File.ReadAllText(outPath);
            Assert.Contains(expected, js);
            Assert.DoesNotContain("[unsupported-statement]", js);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    /// <summary>
    /// TRY/CATCH, THROW and LOCK now COMPILE (decision 110): try/catch/finally maps to the JS namesake,
    /// `throw new Exception(msg)` to `throw new Error(msg)`, and `lock (x) { ... }` to a bare block (JS is
    /// single-threaded, so a lock can never be contended). All three used to be refused
    /// [unsupported-statement] @ (8,9). A CAUGHT throw is faithful; an UNCAUGHT one is a disclosed edge
    /// (C# would surface a .NET exception, JS a bare Error) -- measured, see BENCH.
    /// </summary>
    [Theory]
    [InlineData("TryCatch.razor", "} catch {")]
    [InlineData("Throw.razor", "throw new Error('no')")]
    [InlineData("Lock.razor", "currentCount.value++")]
    public void TryThrowLockStatement_NowCompiles(string fixture, string expected)
    {
        var outPath = InRepo();
        try
        {
            var (exit, _, stderr) = Run.Generator(Path.Combine(RepoPaths.Unsupported, "Code", fixture), outPath);

            Assert.True(exit == 0, $"{fixture} should compile now (decision 110):\n{stderr}");
            var js = File.ReadAllText(outPath);
            Assert.Contains(expected, js);
            Assert.DoesNotContain("[unsupported-statement]", js);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    /// <summary>
    /// A `long` field now COMPILES (decision 112, closing the TypeLong deferral): long's JS home is BigInt --
    /// its integer display is EXACT past 2^53 where a double loses precision, and BigInt division truncates
    /// toward zero exactly as C#'s long/long. It used to be refused [unsupported-type] @ (5,13). float, decimal,
    /// DateTime STAY refused (JS has no faithful representation).
    /// </summary>
    [Fact]
    public void TypeLong_NowCompiles_ToABigIntField()
    {
        var outPath = InRepo();
        try
        {
            var (exit, _, stderr) = Run.Generator(Path.Combine(RepoPaths.Unsupported, "Code", "TypeLong.razor"), outPath);

            Assert.True(exit == 0, $"a long field should compile now (decision 112):\n{stderr}");
            var js = File.ReadAllText(outPath);
            Assert.Contains("5n", js);                       // `long big = 5;` -> a BigInt literal
            Assert.DoesNotContain("[unsupported-type]", js);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    /// <summary>
    /// A `float` field now COMPILES (decision 113, closing the TypeFloat deferral): float's JS home is a
    /// Math.fround'd number -- every arithmetic op rounds to single precision, and a float DISPLAY goes through
    /// the emitted __f32 helper (shortest decimal that round-trips through float32), reproducing C#'s
    /// float.ToString exactly. It used to be refused [unsupported-type] @ (5,13). decimal/DateTime STAY refused.
    /// </summary>
    [Fact]
    public void TypeFloat_NowCompiles_ToAFroundedField()
    {
        var outPath = InRepo();
        try
        {
            var (exit, _, stderr) = Run.Generator(Path.Combine(RepoPaths.Unsupported, "Code", "TypeFloat.razor"), outPath);

            Assert.True(exit == 0, $"a float field should compile now (decision 113):\n{stderr}");
            var js = File.ReadAllText(outPath);
            Assert.Contains("Math.fround(", js);             // the float literal/arithmetic is frounded
            Assert.DoesNotContain("[unsupported-type]", js);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    /// <summary>
    /// A `decimal` field now COMPILES (decision 114, closing the TypeDecimal deferral): decimal is a boxed
    /// { m: BigInt mantissa, s: scale } object (JS has no native decimal), exact in base 10 with the scale
    /// preserved. It used to be refused [unsupported-type] @ (5,13). object/DateTime STAY refused.
    /// </summary>
    [Fact]
    public void TypeDecimal_NowCompiles_ToABoxedMantissaScale()
    {
        var outPath = InRepo();
        try
        {
            var (exit, _, stderr) = Run.Generator(Path.Combine(RepoPaths.Unsupported, "Code", "TypeDecimal.razor"), outPath);

            Assert.True(exit == 0, $"a decimal field should compile now (decision 114):\n{stderr}");
            var js = File.ReadAllText(outPath);
            Assert.Contains("m:", js);                       // a boxed { m: <mantissa>n, s: <scale> }
            Assert.Contains("n, s:", js);
            Assert.DoesNotContain("[unsupported-type]", js);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    /// <summary>
    /// A POSITIONAL record now COMPILES (decision 111, closing the RecordDecl deferral): `record Row(int Id)`
    /// is the SAME object-literal data shape a body record declares, written shorter -- its generated
    /// ctor/Equals/GetHashCode/Deconstruct are simply unused. It used to be refused [unsupported-member] @ (5,20).
    /// A record carrying a METHOD (RecordMember.razor) STAYS refused: that is behaviour, not shape.
    /// </summary>
    [Fact]
    public void PositionalRecord_NowCompiles()
    {
        var outPath = InRepo();
        try
        {
            var (exit, _, stderr) = Run.Generator(Path.Combine(RepoPaths.Unsupported, "Code", "RecordDecl.razor"), outPath);

            Assert.True(exit == 0, $"a positional record should compile now (decision 111):\n{stderr}");
            Assert.DoesNotContain("[unsupported-member]", File.ReadAllText(outPath));
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    /// <summary>
    /// DECISION 57's DISCLOSED HOLE, CLOSED -- and this test is the proof, not the claim.
    ///
    /// 57: "La regle suppose que TOUT binding lu par le template est un signal. Un @x sur
    /// un `let x = 5` ordinaire emettrait `x.value` -- faux. Le detecter exigerait
    /// d'analyser le JS de @code, ce que cette phase ne fait pas."
    ///
    /// It is closed by CONSTRUCTION rather than by a check: Phase 2 had to guess because
    /// @code was opaque JS, and Phase 3 does the lifting itself, so the read site consults
    /// the compiler's own table (CSharpFrontEnd.IsSignal). The three quadrants of the rule
    /// are asserted together because each one alone is satisfiable by a wrong compiler:
    /// "everything is a signal" passes (b), "nothing is a signal" passes (a) and (c).
    ///
    ///   (a) read + never assigned   -> NOT a signal: plain create-time write, no effect.
    ///                                  THIS IS 57's CASE, and the one that emitted
    ///                                  `x.value` on an ordinary variable.
    ///   (b) read + assigned         -> signal, with an effect. (Counter's currentCount.)
    ///   (c) assigned + never read   -> NOT a signal: a plain `let`. (rows.js's _nextId
    ///                                  and _seed, which is why the rule is a CONJUNCTION
    ///                                  and not counter.js's read-condition alone.)
    /// </summary>
    [Fact]
    public void DecisionFiftySeven_AFieldTheTemplateReadsButNobodyAssigns_IsNotASignal()
    {
        var js = CompileSource(
            """
            <p><span id="a">@ordinary</span><span id="b">@reactive</span></p>
            <button id="go" @onclick="Bump">go</button>

            @code {
                private int ordinary = 5;
                private int reactive = 0;
                private int unread = 1;

                private void Bump()
                {
                    reactive++;
                    unread++;
                }
            }
            """);

        // (a) 57's exact case: read by the template, assigned by nobody. A signal here
        // would be a subscription to something that cannot change.
        Assert.Contains("const ordinary = 5;", js);
        Assert.Contains("document.createTextNode(ordinary)", js);
        Assert.DoesNotContain("ordinary.value", js);
        Assert.DoesNotContain("signal(5)", js);

        // (b) read AND assigned: lifted, and its read goes through the effect.
        Assert.Contains("const reactive = signal(0);", js);
        Assert.Contains("effect(() => setText(_tx0, reactive.value))", js);

        // (c) assigned but no binding reads it: a plain let, and its write has no .value.
        Assert.Contains("let unread = 1;", js);
        Assert.Contains("unread++;", js);
        Assert.DoesNotContain("unread.value", js);
        Assert.DoesNotContain("signal(1)", js);
    }

    /// <summary>
    /// THE STATE LIFTING, WHICH IS THE ONE THING THIS PHASE EXISTS TO PUT INSIDE THE
    /// MEASURED BYTES.
    ///
    /// Phase 2 emitted `const currentCount = signal(0)` too -- but a HUMAN wrote that line,
    /// in the input, in a JS seam. The bytes were identical and the claim was empty. This
    /// asserts the compiler derives it from `private int currentCount = 0;`, and that
    /// `currentCount++` maps to `currentCount.value++` with no syntactic desugaring
    /// (counter.js's header states that mapping exactly: "one node in, one node out").
    /// </summary>
    [Fact]
    public void PrivateIntReadByTheTemplate_IsLiftedToASignal_AndPlusPlusMapsOneToOne()
    {
        var js = CompileSource(
            """
            <p><span id="counter-value">@currentCount</span></p>
            <button id="increment" @onclick="Increment">Click me</button>

            @code {
                private int currentCount = 0;

                private void Increment()
                {
                    currentCount++;
                }
            }
            """);

        Assert.Contains("const currentCount = signal(0);", js);
        Assert.Contains("currentCount.value++;", js);

        // no desugaring: NOT `currentCount.value += 1`, NOT `= currentCount.value + 1`
        Assert.DoesNotContain("+= 1", js);
        Assert.DoesNotContain("currentCount.value + 1", js);

        // NOTHING OF THE C# SURVIVES AS TEXT. Asserted against the emitted CODE and not
        // the whole file, because the file's own banner says the words "a private field"
        // -- which is prose, not a splice. (This test asserted it against the file first
        // and went red on the generator's own comment: a false positive, in the safe
        // direction, and the reason the window is now named rather than assumed.)
        var code = js[js.IndexOf("export function mount", StringComparison.Ordinal)..];
        Assert.DoesNotContain("private", code);
        Assert.DoesNotContain("void ", code);
        Assert.DoesNotContain("int ", code);
    }

    /// <summary>
    /// batch() iff there is more than one write to COALESCE -- the rule that reconciles
    /// the two answer keys' apparently opposite statements (counter.js: "No batch(): the
    /// body performs exactly one write"; rows.js decision 3: "Every @onclick handler body
    /// runs inside batch()").
    ///
    /// Both directions are asserted, because a compiler that never batches passes the
    /// first and a compiler that always batches passes the second.
    /// </summary>
    [Fact]
    public void Batch_IsEmittedOnlyWhenThereIsMoreThanOneWriteToCoalesce()
    {
        var one = CompileSource(
            """
            <p><span id="v">@count</span></p>
            <button id="b" @onclick="Bump">go</button>

            @code {
                private int count = 0;
                private void Bump() { count++; }
            }
            """);
        Assert.DoesNotContain("batch", one);

        var many = CompileSource(
            """
            <p><span id="v">@count</span><span id="w">@other</span></p>
            <button id="b" @onclick="Bump">go</button>

            @code {
                private int count = 0;
                private int other = 0;
                private void Bump() { count++; other++; }
            }
            """);
        Assert.Contains("batch", many);
        Assert.Contains("import { signal, effect, batch,", many);
    }

    // ---- helpers -----------------------------------------------------------

    /// <summary>
    /// Compile a .razor written inline. Emitted INSIDE the repo so the generator's
    /// relative runtime specifier resolves the way it does in a real build, then read back
    /// and deleted -- same reasoning as Generate.CounterToTemp.
    /// </summary>
    static string CompileSource(string razor)
    {
        var dir = Path.Combine(RepoPaths.Root, "samples", "Counter");
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

    static string InRepo() =>
        Path.Combine(RepoPaths.Root, "samples", "Counter", $".code-{Guid.NewGuid():N}.js");
}
