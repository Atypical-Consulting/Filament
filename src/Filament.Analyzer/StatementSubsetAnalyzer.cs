using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Filament.Subset;

namespace Filament.Analyzer;

/// <summary>
/// Author-time FIL0001 (statement kind): flags a statement whose kind is outside the Filament C#
/// subset in any Blazor component's @code, using the SAME decision the generator uses
/// (Filament.Subset.ConstructSubset.ClassifyStatement — decisions 53/61).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StatementSubsetAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Fil0001 = new(
        id: "FIL0001",
        title: "Construct is outside the Filament C# subset",
        messageFormat: "{0}",
        category: "Filament.Subset",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "@code admits local declarations, assignment, if/else, for, foreach, return and calls to the component's own methods.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Fil0001);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.RegisterSymbolStartAction(OnTypeStart, SymbolKind.NamedType);
    }

    static void OnTypeStart(SymbolStartAnalysisContext context)
    {
        if (!ComponentScope.IsComponent((INamedTypeSymbol)context.Symbol)) return;
        context.RegisterSyntaxNodeAction(OnMethod, SyntaxKind.MethodDeclaration);
    }

    static void OnMethod(SyntaxNodeAnalysisContext context)
    {
        var body = ((MethodDeclarationSyntax)context.Node).Body;
        if (body is not null) WalkBlock(body, context);
    }

    // Mirror the generator's Statement()/Nest()/Body() traversal: recurse into supported
    // containers, report and STOP at an unsupported statement (so a switch is flagged once,
    // not also its inner break — matching the generator's first-hit refuse).
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
}
