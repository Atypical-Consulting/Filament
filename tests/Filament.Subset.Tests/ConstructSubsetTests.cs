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
    [InlineData("return;")]
    [InlineData("{ }")]
    public void SupportedStatementKinds_ClassifyToNull(string body)
        => Assert.Null(ConstructSubset.ClassifyStatement(FirstStatement(body)));

    [Theory]
    [InlineData("while (b) { }")]
    [InlineData("switch (x) { }")]
    [InlineData("try { } catch { }")]
    [InlineData("throw new System.Exception();")]
    [InlineData("using (d) { }")]
    [InlineData("lock (o) { }")]
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
}
