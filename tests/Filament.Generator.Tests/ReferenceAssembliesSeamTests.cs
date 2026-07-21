using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// FILAMENT_DOTNET_ROOT (decision 144) — the ONE seam for a host with no SDK on disk: the WASM
/// playground fetches the reference packs over HTTP, writes them into MEMFS, and points this
/// variable at them. Same packs layout, same loud FIL-WIRING failures; nothing else about
/// resolution changes. Tested through the GENERATOR SUBPROCESS because the seam is process-global
/// (an env var + a static cache) -- in-process it would poison every other test's references.
/// </summary>
public class ReferenceAssembliesSeamTests
{
    static (int exit, string stderr) RunWithRoot(string dotnetRoot, string input)
    {
        var outPath = Path.Combine(RepoPaths.Root, "samples", "Counter", $".seam-{Guid.NewGuid():N}.js");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = RepoPaths.Root,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            var dll = Path.Combine(RepoPaths.Root, "src", "Filament.Generator", "bin",
#if DEBUG
                "Debug",
#else
                "Release",
#endif
                "net8.0", "Filament.Generator.dll");
            foreach (var a in new[] { dll, input, outPath }) psi.ArgumentList.Add(a);
            psi.Environment["FILAMENT_DOTNET_ROOT"] = dotnetRoot;
            using var p = System.Diagnostics.Process.Start(psi)!;
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, stderr);
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    /// <summary>
    /// A mirror layout under the override root behaves exactly as the SDK's own packs: the Counter
    /// witness compiles cleanly. The mirror is SYMLINKED to the real ref dirs, so this test proves
    /// the RESOLUTION path, not a copy of 80 MB of assemblies.
    /// </summary>
    [Fact]
    public void OverrideRoot_WithMirroredPacks_CompilesTheCounterWitness()
    {
        var real = RealDotnetRoot();
        var root = Path.Combine(Path.GetTempPath(), $"filament-seam-{Guid.NewGuid():N}");
        try
        {
            foreach (var pack in new[] { "Microsoft.NETCore.App.Ref", "Microsoft.AspNetCore.App.Ref" })
            {
                var src = Path.Combine(real, "packs", pack);
                Assert.True(Directory.Exists(src), $"host SDK has no {pack} at {src}");
                var dst = Path.Combine(root, "packs", pack);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                try
                {
                    // Directory.CreateSymbolicLink, not File.*: a FILE symlink pointing at a directory
                    // resolves on Unix but is BROKEN on Windows (the CI runner proved it -- the seam
                    // reported the pack "not found" through a link that existed).
                    Directory.CreateSymbolicLink(dst, src);
                }
                catch (IOException)
                {
                    // Windows without symlink privilege (no Developer Mode, not elevated). Mirroring by
                    // COPY would move ~80 MB per run for a resolution-path test that the other two OSes
                    // already prove; the loud-failure half below runs everywhere regardless.
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
            }
            var (exit, stderr) = RunWithRoot(root, Path.Combine(RepoPaths.Root, "baseline", "Counter.Blazor", "App.razor"));
            Assert.True(exit == 0, $"override root refused what the real root accepts:\n{stderr}");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>An override root with NO packs fails FIL-WIRING, loudly, naming the overridden path --
    /// never a silent fallback to the machine's SDK (the playground would ship subtly different refs).</summary>
    [Fact]
    public void OverrideRoot_WithoutPacks_FailsLoud_AndNamesThePath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"filament-seam-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var (exit, stderr) = RunWithRoot(root, Path.Combine(RepoPaths.Root, "baseline", "Counter.Blazor", "App.razor"));
            Assert.NotEqual(0, exit);
            Assert.Contains("FIL-WIRING", stderr);
            Assert.Contains(root, stderr);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static string RealDotnetRoot()
    {
        // The same derivation ReferenceAssemblies uses: runtime dir -> ../../..
        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        return Path.GetFullPath(Path.Combine(runtimeDir, "..", "..", ".."));
    }
}
