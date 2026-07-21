using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// THE TAILWIND TODO-LIST (decision 154) — the program's app-level witness, the Duel's pattern
/// at composition scale: TodoShell places the whole app through ChildContent (#131), TodoFooter
/// takes a reactive bound string down (#90) and raises ClearDone up (#130), the rows carry the
/// reactive loop-variable class (#152) and every static value is multi-token Tailwind (#151).
/// Each constituent's own gate lives in its slice's tests; these pin the COMPOSITION and the
/// app-level emission. The measurement is the bench 'todo' contract (BENCH n°65).
/// </summary>
public class TodoTests
{
    /// <summary>THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).</summary>
    [Fact]
    public void Gate_GeneratedTodo_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.TodoToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.TodoAnswerKey);
        Assert.True(exit == 0,
            "PHASE: Todo gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/Todo/todo.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedTodoJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.TodoToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "Todo.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The three attribute behaviours of the program, live in one module: the exotic multi-token
    /// static classes verbatim (#151), the row-class effect INSIDE the row create function with a
    /// parenthesised ternary over the per-record signal (#152), and the plain-@bind field lifted to
    /// a signal by the bind alone (#154 — nothing else reads newText).
    /// </summary>
    [Fact]
    public void EmittedTodo_CarriesTheProgramsThreeAttributeBehaviours()
    {
        var js = File.ReadAllText(Generate.TodoToTemp());
        Assert.Contains("'mx-auto max-w-[42rem] rounded-xl bg-white/90 p-6 shadow-lg sm:px-4 md:px-8'", js);
        Assert.Contains("'w-1/2 rounded border px-3 py-2 focus:ring-2'", js);
        Assert.Contains("'rounded bg-amber-500 px-4 py-2 hover:bg-amber-400 disabled:opacity-50'", js);

        var create = js.Substring(js.IndexOf("function createT"));
        create = create[..(create.IndexOf("\n  }") + 4)];
        Assert.Contains("effect(() => setAttr(", create);
        Assert.Contains("'flex gap-2 max-w-[42rem] ' + (t.done.value ? 'line-through text-slate-400' : 'text-slate-900')", create);

        Assert.Contains("const newText = signal('');", js);
        Assert.Contains("listen(_el3, 'change', (e) => { newText.value = e.target.value; });", js);
    }

    /// <summary>
    /// Composition fully ERASED: one module, no component boundary. The children's markup
    /// (#shell/#title from TodoShell, #footer/#left/#clear from TodoFooter) is inlined into the
    /// parent's create, the footer's Left tracks the parent's leftText signal, and the child's
    /// #clear button listens straight to the parent's translated ClearDone body.
    /// </summary>
    [Fact]
    public void EmittedTodo_InlinesBothChildren_NoComponentBoundarySurvives()
    {
        var js = File.ReadAllText(Generate.TodoToTemp());
        foreach (var id in new[] { "'shell'", "'title'", "'editor'", "'new'", "'add'", "'list'", "'footer'", "'left'", "'clear'" })
            Assert.Contains($".id = {id};", js);
        Assert.Contains("effect(() => setText(_tx0, leftText.value));", js);   // Left, reactive across the boundary
        Assert.DoesNotContain("TodoShell", js);                                // no component names survive
        Assert.DoesNotContain("TodoFooter", js);
        Assert.DoesNotContain("ChildContent", js);
    }

    /// <summary>Closed-runtime invariant: the app adds NO new runtime primitive.</summary>
    [Fact]
    public void EmittedTodo_OnlyImportsClosedRuntimePrimitives()
    {
        var js = File.ReadAllText(Generate.TodoToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name), $"'{name}' is not a runtime export.");
    }
}
