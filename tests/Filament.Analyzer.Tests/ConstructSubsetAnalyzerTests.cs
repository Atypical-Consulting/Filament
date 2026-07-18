using System.Threading.Tasks;
using Xunit;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Filament.Analyzer.ConstructSubsetAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Filament.Analyzer.Tests;

public class ConstructSubsetAnalyzerTests
{
    const string ComponentBase =
        "namespace Microsoft.AspNetCore.Components { public class ComponentBase {} }\n" +
        "namespace System.Runtime.CompilerServices { internal static class IsExternalInit {} }\n";

    static Verify Component(string members) => new()
    {
        TestCode = ComponentBase +
            "class App : Microsoft.AspNetCore.Components.ComponentBase {\n" + members + "\n}",
    };

    static Verify Method(string body) => Component("    void M(bool b) {\n" + body + "\n    }");

    // ---- statement kinds ----------------------------------------------------

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
        => await Method(
            "        int x = 0;\n" +
            "        {|FIL0001:switch (x) { default: break; }|}").RunAsync();

    // ---- member kinds -------------------------------------------------------

    [Fact]
    public async Task Property_IsFlagged()
        => await Component("    {|FIL0001:public int P { get; set; }|}").RunAsync();

    [Fact]
    public async Task Constructor_IsFlagged()
        => await Component("    {|FIL0001:public App() { }|}").RunAsync();

    [Fact]
    public async Task NestedClass_IsFlagged()
        => await Component("    {|FIL0001:class N { }|}").RunAsync();

    [Fact]
    public async Task SupportedMembers_ProduceNoDiagnostics()
        => await Component(
            "    private int x = 0;\n" +
            "    void M() { x = 1; }\n" +
            "    private record Row(int Id);").RunAsync();
}
