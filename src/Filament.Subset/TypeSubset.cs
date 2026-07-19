using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Filament.Subset;

/// <summary>A type-subset refusal: the reason slug and the author-facing message. Span-agnostic:
/// the caller (generator or analyzer) supplies the location. All FIL0002.</summary>
public readonly record struct TypeRefusal(string Reason, string Message);

/// <summary>Spec 5's type list — the single source of the FIL0002 type subset, shared by the
/// generator's CheckType and the analyzer. Pure over an ITypeSymbol; no diagnostics, no spans.</summary>
public static class TypeSubset
{
    static readonly HashSet<SpecialType> Scalars = new()
    {
        SpecialType.System_Int32, SpecialType.System_Int64,
        SpecialType.System_Single, SpecialType.System_Double, SpecialType.System_Decimal,
        SpecialType.System_DateTime,
        SpecialType.System_Boolean, SpecialType.System_String,
    };

    /// <summary>null = in subset; non-null = the refusal to report at the caller's location.</summary>
    public static TypeRefusal? Classify(
        ITypeSymbol? type, IReadOnlyCollection<INamedTypeSymbol> componentRecords, bool allowList = true)
    {
        if (type is null || type.TypeKind == TypeKind.Error)
            return new TypeRefusal("unresolved-type", "this type does not resolve. Refusing to emit.");

        // void is acceptable — it only ever reaches here as a method return type (a field/local/
        // parameter cannot be void), so admitting it here can never mask a real refusal. The
        // generator guards void at its call site; this keeps both consumers consistent (decision 53).
        if (type.SpecialType == SpecialType.System_Void) return null;

        if (Scalars.Contains(type.SpecialType)) return null;
        if (IsComponentRecord(type, componentRecords)) return null;

        if (allowList && (ListElement(type) ?? ArrayElement(type)) is { } element)
        {
            if (Scalars.Contains(element.SpecialType)) return null;
            if (IsComponentRecord(element, componentRecords)) return null;

            return new TypeRefusal("unsupported-type",
                $"'{type.ToDisplayString()}' is not in the C# subset. Section 5 admits List<T> or T[] of int, long, float, double, decimal, DateTime, " +
                "bool, string, or of a record declared in the component. Refusing to emit.");
        }

        // A Dictionary<K,V> -> a JS Map (decision 118), admitted when BOTH K and V are scalars. Scalar keys are
        // required because JS Map keys use SameValueZero equality, which matches C#'s default for the primitive
        // key types; a record key would use reference identity and diverge.
        if (allowList && DictionaryTypes(type) is var (k, v) && k is not null && v is not null)
        {
            if (Scalars.Contains(k.SpecialType) && (Scalars.Contains(v.SpecialType) || IsComponentRecord(v, componentRecords)))
                return null;
            return new TypeRefusal("unsupported-type",
                $"'{type.ToDisplayString()}' is not in the C# subset. Section 5 admits Dictionary<K,V> with a scalar " +
                "key and a scalar or record value. Refusing to emit.");
        }

        return new TypeRefusal("unsupported-type",
            $"'{type.ToDisplayString()}' is not in the C# subset. Section 5 admits int, long, float, double, decimal, DateTime, bool, " +
            "string, and List<T> of those or of a record declared in the component. Refusing to emit.");
    }

    static bool IsComponentRecord(ITypeSymbol type, IReadOnlyCollection<INamedTypeSymbol> records) =>
        records.Any(r => SymbolEqualityComparer.Default.Equals(r, type));

    public static ITypeSymbol? ListElement(ITypeSymbol type) =>
        type.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>" &&
        type is INamedTypeSymbol { TypeArguments.Length: 1 } n
            ? n.TypeArguments[0]
            : null;

    /// <summary>A Dictionary&lt;K,V&gt; -> (K, V), else (null, null) (decision 118). A Dictionary maps to a JS Map.</summary>
    public static (ITypeSymbol? Key, ITypeSymbol? Value) DictionaryTypes(ITypeSymbol type) =>
        type.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.Dictionary<TKey, TValue>" &&
        type is INamedTypeSymbol { TypeArguments.Length: 2 } n
            ? (n.TypeArguments[0], n.TypeArguments[1])
            : (null, null);

    /// <summary>A single-rank array `T[]` -> its element type, else null (decision 117). A T[] maps to the
    /// SAME JS array a List<T> does; the difference is only mutability (an array is fixed-size, so it is
    /// admitted READ-ONLY — indexing, .Length, iteration; element assignment is refused in the generator).
    /// Multi-dimensional/jagged arrays are out (rank != 1).</summary>
    public static ITypeSymbol? ArrayElement(ITypeSymbol type) =>
        type is IArrayTypeSymbol { Rank: 1 } a ? a.ElementType : null;
}
