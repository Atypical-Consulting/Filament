namespace Filament.Generator;

/// <summary>
/// A ROUTE TEMPLATE, parsed — `/item/{Id:int}` as segments rather than as a string (decision 163).
///
/// WHY THIS TYPE EXISTS. Decision 139 shipped a router whose table is matched by STRING EQUALITY:
/// `routes.find(([r]) => r === path)`. That is exactly right for `/` and `/about`, and it is a BLANK
/// SCREEN for `@page "/item/{Id:int}"` — the route literal goes in verbatim, `/item/7` equals nothing in
/// the table, and the app renders nothing with no diagnostic at exit 0. The honesty register recorded it
/// as A9, together with the reason it could not be patched: a route parameter needs a real matcher, a
/// precedence order, and a channel that carries the captured value into the page. That is one slice, and
/// this file is its front half.
///
/// WHAT IS ADMITTED, AND WHY THE REST IS REFUSED RATHER THAN APPROXIMATED. Four shapes:
///
///     /item/{Id}          a string parameter
///     /item/{Id:int}      Int32,  NumberStyles.Integer + range   -> a JS number
///     /item/{Id:long}     Int64,  NumberStyles.Integer + range   -> a JS BigInt (decision 112)
///     /flag/{On:bool}     bool.TryParse                          -> a JS boolean
///
/// Each of those has a JS spelling that is EXACT, and the exactness is the admission criterion — the same
/// one decision 112 applied when it sent `long` to BigInt rather than to a double. Everything else is
/// refused WITH ITS REASON:
///
///   - `:guid` — `System.Guid` is not in the type subset (TypeSubset.cs), so the `[Parameter]` the route
///     would have to feed cannot be declared in the first place. Admitting the constraint would mean
///     capturing a value with nowhere to put it. And Blazor NORMALISES a Guid (the `N`, `B` and `P`
///     formats all match, and all render lowercase-dashed), so an opaque string diverges on the rendered
///     text as well as on the match.
///   - `:double`/`:decimal`/`:float`/`:datetime` — culture-dependent parses. `Number()` is not
///     `double.TryParse(…, InvariantCulture)` and the divergences are silent.
///   - an OPTIONAL parameter (`{Id:int?}`) and a CATCH-ALL (`{*Rest}`) — both change the MATCHER, not
///     just the converter: Blazor renders `/files` for `/files/{*Rest}` with `Rest = null`, so a naive
///     `^/files/(.*)$` misses it and the app falls through to a DIFFERENT page. That is the register's
///     D12 finding, and it is refused here rather than shipped wrong.
///   - a mixed segment (`/item-{Id}`) — Blazor rejects it too.
///
/// A refusal emits nothing, so a refusal is always faithful. A wrong match renders the wrong page.
/// </summary>
public sealed record RouteTemplate(string Raw, IReadOnlyList<RouteSegment> Segments)
{
    /// <summary>Does any segment capture a value? This is the switch the whole slice hangs on: a route
    /// table with NO parameters keeps decision 139's string-equality router, byte for byte, so an app
    /// that does not use route parameters pays nothing for the fact that they exist — the same
    /// pay-for-what-you-use argument decision 139 made about the runtime, one level down.</summary>
    public bool HasParameters => Segments.Any(s => s.IsParameter);

    public IEnumerable<RouteSegment> Parameters => Segments.Where(s => s.IsParameter);

    /// <summary>
    /// The specificity digits ASP.NET Core's `RoutePrecedence.ComputeInbound` assigns, one per segment:
    /// a literal is 1, a CONSTRAINED parameter is 2, a bare parameter is 3 — lower is more specific, and
    /// an earlier segment dominates a later one. Sorting the table by this at BUILD TIME is what makes
    /// Blazor's most-specific-wins ordering cost ZERO router bytes: the router keeps its linear scan and
    /// simply meets the routes in the right order. (4 and 5, the catch-all digits, cannot occur — a
    /// catch-all is refused above.)
    /// </summary>
    public string Precedence => string.Concat(Segments.Select(s => s.IsParameter ? (s.Constraint is null ? '3' : '2') : '1'));

