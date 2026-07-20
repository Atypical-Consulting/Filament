using Xunit;

namespace Filament.Generator.Tests;

public class ArrayIndexTests
{
    /// <summary>THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).</summary>
    [Fact]
    public void Gate_GeneratedArrayIndex_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.ArrayIndexToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.ArrayIndexAnswerKey);
        Assert.True(exit == 0,
            "PHASE: array gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/ArrayIndex/arrayindex.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedArrayIndexJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.ArrayIndexToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "ArrayIndex.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract (decision 117): a `T[]` literal `new int[]{10,20,30}` lowers to a JS array literal
    /// `[10, 20, 30]`; `@items[i]` lowers to `items[i.value]` (the array's own indexer, reactive on the signal
    /// index); and `items.Length` lowers to `items.length`. A T[] IS the same JS array a List<T> is.
    /// </summary>
    [Fact]
    public void EmittedArrayIndex_LowersLiteralIndexAndLength()
    {
        var js = File.ReadAllText(Generate.ArrayIndexToTemp());
        Assert.Contains("const items = [10, 20, 30]", js);          // T[] literal -> JS array
        Assert.Contains("items[i.value]", js);                     // @items[i] -> the array's indexer
        Assert.Contains("items.length", js);                       // .Length -> .length
        Assert.DoesNotContain("[unsupported-type]", js);
    }

    /// <summary>Closed-runtime invariant: an array adds NO new runtime primitive — indexing and .length are the array's own.</summary>
    [Fact]
    public void EmittedArrayIndex_OnlyImportsClosedRuntimePrimitives_NoHelper()
    {
        var js = File.ReadAllText(Generate.ArrayIndexToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name), $"'{name}' is not a runtime export.");
    }

    /// <summary>
    /// Array element assignment (`arr[i] = v`) on a REACTIVE array now COMPILES to copy-on-write (decision 127):
    /// `xs.value = xs.value.with(0, 9)` -- a new reference, so the signal fires and `@xs[0]` refreshes. Was refused
    /// under #117 (an array had no reactive write); the array-as-signal of #124 gives it one.
    /// </summary>
    [Fact]
    public void ArrayElementAssignment_OnAReactiveArray_CompilesToCopyOnWrite()
    {
        var src = Path.Combine(RepoPaths.Unsupported, $".arrw-{Guid.NewGuid():N}.razor");
        var outPath = Path.Combine(RepoPaths.Unsupported, $".arrw-{Guid.NewGuid():N}.js");
        try
        {
            File.WriteAllText(src,
                "<p id=\"p\">@xs[0]</p>\n<button id=\"b\" @onclick=\"M\">m</button>\n" +
                "@code {\n    private int[] xs = new int[] { 1, 2 };\n    private void M() { xs[0] = 9; }\n}\n");
            var (exit, _, stderr) = Run.Generator(src, outPath);
            Assert.True(exit == 0, "arr[i] = v on a reactive array is in the subset (decision 127):\n" + stderr);
            Assert.Contains("xs.value = xs.value.with(0, 9)", File.ReadAllText(outPath));
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); if (File.Exists(src)) File.Delete(src); }
    }

    /// <summary>
    /// The boundary: `arr[i] = v` on an array NOTHING displays has no observer, so it stays refused (decision 127).
    /// Here xs is written but never read by the template -> not a signal -> the copy-on-write would fire nothing.
    /// </summary>
    [Fact]
    public void ArrayElementAssignment_OnANonReactiveArray_IsRefused()
    {
        var src = Path.Combine(RepoPaths.Unsupported, $".arrw-{Guid.NewGuid():N}.razor");
        var outPath = Path.Combine(RepoPaths.Unsupported, $".arrw-{Guid.NewGuid():N}.js");
        try
        {
            File.WriteAllText(src,
                "<button id=\"b\" @onclick=\"M\">m</button>\n" +
                "@code {\n    private int[] xs = new int[] { 1, 2 };\n    private void M() { xs[0] = 9; }\n}\n");
            var (exit, _, stderr) = Run.Generator(src, outPath);
            Assert.NotEqual(0, exit);
            Assert.Contains("[unsupported-expression]", stderr);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); if (File.Exists(src)) File.Delete(src); }
    }
}
