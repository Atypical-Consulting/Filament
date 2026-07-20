using Filament.Generator;
using Microsoft.AspNetCore.Razor.Language;

/*
 * Filament.Generator — Razor template -> Filament JS.
 *
 *     dotnet run --project src/Filament.Generator -- <in.razor> <out.js> [--runtime <specifier>]
 *
 * WHY A CONSOLE APP AND NOT AN ISourceGenerator.
 * Spec 4.3 is explicit that Roslyn cannot emit non-C# into the compilation, and JS
 * is non-C#. 4.3 wants an MSBuild target writing into obj/filament/ eventually. For
 * the POC a console app invoked by the build script is the shortest path to a
 * MEASUREMENT, and a measurement is the point ("devant tout choix, prendre le chemin
 * qui mesure le plus vite"). The MSBuild target is a packaging concern that changes
 * no emitted byte, so it is deferred, not skipped. THIS IS AN ARBITRAGE, RECORDED.
 *
 * WHITESPACE BETWEEN SIBLINGS — DECIDED, NOT DEFAULTED.
 * The IR carries HtmlContentIntermediateNode "\n\n" nodes between the top-level
 * <h1>/<p>/<button>, because App.razor has blank lines between them. This generator
 * EMITS them as real Text nodes. That is deliberate and it is checked against the
 * artifact, not against an assumption: Blazor's own .NET 10 compiler emits
 *
 *     __builder.AddMarkupContent(0, "<h1 id=\"title\">Counter</h1>\n\n");
 *     __builder.AddMarkupContent(6, "\n\n");
 *
 * for this exact file, so Blazor ships those two text nodes into the DOM. Stripping
 * them would make Filament build a DIFFERENT DOM than Blazor from the SAME source
 * while the benchmark claims both frameworks do the same work (decision 5), and it
 * would silently bank the free advantage that decision 20 lists as an OPEN debt to
 * be pinned before any comparison ("~25 % de noeuds DOM en moins, gratuitement").
 * Taking that advantage by default is precisely what decision 20 forbids.
 *
 * HISTORY, KEPT BECAUSE IT IS THE EVIDENCE: samples/Counter/counter.js -- the Phase 1
 * answer key -- did NOT create these two text nodes. Its own header transcribed the
 * source without the blank lines App.razor actually has, which is how they were lost.
 * So the generator and the answer key built different DOMs and the gate reported it,
 * with the answer key as the side that diverged from the baseline. It was reported and
 * NOT silently matched: decision 21/51 says the answer key is the reference and the
 * generator is what is judged.
 *
 * RESOLVED BY THE OWNER -- decision 64. The answer key is now CORRECTED and builds
 * these two nodes too, measured in-browser: Blazor 7, generator 5, answer key 5 (it
 * was 3). The motive was THE CONTRACT, not the gate: a DOM contract that is not
 * actually shared invalidates every C4 comparison built on it. The gate narrowed from
 * two divergences to one as a SIDE EFFECT and still FAILS on the handler.
 *
 * THE RESIDUAL IS STILL OPEN: 5 < 7. Blazor's two extra nodes are `<!--!-->` comment
 * markers, one per AddMarkupContent call -- its own bookkeeping for finding a markup
 * range later. Filament has no render tree and nothing to find later, so it emits
 * none; that is defensible, and it is still a free create-time advantage. Decision
 * 20's open debt, disclosed, not banked.
 */

if (args.Length < 2 || args.Contains("--help") || args.Contains("-h"))
{
    Console.Error.WriteLine("usage: Filament.Generator <in.razor> <out.js> [--runtime <specifier>]");
    Console.Error.WriteLine("       Filament.Generator --dump-ir <in.razor>");
    Console.Error.WriteLine("       Filament.Generator --router <out.js> <page1.razor> [page2.razor ...]");
    return 2;
}

