using System.Threading.Tasks;
using Xunit;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Filament.Analyzer.TypeSubsetAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Filament.Analyzer.Tests;

public class TypeSubsetAnalyzerTests
{
    const string ComponentBase =
        "namespace Microsoft.AspNetCore.Components { public class ComponentBase {} }\n";

    static Verify Case(string body) => new()
    {
        TestCode = ComponentBase +
            "class App : Microsoft.AspNetCore.Components.ComponentBase {\n" + body + "\n}",
    };

    [Fact]
    public async Task OutOfSubsetFieldType_IsFlagged()
    {
        await Case("    private {|FIL0002:decimal|} x = 0;").RunAsync();
    }

    [Fact]
    public async Task OutOfSubsetLocalType_IsFlagged()
    {
        await Case(
            "    private void M() {\n" +
            "        {|FIL0002:System.Collections.Generic.List<long>|} ys = null;\n" +
            "    }").RunAsync();
    }

    [Fact]
    public async Task InSubsetTypes_ProduceNoDiagnostics()
    {
        await Case(
            "    private int a = 0;\n" +
            "    private double b = 0;\n" +
            "    private bool c = false;\n" +
            "    private string d = null;\n" +
            "    private System.Collections.Generic.List<int> e = null;").RunAsync();
    }

    [Fact]
    public async Task NonComponentClass_IsNotChecked()
    {
        // No ComponentBase base type -> whole-project opt-in still ignores plain classes.
        await new Verify { TestCode = "class Plain { private decimal x = 0; }" }.RunAsync();
    }
}
