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
 * CONSEQUENCE, STATED PLAINLY: samples/Counter/counter.js -- the Phase 1 answer key --
 * does NOT create these two text nodes. Its own header comment transcribes the source
 * without the blank lines that App.razor actually has, which is how they were lost.
 * So this generator and the answer key build different DOMs, the gate reports it, and
 * the answer key is the one that diverges from the baseline. NOT changed to match:
 * decision 21/51 says the answer key is the reference and the generator is what is
 * judged, so a disagreement is a REPORT, not an edit.
 */

if (args.Length < 2 || args.Contains("--help") || args.Contains("-h"))
{
    Console.Error.WriteLine("usage: Filament.Generator <in.razor> <out.js> [--runtime <specifier>]");
    Console.Error.WriteLine("       Filament.Generator --dump-ir <in.razor>");
    return 2;
}

try
{
    if (args[0] == "--dump-ir")
    {
        Console.WriteLine(IrDumper.Dump(RazorFrontEnd.Parse(args[1]).Ir));
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
    throw new GeneratorException(
        $"FIL000: could not locate src/filament-runtime/src/index.ts above '{outDir}'. Pass --runtime <specifier>.");
}
