using Microsoft.AspNetCore.Razor.Language.Intermediate;

namespace Filament.Generator;

/// <summary>
/// THE SHAPE DECISION 54 FORCED, AND THE ONLY HONEST WAY OUT OF IT.
///
/// Decision 54, verified again on this exact file before a line of this was written:
///
///     CSharpCodeIntermediateNode     [CS] "foreach (Row row in _rows)\n            {\n"
///     MarkupElementIntermediateNode  &lt;tr&gt;                       &lt;- a SIBLING, not a child
///     CSharpCodeIntermediateNode     [CS] "            }\n"
///
/// Razor emits NO loop node. There is no scope, no balanced tree, and the braces do not
/// close. That is not a bug: Blazor never needs the loop to be UNDERSTOOD, it re-parses
/// this text with Roslyn and calls RenderTreeBuilder at runtime. Filament emits JS, so it
/// must TRANSLATE the loop, which means it needs the one thing Razor destroyed -- the
/// syntax tree.
///
/// SO THE SPANS ARE REASSEMBLED AND RE-PARSED, and the markup is put back as a CALL:
///
///     foreach (Row row in _rows)
///     {
///     __m0();                  &lt;- "emit markup node 0 here"
///     __s0(row.Id);            &lt;- "this expression is read HERE, in THIS scope"
///     __s1(row.Label);
///     __s2(row.Id);            &lt;- @key
///     }
///
/// That text BALANCES, so Roslyn parses it, and it is parsed INSIDE the same class as
/// @code, so `Row`, `_rows` and `row` all resolve to real symbols. The markers are what
/// carry the two facts the concatenation would otherwise lose: WHERE a markup node sat in
/// the statement stream, and WHICH SCOPE each of its @expressions is read in. `row` is a
/// loop local; nothing outside the loop can resolve it, and nothing here has to guess --
/// `__s0(row.Id)` is inside the loop, so the semantic model answers.
///
/// WHY NOT SPLICE, WHICH IS THE OBVIOUS SHORTCUT. Because the braces balance only once the
/// markup is between them, a splicer would have to emit the JS for &lt;tr&gt; INTO a C# string
/// and hope. Section 10: "Toute construction hors sous-ensemble doit produire un
/// diagnostic, jamais du JS silencieusement faux." A concatenation that is never parsed
/// cannot produce a diagnostic about anything -- it can only produce text.
///
/// THE MARKER NAMES ARE RESERVED, AND THAT IS CHECKED (see CSharpFrontEnd.Compile):
/// user code containing `__filament` is refused rather than silently colliding with a
/// marker and being mistaken for one.
/// </summary>
public sealed class TemplatePlan
{
    /// <summary>One per container element whose children hold template C#.</summary>
    public List<TemplateRegion> Regions { get; } = [];

    /// <summary>
    /// Every @expression the template reads OUTSIDE any region -- Counter's @currentCount
    /// is one. They still go through the compilation (in a synthetic method at class
    /// scope) rather than through a regex, because "is this name a signal?" must be
    /// answered by the compiler's own table and not by spelling (decision 57).
    /// </summary>
    public List<IntermediateNode> FreeSlots { get; } = [];

    /// <summary>
    /// Every inline lambda EVENT HANDLER -- `@onclick="() => currentCount++"` (decision 105). Each is
    /// wrapped as a synthetic method `void __filament_lambda_k() { … }` in the compilation, so its body
    /// goes through the SAME marking + translation the @code method bodies do (decision 57's reason again:
    /// "is this name a signal?" is answered by the compiler, not a regex). Keyed by the attribute node so
    /// emission can read the translated body back.
    /// </summary>
    public List<LambdaHandler> LambdaHandlers { get; } = [];
}

/// <param name="Attr">the event attribute node, the key emission looks the body up by</param>
/// <param name="DomEvent">the DOM event name, e.g. "click"</param>
/// <param name="RawHandler">the unwrapped handler text, e.g. "() => currentCount++" (CSharpFrontEnd parses it)</param>
public sealed record LambdaHandler(IntermediateNode Attr, string DomEvent, string RawHandler);

/// <summary>One container's children, in document order, as the raw material of a re-parse.</summary>
public sealed class TemplateRegion
{
    public required IntermediateNode Container { get; init; }
    public required IReadOnlyList<RegionItem> Items { get; init; }
}

public abstract record RegionItem;

/// <summary>A raw C# span Razor left lying in the child list. Unbalanced by itself.</summary>
public sealed record CodeItem(CSharpCodeIntermediateNode Node) : RegionItem;

/// <summary>
/// A markup node sitting in the middle of that C#. <paramref name="Slots"/> is every
/// @expression and @key inside its subtree, in document order -- they must be re-parsed in
/// the scope the markup node occupies, which is the loop's, not the component's.
/// </summary>
public sealed record MarkupItem(IntermediateNode Node, IReadOnlyList<IntermediateNode> Slots) : RegionItem;

// ---- what the front end hands back --------------------------------------

/// <summary>One thing to emit, in order, for a container that held template C#.</summary>
public abstract record TemplateOp;

/// <summary>Emit this markup node as a child of the container, exactly as if no C# were there.</summary>
public sealed record MarkupOp(IntermediateNode Node) : TemplateOp;

/// <summary>
/// `@foreach (Row row in _rows) { &lt;tr @key="row.Id"&gt;...&lt;/tr&gt; }` -> `list(...)`.
/// Everything here is read off the RE-PARSED tree and resolved against real symbols.
/// </summary>
/// <param name="Var">the loop variable's JS name -- list()'s create/keyOf parameter</param>
/// <param name="ListJs">the JS binding holding the mutable array (rows.js mapping decision 1)</param>
/// <param name="VersionJs">the version Signal that binding's mutations bump</param>
/// <param name="Body">the ONE markup node the loop body produces</param>
/// <param name="Key">the @key node, which becomes list()'s keyOf</param>
public sealed record ForEachOp(
    string Var, string ListJs, string VersionJs, IntermediateNode Body, IntermediateNode Key) : TemplateOp;

/// <summary>
/// `@if (c0) { &lt;b0&gt; } else if (c1) { &lt;b1&gt; } … else { &lt;bn&gt; }` -> a conditional list()
/// whose single item's value is the ACTIVE BRANCH INDEX, with a comment anchor. A plain @if is the
/// one-branch case, and it keeps its exact #81 emission.
/// </summary>
/// <param name="Branches">the if / else-if / else branches, in source order</param>
public sealed record IfOp(IReadOnlyList<IfBranch> Branches) : TemplateOp;

/// <param name="Cond">the branch condition, already translated to JS (e.g. "n.value === 0"),
/// or null for the trailing @else</param>
/// <param name="Body">the ops this branch produces: one or more markup nodes (each a leaf of the
/// conditional list), OR a single nested @if (an IfOp, flattened into the decision-tree source).</param>
public sealed record IfBranch(string? Cond, IReadOnlyList<TemplateOp> Body);
