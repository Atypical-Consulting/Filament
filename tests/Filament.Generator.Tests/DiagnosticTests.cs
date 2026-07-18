using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// SECTION 10, MECHANICALLY: "Toute construction hors sous-ensemble doit produire un
/// diagnostic, jamais du JS silencieusement faux."
///
/// Every case here was SILENT before this suite existed. Not "theoretically reachable"
/// -- measured, by running the generator and reading the module it produced:
///
///     @inject  ->  ComponentInjectIntermediateNode  (class child)      DROPPED, clean module emitted
///     @page    ->  RouteAttributeExtensionNode      (namespace child)  DROPPED, clean module emitted
///     @layout  ->  CSharpCodeIntermediateNode       (namespace child)  DROPPED, clean module emitted
///     @using   ->  UsingDirectiveIntermediateNode   (namespace child)  DROPPED, clean module emitted
///     &lt;Widget/&gt; ->  MarkupElementIntermediateNode                     EMITTED as
///                  document.createElement('SomeWidget') -- an unknown element that
///                  renders nothing, and Razor itself reports NOTHING for it.
///
/// The old compiler reached into the class for BuildRenderTree and @code and never
/// looked at the rest of the tree, so all of the above compiled to a module that looked
/// correct and quietly did less than the source said. That is exactly the failure mode
/// section 10 names.
///
/// TWO ASSERTIONS PER CASE, and the second one is the one with teeth:
///   1. it is REFUSED -- exit code non-zero, and NO FILE ON DISK.
///   2. the diagnostic points at the RIGHT LINE. A diagnostic with no location, or with
///      a location of "somewhere in this file", is not actionable, and it is also how
///      you fail to notice that a rule fires for the wrong reason. The line numbers
///      below are the real line numbers in the fixtures next to this file.
/// </summary>
public class DiagnosticTests
{
    // ---- unsupported directives (Phase 2's own code: FIL0003) ---------------

    [Theory]
    // fixture,                 line, col, the reason tag, and what must be named in the text
    [InlineData("Inject.razor", 1, 1, "unsupported-directive", "@inject")]
    [InlineData("Page.razor", 1, 1, "unsupported-directive", "@page")]
    [InlineData("Layout.razor", 1, 1, "unsupported-directive", "@layout")]
    [InlineData("Inherits.razor", 1, 1, "unsupported-directive", "@inherits")]
    [InlineData("Using.razor", 1, 2, "unsupported-directive", "@using")]
    public void UnsupportedDirective_IsRefused_AtItsExactLocation(
        string fixture, int line, int col, string reason, string mentions)
    {
        var d = Refused(fixture);

        Assert.Contains($"{fixture}({line},{col}): FIL0003: [{reason}]", d);
        Assert.Contains(mentions, d);
    }

    /// <summary>
    /// CONTROL FLOW, AFTER THE ROWS STEP REACHED IT. Foreach.razor is KEPT and still refuses --
    /// for a reason that is now specific instead of "this phase does not do C#". (If.razor left
    /// this theory when a plain @if started compiling -- see IfTests.)
    ///
    /// The premise changed, and saying so is the point. Decision 54: Razor emits no
    /// loop/branch node, the header is raw C# with unbalanced braces and the body element is
    /// a SIBLING of it. That was the reason @foreach was out of reach; the Rows step
    /// reassembles those spans and RE-PARSES them, so @foreach is compiled now and an
    /// assertion that it is refused would be asserting a bug.
    ///
    /// What is tested here instead is the edge the reassembly creates:
    ///   Foreach.razor  iterates `items`, which no @code declares -- so there is nothing
    ///                  for list() to subscribe to. Reported AT `items` (2,20), not at the
    ///                  @foreach: the loop is fine, its source is not.
    ///
    /// The CODE changed FIL0003 -> FIL0001 on purpose, and it is decision 54 being taken at
    /// its word: @foreach and @if are not Razor structure, they ARE C#, which is why Razor
    /// hands them over as text. The construct being refused is a C# statement in the
    /// re-parsed tree, so it carries the C# subset's code.
    /// </summary>
    [Theory]
    [InlineData("Foreach.razor", 2, 20, "unsupported-foreach")]
    [InlineData("IfNested.razor", 2, 1, "unsupported-if-body")]
    [InlineData("IfMultiBody.razor", 2, 1, "unsupported-if-body")]
    [InlineData("IfElseMultiBody.razor", 6, 1, "unsupported-if-body")]
    public void ControlFlow_OutsideTheSubset_IsRefused_AtItsExactLocation(
        string fixture, int line, int col, string reason)
    {
        var d = Refused(fixture);

        Assert.Contains($"{fixture}({line},{col}): FIL0001: [{reason}]", d);
    }