    /// <summary>
    /// Could a single path match BOTH templates? Blazor's own ambiguity rule, and deliberately the coarse
    /// one it uses: same segment count, and at every index either two equal literals or two parameters.
    /// Constraints are NOT consulted — `/item/{Id:int}` and `/item/{Slug}` are ambiguous to Blazor even
    /// though `/item/x` only satisfies one of them, and matching that here means the compiler refuses
    /// exactly what Blazor throws on rather than inventing a narrower rule of its own.
    /// </summary>
    public bool Overlaps(RouteTemplate other)
    {
        if (Segments.Count != other.Segments.Count) return false;
        for (var i = 0; i < Segments.Count; i++)
        {
            var a = Segments[i];
            var b = other.Segments[i];
            if (a.IsParameter != b.IsParameter) return false;
            if (!a.IsParameter && !string.Equals(a.Text, b.Text, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
}

/// <summary>One segment of a route template. A literal carries its text; a parameter carries the name as
/// the author WROTE it — the router keys the captured values by that spelling and the page reads them back
/// by it, so the template is the single source of the name and the two can never drift.</summary>
/// <param name="IsParameter">is this a `{…}` capture, or a literal path segment?</param>
/// <param name="Text">the literal text, or the parameter name</param>
/// <param name="Constraint">the route constraint (`int`, `long`, `bool`), or null for a bare `{Name}`</param>
public readonly record struct RouteSegment(bool IsParameter, string Text, string? Constraint)
{
    /// <summary>The one-letter kind the emitted table carries. Single letters because this string is
    /// repeated once per parameter in shipped bytes, and because the converter table is keyed by it.</summary>
    public char Kind => Constraint switch { null => 's', "int" => 'i', "long" => 'l', "bool" => 'b', _ => '?' };

    /// <summary>The C# type a `[Parameter]` must declare for this segment — Blazor requires the two to
    /// agree, and a mismatch there is a runtime failure rather than a build one.</summary>
    public string ClrType => Constraint switch { null => "string", "int" => "int", "long" => "long", "bool" => "bool", _ => "?" };
}

/// <summary>The parser. Separate from the record so a refusal is a returned REASON rather than an
/// exception: every caller has a source span to attach it to, and none of them wants a stack trace.</summary>
public static class RouteTemplateParser
{
    /// <summary>The constraints whose JS conversion is exact. Named in the refusal message, so an author
    /// who wrote `:guid` is told what IS admitted rather than only what is not.</summary>
    public static readonly string[] AdmittedConstraints = ["int", "long", "bool"];

    /// <summary>
    /// Parse a route template, or say why not. `error` is a complete sentence in the compiler's voice:
    /// what was written, what it would have done, and what to write instead.
    /// </summary>
    public static RouteTemplate? TryParse(string raw, out string? error)
    {
        error = null;

        if (!raw.StartsWith('/'))
        {
            error = $"the route template '{raw}' does not begin with '/'. Blazor requires it (RZ9988), so " +
                    "this page could not be reached in Blazor either. Refusing to emit.";
            return null;
        }

        var segments = new List<RouteSegment>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Blazor's own split: EMPTY segments are dropped, so '/a//b' and '/a/b/' are both ['a','b'].
        // The matcher does the same to the live pathname, and doing it identically on both sides is what
        // makes a trailing slash a match rather than a blank screen.
        foreach (var part in raw.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var open = part.IndexOf('{');
            var close = part.IndexOf('}');

            if (open < 0 && close < 0)
            {
                if (part.Contains('*'))
                {
                    error = $"the route template '{raw}' has a '*' in the literal segment '{part}'. A catch-all " +
                            "is spelled `{*Rest}` in Blazor and is not in this subset (see below); a bare '*' is " +
                            "nothing at all. Refusing to emit.";
                    return null;
                }
                segments.Add(new RouteSegment(false, part, null));
                continue;
            }

            // A brace that opens and never closes is the register's `Hmalformed` witness: it compiled at
            // exit 0 and copied '/h/{Id' into the table verbatim. It is a typo, and it is reported as one.
            if (open < 0 || close < 0 || close < open)
            {
                error = $"the route template '{raw}' has an unbalanced '{{' or '}}' in the segment '{part}'. " +
                        "Refusing to emit rather than copy a malformed route into the route table, where it " +
                        "would simply never match and the page would be a blank screen.";
                return null;
            }

            // Blazor rejects a segment that mixes a literal and a parameter, so the refusal is not a
            // subset boundary here -- it is the same error, reported earlier.
            if (open != 0 || close != part.Length - 1)
            {
                error = $"the route template '{raw}' mixes literal text and a parameter in the segment " +
                        $"'{part}'. Blazor does not support that either: a '{{…}}' must be the WHOLE segment. " +
                        "Refusing to emit.";
                return null;
            }

            var inner = part[1..^1];

            if (inner.StartsWith('*'))
            {
                error = $"the route template '{raw}' declares the CATCH-ALL parameter '{part}', which is not " +
                        "in this subset. A catch-all is not just a wider match: Blazor renders the page for " +
                        "the parameter's own prefix too (`/files` matches `/files/{*Rest}` with Rest = null), " +
                        "so a matcher that requires the trailing segments would fall through and render a " +
                        "DIFFERENT page. Refusing to emit rather than route to the wrong page.";
                return null;
            }

            if (inner.EndsWith('?'))
            {
                error = $"the route template '{raw}' declares the OPTIONAL parameter '{part}', which is not in " +
                        "this subset. An optional parameter makes one template match two different segment " +
                        "counts and changes how it is ordered against the others; it is a matcher change, not " +
                        "a converter change. Refusing to emit.";
                return null;
            }

            var colon = inner.IndexOf(':');
            var name = colon < 0 ? inner : inner[..colon];
            var constraint = colon < 0 ? null : inner[(colon + 1)..];

            if (name.Length == 0)
            {
                error = $"the route template '{raw}' has a parameter with no name in the segment '{part}'. " +
                        "Refusing to emit.";
                return null;
            }

            if (constraint is not null && !AdmittedConstraints.Contains(constraint, StringComparer.Ordinal))
            {
                // `:guid` gets its own sentence: it is a REAL Blazor constraint refused for a reason
                // about this compiler, not a typo, and telling an author "unknown constraint" would be
                // wrong as well as unhelpful.
                var why = constraint == "guid"
                    ? "`System.Guid` is not in this compiler's type subset, so the `[Parameter]` this route " +
                      "would feed cannot be declared. Blazor also NORMALISES a Guid — the `N`, `B` and `P` " +
                      "spellings all match and all render lowercase-dashed — so carrying the raw segment " +
                      "would diverge on the rendered text as well as on the match."
                    : $"'{constraint}' is not a constraint this compiler converts exactly. Culture-dependent " +
                      "parses (`double`, `decimal`, `float`, `datetime`) have no exact JS spelling, and an " +
                      "inexact one diverges silently.";

                error = $"the route template '{raw}' uses the route constraint ':{constraint}'. {why} " +
                        $"Admitted: a bare {{Name}} (string), {string.Join(", ", AdmittedConstraints.Select(c => ":" + c))}. " +
                        "Refusing to emit.";
                return null;
            }

            // Blazor matches a route parameter to a [Parameter] case-insensitively, so two parameters
            // differing only in case are the SAME parameter and the second would overwrite the first.
            if (!seen.Add(name))
            {
                error = $"the route template '{raw}' declares the parameter '{name}' twice. Blazor matches a " +
                        "route parameter to its [Parameter] case-insensitively, so the second capture would " +
                        "silently overwrite the first. Refusing to emit.";
                return null;
            }

            segments.Add(new RouteSegment(true, name, constraint));
        }

        return new RouteTemplate(raw, segments);
    }
}
