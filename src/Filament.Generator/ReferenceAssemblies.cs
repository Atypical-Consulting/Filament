using Microsoft.CodeAnalysis;

namespace Filament.Generator;

/// <summary>
/// The reference assemblies both front ends resolve types against.
///
/// ONE description of the probe, for two consumers, because it is exactly the shape
/// decision 53 got burned by: the Razor tag-helper chain needs them (without them
/// @onclick mis-parses IN SILENCE) and the C# front end needs them (without them no type
/// in @code resolves). Two copies of a filesystem probe is two things to keep in step
/// and one of them silently degrading.
///
/// It is discovered from the RUNNING runtime rather than hardcoded, so it survives a
/// machine that is not the author's -- but it is still a filesystem probe, and it FAILS
/// LOUDLY rather than returning an empty list, because an empty list is the silent
/// mis-parse of decision 53 and, here, a wall of false FIL0002s blaming the author for
/// the tool being broken.
///
/// Cached: the C# front end builds a Compilation per @code block, and re-reading ~200
/// assemblies off disk each time is a cost with no measurement behind it.
/// </summary>
public static class ReferenceAssemblies
{
    static List<MetadataReference>? _cache;

    /// <summary>Everything: the BCL and ASP.NET Core's ref pack. Razor's tag helper discovery needs both.</summary>
    public static List<MetadataReference> All() => _cache ??= Load();

    /// <summary>
    /// What a @code block resolves against. Today that is the same set -- @code's subset
    /// only reaches System.Int32/Double/Boolean/String and List&lt;T&gt;, all of which
    /// live in the BCL pack -- and it is a separate entry point so that narrowing it
    /// later is a change in ONE place with a name, not a silent divergence.
    /// </summary>
    public static List<MetadataReference> ForCode() => All();

    static List<MetadataReference> Load()
    {
        // FILAMENT_DOTNET_ROOT (decision 144): the ONE seam for a host with no SDK on disk -- the WASM
        // playground fetches the packs over HTTP, writes them into MEMFS, and points this variable at
        // them. Same layout, same loud failures below; nothing else about resolution changes. When set
        // it is AUTHORITATIVE: no silent fallback to the machine's SDK, or two hosts would resolve
        // against subtly different references and disagree in silence.
        var overrideRoot = Environment.GetEnvironmentVariable("FILAMENT_DOTNET_ROOT");

        // The SDK probe runs ONLY when no override is set: on the WASM host there is no SDK and
        // GetRuntimeDirectory's answer is meaningless at best -- the override must engage before it.
        // .../shared/Microsoft.NETCore.App/10.0.9/  ->  .../
        var dotnetRoot = overrideRoot is { Length: > 0 }
            ? Path.GetFullPath(overrideRoot)
            : Path.GetFullPath(Path.Combine(
                System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        var packs = Path.Combine(dotnetRoot, "packs");

        if (!Directory.Exists(packs))
            throw new GeneratorException(
                $"FIL-WIRING: no reference packs under '{packs}'. Tag helper discovery cannot run, and " +
                "without it @onclick mis-parses in silence (decision 53); nor can any type in @code be " +
                "resolved. Refusing to emit.");

        var netRef = NewestRefDir(Path.Combine(packs, "Microsoft.NETCore.App.Ref"));
        var aspRef = NewestRefDir(Path.Combine(packs, "Microsoft.AspNetCore.App.Ref"));

        var files = Directory.GetFiles(netRef, "*.dll").Concat(Directory.GetFiles(aspRef, "*.dll"));
        var refs = files.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();

        if (refs.Count == 0)
            throw new GeneratorException("FIL-WIRING: reference packs resolved to zero assemblies. Refusing to emit.");
        return refs;
    }

    static string NewestRefDir(string packRoot)
    {
        if (!Directory.Exists(packRoot))
            throw new GeneratorException($"FIL-WIRING: reference pack '{packRoot}' not found. Refusing to emit.");

        var best = Directory.GetDirectories(packRoot)
            .Select(d => (dir: d, ver: ParseVersion(Path.GetFileName(d))))
            .Where(x => x.ver is not null)
            .OrderByDescending(x => x.ver)
            .Select(x => x.dir)
            .FirstOrDefault()
            ?? throw new GeneratorException($"FIL-WIRING: no versioned directory under '{packRoot}'.");

        var refDir = Directory.GetDirectories(Path.Combine(best, "ref")).OrderByDescending(x => x).FirstOrDefault()
            ?? throw new GeneratorException($"FIL-WIRING: no ref/<tfm> directory under '{best}'.");
        return refDir;
    }

    static Version? ParseVersion(string s) => Version.TryParse(s.Split('-')[0], out var v) ? v : null;
}