    /// <summary>
    /// @if AT THE TEMPLATE ROOT -- not a C#-subset refusal like the three above, but the
    /// PRE-EXISTING root-code guard (TemplateCompiler.Collect, decision-61-adjacent): a
    /// root-level region has no element to attach to, so Collect() never plans it and the
    /// gap is caught before the C# front end ever sees the @if. Split out of the FIL0001
    /// theory above (which hardcodes that code) because this one carries FIL0003.
    ///
    /// Column is 2, not 1: the located span is the C# node's own text ("if (show)..."),
    /// which starts right after the '@' Razor keeps for itself. Measured, not guessed --
    /// the brief's placeholder was (1,1) and the generator disagreed.
    ///
    /// The fixture's `@if { ... }` splits into TWO root-level CSharpCodeIntermediateNodes
    /// around the &lt;span&gt; markup ("if (show)\n{\n" and "}\n"), so the guard actually
    /// raises TWO diagnostics here (also at (4,1)) -- both true and both
    /// [template-code-at-root]. Only the first is asserted; the second is not spurious,
    /// just the same guard firing again for the closing brace's own root-level node.
    /// </summary>
    [Fact]
    public void IfAtRoot_IsRefused_ByTheRootCodeGuard_AtItsExactLocation()
    {
        var d = Refused("IfAtRoot.razor");

        Assert.Contains("IfAtRoot.razor(1,2): FIL0003: [template-code-at-root]", d);
    }

    /// <summary>
    /// COMPONENT COMPOSITION -- static-leaf composition now COMPILES when the child resolves
    /// (decision 88; see ComposeTests). &lt;SomeWidget /&gt; here has NO sibling SomeWidget.razor in the
    /// fixture directory, so it refuses [unresolved-component] -- a clear located error, not the old
    /// blanket refusal, and not a silent document.createElement('SomeWidget').
    /// </summary>
    [Fact]
    public void UnresolvedComponent_IsRefused_AtItsExactLocation()
    {
        var d = Refused("Component.razor");

        Assert.Contains("Component.razor(2,5): FIL0003: [unresolved-component]", d);
        Assert.Contains("SomeWidget", d);
    }

    /// <summary>
    /// @bind lowers into value= + onchange= carrying BindConverter, so it arrives as a
    /// dynamic attribute value rather than as a surviving '@bind'. Refused either way;
    /// this test pins WHICH way, because a rule that fires for a reason you did not
    /// measure is a rule you do not have.
    /// </summary>
    [Fact]
    public void Bind_IsRefused_AtItsExactLocation()
    {
        var d = Refused("Bind.razor");

        Assert.Contains("Bind.razor(1,24): FIL0003: [dynamic-attribute]", d);
        Assert.Contains("BindConverter", d);
    }

