using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Filament.Subset;

/// <summary>
/// The CALL subset (decision 148) — the last refusal kind to get an author-time squiggle. The single
/// source of "which invocations have a faithful mapping", mirroring the generator's dispatch guichets
/// one table row per decision: a call is admitted when it targets (a) a method of the component itself
/// (or an ancestor short of ComponentBase — @inherits, decision 136), or (b) one of the API rows below,
/// each of which the generator maps (the generator remains the emission authority and refuses finer
/// shapes — non-constant AddDays args, non-numeric OrderBy keys — at its own guichet; this table is the
/// NAME-level contract that gives every other call its squiggle instead of a build-time surprise).
/// </summary>
public static class CallSubset
{
    static readonly Dictionary<string, HashSet<string>> Admitted = new()
    {
        // LINQ over a materialised array (decisions 116/121/126/128).
        ["System.Linq.Enumerable"] = new()
        {
            "Where", "Select", "Count", "Any", "All", "ToList",
            "Sum", "Min", "Max", "Average", "First", "Last",
            "OrderBy", "OrderByDescending", "Skip", "Take", "Reverse", "ElementAt", "GroupBy",
        },
        // List mutations, version-bumped at statement level (rows.js decisions; decision 106).
        ["System.Collections.Generic.List<T>"] = new() { "Add", "RemoveAt", "Clear" },
        // A read-only Dictionary probe (decision 118).
        ["System.Collections.Generic.Dictionary<TKey, TValue>"] = new() { "ContainsKey" },
        // Tick arithmetic (decision 115).
        ["System.DateTime"] = new() { "AddDays" },
        // The Knuth-subtractive / Math.random factory (decision 146).
        ["System.Random"] = new() { "Next", "NextDouble" },
        // The one Task member (decision 119).
        ["System.Threading.Tasks.Task"] = new() { "Delay" },
        // The erased JS bridge (decision 133).
        ["Microsoft.JSInterop.IJSRuntime"] = new() { "InvokeVoidAsync", "InvokeAsync" },
        ["Microsoft.JSInterop.JSRuntimeExtensions"] = new() { "InvokeVoidAsync", "InvokeAsync" },
        // The erased HTTP bridge (decision 147).
        ["System.Net.Http.HttpClient"] = new() { "GetStringAsync" },
        ["System.Net.Http.Json.HttpClientJsonExtensions"] = new() { "GetFromJsonAsync", "PostAsJsonAsync" },
        // @ref's one faithful member (decision 132).
        ["Microsoft.AspNetCore.Components.ElementReferenceExtensions"] = new() { "FocusAsync" },
        // EventCallback raising, erased to the parent's method (decision 130).
        ["Microsoft.AspNetCore.Components.EventCallback"] = new() { "InvokeAsync" },
    };

    /// <summary>
    /// null = the call has a faithful home; non-null = the FIL0001 refusal. `component` is the class
    /// under analysis: its OWN methods — and its non-framework ancestors' (decision 136) — are the
    /// "methods declared in this component" spec 5 admits. An UNBOUND call returns null: C# itself
    /// reports it, and a squiggle on top of CS0103 would be noise.
    /// </summary>
    public static Refusal? ClassifyCall(InvocationExpressionSyntax inv, SemanticModel model, INamedTypeSymbol? component)
    {
        if (model.GetSymbolInfo(inv).Symbol is not IMethodSymbol m) return null;

        // A local function: the generator refuses its DECLARATION (unsupported-statement), which is
        // where the author's fix is; flagging every call site too is noise.
        if (m.MethodKind == MethodKind.LocalFunction) return null;

        var container = m.ContainingType;
        for (var c = component; c is not null; c = c.BaseType)
        {
            if (c.Name is "ComponentBase" or "Object") break;
            if (SymbolEqualityComparer.Default.Equals(container, c)) return null;
        }

        var display = container?.OriginalDefinition.ToDisplayString();
        if (display is not null && Admitted.TryGetValue(display, out var names) && names.Contains(m.Name))
            return null;

        // A nested record's members are refused at the RECORD (unsupported-member); calls to anything
        // else declared in the same source file still deserve their own squiggle, so no source-file
        // exemption here beyond the component chain above.
        return new Refusal("FIL0001", "unsupported-call",
            $"'{m.ContainingType?.Name}.{m.Name}' is not a call the subset maps. Section 5 admits calls to " +
            "methods declared in the component itself, plus the mapped API surface: LINQ's common operators " +
            "(decisions 116/121/126/128), List Add/RemoveAt/Clear, Dictionary.ContainsKey, DateTime.AddDays, " +
            "Random.Next/NextDouble (146), Task.Delay (119), IJSRuntime Invoke*Async (133) — the JS escape " +
            "hatch for everything else — and HttpClient's JSON surface (147). A Filament module ships no BCL.");
    }
}
