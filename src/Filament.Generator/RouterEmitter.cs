using System.Text;

namespace Filament.Generator;

/// <summary>
/// THE ROUTER (decision 139) — the one spec §3 non-goal that could not be erased.
///
/// Every other framework feature this compiler admits turns out to be a LOOKUP it can perform at build
/// time and then delete: @ref is a naming decision, JS interop is a direct call, a cascade is lexical
/// scope, generics erase, @inherits merges text. Routing is not like them, and the difference is not a
/// matter of effort. A route must be matched against a URL that only exists while the page is running,
/// re-matched when the user navigates, and un-mounted and re-mounted as it changes. That is BEHAVIOUR,
/// and behaviour has to be somewhere at run time.
///
/// SO IT IS GENERATED INTO THE APP, NEVER ADDED TO THE RUNTIME, and that placement is the whole point:
///
///   - the shared signals runtime stays byte-frozen at 1,943 B, so C1's budget is untouched and every
///     app that does NOT route pays exactly nothing for the fact that routing exists;
///   - the cost lands in the app that asked for it, where it is visible on the wire and MEASURED
///     (BENCH n°57). A routing slice that reported no weight change would have measured the wrong thing.
///
/// The emitted router is deliberately small and deliberately readable. It does four things, and each is
/// there because leaving it out is a bug a user would hit immediately:
///
///   1. MATCH the current pathname against the routes, falling back to the catch-all if one is declared.
///   2. MOUNT the match into the target, clearing what was there — a router that appends rather than
///      replaces shows two pages at once on the first navigation.
///   3. INTERCEPT same-origin link clicks and push history instead of letting the browser reload. Without
///      this, "navigation" is a full page load and the SPA is a multi-page app with extra steps.
///   4. LISTEN for popstate, so Back works. A router that only handles clicks strands the user.
///
/// ROUTE PARAMETERS ARE A SECOND EMISSION, NOT A WIDER FIRST ONE (decision 163). A table of literal
/// routes is matched by string equality and that is not an approximation of anything — it is exactly
/// right, and it is what BENCH n°57 measured. So a route table with no parameters still emits the router
/// above, BYTE FOR BYTE, and only a table that actually declares a `{…}` gets the segment matcher, the
/// converters and the parameter channel. That is decision 139's own pay-for-what-you-use argument applied
/// one level down: the app that does not use route parameters pays nothing for the fact that they exist.
/// </summary>
public static class RouterEmitter
{
    /// <param name="Module">the page module's import specifier, e.g. "./Home.g.js"</param>
    /// <param name="Route">the route the page declared, parsed (decision 163)</param>
    /// <param name="Local">the local name its mount is imported as, e.g. "mountHome"</param>
    public readonly record struct Page(string Module, RouteTemplate Route, string Local);

    /// <summary>
    /// The router module. It IMPORTS each page rather than inlining it, so every page keeps compiling
    /// exactly as it does standalone — routing changes how pages are ASSEMBLED, not how they are
    /// compiled, and a page module is byte-identical whether or not it is routed.
    /// </summary>
    public static string Emit(IReadOnlyList<Page> pages)
    {
        if (pages.Count == 0)
            throw new GeneratorException("FIL-WIRING: the router was asked to emit with no pages.");

        return pages.Any(p => p.Route.HasParameters) ? EmitParameterised(pages) : EmitLiteral(pages);
    }

    /// <summary>Decision 139's router, unchanged. Reached when no route declares a parameter.</summary>
    static string EmitLiteral(IReadOnlyList<Page> pages)
    {
        var sb = new StringBuilder();
        Header(sb);
        sb.Append('\n');

        foreach (var p in pages)
            sb.Append($"import {{ mount as {p.Local} }} from '{p.Module}';\n");

        sb.Append("\nconst routes = [\n");
        foreach (var p in pages)
            sb.Append($"  [{Js(p.Route.Raw)}, {p.Local}],\n");
        sb.Append("];\n\n");

        sb.Append(
            """
            export function mount(target) {
              // The active page's target is cleared before the next one mounts: a router that APPENDS
              // shows two pages at once the moment anyone navigates.
              function render() {
                const path = location.pathname;
                const hit = routes.find(([r]) => r === path) ?? routes.find(([r]) => r === '*');
                target.textContent = '';
                if (hit) hit[1](target);
              }

              // Same-origin links become history pushes. Without this, every link is a full page load and
              // the app is a multi-page app with extra steps. External links are left alone, and so are
              // modified clicks (ctrl/meta/shift) -- those mean "open elsewhere" and are the user's.
              addEventListener('click', (e) => {
                if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
                const a = e.target.closest && e.target.closest('a[href]');
                if (!a || a.target || a.hasAttribute('download')) return;
                const url = new URL(a.getAttribute('href'), location.href);
                if (url.origin !== location.origin) return;
                e.preventDefault();
                if (url.pathname !== location.pathname) {
                  history.pushState(null, '', url.pathname);
                  render();
                }
              });

              // Back and Forward. A router that only handles clicks strands the user on the first Back.
              addEventListener('popstate', render);

              render();
            }

            """);

        return sb.ToString();
    }

