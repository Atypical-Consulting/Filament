using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Filament.Subset;

/// <summary>A type-subset refusal: the reason slug and the author-facing message. Span-agnostic:
/// the caller (generator or analyzer) supplies the location. All FIL0002.</summary>
public readonly record struct TypeRefusal(string Reason, string Message);

/// <summary>Spec 5's type list — the single source of the FIL0002 type subset, shared by the
/// generator's CheckType and the analyzer. Pure over an ITypeSymbol; no diagnostics, no spans.</summary>
public static class TypeSubset
{
    /// <summary>null = in subset; non-null = the refusal to report at the caller's location.</summary>
    public static TypeRefusal? Classify(
        ITypeSymbol? type, IReadOnlyCollection<INamedTypeSymbol> componentRecords, bool allowList = true)
        => null; // Task 2 fills this
}
