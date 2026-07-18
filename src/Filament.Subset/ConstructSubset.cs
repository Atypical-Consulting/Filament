using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Filament.Subset;

/// <summary>A construct refusal (FIL0001): code, reason slug, author message. Span-agnostic —
/// the caller supplies the location. Single source of the §5 CONSTRUCT subset, shared by the
/// generator's woven Refuse() sites and the analyzer (decisions 53/61).</summary>
public readonly record struct Refusal(string Code, string Reason, string Message);

public static class ConstructSubset
{
    /// <summary>null = the statement KIND is in §5; non-null = the FIL0001 refusal. Expression- and
    /// call-level refusals inside a supported statement are separate classifiers (slices 1b-ii/iii).</summary>
    public static Refusal? ClassifyStatement(StatementSyntax s) => s switch
    {
        LocalDeclarationStatementSyntax => null,
        ExpressionStatementSyntax => null,
        IfStatementSyntax => null,
        ForStatementSyntax => null,
        ForEachStatementSyntax => null,
        ReturnStatementSyntax => null,
        BlockSyntax => null,
        _ => new Refusal("FIL0001", "unsupported-statement",
            $"{Describe(s)} is not in the C# subset. Section 5 admits local declarations, " +
            "assignment and compound assignment, if/else, for, foreach, and calls to methods " +
            "declared in the same component. Refusing to emit."),
    };

    /// <summary>null = the member KIND is in §5 (@code admits fields, methods, records); non-null =
    /// the FIL0001 refusal. A record's INTERNAL shape is a separate concern (not this classifier).</summary>
    public static Refusal? ClassifyMember(MemberDeclarationSyntax member) => member switch
    {
        FieldDeclarationSyntax => null,
        MethodDeclarationSyntax => null,
        RecordDeclarationSyntax => null,
        _ => new Refusal("FIL0001", "unsupported-member",
            $"{Describe(member)} is not in the C# subset. @code admits FIELDS (state), METHODS " +
            "(behaviour) and RECORDS (row shapes) only (spec 5). Refusing to emit rather than drop it " +
            "silently -- a dropped member is a module that looks right and does less than the source says."),
    };

    /// <summary>The JS binary operator, or null if the operator is out of §5. Division is deliberately
    /// absent: C#'s int/int is integer division and JS's `/` is not (`7/2` = 3 vs 3.5), so `/` on ANY
    /// operands is refused rather than silently mistranslated. Pure syntactic — no model needed.</summary>
    public static string? JsBinaryOperator(BinaryExpressionSyntax b) => b.Kind() switch
    {
        SyntaxKind.AddExpression => "+",
        SyntaxKind.SubtractExpression => "-",
        SyntaxKind.MultiplyExpression => "*",
        SyntaxKind.ModuloExpression => "%",
        SyntaxKind.LessThanExpression => "<",
        SyntaxKind.LessThanOrEqualExpression => "<=",
        SyntaxKind.GreaterThanExpression => ">",
        SyntaxKind.GreaterThanOrEqualExpression => ">=",
        SyntaxKind.EqualsExpression => "===",
        SyntaxKind.NotEqualsExpression => "!==",
        SyntaxKind.LogicalAndExpression => "&&",
        SyntaxKind.LogicalOrExpression => "||",
        _ => null,
    };

    public static string? JsPrefixOperator(PrefixUnaryExpressionSyntax p) => p.Kind() switch
    {
        SyntaxKind.LogicalNotExpression => "!",
        SyntaxKind.UnaryMinusExpression => "-",
        SyntaxKind.UnaryPlusExpression => "+",
        SyntaxKind.PreIncrementExpression => "++",
        SyntaxKind.PreDecrementExpression => "--",
        _ => null,
    };

    public static string? JsAssignmentOperator(AssignmentExpressionSyntax a) => a.Kind() switch
    {
        SyntaxKind.SimpleAssignmentExpression => "=",
        SyntaxKind.AddAssignmentExpression => "+=",
        SyntaxKind.SubtractAssignmentExpression => "-=",
        SyntaxKind.MultiplyAssignmentExpression => "*=",
        SyntaxKind.ModuloAssignmentExpression => "%=",
        _ => null,
    };

    /// <summary>null = the expression FORM is in §5; non-null = the FIL0001 refusal — the decision
    /// behind Expr()'s default. Call-, member- and name-level refusals INSIDE a blessed form
    /// (invocation target, member access, identifier resolution) are separate concerns, not this.</summary>
    public static Refusal? ClassifyExpression(ExpressionSyntax e, SemanticModel model)
    {
        var supported = e switch
        {
            LiteralExpressionSyntax => true,
            IdentifierNameSyntax => true,
            ParenthesizedExpressionSyntax => true,
            BinaryExpressionSyntax b => JsBinaryOperator(b) != null,
            PrefixUnaryExpressionSyntax p => JsPrefixOperator(p) != null,
            PostfixUnaryExpressionSyntax p =>
                p.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression,
            ConditionalExpressionSyntax => true,
            AssignmentExpressionSyntax a => JsAssignmentOperator(a) != null,
            InvocationExpressionSyntax => true,
            MemberAccessExpressionSyntax => true,
            ElementAccessExpressionSyntax ea => IsListFieldIndex(ea, model),
            InterpolatedStringExpressionSyntax => true,
            CastExpressionSyntax c => IsIntFromDouble(c, model),
            _ => false,
        };
        if (supported) return null;
        return new Refusal("FIL0001", "unsupported-expression",
            $"{Describe(e)} is not in the C# subset. Section 5 admits literals, arithmetic and " +
            "comparison operators, &&, ||, !, ternary, string interpolation, member access on a " +
            "local record, List<T> indexing, .Count, .Add and .RemoveAt. Refusing to emit.");
    }

    // Matches the generator's ListReceiver: indexing a field whose type is List<T> (one argument).
    static bool IsListFieldIndex(ElementAccessExpressionSyntax ea, SemanticModel model) =>
        ea.ArgumentList.Arguments.Count == 1 &&
        model.GetSymbolInfo(ea.Expression).Symbol is IFieldSymbol f &&
        TypeSubset.ListElement(f.Type) is not null;

    // (int) on a double truncates toward zero -> Math.trunc; the only cast in the subset.
    static bool IsIntFromDouble(CastExpressionSyntax c, SemanticModel model) =>
        model.GetTypeInfo(c.Type).Type?.SpecialType == SpecialType.System_Int32 &&
        model.GetTypeInfo(c.Expression).Type?.SpecialType == SpecialType.System_Double;

    static string Describe(SyntaxNode n) =>
        n.Kind().ToString().Replace("Syntax", "") + " (`" + Trunc(n.ToString(), 40) + "`)";

    static string Trunc(string s, int n)
    {
        s = s.Replace("\r", "").Replace("\n", "\\n");
        return s.Length <= n ? s : s.Substring(0, n) + "...";
    }
}
