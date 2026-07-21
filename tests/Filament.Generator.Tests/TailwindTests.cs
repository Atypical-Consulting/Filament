using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// THE TAILWIND PROGRAM (decisions 151+). Tailwind's utility syntax is hostile to naive
/// attribute handling: variant colons, fraction slashes, arbitrary-value brackets, leading
/// dashes, and MANY tokens per value. These tests pin the pipeline's answer: the class string
/// an author writes is the class string the module sets -- byte for byte, on every path
/// (static, composed-to-a-child, and reactive-in-a-row).
/// </summary>
public class TailwindTests
{
    static string Emit(string fixture, params string[] siblings)
    {
        // Under the repo root, NOT the system temp: the generator resolves the runtime specifier
        // by walking up to src/filament-runtime, which only exists above repo paths.
        var dir = Path.Combine(RepoPaths.Supported, "Code", $".tw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // Children resolve as SIBLINGS of the parent file, so the fixture set is staged together.
            foreach (var name in siblings.Append(fixture))
                File.Copy(Path.Combine(RepoPaths.Supported, "Code", name + ".razor"),
                          Path.Combine(dir, name + ".razor"));
            var outPath = Path.Combine(dir, fixture + ".g.js");
            var (exit, stdout, stderr) = Run.Generator(Path.Combine(dir, fixture + ".razor"), outPath);
            Assert.True(exit == 0, $"{fixture}.razor refused:\n{stdout}\n{stderr}");
            return File.ReadAllText(outPath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// DEFECT D1 (decision 151), the static path. Razor lowers a multi-token attribute value as
    /// SEVERAL HtmlAttributeValue nodes, each carrying its leading whitespace as a PREFIX rather
    /// than a token; the concat must include those prefixes or the tokens weld together --
    /// 'w-1/2hover:bg-amber-400…' is ONE garbage class where the author wrote seven utilities.
    /// </summary>
    [Fact]
    public void StaticExoticClasses_KeepTheirSpaces()
    {
        var js = Emit("TailwindClasses");
        Assert.Contains("'w-1/2 hover:bg-amber-400 focus:ring-2 max-w-[42rem] sm:px-4 -mt-2 md:grid-cols-[1fr_2fr]'", js);
        Assert.Contains("'bg-cyan-500/75 disabled:opacity-50'", js);
        Assert.Contains("'flex w-1/2 hover:bg-amber-400 max-w-[42rem]'", js);   // inside the keyed row too
        Assert.DoesNotContain("w-1/2hover:", js);
    }

    /// <summary>
    /// DEFECT D1, the composition path. The value a parent binds to a child's string parameter is
    /// folded by EmitComposition's own concat; the same prefix rule applies, so the child's
    /// class="@Cls" renders the authored seven utilities, spaces intact.
    /// </summary>
    [Fact]
    public void ComposedChildClassParam_KeepsItsSpaces()
    {
        var js = Emit("TwCompose", "TwCard");
        Assert.Contains("'w-1/2 hover:bg-amber-400 focus:ring-2 max-w-[42rem] sm:px-4 -mt-2 md:grid-cols-[1fr_2fr]'", js);
        Assert.Contains("'alpha beta'", js);
        Assert.DoesNotContain("w-1/2hover:", js);
    }

    /// <summary>
    /// WIDENING W1 (decision 152). A row's class reads the LOOP VARIABLE: the attribute-value
    /// slot is claimed by the region (SlotsIn), so it compiles inside the loop's braces where `t`
    /// resolves, and the fold lands in an effect INSIDE the row create function -- the attribute
    /// analogue of the Duel's row-text effect. Persisting keyed rows re-run it when t.Done flips;
    /// a create-time setAttr would freeze (the #125 stale trap). The ternary is PARENTHESISED:
    /// `+` binds tighter than `?:`, so the unparenthesised fold is (truthy-string) ? a : b --
    /// always the true branch, the literal prefix gone.
    /// </summary>
    [Fact]
    public void RowClass_LoopVarTernary_CompilesToAnEffectInsideTheRowCreate()
    {
        var js = Emit("RowClass");
        var create = js.Substring(js.IndexOf("function createT"));
        create = create[..(create.IndexOf("\n  }") + 4)];
        Assert.Contains("effect(() => setAttr(", create);
        Assert.Contains("'flex gap-2 ' + (t.done.value ? 'line-through text-slate-400' : 'text-slate-900')", create);
    }

    /// <summary>
    /// DEFECT D2 (decision 153). One field BOTH structurally mutated (Add) and wholesale
    /// reassigned (Where…ToList) is valid authored Blazor -- the baseline builds -- but the two
    /// Filament state models collide: mutate+version declares `const items`, reassign-as-signal
    /// needs the field to BE the signal. The mixed shape emitted `const items = […]` then
    /// `items = …`: a module that throws at the first click, at exit 0. Refused at the colliding
    /// write, naming the split fix.
    /// </summary>
    [Fact]
    public void ListMutateReassign_RefusesTheMixedIdiom()
    {
        var outPath = Path.Combine(RepoPaths.Unsupported, "Code", $".mutreassign-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Code", "ListMutateReassign.razor"), outPath);
            Assert.True(exit != 0, "the mixed mutate+reassign idiom was COMPILED -- the emitted module assigns to a const and throws.");
            Assert.False(File.Exists(outPath), "refused AND wrote the module anyway");
            Assert.Contains("[list-mutate-and-reassign]", stderr);
            Assert.Contains("split it into two fields", stderr);
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    /// <summary>
    /// BOUNDARY for decision 152: the loop variable reaches attribute expressions, the expression
    /// SUBSET is unchanged -- a call in a class value refuses at its own span, row or not.
    /// </summary>
    [Fact]
    public void RowClassCall_RefusesAtItsSpan()
    {
        var outPath = Path.Combine(RepoPaths.Unsupported, "Code", $".rowclasscall-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Code", "RowClassCall.razor"), outPath);
            Assert.True(exit != 0, "a call inside a row's class value was COMPILED, not refused.");
            Assert.False(File.Exists(outPath), "refused AND wrote the module anyway");
            Assert.Contains("Trim", stderr);
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }
}