    /// <summary>
    /// DECISION 53's backstop, from the input side. An attribute that still carries its
    /// '@' never resolved to a directive attribute. The cause is either an
    /// out-of-subset directive attribute or -- and this is the one that matters -- the
    /// missing tag helper descriptors, in which case @onclick ITSELF lands here and a
    /// generator would emit it as a literal HTML attribute while appearing to work.
    /// </summary>
    [Fact]
    public void UnresolvedDirectiveAttribute_IsRefused_AndNamesBothCauses()
    {
        var d = Refused("DirectiveAttribute.razor");

        // col 15 is the whitespace immediately before '@wat': that is where Razor puts
        // the attribute's span, and the number is read off the generator rather than
        // reasoned about. Being wrong about it is how you discover a rule fires from a
        // node you did not think it fired from.
        Assert.Contains("DirectiveAttribute.razor(1,15): FIL0003: [unsupported-directive]", d);
        Assert.Contains("'@wat'", d);
        Assert.Contains("decision 53", d);
    }

    /// <summary>
    /// THE NODE GATE'S OWN TEST, and the reason it exists is worth writing down.
    ///
    /// Every other fixture here is caught by the DIRECTIVE gate, so no-oping the node
    /// gate left the whole suite GREEN -- an untested backstop, i.e. a claim rather than
    /// an invariant. @attributes is the case only the node gate can see: it lowers to a
    /// SplatIntermediateNode, which is not spelled as a directive (the spy sees nothing)
    /// and is not a node the emitter handles, so it falls to the default branch and
    /// nothing else in the compiler has an opinion about it.
    ///
    /// MUTATION-TESTED: no-op Unaccounted() -> RED (and green before this fixture
    /// existed, which is exactly why it exists).
    /// </summary>
    [Fact]
    public void Splat_IsRefused_ByTheNodeGate_AtItsExactLocation()
    {
        var d = Refused("Splat.razor");

        Assert.Contains("Splat.razor(1,26): FIL0003: [unaccounted-node]", d);
        Assert.Contains("SplatIntermediateNode", d);
    }

    /// <summary>
    /// A SILENT DROP THAT SHIPPED AT HEAD, in the same method as the handler splice, and
    /// worse than it: a splice is loud, this one renders perfectly and lies.
    ///
    /// EmitAttribute collected two node types -- CSharpExpressionAttributeValue and
    /// HtmlAttributeValue -- and a THIRD exists. C# control flow in an attribute value
    /// (`class="@if (c) { <text>active</text> }"`) lowers to CSharpCodeAttributeValue-
    /// IntermediateNode, which matched neither, so the value silently stayed "" and the
    /// compiler emitted `_el0.className = ''` at exit 0 with ZERO diagnostics. Measured on
    /// all three attribute paths (className, id, setAttr) before the guard existed.
    ///
    /// THE MIXED CASE IS THE INSIDIOUS ONE and is what this fixture uses: `class="box @if
    /// (...)"` emitted `className = 'box'` -- the literal half surviving, so the output
    /// looks like it worked. #title and #increment are the shared DOM contract the whole
    /// benchmark keys on.
    ///
    /// The class's document walk had Unaccounted() for exactly this shape; the ATTRIBUTE
    /// VALUE walk had no equivalent. Decision 41's pattern, and the reason the guard is on
    /// "every child node is accounted for" rather than on this one node type.
    /// </summary>
    [Fact]
    public void CsharpControlFlowInAnAttributeValue_IsRefused_NeverSilentlyDropped()
    {
        var d = Refused("AttributeCodeValue.razor");

        Assert.Contains("AttributeCodeValue.razor(1,26): FIL0003: [unaccounted-attribute-value]", d);
        Assert.Contains("CSharpCodeAttributeValueIntermediateNode", d);
    }

    /// <summary>@ref resolves (the descriptors are live) to a capture whose span is the captured name.</summary>
    [Fact]
    public void Ref_IsRefused_AtItsExactLocation()
    {
        var d = Refused("Ref.razor");

        Assert.Contains("Ref.razor(1,22): FIL0003: [unsupported-directive]", d);
        Assert.Contains("@ref", d);
    }

