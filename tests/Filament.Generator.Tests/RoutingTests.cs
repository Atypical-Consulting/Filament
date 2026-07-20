using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// ROUTING (decision 139) — the LAST spec §3 non-goal, and the only one that could not be erased.
///
/// The other eight turned out to be lookups the compiler performs at build time and then deletes. A
/// route cannot be: it must be matched against a URL that exists only while the page runs, re-matched on
/// navigation, and pages un-mounted and re-mounted as it changes. That is behaviour, and behaviour has to
/// be somewhere at run time — so it is GENERATED INTO THE APP, never added to the shared runtime.
/// </summary>
public class RoutingTests
{
    static string RouterInTemp()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"filament-router-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var routerOut = Path.Combine(dir, "Router.g.js");
        var (exit, stdout, stderr) = Run.Router(routerOut, RepoPaths.RoutingPages);
        Assert.True(exit == 0, $"the generator refused to emit the routed app:\n{stdout}\n{stderr}");
        return routerOut;
    }

    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the emitted router is alpha-equivalent to the hand-written
    /// samples/Routing/router.js. The key is the SPEC and the REFERENCE (oracle: BENCH n°57).
    /// </summary>
    [Fact]
    public void Gate_GeneratedRouter_IsAlphaEquivalentToAnswerKey()
    {
        var router = RouterInTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, router, RepoPaths.RouterAnswerKey);
        Assert.True(exit == 0,
            "routing gate FAILED. The generated router is NOT alpha-equivalent to samples/Routing/router.js.\n"
            + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>
    /// THE ROUTE TABLE IS BUILT FROM THE @page DIRECTIVES, and the pages are IMPORTED rather than inlined —
    /// which is what makes a page module byte-identical whether it is routed or compiled alone.
    /// </summary>
    [Fact]
    public void EmittedRouter_ImportsEachPage_AndTabulatesItsRoute()
    {
        var js = File.ReadAllText(RouterInTemp());

        Assert.Contains("import { mount as mountHome } from './Home.g.js';", js);
        Assert.Contains("import { mount as mountAbout } from './About.g.js';", js);
        Assert.Contains("['/', mountHome]", js);
        Assert.Contains("['/about', mountAbout]", js);
    }

    /// <summary>
    /// THE FOUR BEHAVIOURS, each asserted because leaving any one out is a bug a user hits immediately:
    /// match, mount into a CLEARED target, intercept same-origin clicks, and listen for popstate.
    /// </summary>
    [Fact]
    public void EmittedRouter_Matches_Clears_Intercepts_AndHandlesBack()
    {
        var js = File.ReadAllText(RouterInTemp());

        Assert.Contains("location.pathname", js);              // 1. match
        Assert.Contains("target.textContent = '';", js);       // 2. clear before mounting
        Assert.Contains("e.preventDefault();", js);            // 3. intercept
        Assert.Contains("history.pushState", js);
        Assert.Contains("addEventListener('popstate', render);", js);   // 4. Back
    }

    /// <summary>
    /// A PAGE'S OWN MODULE IS UNCHANGED BY ROUTING. The route is metadata the router reads, so nothing
    /// about it leaks into the page — this is what lets @page compile standalone too.
    /// </summary>
    [Fact]
    public void EmittedPages_CarryNoTraceOfTheirRoute()
    {
        var router = RouterInTemp();
        var dir = Path.GetDirectoryName(router)!;

        var home = File.ReadAllText(Path.Combine(dir, "Home.g.js"));
        Assert.Contains("export function mount(target)", home);
        // It DOES contain '/about' -- that is the href of the link it renders, which is page CONTENT.
        // What it must not contain is any routing BEHAVIOUR, or a route table of its own.
        Assert.DoesNotContain("pushState", home);
        Assert.DoesNotContain("popstate", home);
        Assert.DoesNotContain("location.pathname", home);
        Assert.DoesNotContain("const routes", home);
    }

    /// <summary>
    /// TWO PAGES ON ONE ROUTE IS REFUSED. The first would always win and the second would be unreachable,
    /// so resolving it by file order would silently drop a page the author wrote.
    /// </summary>
    [Fact]
    public void DuplicateRoutes_AreRefused_NotResolvedByFileOrder()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"filament-dup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var a = Path.Combine(dir, "First.razor");
        var b = Path.Combine(dir, "Second.razor");
        File.WriteAllText(a, "@page \"/dup\"\n<p id=\"a\">a</p>\n");
        File.WriteAllText(b, "@page \"/dup\"\n<p id=\"b\">b</p>\n");

        var (exit, _, stderr) = Run.Router(Path.Combine(dir, "Router.g.js"), a, b);

        Assert.True(exit != 0, "two pages on the same route were COMPILED, not refused");
        Assert.Contains("unreachable", stderr);
    }

    /// <summary>
    /// A PAGE WITHOUT @page IS REFUSED, not dropped. The router could not reach it, and a component
    /// silently absent from an app is the failure section 10 forbids.
    /// </summary>
    [Fact]
    public void APageWithNoRoute_IsRefused_NotDropped()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"filament-noroute-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var page = Path.Combine(dir, "Orphan.razor");
        File.WriteAllText(page, "<p id=\"a\">a</p>\n");

        var (exit, _, stderr) = Run.Router(Path.Combine(dir, "Router.g.js"), page);

        Assert.True(exit != 0, "a routeless page was COMPILED into an app, not refused");
        Assert.Contains("@page", stderr);
    }

    /// <summary>
    /// THE PLACEMENT CLAIM, ASSERTED: the router is APP code. It imports no runtime primitive, so an app
    /// that does not route pays nothing for the fact that routing exists — the shared runtime is untouched.
    /// </summary>
    [Fact]
    public void EmittedRouter_ImportsNoRuntimePrimitive()
    {
        var js = File.ReadAllText(RouterInTemp());

        // Asserted on the IMPORTS, not on the whole text: the router's own comments discuss the runtime
        // it deliberately does not touch, and a naive substring search reads those as usage.
        var imports = js.Split('\n').Where(l => l.TrimStart().StartsWith("import ")).ToList();
        Assert.NotEmpty(imports);
        Assert.All(imports, line =>
        {
            Assert.DoesNotContain("filament-runtime", line);
            Assert.DoesNotContain("signal", line);
            Assert.DoesNotContain("effect", line);
        });
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedRouterJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(RouterInTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "Router.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
