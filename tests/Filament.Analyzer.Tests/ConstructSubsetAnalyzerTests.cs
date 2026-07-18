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
        // foreach iterates a List field (in-subset); `new int[0]` would itself be out of subset.
        => await Body(
            "        int x = 0;\n" +
            "        if (i > 0) { x = 1; } else { x = 2; }\n" +
            "        foreach (var y in _rows) { x = y; }\n" +
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

    // ---- expression forms ---------------------------------------------------

    static Verify Body(string statements) => Component(
        "    private int i = 0;\n" +
        "    private double dbl = 0;\n" +
        "    private System.Collections.Generic.List<int> _rows = null;\n" +
        "    void M() {\n" + statements + "\n    }");

    [Fact]
    public async Task IntegerDivision_IsFlagged()
        => await Body("        int x = {|FIL0001:i / 2|};").RunAsync();

    [Fact]
    public async Task Await_IsFlagged()
        => await Component(
            "    async System.Threading.Tasks.Task M() {\n" +
            "        var x = {|FIL0001:await System.Threading.Tasks.Task.FromResult(0)|};\n" +
            "    }").RunAsync();

    [Fact]
    public async Task DivisionNestedInSupportedExpression_IsFlaggedOnce()
        // outer `+` is supported; only the `i / 2` sub-expression is flagged, once.
        => await Body("        int x = {|FIL0001:i / 2|} + 1;").RunAsync();

    [Fact]
    public async Task SupportedExpressions_ProduceNoDiagnostics()
        // includes a cast — its `int` TYPE must not be misread as a value expression.
        => await Body(
            "        int a = i + 1;\n" +
            "        int b = i > 0 ? 1 : 2;\n" +
            "        int c = (int)dbl;\n" +
            "        int d = _rows[0];\n" +
            "        i++;").RunAsync();

    [Fact]
    public async Task DoubleDivision_IsNotFlagged()
        // double / double is faithful in JS -> in §5 -> no diagnostic (single-sourced via ConstructSubset).
        => await Body("        double x = dbl / 2.0;").RunAsync();
}
