using System.Threading.Tasks;
using Xunit;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Filament.Analyzer.StatementSubsetAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Filament.Analyzer.Tests;

public class StatementSubsetAnalyzerTests
{
    const string ComponentBase =
        "namespace Microsoft.AspNetCore.Components { public class ComponentBase {} }\n";

    static Verify Method(string body) => new()
    {
        TestCode = ComponentBase +
            "class App : Microsoft.AspNetCore.Components.ComponentBase {\n" +
            "    void M(bool b) {\n" + body + "\n    }\n}",
    };

    [Fact]
    public async Task WhileLoop_IsFlagged()
        => await Method("        {|FIL0001:while (b) { }|}").RunAsync();

    [Fact]
    public async Task TryCatch_IsFlagged()
        => await Method("        {|FIL0001:try { } catch { }|}").RunAsync();

    [Fact]
    public async Task SupportedStatements_ProduceNoDiagnostics()
        => await Method(
            "        int x = 0;\n" +
            "        if (b) { x = 1; } else { x = 2; }\n" +
            "        foreach (var y in new int[0]) { x = y; }\n" +
            "        return;").RunAsync();

    [Fact]
    public async Task UnsupportedStatement_IsFlaggedOnce_NotItsInnards()
        // The switch is flagged; the inner `break;` (also an unsupported kind) is NOT, because the
        // walk stops descending at an unsupported construct — mirroring the generator's first-hit refuse.
        => await Method(
            "        int x = 0;\n" +
            "        {|FIL0001:switch (x) { default: break; }|}").RunAsync();
}
