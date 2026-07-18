using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Filament.Subset;

namespace Filament.Analyzer;

/// <summary>
/// Author-time FIL0002: flags a type outside the Filament C# subset in any Blazor component's
/// @code, using the SAME decision the generator uses (Filament.Subset.TypeSubset.Classify —
/// decisions 53/61). Whole-project opt-in: every ComponentBase-derived type in a project that
/// references this analyzer is checked.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TypeSubsetAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Fil0002 = new(
        id: "FIL0002",
        title: "Type is outside the Filament C# subset",
        messageFormat: "{0}",
        category: "Filament.Subset",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "@code may use only int, double, bool, string, List<T> of those, and records declared in the component.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Fil0002);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        // Razor @code compiles to GENERATED C#; we must opt in to see it.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.RegisterSymbolStartAction(OnTypeStart, SymbolKind.NamedType);
    }

    // Per component type: decide once whether it is in scope and gather its declared records,
    // then let the nested node action (with a cached SemanticModel — no RS1030) do the checks.
    static void OnTypeStart(SymbolStartAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!ComponentScope.IsComponent(type)) return;

        var records = type.GetTypeMembers()
            .Where(m => m.TypeKind == TypeKind.Struct || m.IsRecord)
            .Cast<INamedTypeSymbol>()
            .ToImmutableArray();

        context.RegisterSyntaxNodeAction(nc => OnNode(nc, records),
            SyntaxKind.FieldDeclaration, SyntaxKind.LocalDeclarationStatement, SyntaxKind.MethodDeclaration);
    }

    static void OnNode(SyntaxNodeAnalysisContext context, ImmutableArray<INamedTypeSymbol> records)
    {
        foreach (var typeSyntax in TypePositions(context.Node))
        {
            var resolved = context.SemanticModel.GetTypeInfo(typeSyntax, context.CancellationToken).Type;
            if (TypeSubset.Classify(resolved, records) is { } refusal)
                context.ReportDiagnostic(Diagnostic.Create(Fil0002, typeSyntax.GetLocation(), refusal.Message));
        }
    }

    // The type-bearing positions the generator's CheckType covers: field decls, local decls,
    // method return types and parameter types. (Increment 1a scope.)
    static IEnumerable<TypeSyntax> TypePositions(SyntaxNode node)
    {
        switch (node)
        {
            case FieldDeclarationSyntax f:
                yield return f.Declaration.Type;
                break;
            case LocalDeclarationStatementSyntax l:
                yield return l.Declaration.Type;
                break;
            case MethodDeclarationSyntax m:
                yield return m.ReturnType;
                foreach (var p in m.ParameterList.Parameters)
                    if (p.Type is { } pt) yield return pt;
                break;
        }
    }
}