try
{
    if (args[0] == "--dump-ir")
    {
        Console.WriteLine(IrDumper.Dump(RazorFrontEnd.Parse(args[1]).Ir));
        return 0;
    }

    // --router: compile a SET of @page components into one app (decision 139). Each page is compiled to
    // its own module exactly as it would be standalone -- routing changes how pages are ASSEMBLED, not
    // how they are compiled -- and the router module that imports them is generated alongside.
    if (args[0] == "--router")
    {
        var routerOut = args[1];
        // Skip flags AND the value that follows one: `--runtime ./x.js` must not be read as a page.
        var pageFiles = new List<string>();
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i].StartsWith("--")) { i++; continue; }
            pageFiles.Add(args[i]);
        }
        if (pageFiles.Count == 0)
        {
            Console.Error.WriteLine("error: --router needs at least one page .razor");
            return 2;
        }

        var outDir = Path.GetDirectoryName(Path.GetFullPath(routerOut))!;
        Directory.CreateDirectory(outDir);
        var routerRuntime = ArgValue(args, "--runtime") ?? ResolveRuntimeSpecifier(routerOut);

        var pages = new List<RouterEmitter.Page>();
        foreach (var pageFile in pageFiles)
        {
            var pageParse = RazorFrontEnd.Parse(pageFile);
            if (Refused(pageParse, pageFile)) return 1;

            // A page WITHOUT @page has no route, so the router could not reach it. Refused rather than
            // dropped: a component silently absent from an app is the failure section 10 forbids.
            if (RazorFrontEnd.RouteOf(pageParse) is not { } route)
            {
                Console.Error.WriteLine(
                    $"error: {Path.GetFileName(pageFile)} has no @page route, so the router could not reach it. " +
                    "Give it an @page directive or leave it out of the app.");
                return 1;
            }

            var pageCompiler = new TemplateCompiler();
            var pageJs = pageCompiler.Compile(pageParse, routerRuntime, Path.GetFileName(pageFile));
            if (pageCompiler.Diagnostics.Count > 0)
            {
                Console.Error.WriteLine(
                    $"{Path.GetFileName(pageFile)}: refusing to emit ({pageCompiler.Diagnostics.Count} diagnostic(s)):");
                foreach (var d in pageCompiler.Diagnostics) Console.Error.WriteLine($"  error {d}");
                return 1;
            }

            var name = Path.GetFileNameWithoutExtension(pageFile);
            var pageOut = Path.Combine(outDir, name + ".g.js");
            File.WriteAllText(pageOut, pageJs);
            Console.Error.WriteLine($"{pageFile} -> {pageOut} ({System.Text.Encoding.UTF8.GetByteCount(pageJs)} B)  route {route}");
            pages.Add(new RouterEmitter.Page($"./{name}.g.js", route, "mount" + name));
        }

        var duplicate = pages.GroupBy(p => p.Route, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            Console.Error.WriteLine(
                $"error: two pages declare the route '{duplicate.Key}'. The first would always win and the " +
                "second would be unreachable, so this is refused rather than resolved by file order.");
            return 1;
        }

        var routerJs = RouterEmitter.Emit(pages);
        File.WriteAllText(routerOut, routerJs);
        Console.Error.WriteLine($"router -> {routerOut} ({System.Text.Encoding.UTF8.GetByteCount(routerJs)} B, {pages.Count} pages)");
        return 0;
    }

    var input = args[0];
    var output = args[1];

    var runtime = ArgValue(args, "--runtime") ?? ResolveRuntimeSpecifier(output);

    var parse = RazorFrontEnd.Parse(input);

    // Razor's own diagnostics first: a parse error must never be compiled past.
    var razorErrors = parse.Document.GetSyntaxTree().Diagnostics
        .Concat(parse.Ir.Diagnostics)
        .Where(d => d.Severity == Microsoft.AspNetCore.Razor.Language.RazorDiagnosticSeverity.Error)
        .ToList();
    if (razorErrors.Count > 0)
    {
        foreach (var d in razorErrors) Console.Error.WriteLine($"error: {d}");
        return 1;
    }

    var compiler = new TemplateCompiler();
    var js = compiler.Compile(parse, runtime, Path.GetFileName(input));

    if (compiler.Diagnostics.Count > 0)
    {
        Console.Error.WriteLine($"{Path.GetFileName(input)}: refusing to emit ({compiler.Diagnostics.Count} diagnostic(s)):");
        foreach (var d in compiler.Diagnostics) Console.Error.WriteLine($"  error {d}");
        return 1;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    File.WriteAllText(output, js);
    Console.Error.WriteLine($"{input} -> {output} ({System.Text.Encoding.UTF8.GetByteCount(js)} B)");
    return 0;
}
catch (GeneratorException ex)
{
    Console.Error.WriteLine($"error {ex.Message}");
    return 1;
}

/// <summary>Razor's OWN diagnostics, reported before anything is compiled past them. Shared by the
/// single-file and --router paths so a page in an app is gated exactly as it is on its own.</summary>
static bool Refused(ParseResult parse, string file)
{
    var errors = parse.Document.GetSyntaxTree().Diagnostics
        .Concat(parse.Ir.Diagnostics)
        .Where(d => d.Severity == Microsoft.AspNetCore.Razor.Language.RazorDiagnosticSeverity.Error)
        .ToList();
    if (errors.Count == 0) return false;

    Console.Error.WriteLine($"{Path.GetFileName(file)}: Razor reported {errors.Count} error(s):");
    foreach (var d in errors) Console.Error.WriteLine($"  error: {d}");
    return true;
}

static string? ArgValue(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

/// <summary>
/// The import specifier the emitted module uses to reach the runtime, computed as a
/// real relative path from the OUTPUT file rather than hardcoded, then verified to
/// point at a file that exists. A wrong specifier is a module that does not load.
/// </summary>
static string ResolveRuntimeSpecifier(string output)
{
    var outDir = Path.GetDirectoryName(Path.GetFullPath(output))!;
    for (var d = new DirectoryInfo(outDir); d is not null; d = d.Parent)
    {
        var candidate = Path.Combine(d.FullName, "src", "filament-runtime", "src", "index.ts");
        if (!File.Exists(candidate)) continue;
        var rel = Path.GetRelativePath(outDir, candidate).Replace(Path.DirectorySeparatorChar, '/');
        return rel.StartsWith('.') ? rel : "./" + rel;
    }
    // FIL-WIRING, not FIL000. Failing to find the runtime is the TOOL being misused, not
    // "your Razor is unsupported", and FIL000 squatted the spec's reserved FILxxxx
    // namespace -- it reads exactly like FIL0001 at a glance. Decision 61 set that policy
    // and this line was the one place that still broke it.
    throw new GeneratorException(
        $"FIL-WIRING: could not locate src/filament-runtime/src/index.ts above '{outDir}'. Pass --runtime <specifier>.");
}
