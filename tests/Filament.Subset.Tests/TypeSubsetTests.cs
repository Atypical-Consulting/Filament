using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Filament.Subset.Tests;

public class TypeSubsetTests
{
    static ITypeSymbol TypeOfField(string decls, string fieldName)
    {
        var tree = CSharpSyntaxTree.ParseText(
            "using System;using System.Collections.Generic;class C {" + decls + "}");
        var comp = CSharpCompilation.Create("t", new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var model = comp.GetSemanticModel(tree);
        var field = tree.GetRoot().DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .First(v => v.Identifier.Text == fieldName);
        return ((IFieldSymbol)model.GetDeclaredSymbol(field)!).Type;
    }

    [Theory]
    [InlineData("int x;", "x")]
    [InlineData("long x;", "x")]                          // decision 112: long -> BigInt
    [InlineData("double x;", "x")]
    [InlineData("bool x;", "x")]
    [InlineData("string x;", "x")]
    [InlineData("List<int> x;", "x")]
    [InlineData("List<long> x;", "x")]                    // decision 112: List<long> -> array of BigInt
    [InlineData("List<string> x;", "x")]
    public void InSubsetTypes_ClassifyToNull(string decls, string field)
    {
        var t = TypeOfField(decls, field);
        Assert.Null(TypeSubset.Classify(t, Array.Empty<INamedTypeSymbol>()));
    }

    [Theory]
    [InlineData("decimal x;", "x")]
    [InlineData("float x;", "x")]
    [InlineData("object x;", "x")]
    [InlineData("DateTime x;", "x")]
    [InlineData("List<decimal> x;", "x")]                 // the ELEMENT is out of subset (long is now IN)
    public void OutOfSubsetTypes_ClassifyToUnsupportedType(string decls, string field)
    {
        var t = TypeOfField(decls, field);
        var r = TypeSubset.Classify(t, Array.Empty<INamedTypeSymbol>());
        Assert.NotNull(r);
        Assert.Equal("unsupported-type", r!.Value.Reason);
    }

    [Fact]
    public void Void_IsAcceptable_ClassifiesToNull()
    {
        // void only ever appears as a return type; Classify must admit it (it cannot be a
        // field/local/param), matching the generator's call-site guard.
        var tree = CSharpSyntaxTree.ParseText("class C { void M() {} }");
        var comp = CSharpCompilation.Create("t", new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var model = comp.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        var returnType = model.GetTypeInfo(method.ReturnType).Type;
        Assert.Null(TypeSubset.Classify(returnType, Array.Empty<INamedTypeSymbol>()));
    }

    [Fact]
    public void ErrorType_ClassifiesToUnresolvedType()
    {
        var t = TypeOfField("Nonexistent x;", "x");
        var r = TypeSubset.Classify(t, Array.Empty<INamedTypeSymbol>());
        Assert.NotNull(r);
        Assert.Equal("unresolved-type", r!.Value.Reason);
    }
}
