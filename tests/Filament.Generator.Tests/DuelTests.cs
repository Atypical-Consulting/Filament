using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// THE DUEL (decision 142) — one non-trivial app, both compilers. baseline/Duel.Blazor is a routed
/// task board whose Board page composes, in ONE component, most of what the register banked one
/// witness at a time: EditForm/@bind-Value (#137/138), a keyed @foreach over a reassigned List
/// (#140) of mutable records, per-row captured-lambda handlers (#141), element-level control flow
/// beside component-level control flow (#142's multi-region collect), LINQ (#116), and a generated
/// router over two pages (#139). These tests pin that the COMPOSITION keeps compiling — each
/// constituent's own gate lives in its own slice's tests; the app-level measurement is the bench
/// (site page /benchmark).
/// </summary>
public class DuelTests
{
    static string DuelInTemp()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"filament-duel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var routerOut = Path.Combine(dir, "Router.g.js");
        var (exit, stdout, stderr) = Run.Router(routerOut,
            Path.Combine(RepoPaths.Root, "baseline", "Duel.Blazor", "Pages", "Board.razor"),
            Path.Combine(RepoPaths.Root, "baseline", "Duel.Blazor", "Pages", "About.razor"));
        Assert.True(exit == 0, $"the generator refused to emit the Duel app:\n{stdout}\n{stderr}");
        return dir;
    }

    /// <summary>The whole app emits: two page modules + the router that imports them.</summary>
    [Fact]
    public void Duel_BothPagesAndRouter_Compile()
    {
        var dir = DuelInTemp();
        Assert.True(File.Exists(Path.Combine(dir, "Board.g.js")), "Board.g.js missing");
        Assert.True(File.Exists(Path.Combine(dir, "About.g.js")), "About.g.js missing");
        Assert.True(File.Exists(Path.Combine(dir, "Router.g.js")), "Router.g.js missing");
    }

    /// <summary>
    /// The decision-142 shape holds: component-level @if (the empty-state) AND an element-level
    /// @foreach live in ONE page as SEPARATE regions -- the @if's list() is anchored at its comment,
    /// the task list()'s source is the reassigned-List signal (#140), and the per-row handlers are
    /// wired inside the row create function (#141). Pinned on the emitted artifact, not assumed.
    /// </summary>
    [Fact]
    public void DuelBoard_ComposesTheRegisterInOneModule()
    {
        var js = File.ReadAllText(Path.Combine(DuelInTemp(), "Board.g.js"));
        Assert.Contains("list(_el0, () => (total.value === 0) ? [0] : [], () => 0, ifBody, _if0);", js);
        Assert.Contains("() => visible.value", js);
        var createAt = js.IndexOf("function createT(t)", StringComparison.Ordinal);
        var toggleAt = js.IndexOf("toggle(t.id)", StringComparison.Ordinal);
        var listAt = js.IndexOf("list(_el12", StringComparison.Ordinal);
        Assert.True(createAt >= 0 && toggleAt > createAt && listAt > toggleAt,
            "the per-row toggle handler must be wired inside createT, before list() registers it");
        // The row's state span is REACTIVE: Done is a per-record signal (Rows' Label story), so a
        // persisting row re-renders on toggle -- the #125 silent-stale-render class, held out.
        Assert.Contains("done: signal(false)", js);
        Assert.Contains("t.done.value ? 'done' : 'todo'", js);
    }
}
