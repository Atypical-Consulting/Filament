using System.Threading.Tasks;
using Xunit;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Filament.Analyzer.ConstructSubsetAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Filament.Analyzer.Tests;

public class ConstructSubsetAnalyzerTests
{
    const string ComponentBase =
        "namespace Microsoft.AspNetCore.Components { public class ComponentBase {} public class ParameterAttribute : System.Attribute {} }\n" +
        "namespace System.Runtime.CompilerServices { internal static class IsExternalInit {} }\n";

    static Verify Component(string members) => new()
    {
        TestCode = ComponentBase +
            "class App : Microsoft.AspNetCore.Components.ComponentBase {\n" + members + "\n}",
    };

    static Verify Method(string body) => Component("    void M(bool b) {\n" + body + "\n    }");

    // ---- statement kinds ----------------------------------------------------

    [Fact]
    public async Task WhileLoop_IsNotFlagged()
        // while / do-while / switch entered §5 at decision 102 (single-sourced via ConstructSubset).
        => await Method("        while (b) { }").RunAsync();

    [Fact]
    public async Task TryCatch_IsNotFlagged()
        // try/catch/finally, throw and lock entered §5 at decision 110 (single-sourced via ConstructSubset).
        => await Method("        try { } catch { }").RunAsync();

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
        // try/catch entered §5 at decision 110, so `using` is the still-unsupported witness: the using
        // is flagged once and its supported innards (x = 1) are NOT separately flagged.
        => await Method(
            "        int x = 0;\n" +
            "        {|FIL0001:using (System.IDisposable d = null) { x = 1; }|}").RunAsync();

    // ---- member kinds -------------------------------------------------------

    [Fact]
    public async Task Property_IsFlagged()
        => await Component("    {|FIL0001:public int P { get; set; }|}").RunAsync();

    [Fact]
    public async Task ComponentParameter_ScalarProperty_IsNotFlagged()
        // A [Parameter] property is the one admitted property kind (component-parameter carve-out).
        => await Component(
            "    [Microsoft.AspNetCore.Components.Parameter] public string Name { get; set; }").RunAsync();

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
    public async Task IntegerDivision_IsNotFlagged()
        // int/int is faithful in JS via Math.trunc -> in §5 -> no diagnostic (single-sourced via
        // ConstructSubset; decision 101 closed #87's deferral).
        => await Body("        int x = i / 2;").RunAsync();

    [Fact]
    public async Task Await_IsNotFlagged()
        // await entered §5 at decision 119 (in an async method) -> the construct analyzer no longer flags it.
        => await Component(
            "    async System.Threading.Tasks.Task M() {\n" +
            "        await System.Threading.Tasks.Task.Delay(1);\n" +
            "    }").RunAsync();

    [Fact]
    public async Task UnsupportedCastNestedInSupportedExpression_IsFlaggedOnce()
        // outer `+` is supported; only the still-unsupported `(long)i` sub-expression is flagged, once.
        // (int division moved into §5 at decision 101, so it is no longer the nesting witness here.)
        => await Body("        long x = {|FIL0001:(long)i|} + 1;").RunAsync();

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

    // ---- the CALL check (decision 148: the last refusal kind to get a squiggle) ----

    [Fact]
    public async Task SameComponentCall_IsNotFlagged()
        => await Component(
            "    void Helper() { }\n" +
            "    void M() { Helper(); }").RunAsync();

    [Fact]
    public async Task InheritedBaseCall_IsNotFlagged()
        // @inherits (decision 136) merges a base's members; a call up the chain short of ComponentBase
        // is a call to "a method declared in this component" in the merged model.
        => await new Verify
        {
            TestCode = ComponentBase +
                "class CounterBase : Microsoft.AspNetCore.Components.ComponentBase { protected void FromBase() { } }\n" +
                "class App : CounterBase {\n    void M() { FromBase(); }\n}",
        }.RunAsync();

    [Fact]
    public async Task MappedApiCalls_AreNotFlagged()
        // One row per table family: LINQ (116), List mutation (rows), Random (146), Task.Delay (119).
        => await Component(
            "    private System.Collections.Generic.List<int> xs = null;\n" +
            "    private System.Random rng = new System.Random(42);\n" +
            "    async System.Threading.Tasks.Task M() {\n" +
            "        int n = System.Linq.Enumerable.Count(xs);\n" +
            "        xs.Add(rng.Next(1, 7));\n" +
            "        await System.Threading.Tasks.Task.Delay(1);\n" +
            "    }").RunAsync();

    [Fact]
    public async Task LinqPredicateLambda_IsNotFlagged_ItsBodyIsWalked()
        // The lambda ARG of an admitted LINQ call is how LINQ is written; its BODY is what gets judged.
        // (This was a measured false positive before decision 148: the lambda FORM was flagged.)
        => await new Verify
        {
            TestCode = "using System.Linq;\n" + ComponentBase +
                "class App : Microsoft.AspNetCore.Components.ComponentBase {\n" +
                "    private System.Collections.Generic.List<int> xs = null;\n" +
                "    void M() { int n = xs.Where(x => x > 0).Count(); }\n}",
        }.RunAsync();

    [Fact]
    public async Task ConsoleWriteLine_IsFlagged()
        => await Method("        {|FIL0001:System.Console.WriteLine(1)|};").RunAsync();

    [Fact]
    public async Task FileReadAllText_IsFlagged()
        => await Method("        string s = {|FIL0001:System.IO.File.ReadAllText(\"x\")|};").RunAsync();
}
