using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// ROUTE PARAMETERS (decision 163) — the honesty register's A9 and D12, closed.
///
/// A9 recorded what decision 139's router did with `@page "/item/{Id:int}"`: it copied the literal into
/// a string-equality table, admitted the page at exit 0 with no diagnostic, and rendered a BLANK SCREEN
/// — for a page real Blazor renders fine. It admitted `/h/{Id` too, unbalanced brace and all.
///
/// D12 recorded why that could not be patched, by measuring each proposed piece against real Blazor and
/// refuting it: `mount(target)` has no second argument for a captured value; Blazor REUSES a component
/// on a parameter-only navigation, so re-mounting resets state the browser is still showing; `(-?\d+)`
/// + `Number()` diverges from `:int` in BOTH directions; and precedence ranking would be new router
/// bytes, contradicting the slice's own cost claim.
///
/// This suite is the answer to each, and the two gates below divide the work deliberately:
///
///   canon decides the BYTES — the emitted router is alpha-equivalent to a hand-written answer key.
///   route-contract decides the BEHAVIOUR — the emitted bytes are RUN, in a DOM, and asked whether
///   /item/new outranks /item/{Id:int}, whether /item/2147483648 matches (it must not), and whether a
///   page keeps its state across /item/7 -> /item/8 (it must).
///
/// Neither gate subsumes the other, and that is not belt-and-braces: canon's own limitation L3 says
/// object-literal keys are invisible to it, and the converter table IS an object literal, so the
/// contract is what actually holds it. Each contract step has a control that makes it fail — recorded
/// in DECISIONS #163, not left as a claim.
/// </summary>
public class RouteParamTests
{
    /// <summary>
    /// Emit the routed app into the repo, at a depth whose runtime specifier is stated, and hand back
    /// the directory. IN-REPO because the contract RESOLVES the runtime import and bundles it; a temp
    /// directory outside the tree cannot reach src/filament-runtime.
    /// </summary>
    static string AppInRepo()
    {
        var dir = Path.Combine(RepoPaths.Root, "samples", "RouteParams", $".gen-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var (exit, stdout, stderr) = Run.RouterWithRuntime(
            Path.Combine(dir, "Router.g.js"),
            "../../../src/filament-runtime/src/index.ts",
            RepoPaths.RouteParamsPages);
        Assert.True(exit == 0, $"the generator refused to emit the routed app:\n{stdout}\n{stderr}");
        return dir;
    }

