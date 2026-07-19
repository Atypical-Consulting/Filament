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

        if (allowList && ListElement(type) is { } element)
        {
            if (Scalars.Contains(element.SpecialType)) return null;
            if (IsComponentRecord(element, componentRecords)) return null;

            return new TypeRefusal("unsupported-type",
                $"'{type.ToDisplayString()}' is not in the C# subset. Section 5 admits List<T> of int, long, float, double, " +
                "bool, string, or of a record declared in the component. Refusing to emit.");
        }

        return new TypeRefusal("unsupported-type",
            $"'{type.ToDisplayString()}' is not in the C# subset. Section 5 admits int, long, float, double, bool, " +
            "string, and List<T> of those or of a record declared in the component. Refusing to emit.");
    }

    static bool IsComponentRecord(ITypeSymbol type, IReadOnlyCollection<INamedTypeSymbol> records) =>
        records.Any(r => SymbolEqualityComparer.Default.Equals(r, type));

    public static ITypeSymbol? ListElement(ITypeSymbol type) =>
        type.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>" &&
        type is INamedTypeSymbol { TypeArguments.Length: 1 } n
            ? n.TypeArguments[0]
            : null;
}
