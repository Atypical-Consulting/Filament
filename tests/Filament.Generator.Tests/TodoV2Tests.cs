using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// THE TODO-V2 PROGRAM (decisions 156+): the capabilities a REAL todo app needs, each probed
/// missing on 2026-07-21 and widened as its own slice — the init lifecycle (#156), local JSON
/// (#157), @if inside a row (#158), keyboard events (#159), computed properties (#160). The
/// app-level measurement is the bench 'todo' contract v2 (BENCH n°66).
/// </summary>
public class TodoV2Tests
{
    static string Emit(string fixture)
    {
        // Under the repo root, NOT the system temp: the generator resolves the runtime specifier
        // by walking up to src/filament-runtime.
        var outPath = Path.Combine(RepoPaths.Supported, "Code", $".v2-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, stdout, stderr) = Run.Generator(Path.Combine(RepoPaths.Supported, "Code", fixture + ".razor"), outPath);
            Assert.True(exit == 0, $"{fixture}.razor refused:\n{stdout}\n{stderr}");
            return File.ReadAllText(outPath);
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    static string Refused(string fixture)
    {
        var outPath = Path.Combine(RepoPaths.Unsupported, "Code", $".v2-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(Path.Combine(RepoPaths.Unsupported, "Code", fixture + ".razor"), outPath);
            Assert.True(exit != 0, $"{fixture}.razor COMPILED; expected a located refusal.");
            Assert.False(File.Exists(outPath), "refused AND wrote the module anyway");
            return stderr;
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    // ---- W1: the init lifecycle (decision 156) ------------------------------

    /// <summary>
    /// OnInitialized runs ONCE, BEFORE the first paint: the call sits after the state prologue and
    /// before the first createElement, so the signals it writes are what the effects' first run
    /// reads. The override modifier is admitted for exactly this pair.
    /// </summary>
    [Fact]
    public void OnInitialized_RunsBeforeCreate()
    {
        var js = Emit("OnInit");
        Assert.Contains("onInitialized();", js);
        Assert.True(js.IndexOf("onInitialized();") < js.IndexOf("document.createElement"),
            "the init call must precede create() -- Blazor runs OnInitialized before the first render");
        Assert.Contains("function onInitialized()", js);
        Assert.Contains("msg.value = 'ready';", js);
    }

    /// <summary>
    /// OnInitializedAsync is called UN-AWAITED: its sync prefix runs before create, each
    /// continuation writes signals and the effects re-render. mount() itself stays synchronous.
    /// </summary>
    [Fact]
    public void OnInitializedAsync_CalledUnAwaited_BeforeCreate()
    {
        var js = Emit("OnInitAsync");
        Assert.Contains("async function onInitializedAsync()", js);
        Assert.Contains("onInitializedAsync();", js);
        Assert.DoesNotContain("await onInitializedAsync", js);
        Assert.True(js.IndexOf("onInitializedAsync();") < js.IndexOf("document.createElement"));
    }

    /// <summary>The REST of the lifecycle refuses BY NAME: no re-render pass exists to hook.</summary>
    [Fact]
    public void OnAfterRender_IsRefused_WithTheLifecycleReason()
    {
        var stderr = Refused("OnAfterRender");
        Assert.Contains("[unsupported-lifecycle]", stderr);
        Assert.Contains("OnInitialized", stderr);   // the message names the admitted pair
    }
}
