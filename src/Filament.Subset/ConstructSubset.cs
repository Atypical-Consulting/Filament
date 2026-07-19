using System.Linq;
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
        WhileStatementSyntax => null,
        DoStatementSyntax => null,
        BreakStatementSyntax => null,   // break is valid only inside a loop/switch (Roslyn enforces); switch needs it
        // switch with CONSTANT case labels + default only; pattern / when-guard labels are deferred (fall to refusal).
        SwitchStatementSyntax sw when sw.Sections.All(sec =>
            sec.Labels.All(l => l is CaseSwitchLabelSyntax or DefaultSwitchLabelSyntax)) => null,
        TryStatementSyntax => null,     // try/catch/finally -> the JS namesake (decision 110)
        ThrowStatementSyntax => null,   // throw -> JS throw (a caught throw is faithful; uncaught is a disclosed edge)
        LockStatementSyntax => null,    // lock -> a no-op block: JS is single-threaded, so a lock cannot be contended
        ReturnStatementSyntax => null,
        BlockSyntax => null,
        _ => new Refusal("FIL0001", "unsupported-statement",
            $"{Describe(s)} is not in the C# subset. Section 5 admits local declarations, " +
            "assignment and compound assignment, if/else, for, foreach, while, do-while, switch " +
            "(constant labels), try/catch, throw, lock, and calls to methods declared in the same component. " +
            "Refusing to emit."),
    };

    /// <summary>True for a `[Parameter]` attribute in any of its written forms. The generator's
    /// CheckNoAttributes consults this so it can admit a component-parameter property IFF ALL its
    /// attributes are parameter attributes — a foreign attribute never slips through the carve-out.</summary>
    public static bool IsParameterAttribute(AttributeSyntax a) =>
        a.Name.ToString() is "Parameter"
            or "Microsoft.AspNetCore.Components.Parameter"
            or "Components.Parameter"
            or "ParameterAttribute"
            or "Microsoft.AspNetCore.Components.ParameterAttribute"
            or "Components.ParameterAttribute";

    /// <summary>A [Parameter]-attributed property is admitted ONLY in the component-parameter role —
    /// a narrow carve-out from §5's no-properties (#85) / no-attributes (#77) rules, forced because a
    /// Blazor component parameter IS a `[Parameter] public T X { get; set; }`. Syntactic; the
    /// scalar-type and auto-property shape are checked semantically in the generator's ParamDecl.</summary>
    public static bool IsComponentParameter(PropertyDeclarationSyntax p) =>
        p.AttributeLists.SelectMany(l => l.Attributes).Any(IsParameterAttribute);

    /// <summary>null = the member KIND is in §5 (@code admits fields, methods, records, and a
    /// [Parameter] scalar property); non-null = the FIL0001 refusal. A record's INTERNAL shape is a
    /// separate concern (not this classifier).</summary>
    public static Refusal? ClassifyMember(MemberDeclarationSyntax member) => member switch
    {
        FieldDeclarationSyntax => null,
        MethodDeclarationSyntax => null,
        RecordDeclarationSyntax => null,
        PropertyDeclarationSyntax p when IsComponentParameter(p) => null,
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

    /// <summary>Division is the one operator whose subset membership depends on operand TYPES, not
    /// syntax: C#'s int/int truncates and JS's `/` does not (7/2 = 3 vs 3.5), but C#'s double
    /// division and JS's `/` are the same IEEE-754 op. So `/` is admitted exactly when its RESULT
    /// is double. Kept OUT of the JsBinaryOperator table on purpose — a syntactic bless would admit
    /// int/int too. Decided semantically here, exactly like the (int)double cast.</summary>
    public static bool IsFaithfulDivision(BinaryExpressionSyntax b, SemanticModel model) =>
        b.IsKind(SyntaxKind.DivideExpression) &&
        model.GetTypeInfo(b).Type?.SpecialType == SpecialType.System_Double;

    /// <summary>Integer division: a DivideExpression whose RESULT type is int. Faithful in JS via
    /// Math.trunc (truncation toward zero, exactly C#'s int/int: 7/2 = 3, -7/2 = -3), and for 32-bit
    /// ints the quotient is exact in a JS double. Kept OUT of JsBinaryOperator (syntactic) because,
    /// like IsFaithfulDivision, admission is TYPE-dependent. CSharpFrontEnd emits the Math.trunc.</summary>
    public static bool IsIntegerDivision(BinaryExpressionSyntax b, SemanticModel model) =>
        b.IsKind(SyntaxKind.DivideExpression) &&
        model.GetTypeInfo(b).Type?.SpecialType == SpecialType.System_Int32;

    /// <summary>Long division: a DivideExpression whose RESULT type is long. Faithful in JS via a BARE `/`
    /// on BigInt operands -- BigInt division truncates toward zero, EXACTLY C#'s long/long (7L/2L = 3,
    /// -7L/2L = -3), and unlike int it needs NO Math.trunc because BigInt has no fractional part to drop.
    /// TYPE-dependent like the int and double divisions, so kept out of JsBinaryOperator.</summary>
    public static bool IsLongDivision(BinaryExpressionSyntax b, SemanticModel model) =>
        b.IsKind(SyntaxKind.DivideExpression) &&
        model.GetTypeInfo(b).Type?.SpecialType == SpecialType.System_Int64;

    /// <summary>Float division: a DivideExpression whose RESULT type is float (Single). Faithful in JS via
    /// `Math.fround(a / b)` -- the division runs in double then rounds to single, exactly C#'s float/float.
    /// The Math.fround wrap is applied by the generic float-arithmetic rule (CSharpFrontEnd wraps every
    /// Single-typed operation), so admission here only has to bless the `/`. TYPE-dependent like the others.</summary>
    public static bool IsFloatDivision(BinaryExpressionSyntax b, SemanticModel model) =>
        b.IsKind(SyntaxKind.DivideExpression) &&
        model.GetTypeInfo(b).Type?.SpecialType == SpecialType.System_Single;

    /// <summary>`new Exception(...)` (or a subtype) — the ONE object creation in §5, for `throw`. It maps
    /// to `new Error(...)` (CSharpFrontEnd). Every other `new` stays refused: a Filament module has no BCL,
    /// so `new StringBuilder()` etc. have nothing to become.</summary>
    public static bool IsExceptionCreation(ObjectCreationExpressionSyntax oc, SemanticModel model)
    {
        for (var b = model.GetTypeInfo(oc).Type; b is not null; b = b.BaseType)
            if (b.ToDisplayString() == "System.Exception") return true;
        return false;
    }

    /// <summary>`new Row(...)` or `new Row { … }` where `Row` is a LOCAL record (declared in source) — a data
    /// shape that compiles to an object literal (rows.js decision 2). Admitted in expression position so a
    /// record can be constructed INLINE (in a list literal, a `.Add(...)`, a local): the generator maps the
    /// positional args or the object-initialiser assignments to the object's properties. A record from the BCL
    /// or another assembly (no source declaration) has no such mapping and stays refused.</summary>
    public static bool IsLocalRecordCreation(ObjectCreationExpressionSyntax oc, SemanticModel model) =>
        model.GetTypeInfo(oc).Type is INamedTypeSymbol { IsRecord: true } t &&
        t.DeclaringSyntaxReferences.Length > 0;

    /// <summary>`new DateTime(...)` (decision 115). A DateTime is a BigInt tick count; the generator computes the
    /// ticks at generate-time from CONSTANT arguments and refuses non-constant construction. Admitted here so the
    /// expression FORM passes; the constant-args check is the generator's (it needs the values, not just the type).</summary>
    public static bool IsDateTimeCreation(ObjectCreationExpressionSyntax oc, SemanticModel model) =>
        model.GetTypeInfo(oc).Type?.SpecialType == SpecialType.System_DateTime;

    /// <summary>null = the expression FORM is in §5; non-null = the FIL0001 refusal — the decision
    /// behind Expr()'s default. Call-, member- and name-level refusals INSIDE a blessed form
    /// (invocation target, member access, identifier resolution) are separate concerns, not this.</summary>
    public static Refusal? ClassifyExpression(ExpressionSyntax e, SemanticModel model)
    {
        // Division: the one operator whose admission is TYPE-dependent, and whose refusal deserves a
        // TRUE reason rather than the generic "arithmetic operators are admitted" text (decision 77).
        if (e is BinaryExpressionSyntax bin && bin.IsKind(SyntaxKind.DivideExpression))
        {
            if (IsFaithfulDivision(bin, model)) return null;               // double result: verbatim `/`
            if (IsIntegerDivision(bin, model)) return null;                // int result: Math.trunc (CSharpFrontEnd)
            if (IsLongDivision(bin, model)) return null;                   // long result: verbatim `/` on BigInt (truncates)
            if (IsFloatDivision(bin, model)) return null;                  // float result: `/` under Math.fround (CSharpFrontEnd)
            return new Refusal("FIL0001", "unsupported-expression",         // none of int/long/float/double: refused
                $"{Describe(bin)} divides operands whose result is not int, long, float or double; only those four " +
                "numeric divisions are in section 5 (int via Math.trunc, float under Math.fround, long and double " +
                "verbatim). Refusing to emit.");
        }

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
            ElementAccessExpressionSyntax ea => IsIndexableFieldIndex(ea, model) || IsDictionaryFieldIndex(ea, model),
            InterpolatedStringExpressionSyntax => true,
            CastExpressionSyntax c => IsIntFromDouble(c, model),
            ObjectCreationExpressionSyntax oc =>
                IsExceptionCreation(oc, model) || IsLocalRecordCreation(oc, model) || IsDateTimeCreation(oc, model)
                || IsDictionaryCreation(oc, model),
            // A T[] LITERAL (`new int[]{…}`, `new[]{…}`) -> a JS array literal. Decision 117. A sized array with
            // no initialiser (`new int[n]`) is NOT admitted -- it needs an n-defaults array, deferred.
            ArrayCreationExpressionSyntax ac => ac.Initializer is not null && IsArrayCreation(ac.Type.ElementType, model),
            ImplicitArrayCreationExpressionSyntax => true,
            _ => false,
        };
        if (supported) return null;
        return new Refusal("FIL0001", "unsupported-expression",
            $"{Describe(e)} is not in the C# subset. Section 5 admits literals, arithmetic and " +
            "comparison operators, &&, ||, !, ternary, string interpolation, member access on a " +
            "local record, List<T> indexing, .Count, .Add, .RemoveAt, .Clear, `new Exception(...)` and " +
            "`new Record(...)` for a local record. Refusing to emit.");
    }

    // Indexing a field whose type is List<T> OR a single-rank array (one argument). Both are JS arrays, so the
    // indexer is the array's own (decision 117 generalises the List-only rule of rows.js decision 1).
    static bool IsIndexableFieldIndex(ElementAccessExpressionSyntax ea, SemanticModel model) =>
        ea.ArgumentList.Arguments.Count == 1 &&
        model.GetSymbolInfo(ea.Expression).Symbol is IFieldSymbol f &&
        (TypeSubset.ListElement(f.Type) ?? TypeSubset.ArrayElement(f.Type)) is not null;

    // Indexing a field whose type is a Dictionary<K,V> (decision 118). The generator emits `d.get(key)`.
    static bool IsDictionaryFieldIndex(ElementAccessExpressionSyntax ea, SemanticModel model) =>
        ea.ArgumentList.Arguments.Count == 1 &&
        model.GetSymbolInfo(ea.Expression).Symbol is IFieldSymbol f &&
        TypeSubset.DictionaryTypes(f.Type).Key is not null;

    // `new Dictionary<K,V>()` / with a collection initialiser -> a JS Map. Decision 118.
    static bool IsDictionaryCreation(ObjectCreationExpressionSyntax oc, SemanticModel model) =>
        model.GetTypeInfo(oc).Type is { } t && TypeSubset.DictionaryTypes(t).Key is not null;

    // A T[] literal's element type is in the subset (a scalar). The generator emits `[a, b, c]`.
    static bool IsArrayCreation(TypeSyntax elementType, SemanticModel model) =>
        model.GetTypeInfo(elementType).Type?.SpecialType is { } st &&
        st is SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Single
            or SpecialType.System_Double or SpecialType.System_Decimal or SpecialType.System_DateTime
            or SpecialType.System_Boolean or SpecialType.System_String;

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
