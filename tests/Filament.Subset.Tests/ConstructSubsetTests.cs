using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Filament.Subset.Tests;

public class ConstructSubsetTests
{
    static StatementSyntax FirstStatement(string body)
    {
        var tree = CSharpSyntaxTree.ParseText("class C { void M() {" + body + "} }");
        return tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .First().Body!.Statements.First();
    }

    [Theory]
    [InlineData("int x = 0;")]
    [InlineData("x = 1;")]
    [InlineData("if (b) { }")]
    [InlineData("for (;;) { }")]
    [InlineData("foreach (var y in ys) { }")]
    [InlineData("while (b) { }")]                        // decision 102
    [InlineData("do { } while (b);")]                    // decision 102
    [InlineData("switch (x) { case 1: break; default: break; }")]  // decision 102: constant labels
    [InlineData("break;")]                              // decision 102: needed inside a switch/loop
    [InlineData("try { } catch { }")]                   // decision 110: try/catch/finally -> JS namesake
    [InlineData("throw new System.Exception();")]       // decision 110: throw -> JS throw
    [InlineData("lock (o) { }")]                        // decision 110: lock -> no-op block (single-threaded JS)
    [InlineData("return;")]
    [InlineData("{ }")]
    public void SupportedStatementKinds_ClassifyToNull(string body)
        => Assert.Null(ConstructSubset.ClassifyStatement(FirstStatement(body)));

    [Theory]
    [InlineData("switch (x) { case int y: break; }")]   // decision 102: PATTERN label still refused
    [InlineData("using (d) { }")]                        // no IDisposable in a Filament module -> nothing to dispose
    [InlineData("goto done;")]
    public void UnsupportedStatementKinds_ClassifyToUnsupportedStatement(string body)
    {
        var r = ConstructSubset.ClassifyStatement(FirstStatement(body));
        Assert.NotNull(r);
        Assert.Equal("FIL0001", r!.Value.Code);
        Assert.Equal("unsupported-statement", r.Value.Reason);
    }

    static MemberDeclarationSyntax FirstMember(string members)
    {
        var tree = CSharpSyntaxTree.ParseText("class C {" + members + "}");
        return tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .First().Members.First();
    }

    [Theory]
    [InlineData("int x;")]
    [InlineData("void M() {}")]
    [InlineData("record R(int x);")]
    public void SupportedMemberKinds_ClassifyToNull(string members)
        => Assert.Null(ConstructSubset.ClassifyMember(FirstMember(members)));

    [Theory]
    [InlineData("int P { get; set; }")]
    [InlineData("C() {}")]
    [InlineData("class N {}")]
    public void UnsupportedMemberKinds_ClassifyToUnsupportedMember(string members)
    {
        var r = ConstructSubset.ClassifyMember(FirstMember(members));
        Assert.NotNull(r);
        Assert.Equal("FIL0001", r!.Value.Code);
        Assert.Equal("unsupported-member", r.Value.Reason);
    }

    [Fact]
    public void ComponentParameter_ScalarProperty_ClassifiesToNull()
        => Assert.Null(ConstructSubset.ClassifyMember(
            FirstMember("[Microsoft.AspNetCore.Components.Parameter] public string Name { get; set; }")));

    [Fact]
    public void PlainProperty_WithoutParameterAttribute_StaysRefused()
    {
        var r = ConstructSubset.ClassifyMember(FirstMember("public string Name { get; set; }"));
        Assert.NotNull(r);
        Assert.Equal("unsupported-member", r!.Value.Reason);
    }

    static (Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax e, Microsoft.CodeAnalysis.SemanticModel m)
        ParseExpr(string exprSrc)
    {
        var tree = CSharpSyntaxTree.ParseText(
            "using System.Collections.Generic;\n" +
            "class C {\n" +
            "  List<int> _rows = null;\n" +
            "  double dbl = 0;\n" +
            "  int i = 0;\n" +
            "  async System.Threading.Tasks.Task M() { var _ = " + exprSrc + "; }\n" +
            "}");
        var comp = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("t", new[] { tree },
            new[] { Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var model = comp.GetSemanticModel(tree);
        var init = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.EqualsValueClauseSyntax>().Last().Value;
        return (init, model);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("i")]
    [InlineData("i + 1")]
    [InlineData("i % 2")]
    [InlineData("i < 5")]
    [InlineData("i == 3")]
    [InlineData("!(i < 5)")]
    [InlineData("-i")]
    [InlineData("i > 0 ? 1 : 2")]
    [InlineData("_rows[0]")]
    [InlineData("(int)dbl")]
    [InlineData("$\"n={i}\"")]
    [InlineData("dbl / 2.0")]   // double / double -> double result: faithful (verbatim /)
    [InlineData("dbl / i")]     // double / int    -> double result: faithful
    [InlineData("i / dbl")]     // int / double    -> double result: faithful
    [InlineData("i / 2")]       // int / int       -> int result: faithful via Math.trunc (decision 101)
    [InlineData("await System.Threading.Tasks.Task.FromResult(0)")]   // await -> JS await (decision 119; async-context is a generator guard)
    public void SupportedExpressionForms_ClassifyToNull(string exprSrc)
    {
        var (e, m) = ParseExpr(exprSrc);
        Assert.Null(ConstructSubset.ClassifyExpression(e, m));
    }

    [Theory]
    [InlineData("(long)i")]                                            // cast that is not int-from-double
    [InlineData("new int[0]")]                                        // array creation
    public void UnsupportedExpressionForms_ClassifyToUnsupportedExpression(string exprSrc)
    {
        var (e, m) = ParseExpr(exprSrc);
        var r = ConstructSubset.ClassifyExpression(e, m);
        Assert.NotNull(r);
        Assert.Equal("FIL0001", r!.Value.Code);
        Assert.Equal("unsupported-expression", r.Value.Reason);
    }
}
