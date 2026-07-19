using Xunit;

namespace Filament.Generator.Tests;

public class CodeBlockTests
{
    [Fact]
    public void Gate_GeneratedCodeBlock_IsAlphaEquivalentToAnswerKey()
    {
        var generated = Generate.CodeBlockToTemp();
        var (exit, stdout, stderr) = Run.Node(RepoPaths.Canon, generated, RepoPaths.CodeBlockAnswerKey);
        Assert.True(exit == 0,
            "PHASE: root @{ } code block gate FAILED. Generated module is NOT alpha-equivalent to " +
            "samples/CodeBlock/codeblock.js.\n" + stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    [Fact]
    public void Snapshot_EmittedCodeBlockJs_MatchesApprovedBytes()
    {
        var actual = File.ReadAllText(Generate.CodeBlockToTemp()).Replace("\r\n", "\n").TrimEnd() + "\n";
        var approved = Path.Combine(RepoPaths.Root, "tests", "Filament.Generator.Tests", "Snapshots", "CodeBlock.approved.js");
        if (!File.Exists(approved)) { File.WriteAllText(approved, actual); Assert.Fail($"wrote {approved}; review + re-run"); }
        Assert.Equal(File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd() + "\n", actual);
    }

    /// <summary>The contract: the @{ } local is a one-time const, and @total reads it statically.</summary>
    [Fact]
    public void EmittedCodeBlock_IsAOneTimeLocalReadStatically()
    {
        var js = File.ReadAllText(Generate.CodeBlockToTemp());
        Assert.Contains("const total = 3 + 4;", js);
        Assert.Contains("createTextNode(total)", js);
        Assert.DoesNotContain("effect(", js);   // total never changes -> static, no effect
    }
}