    // ---- THE EVENT PATH: the worst defect this generator has shipped ---------

    /// <summary>
    /// THE BLOCKER. Both halves of section 10 violated at once, and measured against the
    /// committed tree rather than theorised:
    ///
    ///     @onclick="() => currentCount++"  ->  exit 0, ZERO diagnostics, FILE WRITTEN,
    ///                                          listen(_el0,'click',() => currentCount++)
    ///
    /// Driven in a browser, that module LOADS, mount() RETURNS, the page renders "0", and
    /// after three clicks it still renders "0". The page looks perfect and THE BUTTON IS
    /// DEAD FOREVER -- no diagnostic AND silently false JS.
    ///
    /// Decision 41's pattern, for the THIRD time in this repo: EmitBinding guarded the
    /// READ path with exactly this rule while EmitAttribute spliced the handler verbatim
    /// ONE FRAME OVER. Both now go through NamedByTemplate(), which is why this theory and
    /// UnresolvedBinding_IsRefused_AtItsExactLocation below assert the same two reasons
    /// from opposite paths: the guard is on the INVARIANT, not on the line a repro pointed at.
    ///
    /// The locations are read off the generator, not reasoned about. Razor synthesises the
    /// EventCallback.Factory.Create(this, wrapper with NO span and leaves the author's own
    /// token carrying the author's own position, so these columns point at the handler
    /// text itself -- col 34 is the character after @onclick=" on line 1.
    ///
    /// MUTATION-TESTED: make NamedByTemplate's BareIdentifier check always succeed and all
    /// three lambda rows go RED (and HandlerUnresolved stays green, which is the point of
    /// having both reasons).
    /// </summary>
    [Theory]
    [InlineData("HandlerLambda.razor", 1, 34, "compound-expression", "() => currentCount++")]
    [InlineData("HandlerLambdaArgs.razor", 1, 36, "compound-expression", "e => Console.WriteLine(e.ClientX)")]
    [InlineData("HandlerAsync.razor", 1, 34, "compound-expression", "async () => { await Task.Delay(1); count++; }")]
    [InlineData("HandlerUnresolved.razor", 1, 34, "unresolved-name", "NoSuchMethodAnywhere")]
    public void EventHandlerOutsideTheSubset_IsRefused_NeverSplicedVerbatim(
        string fixture, int line, int col, string reason, string mentions)
    {
        var d = Refused(fixture);

        Assert.Contains($"{fixture}({line},{col}): FIL0003: [{reason}]", d);
        Assert.Contains(mentions, d);
    }

    /// <summary>
    /// THE BLOCKER'S FOURTH FACE, and it is NOT one of the four cases the fix was asked for
    /// -- it was found by mutation-testing the fix and asking what ELSE reaches a dead button.
    ///
    /// `@onclick="currentCount"` names STATE. It passes the bare-identifier check and the
    /// resolution check (@code really does bind `currentCount`), so before this rule it
    /// emitted `listen(_el0,'click',currentCount)` at exit 0 -- and addEventListener takes a
    /// non-callable object WITHOUT COMPLAINT and never invokes it. Verified in jsdom against
    /// the real runtime: addEventListener does not throw, dispatchEvent does not throw, the
    /// listener never runs. Identical user-visible failure to `@onclick="() => count++"`:
    /// the page renders perfectly and the button is dead forever.
    ///
    /// Spec 5 says "calls to METHODS declared in the same component", so this was always out
    /// of subset; only the guard was missing. Fixing the four listed cases and leaving this
    /// one is precisely decision 41's pattern, which this repo has now paid for three times.
    ///
    /// MUTATION-TESTED: drop the _callable condition -> RED.
    /// </summary>
    [Fact]
    public void HandlerNamingStateInsteadOfAMethod_IsRefused_AtItsExactLocation()
    {
        var d = Refused("HandlerIsState.razor");

        Assert.Contains("HandlerIsState.razor(1,34): FIL0003: [handler-is-not-a-method]", d);
        Assert.Contains("currentCount", d);
    }