    static void Cleanup(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the emitted router is alpha-equivalent to the hand-written
    /// samples/RouteParams/router.js. The key is the SPEC and the REFERENCE.
    /// </summary>
    [Fact]
    public void Gate_GeneratedRouteParamsRouter_IsAlphaEquivalentToAnswerKey()
    {
        var dir = AppInRepo();
        try
        {
            var (exit, stdout, stderr) = Run.Node(
                RepoPaths.Canon, Path.Combine(dir, "Router.g.js"), RepoPaths.RouteParamsAnswerKey);
            Assert.True(exit == 0,
                "route-parameter routing gate FAILED. The generated router is NOT alpha-equivalent to "
                + "samples/RouteParams/router.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// THE BEHAVIOURAL CONTRACT, RUN ON THE EMITTED BYTES. Twenty steps in a DOM: matching, the four
    /// converters, precedence, URL decoding, Blazor's segment rules, clearing on a miss, and instance
    /// reuse across a parameter-only navigation. This is the gate that decides the claims canon cannot.
    /// </summary>
    [Fact]
    public void Contract_TheEmittedApp_BehavesAsBlazorDoes()
    {
        var dir = AppInRepo();
        try
        {
            var (exit, stdout, stderr) = Run.Node(RepoPaths.RouteContract, dir);
            Assert.True(exit == 0,
                "the route-parameter behavioural contract FAILED on the emitted app.\n"
                + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// BLAZOR'S PRECEDENCE, DECIDED AT BUILD TIME. The table is emitted already sorted most-specific
    /// first — literal segments before parameters, constrained parameters before bare ones — so the
    /// router keeps its linear scan and the ranking costs zero shipped bytes.
    ///
    /// Asserted on the ORDER, not on the presence: presence is what the string-equality router already
    /// had, and it is exactly what was not enough.
    /// </summary>
    [Fact]
    public void EmittedTable_IsSortedByBlazorsPrecedence_NotByDeclarationOrder()
    {
        var dir = AppInRepo();
        try
        {
            var js = File.ReadAllText(Path.Combine(dir, "Router.g.js"));
            var table = js[js.IndexOf("const routes = [", StringComparison.Ordinal)..];

            int At(string needle)
            {
                var i = table.IndexOf(needle, StringComparison.Ordinal);
                Assert.True(i >= 0, $"the emitted route table has no entry for {needle}:\n{table}");
                return i;
            }

            // A literal segment outranks a bare parameter: /tag/all must be met before /tag/{Slug}, or
            // {Slug} swallows "all" -- it matches any string, so nothing else can decide this pair.
            Assert.True(At("'tag', 'all'") < At("'tag', ['Slug'"),
                "/tag/all is emitted AFTER /tag/{Slug}; the bare parameter would swallow it.");

            // And before a CONSTRAINED one, which is the same rule one digit down.
            Assert.True(At("'item', 'new'") < At("'item', ['Id'"),
                "/item/new is emitted AFTER /item/{Id:int}.");
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// THE VALUE CHANNEL, AND THE REUSE CHANNEL — D12's named blocker, in the emitted page.
    ///
    /// `mount(target)` had one argument and a captured group had nowhere to go. A page that captures
    /// values now takes a second one, lifts each parameter into a SIGNAL (so every read of it is
    /// already a live effect), and RETURNS the function the router calls instead of re-mounting.
    /// </summary>
    [Fact]
    public void AParameterisedPage_TakesItsValues_AndHandsBackAChannel()
    {
        var dir = AppInRepo();
        try
        {
            var item = File.ReadAllText(Path.Combine(dir, "Item.g.js"));

            Assert.Contains("export function mount(target, __route = {}) {", item);
            Assert.Contains("const id = signal(__route.Id);", item);
            Assert.Contains("return (__p) => {", item);
            Assert.Contains("id.value = __p.Id;", item);

            // The read of @Id is an EFFECT, not a fold. That is what makes the channel enough: assigning
            // the signal is the whole of "the parameters were re-set".
            Assert.Contains("effect(() => setText(", item);

            // The page's OWN state is untouched by any of it -- `seen` is an ordinary signal, and the
            // channel does not reset it. That is instance reuse, in the bytes.
            Assert.Contains("const seen = signal(0);", item);
            Assert.DoesNotContain("seen.value = ", item);
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// PAY FOR WHAT YOU USE, ASSERTED ON BOTH SIDES OF THE LINE (decision 163's own cost claim).
    ///
    /// A page that captures NOTHING keeps `mount(target)` — no second argument, no channel — even
    /// though it is compiled in the same app as pages that do. And decision 139's app is untouched
    /// entirely: RoutingTests' byte snapshot is the wall on the router side, and this is the wall on
    /// the page side.
    /// </summary>
    [Fact]
    public void APageWithNoRouteParameters_IsUnchangedByLivingInAParameterisedApp()
    {
        var dir = AppInRepo();
        try
        {
            var home = File.ReadAllText(Path.Combine(dir, "Home.g.js"));

            Assert.Contains("export function mount(target) {", home);
            Assert.DoesNotContain("__route", home);
            Assert.DoesNotContain("__p", home);
            Assert.DoesNotContain("return (", home);
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// THE CONVERTERS ARE EMITTED PER KIND, not as one table of everything. An app that routes no
    /// `:long` must not ship the BigInt converter — the cost lands in the app that asked for it, which
    /// is the same argument decision 139 made about the runtime.
    /// </summary>
    [Fact]
    public void OnlyTheConvertersTheAppCanReach_AreEmitted()
    {
        var dir = Path.Combine(RepoPaths.Root, "samples", "RouteParams", $".gen-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var page = Path.Combine(dir, "Only.razor");
            File.WriteAllText(page,
                "@page \"/only/{Id:int}\"\n<p id=\"a\">@Id</p>\n@code {\n    [Parameter] public int Id { get; set; }\n}\n");

            var (exit, _, stderr) = Run.RouterWithRuntime(
                Path.Combine(dir, "Router.g.js"), "../../../src/filament-runtime/src/index.ts", page);
            Assert.True(exit == 0, stderr);

            var js = File.ReadAllText(Path.Combine(dir, "Router.g.js"));
            Assert.Contains("i: (v) =>", js);
            Assert.DoesNotContain("BigInt", js);
            Assert.DoesNotContain("l: (v) =>", js);
            Assert.DoesNotContain("b: (v) =>", js);
            Assert.DoesNotContain("s: (v) =>", js);
        }
        finally { Cleanup(dir); }
    }

    // ---- refusals: every shape that cannot be converted EXACTLY -------------------------------------

    /// <summary>
    /// A ROUTE SHAPE THIS COMPILER CANNOT MATCH FAITHFULLY IS REFUSED, LOUD AND LOCATED — which is the
    /// whole of A9. Each of these was previously admitted at exit 0 and became a blank screen.
    ///
    /// A refusal emits nothing, so a refusal is always faithful. A wrong match renders the wrong page.
    /// </summary>
    [Theory]
    // route template,                what the message must name
    [InlineData("/h/{Id", "unbalanced")]                  // the register's Hmalformed witness
    [InlineData("/files/{*Rest}", "CATCH-ALL")]           // D12: /files itself renders in Blazor
    [InlineData("/f/{Id:int?}", "OPTIONAL")]              // changes the matcher, not the converter
    [InlineData("/g/{Id:notaconstraint}", "notaconstraint")]
    [InlineData("/d/{Id:guid}", "Guid")]                  // real constraint, refused for a stated reason
    [InlineData("/x/{Id:double}", "double")]              // culture-dependent parse
    [InlineData("/item-{Id}", "WHOLE segment")]           // Blazor rejects this too
    [InlineData("/dup/{Id}/{id}", "twice")]               // case-insensitive: the second would win
    public void AnUnmatchableRouteShape_IsRefused_NotCopiedIntoTheTable(string route, string mentions)
    {
        var (exit, stderr) = CompileRoute(route, "<p id=\"a\">a</p>\n");

        Assert.True(exit != 0, $"the route '{route}' was COMPILED, not refused");
        Assert.Contains("FIL0003: [unsupported-route]", stderr);
        Assert.Contains(mentions, stderr);
        // LOCATED at the @page the author wrote -- line 1, column 1.
        Assert.Contains("(1,1):", stderr);
    }

    /// <summary>
    /// A ROUTE WITH NO LEADING '/' IS RAZOR'S OWN ERROR, AND RAZOR GETS THERE FIRST.
    ///
    /// This test exists because the parser's leading-slash check looked like a rule of this compiler's
    /// and is not: measured, `@page "relative"` never reaches it — Razor refuses the directive at
    /// RZ9988 before the route is ever read. The check stays, so the parser is TOTAL rather than
    /// relying on a caller that happens to have run Razor first, but the message a user actually sees
    /// is Razor's, and pinning the wrong one here would have documented a rule nobody can trigger.
    /// </summary>
    [Fact]
    public void ARouteWithNoLeadingSlash_IsRefusedByRazorItself_BeforeThisCompilerLooks()
    {
        var (exit, stderr) = CompileRoute("relative", "<p id=\"a\">a</p>\n");

        Assert.True(exit != 0, "a route with no leading '/' was COMPILED");
        Assert.Contains("RZ9988", stderr);
        Assert.DoesNotContain("FIL0003", stderr);
    }

    /// <summary>
    /// A CAPTURED VALUE WITH NOWHERE TO GO IS REFUSED. Blazor compiles this and then fails in the
    /// BROWSER — the register measured `blazor-error-ui` and an empty page on `/e/7` — so refusing it
    /// is strictly better than what Blazor does, and it can never be wrong.
    /// </summary>
    [Fact]
    public void ARouteParameterWithNoMatchingParameter_IsRefused()
    {
        var (exit, stderr) = CompileRoute("/e/{Id}", "<p id=\"a\">a</p>\n");

        Assert.True(exit != 0, "a route parameter with no [Parameter] was COMPILED");
        Assert.Contains("FIL0003: [unsupported-route]", stderr);
        Assert.Contains("no matching", stderr);
    }

    /// <summary>
    /// THE CONSTRAINT DECIDES THE CAPTURED TYPE, so the [Parameter] must agree with it. `:int` hands
    /// the page a JS number; a `string` parameter would receive one and render it while the C# said
    /// otherwise. Blazor requires the same agreement and reports it at RUN time.
    /// </summary>
    [Fact]
    public void AParameterWhoseTypeDisagreesWithTheConstraint_IsRefused()
    {
        var (exit, stderr) = CompileRoute("/c/{Id:int}",
            "<p id=\"a\">@Id</p>\n@code {\n    [Parameter] public string Id { get; set; } = \"\";\n}\n");

        Assert.True(exit != 0, "a [Parameter] disagreeing with its route constraint was COMPILED");
        Assert.Contains("[route-parameter-type]", stderr);
        Assert.Contains("captures a 'int'", stderr);
    }

    /// <summary>
    /// A ROUTE PARAMETER DECLARES A BINDING IN mount()'s SCOPE, so it collides with a field the same way
    /// two fields do.
    ///
    /// FOUND BY PROBING, NOT BY REASONING, and it is the reason this test exists rather than a comment.
    /// `[Parameter] public int Id` beside `private int id` is legal C# and legal Blazor; before the route
    /// parameter joined the collision set it emitted `const id = signal(__route.Id);` followed by
    /// `const id = 5;` — invalid JavaScript, written at exit 0. A module that looks fine and does not
    /// load is exactly the silent mis-compile section 10 forbids.
    /// </summary>
    [Fact]
    public void ARouteParameterThatCollidesWithAField_IsRefused_NotEmittedAsInvalidJs()
    {
        var (exit, stderr) = CompileRoute("/x/{Id:int}",
            "<p id=\"a\">@Id @id</p>\n@code {\n    [Parameter] public int Id { get; set; }\n    private int id = 5;\n}\n");

        Assert.True(exit != 0, "a route parameter colliding with a field was COMPILED (as invalid JS)");
        Assert.Contains("[name-collision]", stderr);
        Assert.Contains("'id' and 'Id'", stderr);
    }

    /// <summary>
    /// AMBIGUITY IS DUPLICATION GENERALISED. Two identical routes were already refused (decision 139);
    /// `/item/{Id:int}` against `/item/{Other}` is the same problem wearing a parameter, and no ordering
    /// makes both reachable. Blazor throws on it; this refuses it, with Blazor's own coarse rule —
    /// constraints are deliberately not consulted.
    /// </summary>
    [Fact]
    public void TwoRoutesThatNoOrderingCanSeparate_AreRefused()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"filament-ambig-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var a = Path.Combine(dir, "First.razor");
        var b = Path.Combine(dir, "Second.razor");
        File.WriteAllText(a, "@page \"/item/{Id:int}\"\n<p id=\"a\">@Id</p>\n@code {\n    [Parameter] public int Id { get; set; }\n}\n");
        File.WriteAllText(b, "@page \"/item/{Other}\"\n<p id=\"b\">@Other</p>\n@code {\n    [Parameter] public string Other { get; set; } = \"\";\n}\n");

        var (exit, _, stderr) = Run.Router(Path.Combine(dir, "Router.g.js"), a, b);

        Assert.True(exit != 0, "two routes no ordering can separate were COMPILED");
        Assert.Contains("ambiguous", stderr);
        Assert.Contains("unreachable", stderr);

        // AND NOTHING WAS WRITTEN. The app-level gates run after every page has compiled, so the pages
        // used to be on disk already by the time the table was found unroutable -- page modules present,
        // no router, exit 1. A half-emitted app is not what "refused" means anywhere else in this
        // compiler, so the pages are now held in memory until every gate has passed.
        Assert.Empty(Directory.GetFiles(dir, "*.g.js"));
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedRouteParamsRouterJs_MatchesApprovedBytes()
    {
        var dir = AppInRepo();
        try
        {
            var actual = File.ReadAllText(Path.Combine(dir, "Router.g.js")).Replace("\r\n", "\n").TrimEnd() + "\n";
            var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots",
                "RouteParamsRouter.approved.js");
            if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
            Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// Compile ONE page carrying `route`, and hand back what the generator said. Single-file, not
    /// --router: a route's shape is gated where the PAGE is compiled, so a parameterised page is
    /// refused identically whether it is in an app or on its own.
    ///
    /// IN-REPO, and that is not a detail. The generator resolves its runtime specifier by walking up
    /// for src/filament-runtime, and it does so BEFORE it parses anything — so a fixture in the system
    /// temp directory dies at FIL-WIRING and the route is never reached. Asserting on that message
    /// would have passed a test that never exercised the rule.
    /// </summary>
    static (int exit, string stderr) CompileRoute(string route, string body)
    {
        var dir = Path.Combine(RepoPaths.Root, "samples", "RouteParams", $".route-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var page = Path.Combine(dir, "Page.razor");
            File.WriteAllText(page, $"@page \"{route}\"\n{body}");

            var (exit, _, stderr) = Run.Generator(page, Path.Combine(dir, "Page.g.js"));
            Assert.DoesNotContain("FIL-WIRING", stderr);
            if (exit != 0)
                Assert.False(File.Exists(Path.Combine(dir, "Page.g.js")), "a refused page still wrote a file");
            return (exit, stderr);
        }
        finally { Cleanup(dir); }
    }
}
