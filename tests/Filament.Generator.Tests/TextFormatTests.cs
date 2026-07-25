using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// TEXT POSITION IS TYPE-DIRECTED EVERYWHERE (the S9 slice; register defects A13, A15).
///
/// A value that reaches a TEXT node is not rendered by JS coercion -- C#'s ToString differs, and the
/// generator already emits one formatter per scalar type (`__f32` decision 113, `__decStr` 114,
/// `__dtStr` 115). Two positions were choosing NO formatter where they should have:
///
///   A13  a `bool` in text rendered JS `true` where C# renders `True` -- the latent divergence
///        decision 107 named. The direct `@flag` case, which compiled all along.
///   A15  a value crossing a `@typeparam` (or any composition boundary) dropped the formatter, because
///        the child's declared type is the erased type parameter. `<Box Value="@f" />` (float) rendered
///        the raw double.
///
/// GENERATOR-ONLY: every formatter already ships as emitted code. The fix makes both positions go
/// through the same type dispatch -- the bool arm of EmitBinding, and a format carried across the
/// composition boundary by BindParameters. Both are MEASURED byte-identical against real Blazor by
/// tools/text-format-oracle (HtmlRenderer) + observe-filament.mjs (happy-dom).
/// </summary>
public class TextFormatTests
{
    /// <summary>
    /// A13: a bool in text position now formats through __bool -> "True"/"False", never the JS
    /// lower-case coercion.
    /// </summary>
    [Fact]
    public void BoolInText_NowCompiles_FormatsThroughTheTrueFalseHelper()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("Code/BoolInText.razor"));

        Assert.Contains("function __bool(b) { return b ? 'True' : 'False'; }", js);
        Assert.Contains("setText(_tx0, __bool(flag.value))", js);
        // The bug was the bare coercion; it must be gone.
        Assert.DoesNotContain("setText(_tx0, flag.value)", js);
    }

    /// <summary>
    /// A15: a float crossing a @typeparam keeps its __f32 formatter. The child renders `@Value` whose
    /// declared type is the erased T, so this ONLY works because the parent carried the format across.
    /// </summary>
    [Fact]
    public void FloatThroughGeneric_NowCompiles_CarriesTheFloatFormatterAcrossTheBoundary()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("Composition/GenericFloat.razor"));

        Assert.Contains("function __f32(x) {", js);            // the helper is emitted into the module
        Assert.Contains("setText(_tx0, __f32(ratio.value))", js);   // applied at the CHILD's render site
        // The bug was the child rendering the bare value; it must be gone.
        Assert.DoesNotContain("setText(_tx0, ratio.value)", js);
        // The type is still ERASED (decision 135): no <T>, no typeparam leaks into the JS.
        Assert.DoesNotContain("typeparam", js);
    }

    /// <summary>
    /// THE LINE THE FIX DOES NOT CROSS. A STATIC bool parameter fold stays refused: a static attribute
    /// splices a JS string literal, so a bool param would fold "true" (a string) where a bool is meant
    /// (the D5 bool arm the register defers). Only a REACTIVELY bound param crosses, because its value
    /// IS the parent's own type-correct expression (decision 90). Refused with a located FIL0003.
    /// </summary>
    [Fact]
    public void StaticBoolParameter_StaysRefused_WithALocatedDiagnostic()
    {
        var outPath = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Unsupported",
            "Gate", $".gen-{Guid.NewGuid():N}.js");
        try
        {
            var (exit, _, stderr) = Run.Generator(
                Path.Combine(RepoPaths.Unsupported, "Gate/StaticBoolParam.razor"), outPath);
            Assert.True(exit != 0, "a static bool parameter was COMPILED, not refused");
            Assert.False(File.Exists(outPath));
            Assert.Contains("composition-out-of-subset", stderr);
            Assert.Contains("is not a string", stderr);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see. Pins BOTH witnesses' emitted bytes.</summary>
    [Theory]
    [InlineData("Code/BoolInText.razor", "BoolInText.approved.js")]
    [InlineData("Composition/GenericFloat.razor", "GenericFloat.approved.js")]
    public void Snapshot_EmittedJs_MatchesApprovedBytes(string fixture, string approvedName)
    {
        var actual = File.ReadAllText(Generate.ToTempFixture(fixture)).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", approvedName);
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
