using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Filament.Generator;
using Microsoft.AspNetCore.Razor.Language;

namespace Filament.Playground;

/// <summary>
/// The bridge between the page and the UNCHANGED generator (decision 144).
///
/// HOW THE SDK-LESS HOST WORKS. The browser has no dotnet root, but it HAS a filesystem --
/// Emscripten's MEMFS -- so nothing about the generator's path-based pipeline needs to change:
/// the page fetches the curated reference pack (playground/refpack.list, proven equivalent by the
/// full test suite) and hands each assembly to <see cref="WriteRefAssembly"/>, which lays it out
/// under /refpack exactly as an SDK would; <see cref="Ready"/> then points FILAMENT_DOTNET_ROOT --
/// the generator's one wiring seam -- at it. Compilation itself is Program.cs's own order: parse,
/// Razor's diagnostics first, then the template compiler's, then (and only then) emitted JS.
/// </summary>
public static partial class PlaygroundApi
{
    const string RefRoot = "/refpack";
    const string WorkDir = "/work";

    /// <summary>One fetched reference assembly into the MEMFS packs layout, e.g.
    /// "packs/Microsoft.NETCore.App.Ref/10.0.9/ref/net10.0/System.Runtime.dll".</summary>
    [JSExport]
    internal static void WriteRefAssembly(string relativePath, byte[] bytes)
    {
        var full = Path.Combine(RefRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }

    /// <summary>
    /// Engage the seam and PROVE the wiring by compiling a known-good component right now: a broken
    /// hydration surfaces at load, in this result, never at the visitor's first keystroke. Returns
    /// the same JSON shape as <see cref="CompileRazor"/> (ok:false means the playground must not open).
    /// </summary>
    [JSExport]
    internal static string Ready()
    {
        Environment.SetEnvironmentVariable("FILAMENT_DOTNET_ROOT", RefRoot);
        return CompileRazor("<p id=\"probe\">@n</p>\n\n@code {\n    private int n = 0;\n}\n", "./filament.js");
    }

    /// <summary>Stage-by-stage boot bisection (dev diagnosis): which pipeline stage trips the runtime.</summary>
    [JSExport]
    internal static string Probe(int stage)
    {
        try
        {
            switch (stage)
            {
                case 0:
                    Environment.SetEnvironmentVariable("FILAMENT_DOTNET_ROOT", RefRoot);
                    return "env:" + (Environment.GetEnvironmentVariable("FILAMENT_DOTNET_ROOT") ?? "null");
                case 1:
                    Directory.CreateDirectory(WorkDir);
                    File.WriteAllText(Path.Combine(WorkDir, "t.txt"), "x");
                    return "fs:" + File.ReadAllText(Path.Combine(WorkDir, "t.txt"));
                case 2:
                    return "refs:" + ReferenceAssemblies.All().Count;
                case 3:
                {
                    Directory.CreateDirectory(WorkDir);
                    var f = Path.Combine(WorkDir, "Static.razor");
                    File.WriteAllText(f, "<p id=\"s\">static</p>\n");
                    var parse = RazorFrontEnd.Parse(f);
                    return "parse:" + parse.Ir.DocumentKind;
                }
                default:
                    return "?" + stage;
            }
        }
        catch (Exception ex)
        {
            return "EX[" + stage + "]: " + ex.GetType().Name + ": " + ex.Message;
        }
    }

    /// <summary>Razor source in -> { ok, js | diagnostics, ms } out. Refusals are the FEATURE: the
    /// diagnostics carry the same wording the CLI prints, FIL codes and all.</summary>
    [JSExport]
    internal static string CompileRazor(string razor, string runtimeSpecifier)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            Directory.CreateDirectory(WorkDir);
            var file = Path.Combine(WorkDir, "Component.razor");
            File.WriteAllText(file, razor);

            var parse = RazorFrontEnd.Parse(file);

            // Razor's own diagnostics first: a parse error must never be compiled past (Program.cs's order).
            var razorErrors = parse.Document.GetSyntaxTree().Diagnostics
                .Concat(parse.Ir.Diagnostics)
                .Where(d => d.Severity == RazorDiagnosticSeverity.Error)
                .Select(d => $"error: {d}")
                .ToList();
            if (razorErrors.Count > 0) return Json(false, null, razorErrors, sw.ElapsedMilliseconds);

            var compiler = new TemplateCompiler();
            var js = compiler.Compile(parse, runtimeSpecifier, "Component.razor");
            if (compiler.Diagnostics.Count > 0)
                return Json(false, null, compiler.Diagnostics.Select(d => $"error {d}").ToList(), sw.ElapsedMilliseconds);

            return Json(true, js, null, sw.ElapsedMilliseconds);
        }
        catch (GeneratorException ex)
        {
            return Json(false, null, [$"error {ex.Message}"], sw.ElapsedMilliseconds);
        }
    }

    static string Json(bool ok, string? js, List<string>? diagnostics, long ms) =>
        JsonSerializer.Serialize(new CompileResult(ok, js, diagnostics, ms), PlaygroundJson.Default.CompileResult);
}

public sealed record CompileResult(bool Ok, string? Js, List<string>? Diagnostics, long Ms);

/// <summary>Source-generated serialization: no reflection dance, identical behaviour trimmed or not.</summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
[System.Text.Json.Serialization.JsonSerializable(typeof(CompileResult))]
public sealed partial class PlaygroundJson : System.Text.Json.Serialization.JsonSerializerContext;
