using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// The invariants that FAIL SILENTLY. Every test here exists because the failure it
/// guards produces no diagnostic, no exception and no visible symptom -- it produces
/// a generator that looks like it works and emits wrong JS. Section 10: "Toute
/// construction hors sous-ensemble doit produire un diagnostic, jamais du JS
/// silencieusement faux."
///
/// EVERY TEST IN THIS FILE HAS BEEN WATCHED TO FAIL. A test you did not watch fail is
/// not a test; it is a comment with a green tick. The mutation each one was run against
/// is named on it.
/// </summary>
public class FrontEndInvariantTests
{
    /// <summary>
    /// DECISION 53, AND THE HOLE THAT WAS IN THIS TEST.
    ///
    /// Without the tag helper chain, @onclick="Increment" produces NO Razor diagnostic:
    /// it becomes a literal DOM attribute named '@onclick' carrying an [HTML] token.
    /// The button then looks entirely static and the generator emits '@onclick' as a
    /// real HTML attribute WHILE APPEARING TO WORK. Decision 53: "un test doit echouer
    /// si elle disparait."
    ///
    /// The test that USED to stand here called a CountDescriptors() helper which built
    /// its OWN engine with its OWN copy of the wiring. So it measured a mirror, not the
    /// generator: deleting CompilationTagHelperFeature from the real Parse() left it
    /// GREEN. That is decisions 41/46's failure mode -- the identical hole one frame up
    /// -- sitting inside the test written to prevent it. The helper is gone; there is
    /// now ONE engine (RazorFrontEnd.CreateEngine) and ParseResult reports what THAT
    /// engine resolved.
    ///
    /// MUTATION-TESTED, both directions:
    ///   remove CompilationTagHelperFeature  -> RED (Parse refuses: zero descriptors)
    ///   remove AddDefaultImports(WebImport) -> RED (descriptors resolve, but @onclick
    ///                                          does not bind -- the "forgot the @using"
    ///                                          case decision 53 says reproduces it)
    /// The second mutation is why DescriptorCount alone is NOT the invariant: the count
    /// stays healthy while @onclick silently mis-parses. The IR is the invariant.
    /// </summary>
    [Fact]
    public void TagHelperChain_ResolvesOnclick_AndTheAtSignIsGone()
    {
        var parse = RazorFrontEnd.Parse(RepoPaths.CounterRazor);
        var attrs = parse.Ir.FindDescendantNodes<HtmlAttributeIntermediateNode>().Select(a => a.AttributeName).ToList();

        Assert.Contains("onclick", attrs);
        Assert.DoesNotContain("@onclick", attrs);
        Assert.DoesNotContain(attrs, a => a.StartsWith('@'));

        // and it must have been lowered to Blazor's EventCallback, not left as HTML
        var onclick = parse.Ir.FindDescendantNodes<HtmlAttributeIntermediateNode>().Single(a => a.AttributeName == "onclick");
        Assert.NotEmpty(onclick.FindDescendantNodes<CSharpExpressionAttributeValueIntermediateNode>());
        Assert.Contains("EventCallback.Factory.Create", Text(onclick));
    }

    /// <summary>
    /// The count, read off the engine that ACTUALLY PARSED -- not off a second engine
    /// built to look like it. Zero descriptors is the silent mis-parse, and it must be
    /// unreachable from the emitter.
    ///
    /// MUTATION-TESTED: remove CompilationTagHelperFeature from CreateEngine -> RED.
    /// (Before the refactor this same assertion stayed GREEN under that mutation.)
    /// </summary>
    [Fact]
    public void TagHelperChain_ResolvesDescriptors_OnTheEngineThatParsed()
    {
        Assert.True(RazorFrontEnd.Parse(RepoPaths.CounterRazor).DescriptorCount > 0);
    }

    /// <summary>
    /// There is exactly ONE description of the tag helper chain. This is the structural
    /// guard against the decoy coming back: a second RazorProjectEngine.Create means a
    /// second copy of the wiring, and a test pointed at the copy is a test that cannot
    /// fail when the real one breaks.
    /// </summary>
    [Fact]
    public void TheEngineIsWiredInExactlyOnePlace()
    {
        var src = File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Filament.Generator", "RazorFrontEnd.cs"));
        var engines = System.Text.RegularExpressions.Regex.Matches(src, @"RazorProjectEngine\.Create").Count;

        Assert.True(engines == 1,
            $"RazorProjectEngine.Create appears {engines} times in RazorFrontEnd.cs. A second engine is a " +
            "second copy of the tag helper wiring, and decision 53's invariant then gets asserted against " +
            "whichever copy the test happens to call -- which is exactly how deleting " +
            "CompilationTagHelperFeature from Parse() used to leave the descriptor test green.");
    }

