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
                WalkBlock(body, context, symbol);
        }
    }

    /// <summary>
    /// Should this invocation get the CALL check (decision 148)? The @code an author wrote is what the
    /// generator will judge; Razor's generated SCAFFOLDING (BuildRenderTree bodies full of framework
    /// calls) is not authored and must never light up. Author code inside a .razor-generated file is
    /// #line-mapped back to the .razor; scaffolding is not — and a plain .cs file (tests, previews of
    /// the class form) has no mapping and is authored by definition.
    /// </summary>
    static bool IsAuthorCode(SyntaxNode node)
    {
        var span = node.GetLocation().GetMappedLineSpan();
        if (span.HasMappedPath) return true;
        var file = node.SyntaxTree.FilePath;
        return !(file.Contains(".razor") || file.Contains(".cshtml"));
    }

    // Mirror the generator's Statement()/Nest()/Body() traversal: recurse into supported
    // containers, report and STOP at an unsupported statement.
    static void WalkBlock(BlockSyntax block, SyntaxNodeAnalysisContext context, INamedTypeSymbol component)
    {
        foreach (var s in block.Statements) Walk(s, context, component);
    }

    static void Walk(StatementSyntax s, SyntaxNodeAnalysisContext context, INamedTypeSymbol component)
    {
        if (ConstructSubset.ClassifyStatement(s) is { } refusal)
        {
            context.ReportDiagnostic(Diagnostic.Create(Fil0001, s.GetLocation(), refusal.Message));
            return; // do not descend into an unsupported construct
        }

        // This supported statement's OWN expressions (not those in nested statements).
        foreach (var expr in OwnExpressions(s)) WalkExpr(expr, context, component);

        switch (s)
        {
            case BlockSyntax b: WalkBlock(b, context, component); break;
            case IfStatementSyntax i:
                Walk(i.Statement, context, component);
                if (i.Else is { } e) Walk(e.Statement, context, component);
                break;
            case ForStatementSyntax f: Walk(f.Statement, context, component); break;
            case ForEachStatementSyntax fe: Walk(fe.Statement, context, component); break;
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
    static void WalkExpr(ExpressionSyntax e, SyntaxNodeAnalysisContext context, INamedTypeSymbol component)
    {
        if (ConstructSubset.ClassifyExpression(e, context.SemanticModel) is { } refusal)
        {
            context.ReportDiagnostic(Diagnostic.Create(Fil0001, e.GetLocation(), refusal.Message));
            return;
        }

        // THE CALL CHECK (decision 148): the invocation FORM is admitted above; whether the CALLEE has a
        // faithful mapping is CallSubset's single-sourced table. Only author code is judged -- Razor's
        // generated scaffolding is full of framework calls the author never wrote (see IsAuthorCode).
        if (e is InvocationExpressionSyntax call && IsAuthorCode(call) &&
            CallSubset.ClassifyCall(call, context.SemanticModel, component) is { } callRefusal)
        {
            context.ReportDiagnostic(Diagnostic.Create(Fil0001, e.GetLocation(), callRefusal.Message));
            return;
        }

        foreach (var sub in SubExpressions(e)) WalkExpr(sub, context, component);
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
        // A predicate/selector LAMBDA in call-argument position is how LINQ is written (decision 116);
        // the generator translates its BODY through the same Expr the rest of @code uses, so the walk
        // descends into the body rather than misflagging the lambda form itself. A lambda anywhere else
        // (a Func local's initializer) still refuses at ITS guichet (the local's type, FIL0002).
        InvocationExpressionSyntax inv => inv.ArgumentList.Arguments.Select(a => a.Expression switch
        {
            SimpleLambdaExpressionSyntax { Body: ExpressionSyntax lb } => lb,
            ParenthesizedLambdaExpressionSyntax { Body: ExpressionSyntax plb } => plb,
            var other => other,
        }),
        InterpolatedStringExpressionSyntax s =>
            s.Contents.OfType<InterpolationSyntax>().Select(i => i.Expression),
        _ => Enumerable.Empty<ExpressionSyntax>(),
    };
}