    /// <summary>
    /// The read path's half of the same guard. @currentCount already had the bare-identifier
    /// check; it did NOT have resolution, so a template could name state that exists
    /// nowhere. Same method, same two reasons, opposite path.
    /// </summary>
    [Fact]
    public void UnresolvedBinding_IsRefused_AtItsExactLocation()
    {
        var d = Refused("BindingUnresolved.razor");

        // FIL0001, not FIL0003, and the location is unchanged. An unresolved NAME is a fact
        // about C#, not about Razor, and the answer now comes from Roslyn refusing to bind a
        // symbol rather than from a regex failing to find a string in a JS seam.
        Assert.Contains("BindingUnresolved.razor(1,30): FIL0001: [unresolved-name]", d);
        Assert.Contains("nowhereDeclared", d);
    }

    /// <summary>
    /// THE @code SEAM, AFTER PHASE 3 INVERTED IT BACK. This test replaces
    /// CsharpInTheCodeSeam_IsRefused_AtItsExactLocation, and the swap is a phase change
    /// rather than a softened assertion -- it is worth being explicit about, because
    /// "delete the test that went red" is what this repo's standard forbids.
    ///
    /// Phase 2 INVERTED the spec: section 5 says @code is C#, but Phase 2 compiled the
    /// template only and required @code to be hand-written JS (spec 6, decision 57). So in
    /// Phase 2, C# in @code was the UNSUPPORTED case and had its own diagnostic. Phase 3
    /// restores the spec: @code is C#, it is COMPILED, and `private int currentCount = 0;`
    /// is now the supported input -- the old fixture compiles, correctly, so an assertion
    /// that it is refused would now be asserting a bug.
    ///
    /// The coverage does not disappear, it FLIPS: the Phase 2 seam -- real, shipped,
    /// hand-written JavaScript -- must now be REFUSED, with a location, and never spliced.
    /// That matters because the old input is still lying around this repo (it was
    /// samples/Counter/Counter.razor one commit ago), and a compiler that silently
    /// accepted it would emit `const currentCount = signal(0);` twice or splice JS it
    /// never parsed.
    ///
    /// (4,24) is the `=` in `const currentCount = signal(0);` -- C# expects an identifier
    /// there, because `const` needs a TYPE. Read off the generator, not reasoned about.
    /// </summary>
    [Fact]
    public void JsInTheCodeSeam_IsRefused_AtItsExactLocation()
    {
        var d = Refused("CodeSeamIsJs.razor");

        Assert.Contains("CodeSeamIsJs.razor(4,24): FIL0001: [not-csharp]", d);
        Assert.Contains("does not parse as C#", d);
    }

    /// <summary>
    /// THE TOOL'S OWN FAILURES MUST NOT SQUAT THE SPEC'S NAMESPACE (decision 61).
    ///
    /// Program.cs minted a bare `FIL000` for "I cannot find the runtime" -- which reads as
    /// the spec's reserved FIL0001 at a glance, and is not a statement about the user's
    /// Razor at all but about the tool being misused. It had ZERO test coverage, which is
    /// how it survived the decision-61 sweep that renamed every other code.
    ///
    /// Asserting FIL\d never appears is what makes this a guard on the CLASS rather than on
    /// the one string: any future code minted out of the reserved namespace on this path
    /// fails here, whatever it is named.
    /// </summary>
    [Fact]
    public void WiringFailure_CarriesFilWiring_AndNeverASpecCode()
    {
        // Outside the repo, so ResolveRuntimeSpecifier cannot find the runtime above it.
        var outside = Path.Combine(Path.GetTempPath(), $"filament-wiring-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Compile(RepoPaths.CounterRazor, outside);

            Assert.NotEqual(0, exit);
            Assert.Contains("FIL-WIRING", stderr);
            Assert.DoesNotMatch(@"FIL\d", stderr);
        }
        finally
        {
            if (File.Exists(outside)) File.Delete(outside);
        }
    }