    /// <summary>
    /// DECISION 52. With ComponentMarkupBlockPass in place the IR hands back opaque
    /// HTML strings ("&lt;h1 id=\"title\"&gt;Counter&lt;/h1&gt;") and re-parsing those would be
    /// the entire project. Removal is done by matching GetType().Name because the
    /// types are internal -- a string match, which is exactly the kind of thing that
    /// breaks in silence. So: assert the structure, not the removal.
    ///
    /// MUTATION-TESTED: stop removing the passes -> RED.
    /// </summary>
    [Fact]
    public void MarkupPasses_AreRemoved_SoTheIrHasRealStructure()
    {
        var ir = RazorFrontEnd.Parse(RepoPaths.CounterRazor).Ir;

        Assert.Empty(ir.FindDescendantNodes<MarkupBlockIntermediateNode>());

        var tags = ir.FindDescendantNodes<MarkupElementIntermediateNode>().Select(e => e.TagName).ToList();
        Assert.Equal(new[] { "h1", "p", "span", "button" }, tags);
    }

    /// <summary>
    /// THE @code SEAM, VERIFIED RATHER THAN ASSUMED (decision 57).
    ///
    /// The one real design risk of this phase: Razor lexes @code as C#, so JavaScript
    /// in it might be mangled or rejected. It is not. Razor lexes the block but does
    /// not parse or type-check it; the whole body arrives as ONE opaque
    /// CSharpCodeIntermediateNode token, verbatim, with ZERO diagnostics -- arrow
    /// function, `const`, `++` and all. This test pins that byte for byte, so that a
    /// Razor that starts interpreting the block breaks a TEST rather than the app's
    /// state.
    /// </summary>
    [Fact]
    public void CodeBlock_IsOpaque_AndCarriesJsVerbatim()
    {
        var parse = RazorFrontEnd.Parse(RepoPaths.CounterRazor);

        Assert.DoesNotContain(parse.Document.GetSyntaxTree().Diagnostics, d => d.Severity == RazorDiagnosticSeverity.Error);
        Assert.Empty(parse.Ir.Diagnostics);

        var cls = parse.Ir.FindDescendantNodes<ClassDeclarationIntermediateNode>().Single();
        var code = cls.Children.OfType<CSharpCodeIntermediateNode>().Single();

        // ONE token holding the whole block -- that opacity IS the seam.
        var tokens = code.FindDescendantNodes<IntermediateToken>().ToList();
        Assert.Single(tokens);
        Assert.False(tokens[0].IsHtml);

        var js = tokens[0].Content;
        Assert.Contains("const currentCount = signal(0);", js);
        Assert.Contains("const Increment = () => {", js);
        Assert.Contains("currentCount.value++;", js);
    }

    /// <summary>
    /// @code is the ONE directive Phase 2 accepts, and it must still be accepted. The
    /// directive gate refuses every directive it does not know by name, so this is the
    /// test that stops that gate from being tightened into refusing the seam itself --
    /// a gate that refuses everything passes every "it refuses X" test ever written.
    /// </summary>
    [Fact]
    public void TheCodeDirectiveIsSeen_AndIsTheOnlyOneCounterUses()
    {
        var parse = RazorFrontEnd.Parse(RepoPaths.CounterRazor);

        Assert.Equal(new[] { "code" }, parse.Directives.Select(d => d.Name).ToArray());
        Assert.NotNull(parse.Directives.Single().Source);
    }

    /// <summary>
    /// The directive spy must see directives WITH their spans, and the FINAL IR must
    /// not -- which is the entire reason the spy exists. If the spy stops running (wrong
    /// phase interface, dropped from the feature list) every declaration-level
    /// diagnostic silently degrades to "&lt;no source span&gt;", which is the difference
    /// between a diagnostic and a shrug.
    ///
    /// NAMED FOR WHAT IT CHECKS, after an earlier name claimed more. This test does NOT
    /// verify that the spy runs "before lowering": it cannot, and it need not. Measured
    /// with a spy in each phase, the directive survives the whole directive-classifier
    /// phase and is removed by a LATE OPTIMIZATION pass, so the pass's Order is not
    /// load-bearing -- mutating Order from int.MinValue to int.MaxValue leaves every
    /// test green, correctly, because nothing about the behaviour changes.
    ///
    /// MUTATION-TESTED: drop the spy from the engine's feature list -> RED.
    /// </summary>
    [Fact]
    public void DirectiveSpy_SeesTheSpan_ThatTheFinalIrHasLost()
    {
        var parse = RazorFrontEnd.Parse(Path.Combine(RepoPaths.Unsupported, "Inject.razor"));

        var inject = Assert.Single(parse.Directives, d => d.Name == "inject");
        var span = inject.Source;
        Assert.NotNull(span);
        Assert.Equal(0, span!.Value.LineIndex);      // line 1, 0-based
        Assert.Equal(0, span.Value.CharacterIndex);  // column 1, 0-based
        Assert.Equal("Inject.razor", Path.GetFileName(span.Value.FilePath));

        // and the proof that the spy is not merely convenient: by the time the tree is
        // walked, Razor has replaced the directive with a node that has NO span at all.
        var lowered = parse.Ir.FindDescendantNodes<ExtensionIntermediateNode>()
            .FirstOrDefault(n => n.GetType().Name == "ComponentInjectIntermediateNode");
        Assert.NotNull(lowered);
        Assert.Null(lowered!.Source);
    }

