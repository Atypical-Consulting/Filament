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

        // System.Random (decision 146): a STATEFUL GENERATOR a component may hold in a field or local.
        // Its surface is method dispatch (Next/NextDouble) in the generator; displaying one is refused
        // at the slot (C# renders the type name, a JS object would render "[object Object]").
        if (IsRandom(type)) return null;

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

    /// <summary>
    /// A non-generic <c>EventCallback</c> (decision 130). Deliberately NOT admitted by Classify: it is
    /// not a VALUE the subset can hold, store or display, and as a field it stays refused exactly as
    /// before. It is admitted in ONE position only -- a <c>[Parameter]</c> the parent bound to one of its
    /// own methods -- where it is not a value at all but an ALIAS the compiler erases: the child inlines
    /// into the parent's mount(), so raising the callback IS calling the parent's method, with no
    /// delegate object and no subscription list at runtime.
    ///
    /// <c>EventCallback&lt;T&gt;</c> is NOT matched, and stays refused: an argument would have to be
    /// marshalled from a DOM event, which is a different question and is not measured yet.
    /// </summary>
    public static bool IsEventCallback(ITypeSymbol? type) => IsComponentsType(type, "EventCallback");

    /// <summary>
    /// <c>Microsoft.AspNetCore.Components.Web.KeyboardEventArgs</c> (decision 159) -- admitted as a
    /// HANDLER PARAMETER type only, on <c>@onkeydown</c>/<c>@onkeyup</c>, never as state: its mapped
    /// members (Key, Code, the modifier flags) are direct projections of the DOM event the listener
    /// already receives, so the bridge erases like every other one.
    /// </summary>
    public static bool IsKeyboardEventArgs(ITypeSymbol? type) =>
        type?.ToDisplayString() == "Microsoft.AspNetCore.Components.Web.KeyboardEventArgs";

    /// <summary>
    /// A non-generic <c>RenderFragment</c> (decision 131). Like EventCallback, deliberately NOT admitted
    /// by Classify -- it is not a value the subset can hold or display -- and admitted in ONE position
    /// only: a <c>[Parameter]</c>, where it names the HOLE a composing parent's markup drops into. There
    /// is no delegate at runtime: the parent's markup subtree is inlined at the child's
    /// <c>@ChildContent</c> position, compiled in the PARENT's scope because that is the scope it was
    /// written in.
    ///
    /// <c>RenderFragment&lt;T&gt;</c> is NOT matched, and stays refused: a templated fragment takes a
    /// context argument per item, which is a different question and is not measured yet.
    /// </summary>
    public static bool IsRenderFragment(ITypeSymbol? type) => IsComponentsType(type, "RenderFragment");

    /// <summary>
    /// An <c>ElementReference</c> (decision 132) — the target of an <c>@ref</c>. Like EventCallback and
    /// RenderFragment, deliberately NOT admitted by Classify: it is not a value §5 can hold, compare or
    /// display. It is admitted in ONE shape only, an uninitialised FIELD an <c>@ref</c> captures into,
    /// where the emitted module already holds the node in a const and the reference is just its NAME.
    /// </summary>
    public static bool IsElementReference(ITypeSymbol? type) => IsComponentsType(type, "ElementReference");

    /// <summary>
    /// <c>IJSRuntime</c> (decision 133) — the ONE injectable service. Like the other three framework
    /// types it is never a §5 value; it is admitted only as the target of an <c>@inject</c>, where it
    /// does not denote an object at all but the HOST GLOBAL SCOPE. Blazor needs a runtime object because
    /// calling JavaScript from .NET means marshalling across a boundary; a module that IS JavaScript has
    /// no boundary, so the bridge is erased and the call becomes the call.
    /// </summary>
    public static bool IsJsRuntime(ITypeSymbol? type) =>
        type is INamedTypeSymbol { TypeArguments.Length: 0 } n &&
        n.Name == "IJSRuntime" &&
        n.ContainingNamespace?.ToDisplayString() == "Microsoft.JSInterop";

    /// <summary>
    /// The JSON gate (decision 147): null = T's JSON shape IS its Filament shape, so
    /// GetFromJsonAsync&lt;T&gt;/PostAsJsonAsync are faithful; non-null = a description of the offender for
    /// the refusal. Faithful: int, double, bool, string (JSON numbers/booleans/strings ARE the JS values
    /// the subset maps them to), records of faithful members, List&lt;T&gt;/T[] of faithful elements.
    /// Unfaithful, each with its reason: long (a JSON number arrives as a JS number, not the BigInt long
    /// maps to), float (not Math.fround'ed), decimal (not the boxed {m,s}), DateTime (JSON carries a
    /// string, not BigInt ticks), Dictionary (a JSON object is a plain object, not the Map the subset
    /// maps Dictionary to), and anything else.
    /// </summary>
    public static string? JsonUnfaithful(ITypeSymbol? type, IReadOnlyCollection<INamedTypeSymbol> componentRecords)
    {
        if (type is null) return "an unresolved type";
        switch (type.SpecialType)
        {
            case SpecialType.System_Int32:
            case SpecialType.System_Double:
            case SpecialType.System_Boolean:
            case SpecialType.System_String:
                return null;
            case SpecialType.System_Int64:
                return "'long' (a JSON number deserializes to a JS number, not the BigInt `long` maps to -- decision 112)";
            case SpecialType.System_Single:
                return "'float' (a JSON number is not Math.fround'ed -- decision 113)";
            case SpecialType.System_Decimal:
                return "'decimal' (a JSON number is not the boxed { m, s } `decimal` maps to -- decision 114)";
            case SpecialType.System_DateTime:
                return "'DateTime' (JSON carries a string, not the BigInt ticks `DateTime` maps to -- decision 115)";
        }
        if ((ListElement(type) ?? ArrayElement(type)) is { } element)
            return JsonUnfaithful(element, componentRecords);
        if (IsComponentRecord(type, componentRecords))
        {
            foreach (var p in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (p.IsStatic || p.DeclaredAccessibility != Accessibility.Public) continue;
                if (p.Name == "EqualityContract") continue;   // the record's synthesized machinery, not data
                if (JsonUnfaithful(p.Type, componentRecords) is { } offender) return offender;
            }
            return null;
        }
        return $"'{type.ToDisplayString()}'";
    }

    /// <summary>
    /// <c>System.Random</c> (decision 146) -- a stateful generator, admitted as a field/local value.
    /// SEEDED, it is the exact .NET Knuth-subtractive sequence (Net5CompatSeedImpl, stable by compat
    /// guarantee), reimplemented in the emitted <c>__rnd(seed)</c>; UNSEEDED (and <c>Random.Shared</c>),
    /// both sides' sequences are arbitrary, so Math.random rides behind the same interface.
    /// </summary>
    public static bool IsRandom(ITypeSymbol? type) =>
        type is INamedTypeSymbol { TypeArguments.Length: 0, Name: "Random" } r &&
        r.ContainingNamespace?.ToDisplayString() == "System";

    /// <summary>Name + namespace rather than a display string, so a NULLABLE annotation cannot change the
    /// answer: `RenderFragment?` is the same type as `RenderFragment`, and the ONE declaration form Blazor
    /// authors actually write for ChildContent is the nullable one.</summary>
    static bool IsComponentsType(ITypeSymbol? type, string name) =>
        type is INamedTypeSymbol { TypeArguments.Length: 0 } n &&
        n.Name == name &&
        n.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Components";
}