    /// <summary>
    /// DECISION 54, ON THE REAL FILE, INVERTED -- and this is the one assertion in this file
    /// whose flip is the whole step.
    ///
    /// It used to assert that baseline/Rows.Blazor/RowsApp.razor is REFUSED, with
    /// FIL0003 [control-flow] at (12,14) and [unsupported-directive] at @key (14,27),
    /// because decision 54 said Razor hands @foreach over as raw C# with unbalanced braces
    /// and a body element that is a SIBLING of the header. That was true. The Rows step
    /// reassembles those spans and re-parses them, so the file COMPILES, and the old
    /// assertion would now be asserting a bug.
    ///
    /// The coverage does not disappear, it moves to RowsTests, where every one of rows.js's
    /// four mapping decisions is asserted against the emitted module. What is kept HERE is
    /// the property this test was really guarding: the real file, not a stand-in.
    /// </summary>
    [Fact]
    public void Rows_CompilesFromPureRazor_TheBaselinesOwnFile()
    {
        var outPath = InRepo();
        try
        {
            var (exit, _, stderr) = Compile(RepoPaths.RowsRazor, outPath);

            Assert.True(exit == 0,
                "baseline/Rows.Blazor/RowsApp.razor -- the file Blazor compiles -- was REFUSED. " +
                "'les deux apps compilent depuis du .razor PUR' is exactly this claim.\n" + stderr);
            Assert.True(File.Exists(outPath), "the generator exited 0 and wrote nothing");
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    // ---- the properties that hold for EVERY refusal -------------------------

    /// <summary>
    /// EVERY diagnostic carries a real location, and a code the SPEC defines. This is the
    /// assertion that stops a future rule from being added with a null span: it does not
    /// name a construct, it quantifies over all of them, so a new fixture is covered the
    /// day it is added.
    ///
    /// WHAT PHASE 3 CHANGED, AND WHAT IT DELIBERATELY DID NOT. This test used to also
    /// assert `FIL0001` and `FIL0002` NEVER appear, because Phase 2 owned exactly one code
    /// and a tool must not squat the namespace it reports in (decision 61). Phase 3 owns
    /// FIL0001 (out-of-subset C#) and FIL0002 (out-of-subset type) legitimately -- they are
    /// the spec's own codes for the spec's own subset -- so that assertion would now be
    /// asserting that the phase does not do its job.
    ///
    /// The anti-squatting guard is KEPT, and kept as a guard on the CLASS rather than on
    /// three strings: the code must match the spec's set EXACTLY. A generator that invents
    /// FIL0004, or resurrects the old private FIL001..FIL011 scheme that reads exactly like
    /// the spec's codes at a glance, still fails here. The location assertion -- the
    /// load-bearing half -- is untouched.
    /// </summary>
    [Theory]
    [InlineData("Inject.razor")]
    [InlineData("Page.razor")]
    [InlineData("Layout.razor")]
    [InlineData("Inherits.razor")]
    [InlineData("Using.razor")]
    [InlineData("Foreach.razor")]
    [InlineData("Bind.razor")]
    [InlineData("Component.razor")]
    [InlineData("DirectiveAttribute.razor")]
    [InlineData("Ref.razor")]
    [InlineData("Splat.razor")]
    [InlineData("HandlerLambda.razor")]
    [InlineData("HandlerLambdaArgs.razor")]
    [InlineData("HandlerAsync.razor")]
    [InlineData("HandlerUnresolved.razor")]
    [InlineData("BindingUnresolved.razor")]
    [InlineData("CodeSeamIsJs.razor")]
    [InlineData("HandlerIsState.razor")]
    public void EveryDiagnostic_CarriesAnExactLocation_AndOneOfTheSpecsCodes(string fixture)
    {
        var d = Refused(fixture);

        foreach (var line in d.Split('\n').Where(l => l.TrimStart().StartsWith("error ")))
        {
            Assert.DoesNotContain("<no source span>", line);

            // file(line,col): FIL000[123]: -- an exact location, and a code the spec
            // defines. Section 5: FIL0001 out-of-subset C#, FIL0002 unsupported type,
            // FIL0003 unsupported Razor directive. Nothing else may appear.
            Assert.Matches(
                $@"{System.Text.RegularExpressions.Regex.Escape(fixture)}\(\d+,\d+\): FIL000[123]: \[",
                line);

            // The tool's own failures must not wear a spec code (decision 61).
            Assert.DoesNotContain("FIL-WIRING", line);
        }
    }

    /// <summary>
    /// The refusal is a REFUSAL: no file. A generator that reports an error and still
    /// writes the module leaves the build to decide whether to believe the exit code,
    /// and something downstream always believes the file.
    /// </summary>
    [Theory]
    [InlineData("Inject.razor")]
    [InlineData("Page.razor")]
    [InlineData("Component.razor")]
    // The handler cases especially: this generator DID write these files, and the module
    // it wrote looked perfect and had a dead button.
    [InlineData("HandlerLambda.razor")]
    [InlineData("HandlerUnresolved.razor")]
    [InlineData("CodeSeamIsJs.razor")]
    [InlineData("HandlerIsState.razor")]
    // The deferred @if variants: refused by ControlFlow_OutsideTheSubset_IsRefused_AtItsExactLocation
    // and IfAtRoot_IsRefused_ByTheRootCodeGuard_AtItsExactLocation above, but those theories only
    // assert exit != 0 via Refused() -- they never asserted the file itself was never written.
    [InlineData("IfNested.razor")]
    [InlineData("IfMultiBody.razor")]
    [InlineData("IfElseMultiBody.razor")]
    [InlineData("IfAtRoot.razor")]
    public void ARefusalWritesNoFile(string fixture)
    {
        var outPath = InRepo();
        try
        {
            var (exit, _, _) = Compile(Path.Combine(RepoPaths.Unsupported, fixture), outPath);

            Assert.NotEqual(0, exit);
            Assert.False(File.Exists(outPath),
                "the generator refused AND wrote the module anyway; downstream will believe the file");
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    // ---- helpers -----------------------------------------------------------

    /// <summary>Compile a fixture that MUST be refused, and hand back stderr.</summary>
    static string Refused(string fixture)
    {
        var outPath = InRepo();
        try
        {
            var (exit, stdout, stderr) = Compile(Path.Combine(RepoPaths.Unsupported, fixture), outPath);

            Assert.True(exit != 0,
                $"{fixture} was COMPILED, not refused. That is the silent mis-compile section 10 forbids.\n" +
                $"stdout:\n{stdout}\nstderr:\n{stderr}\n" +
                (File.Exists(outPath) ? "it emitted:\n" + File.ReadAllText(outPath) : ""));
            return stderr;
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    static (int exit, string stdout, string stderr) Compile(string input, string? output = null)
    {
        var outPath = output ?? InRepo();
        try
        {
            return Run.Generator(input, outPath);
        }
        finally
        {
            if (output is null && File.Exists(outPath)) File.Delete(outPath);
        }
    }

    /// <summary>
    /// An output path inside the repo, so the generator's relative runtime specifier
    /// resolves the way it does in a real build rather than failing for an unrelated
    /// reason and looking like a refusal. (It would: ResolveRuntimeSpecifier throws
    /// when it cannot find the runtime above the output, which is a non-zero exit that
    /// has nothing to do with the diagnostic under test.)
    /// </summary>
    static string InRepo() =>
        Path.Combine(RepoPaths.Root, "samples", "Counter", $".diag-{Guid.NewGuid():N}.js");
}
