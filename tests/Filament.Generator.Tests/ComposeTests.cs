using Xunit;

namespace Filament.Generator.Tests;

public class ComposeTests
{
    /// <summary>
    /// Static-leaf composition: &lt;Greeting Name="World" /&gt; resolves the sibling Greeting.razor, folds
    /// the static param, and INLINES the child's span. No unresolved &lt;Greeting&gt; element, no import of a
    /// child, and the @Name read is the compile-time constant, not the literal expression text.
    /// </summary>
    [Fact]
    public void EmittedCompose_InlinesTheChildWithTheFoldedParam()
    {
        var js = File.ReadAllText(Generate.ComposeToTemp());
        Assert.Contains("document.createElement('span')", js);   // the child's root, inlined
        Assert.Contains("greeting", js);                          // its id survives
        Assert.Contains("World", js);                             // the param folded to a constant
        Assert.DoesNotContain("createElement('Greeting')", js);   // NOT emitted as an unknown element
        Assert.DoesNotContain("@Name", js);                       // NOT the literal expression
    }

    /// <summary>
    /// THE GATE (spec 6 / decisions 21/51): the module emitted from baseline/Compose.Blazor/App.razor
    /// (which composes the sibling Greeting.razor) is alpha-equivalent to the hand-written
    /// samples/Compose/compose.js. Its Blazor-faithfulness is what the DOM-contract oracle measures.
    /// </summary>
    [Fact]
    public void Gate_GeneratedCompose_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.ComposeToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.ComposeAnswerKey);
        Assert.True(exit == 0,
            "composition gate FAILED. Generated module is NOT alpha-equivalent to samples/Compose/compose.js.\n" +
            stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    /// <summary>Section 10: the snapshot is the only wall against silent generator regressions the
    /// name-blind canon gate cannot see.</summary>
    [Fact]
    public void Snapshot_EmittedComposeJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.ComposeToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "Compose.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }
}