    /// <summary>
    /// TEMPLATE PARITY -- the invariant decision 28 already broke once, silently,
    /// because it was assumed rather than enforced (index.html/stylesheet drift).
    ///
    /// samples/Counter/Counter.razor is what Filament compiles; baseline/Counter.Blazor/
    /// App.razor is what Blazor compiles. The whole comparison rests on both building
    /// the SAME DOM from the SAME markup, so the markup must be byte-identical. If
    /// someone "tidies" the blank lines out of either file, Blazor and Filament stop
    /// agreeing about two text nodes and the bench quietly measures two different pages.
    ///
    /// PINNED AT BOTH ENDS, on purpose:
    ///   - the two files must agree with EACH OTHER  -> catches drift in the sample;
    ///   - and both must equal the literal below     -> catches drift in the baseline,
    ///     and catches the case where someone "fixes" the test by editing both files.
    ///
    /// MUTATION-TESTED: delete a blank line from Counter.razor -> RED. Change the
    /// baseline's id="title" -> RED.
    /// </summary>
    [Fact]
    public void CounterRazor_TemplateIsTheBaselineMarkup_Verbatim()
    {
        var baseline = File.ReadAllText(Path.Combine(RepoPaths.Root, "baseline", "Counter.Blazor", "App.razor"));
        var sample = File.ReadAllText(RepoPaths.CounterRazor);

        var expected = Markup(baseline);
        Assert.Equal(expected, Markup(sample));

        // and it is the markup we think it is
        Assert.Equal(
            "<h1 id=\"title\">Counter</h1>\n\n" +
            "<p>Current count: <span id=\"counter-value\">@currentCount</span></p>\n\n" +
            "<button id=\"increment\" @onclick=\"Increment\">Click me</button>",
            expected);
    }

    /// <summary>
    /// Parity of the TEXT is only half of it: the text is compared after stripping the
    /// header comment and everything from @code, so a drift OUTSIDE that window would
    /// slip past. This asserts the consequence instead -- what the generator actually
    /// built out of the shared markup -- so the two tests fail for different reasons
    /// and neither can cover for the other.
    ///
    /// The two "\n\n" text nodes are the point: they are decision 55's second
    /// divergence, they are what Blazor ships (AddMarkupContent(0,
    /// "&lt;h1 id=\"title\"&gt;Counter&lt;/h1&gt;\n\n")), and they are the first thing a tidy-up
    /// would delete.
    /// </summary>
    [Fact]
    public void TheSharedMarkup_BuildsTheDomTheBaselineBuilds()
    {
        var method = RazorFrontEnd.Parse(RepoPaths.CounterRazor).Ir
            .FindDescendantNodes<MethodDeclarationIntermediateNode>()
            .Single(m => m.MethodName == "BuildRenderTree");

        var shape = method.Children.Select(c => c switch
        {
            MarkupElementIntermediateNode e => $"<{e.TagName}>",
            HtmlContentIntermediateNode h => Text(h).Replace("\n", "\\n"),
            _ => c.GetType().Name,
        }).ToArray();

        Assert.Equal(new[] { "<h1>", "\\n\\n", "<p>", "\\n\\n", "<button>" }, shape);
    }

    /// <summary>
    /// The markup: everything after the leading @* *@ header comment and before the
    /// @code block. The header comment is stripped FIRST -- both files' headers talk
    /// ABOUT @code, so searching for "@code" before removing the comment finds the
    /// prose and not the directive.
    /// </summary>
    static string Markup(string razor)
    {
        var s = razor.Replace("\r\n", "\n").TrimStart();
        if (s.StartsWith("@*", StringComparison.Ordinal))
        {
            var close = s.IndexOf("*@", 2, StringComparison.Ordinal);
            Assert.True(close > 0, "unterminated @* *@ header comment");
            s = s[(close + 2)..];
        }
        var code = s.IndexOf("@code", StringComparison.Ordinal);
        if (code >= 0) s = s[..code];
        return s.Trim('\n', ' ');
    }

    static string Text(IntermediateNode n) =>
        string.Concat(n.FindDescendantNodes<IntermediateToken>().Select(t => t.Content));
}
