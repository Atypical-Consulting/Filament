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

    static string Describe(SyntaxNode n) =>
        n.Kind().ToString().Replace("Syntax", "") + " (`" + Trunc(n.ToString(), 40) + "`)";

    static string Trunc(string s, int n)
    {
        s = s.Replace("\r", "").Replace("\n", "\\n");
        return s.Length <= n ? s : s.Substring(0, n) + "...";
    }
}
