using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Filament.Subset;

namespace Filament.Analyzer;

/// <summary>
/// Author-time FIL0001 (out-of-subset C# construct) over any Blazor component's @code, using the
/// SAME decisions the generator uses (Filament.Subset.ConstructSubset — decisions 53/61). Covers
/// member kinds and statement kinds; expression/call families are added incrementally.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConstructSubsetAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Fil0001 = new(
        id: "FIL0001",
        title: "Construct is outside the Filament C# subset",
        messageFormat: "{0}",
        category: "Filament.Subset",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "@code admits fields, methods and records; and in bodies: local declarations, assignment, if/else, for, foreach, return and calls to the component's own methods.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Fil0001);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.RegisterSyntaxNodeAction(OnClass, SyntaxKind.ClassDeclaration);
    }

    static void OnClass(SyntaxNodeAnalysisContext context)
    {
        var decl = (ClassDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(decl, context.CancellationToken) is not { } symbol
            || !ComponentScope.IsComponent(symbol)) return;

        foreach (var member in decl.Members)
        {
            if (ConstructSubset.ClassifyMember(member) is { } refusal)
                context.ReportDiagnostic(Diagnostic.Create(Fil0001, member.GetLocation(), refusal.Message));
            else if (member is MethodDeclarationSyntax { Body: { } body })
                WalkBlock(body, context);
        }
    }

    // Mirror the generator's Statement()/Nest()/Body() traversal: recurse into supported
    // containers, report and STOP at an unsupported statement.
    static void WalkBlock(BlockSyntax block, SyntaxNodeAnalysisContext context)
    {
        foreach (var s in block.Statements) Walk(s, context);
    }

    static void Walk(StatementSyntax s, SyntaxNodeAnalysisContext context)
    {
        if (ConstructSubset.ClassifyStatement(s) is { } refusal)
        {
            context.ReportDiagnostic(Diagnostic.Create(Fil0001, s.GetLocation(), refusal.Message));
            return; // do not descend into an unsupported construct
        }

        // This supported statement's OWN expressions (not those in nested statements).
        foreach (var expr in OwnExpressions(s)) WalkExpr(expr, context);

        switch (s)
        {
            case BlockSyntax b: WalkBlock(b, context); break;
            case IfStatementSyntax i:
                Walk(i.Statement, context);
                if (i.Else is { } e) Walk(e.Statement, context);
                break;
            case ForStatementSyntax f: Walk(f.Statement, context); break;
            case ForEachStatementSyntax fe: Walk(fe.Statement, context); break;
        }
    }

    static IEnumerable<ExpressionSyntax> OwnExpressions(StatementSyntax s)
    {
        switch (s)
        {
            case LocalDeclarationStatementSyntax d:
                foreach (var v in d.Declaration.Variables)
                    if (v.Initializer is { } init) yield return init.Value;
                break;
            case ExpressionStatementSyntax e: yield return e.Expression; break;
            case IfStatementSyntax i: yield return i.Condition; break;
            case ForStatementSyntax f:
                if (f.Declaration is { } fd)
                    foreach (var v in fd.Variables)
                        if (v.Initializer is { } init) yield return init.Value;
                foreach (var init in f.Initializers) yield return init;
                if (f.Condition is { } c) yield return c;
                foreach (var inc in f.Incrementors) yield return inc;
                break;
            case ForEachStatementSyntax fe: yield return fe.Expression; break;
            case ReturnStatementSyntax r: if (r.Expression is { } re) yield return re; break;
        }
    }

    // Report-and-stop over an expression tree, recursing only into VALUE sub-expressions (mirrors
    // Expr()'s recursion) — so a cast's Type or a member name is never misread as a value expression.
    static void WalkExpr(ExpressionSyntax e, SyntaxNodeAnalysisContext context)
    {
        if (ConstructSubset.ClassifyExpression(e, context.SemanticModel) is { } refusal)
        {
            context.ReportDiagnostic(Diagnostic.Create(Fil0001, e.GetLocation(), refusal.Message));
            return;
        }
        foreach (var sub in SubExpressions(e)) WalkExpr(sub, context);
    }

    static IEnumerable<ExpressionSyntax> SubExpressions(ExpressionSyntax e) => e switch
    {
        ParenthesizedExpressionSyntax p => new[] { p.Expression },
        BinaryExpressionSyntax b => new[] { b.Left, b.Right },
        PrefixUnaryExpressionSyntax p => new[] { p.Operand },
        PostfixUnaryExpressionSyntax p => new[] { p.Operand },
        ConditionalExpressionSyntax c => new[] { c.Condition, c.WhenTrue, c.WhenFalse },
        AssignmentExpressionSyntax a => new[] { a.Left, a.Right },
        CastExpressionSyntax c => new[] { c.Expression },
        MemberAccessExpressionSyntax m => new[] { m.Expression },
        ElementAccessExpressionSyntax ea =>
            new[] { ea.Expression }.Concat(ea.ArgumentList.Arguments.Select(a => a.Expression)),
        InvocationExpressionSyntax inv => inv.ArgumentList.Arguments.Select(a => a.Expression),
        InterpolatedStringExpressionSyntax s =>
            s.Contents.OfType<InterpolationSyntax>().Select(i => i.Expression),
        _ => Enumerable.Empty<ExpressionSyntax>(),
    };
}