    /// <summary>
    /// The router for a table that declares at least one route parameter (decision 163).
    ///
    /// It differs from the literal router in exactly three places, and each one is a thing the register's
    /// D12 measured against real Blazor and found the literal router gets wrong:
    ///
    ///   MATCH is per SEGMENT, not per string. Blazor splits the path on '/', DROPS empty segments (so a
    ///   trailing slash and a doubled slash both still match), URL-decodes each one, and compares a
    ///   literal segment case-INSENSITIVELY. A route parameter's segment goes through its constraint's
    ///   converter, and a constraint that REJECTS is a non-match rather than an error — the scan carries
    ///   on to the next route, which is what makes `/item/new` reach a literal `/item/new` page even
    ///   though `/item/{Id:int}` was declared first.
    ///
    ///   ORDER is decided at BUILD TIME. Blazor is most-specific-wins independently of declaration order;
    ///   the emitted table is a linear scan. Rather than rank at run time — which would be real router
    ///   bytes — the compiler SORTS the table by ASP.NET Core's own precedence digits before emitting it,
    ///   so the scan meets the routes in Blazor's order and the ranking costs nothing on the wire.
    ///
    ///   THE PAGE IS REUSED when only its parameters changed. Blazor does not re-create a component on a
    ///   parameter-only navigation: its state survives and OnInitialized does NOT run again (measured in
    ///   the register: n=3 across /item/7 -> /item/8). A router that re-mounts unconditionally resets the
    ///   page and re-runs its init. So a page whose route captures values RETURNS a channel from mount(),
    ///   and the router calls that instead of re-mounting when the match is the same page.
    /// </summary>
    static string EmitParameterised(IReadOnlyList<Page> pages)
    {
        var sb = new StringBuilder();
        Header(sb);
        sb.Append("//\n");
        sb.Append("// This app declares ROUTE PARAMETERS, so the table is matched per SEGMENT rather than by\n");
        sb.Append("// string equality, and it is emitted already sorted most-specific-first -- the ranking is\n");
        sb.Append("// the compiler's, so Blazor's most-specific-wins order costs no bytes here (decision 163).\n\n");

        foreach (var p in pages)
            sb.Append($"import {{ mount as {p.Local} }} from '{p.Module}';\n");

        // Only the converters this app can actually reach. A table of `:int` routes must not ship the
        // BigInt one: the cost lands in the app that asked for it, per parameter kind.
        var kinds = pages.SelectMany(p => p.Route.Parameters).Select(s => s.Kind).ToHashSet();

        sb.Append("\n// A route parameter's value, converted the way Blazor's own route constraint converts it,\n");
        sb.Append("// or undefined when the constraint REJECTS -- which is a NON-MATCH, not an error, so the\n");
        sb.Append("// scan carries on and a later route still gets its turn.\n");
        sb.Append("const convert = {\n");
        if (kinds.Contains('s'))
            sb.Append("  s: (v) => v,\n");
        if (kinds.Contains('i'))
        {
            // NumberStyles.Integer is leading/trailing whitespace + a leading sign, and NOTHING else --
            // no hex, no exponent, no 'Infinity', all of which Number() would happily accept. The Int32
            // range check is the other half: Blazor does NOT match /item/2147483648, and a bare Number()
            // would match it and render a page. This is the same pair TemplateCompiler already applies to
            // an int @bind, for the same reason.
            sb.Append("  i: (v) => { if (!/^\\s*[+-]?\\d+\\s*$/.test(v)) return undefined;\n");
            sb.Append("    const n = Number(v); return n >= -2147483648 && n <= 2147483647 ? n : undefined; },\n");
        }
        if (kinds.Contains('l'))
        {
            // `long` is BigInt (decision 112): the range check is exact past 2^53, where a double is not.
            // BigInt() rejects a leading '+' that NumberStyles.Integer accepts, so the sign is stripped.
            sb.Append("  l: (v) => { if (!/^\\s*[+-]?\\d+\\s*$/.test(v)) return undefined; const t = v.trim();\n");
            sb.Append("    const n = BigInt(t[0] === '+' ? t.slice(1) : t);\n");
            sb.Append("    return n >= -9223372036854775808n && n <= 9223372036854775807n ? n : undefined; },\n");
        }
        if (kinds.Contains('b'))
            sb.Append("  b: (v) => { const t = v.trim().toLowerCase();\n" +
                      "    return t === 'true' ? true : t === 'false' ? false : undefined; },\n");
        sb.Append("};\n");

        // The table, already in Blazor's precedence order. A literal segment is a STRING; a parameter is
        // [name, kind], and the name is the ROUTE TEMPLATE's own spelling -- the page reads it back by
        // that same spelling, so the two can never drift.
        sb.Append("\n// [segments, mount] -- a literal segment is a string, a parameter is [name, kind].\n");
        sb.Append("const routes = [\n");
        foreach (var p in pages)
        {
            var segs = p.Route.Segments.Select(s =>
                s.IsParameter ? $"[{Js(s.Text)}, {Js(s.Kind.ToString())}]" : Js(s.Text));
            sb.Append($"  [[{string.Join(", ", segs)}], {p.Local}],  // {p.Route.Raw}\n");
        }
        sb.Append("];\n\n");

        sb.Append(
            """
            export function mount(target) {
              // The page on screen, and its parameter channel. Blazor REUSES a component when only its
              // route parameters changed -- its state survives and OnInitialized does not run again -- so
              // re-mounting unconditionally would reset the page on /item/7 -> /item/8.
              let active = null;
              let setParams = null;

              // Blazor's own split: EMPTY segments are dropped, so '/a//b' and '/a/b/' both read as
              // ['a','b'], and every segment is URL-decoded before it is compared or converted.
              function match(path) {
                const parts = [];
                for (const s of path.split('/')) if (s !== '') parts.push(decodeURIComponent(s));
                for (const [segs, page] of routes) {
                  if (segs.length !== parts.length) continue;
                  const values = {};
                  let ok = true;
                  for (let i = 0; i < segs.length; i++) {
                    const seg = segs[i];
                    if (typeof seg === 'string') {
                      // Blazor compares a LITERAL segment case-insensitively.
                      if (seg.toLowerCase() !== parts[i].toLowerCase()) { ok = false; break; }
                    } else {
                      const v = convert[seg[1]](parts[i]);
                      if (v === undefined) { ok = false; break; }
                      values[seg[0]] = v;
                    }
                  }
                  if (ok) return [page, values];
                }
                return null;
              }

              // The active page's target is cleared before the next one mounts: a router that APPENDS
              // shows two pages at once the moment anyone navigates. The reuse path clears NOTHING --
              // that is the point of it.
              function render() {
                const hit = match(location.pathname);
                if (hit && hit[0] === active && setParams) { setParams(hit[1]); return; }
                target.textContent = '';
                active = hit && hit[0];
                setParams = hit ? hit[0](target, hit[1]) : null;
              }

              // Same-origin links become history pushes. Without this, every link is a full page load and
              // the app is a multi-page app with extra steps. External links are left alone, and so are
              // modified clicks (ctrl/meta/shift) -- those mean "open elsewhere" and are the user's.
              addEventListener('click', (e) => {
                if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
                const a = e.target.closest && e.target.closest('a[href]');
                if (!a || a.target || a.hasAttribute('download')) return;
                const url = new URL(a.getAttribute('href'), location.href);
                if (url.origin !== location.origin) return;
                e.preventDefault();
                if (url.pathname !== location.pathname) {
                  history.pushState(null, '', url.pathname);
                  render();
                }
              });

              // Back and Forward. A router that only handles clicks strands the user on the first Back.
              addEventListener('popstate', render);

              render();
            }

            """);

        return sb.ToString();
    }

    static void Header(StringBuilder sb)
    {
        sb.Append("// GENERATED by Filament.Generator. DO NOT EDIT.\n");
        sb.Append("//\n");
        sb.Append("// The router for this app, compiled from the @page directives of the components below.\n");
        sb.Append("// It is generated INTO THE APP on purpose: routing is the one feature that needs code at\n");
        sb.Append("// run time, and putting it here keeps the shared signals runtime byte-frozen, so an app\n");
        sb.Append("// that does not route pays nothing for the fact that routing exists.\n");
    }

    /// <summary>A JS string literal. Single-quoted to match every other emission in this compiler.</summary>
    static string Js(string s) => "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
}
