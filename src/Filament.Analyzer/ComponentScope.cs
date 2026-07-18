using Microsoft.CodeAnalysis;

namespace Filament.Analyzer;

internal static class ComponentScope
{
    /// <summary>Whole-project opt-in: a type is in scope iff it derives from Blazor's ComponentBase.</summary>
    public static bool IsComponent(INamedTypeSymbol type)
    {
        for (var b = type.BaseType; b is not null; b = b.BaseType)
            if (b.ToDisplayString() == "Microsoft.AspNetCore.Components.ComponentBase") return true;
        return false;
    }
}
