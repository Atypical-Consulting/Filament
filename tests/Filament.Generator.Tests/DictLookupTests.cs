using Xunit;

namespace Filament.Generator.Tests;

public class DictLookupTests
{
    /// <summary>THE GATE (decision 51): canon(minify(generated)) === canon(minify(key)).</summary>
    [Fact]
    public void Gate_GeneratedDictLookup_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.DictLookupToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.DictLookupAnswerKey);
        Assert.True(exit == 0,
            "PHASE: Dictionary gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/DictLookup/dictlookup.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Snapshot: the only guard against silent generator regression the canon gate is blind to.</summary>
    [Fact]
    public void Snapshot_EmittedDictLookupJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.DictLookupToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "DictLookup.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>
    /// The contract (decision 118): a Dictionary construction lowers to `new Map([[k, v], …])`; `@labels[key]`
    /// lowers to `labels.get(key.value)` (a Map's own getter, reactive on the signal key). A Dictionary IS a JS Map.
    /// </summary>
    [Fact]
    public void EmittedDictLookup_LowersToMapConstructionAndGet()
    {
        var js = File.ReadAllText(Generate.DictLookupToTemp());
        Assert.Contains("new Map([[1, 'one'], [2, 'two'], [3, 'three']])", js);   // Dictionary -> Map
        Assert.Contains("labels.get(key.value)", js);                            // @labels[key] -> .get
        Assert.DoesNotContain("[unsupported-type]", js);
    }

    /// <summary>Closed-runtime invariant: a Dictionary adds NO new runtime primitive — Map is a JS builtin.</summary>
    [Fact]
    public void EmittedDictLookup_OnlyImportsClosedRuntimePrimitives_NoHelper()
    {
        var js = File.ReadAllText(Generate.DictLookupToTemp());
        var import = js.Split('\n').First(l => l.StartsWith("import "));
        string[] allowed =
            ["signal", "computed", "effect", "batch", "untrack", "setText", "setAttr", "listen", "insert", "remove", "list"];
        foreach (var name in import[(import.IndexOf('{') + 1)..import.IndexOf('}')]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.True(allowed.Contains(name), $"'{name}' is not a runtime export.");
    }

    /// <summary>A Dictionary entry write (`d[key] = v`) is REFUSED: a Dictionary is read-only (no reactive version).</summary>
    [Fact]
    public void DictionaryEntryWrite_IsRefused()
    {
        var src = Path.Combine(RepoPaths.Unsupported, $".dw-{Guid.NewGuid():N}.razor");
        var outPath = Path.Combine(RepoPaths.Unsupported, $".dw-{Guid.NewGuid():N}.js");
        try
        {
            File.WriteAllText(src,
                "<p id=\"p\">@d[0]</p>\n<button id=\"b\" @onclick=\"M\">m</button>\n" +
                "@code {\n    private System.Collections.Generic.Dictionary<int,int> d = new System.Collections.Generic.Dictionary<int,int> { { 0, 1 } };\n" +
                "    private void M() { d[0] = 9; }\n}\n");
            var (exit, _, stderr) = Run.Generator(src, outPath);
            Assert.NotEqual(0, exit);
            Assert.Contains("[unsupported-expression]", stderr);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); if (File.Exists(src)) File.Delete(src); }
    }
}
