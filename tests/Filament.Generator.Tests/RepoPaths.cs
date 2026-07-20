namespace Filament.Generator.Tests;

/// <summary>Locate the repo from the test binary, so tests do not depend on the cwd a runner picks.</summary>
public static class RepoPaths
{
    public static string Root { get; } = Find();

    public static string CounterRazor => Path.Combine(Root, "samples", "Counter", "Counter.razor");
    public static string AnswerKey => Path.Combine(Root, "samples", "Counter", "counter.js");

    /// <summary>
    /// THE FILE BLAZOR COMPILES. Rows has no Filament-flavoured stand-in and must not get one:
    /// "les deux apps compilent depuis du .razor PUR" is a claim about THIS file.
    /// </summary>
    public static string RowsRazor => Path.Combine(Root, "baseline", "Rows.Blazor", "RowsApp.razor");

    /// <summary>The Rows SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string RowsAnswerKey => Path.Combine(Root, "samples", "Rows", "rows.js");

    public static string IfRazor => Path.Combine(Root, "samples", "If", "If.razor");

    /// <summary>The @if SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string IfAnswerKey => Path.Combine(Root, "samples", "If", "if.js");

    public static string IfElseRazor => Path.Combine(Root, "samples", "IfElse", "IfElse.razor");

    /// <summary>The @else SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string IfElseAnswerKey => Path.Combine(Root, "samples", "IfElse", "ifelse.js");

    /// <summary>THE FILE BLAZOR COMPILES (no Filament stand-in; no drift, like Rows).</summary>
    public static string DivideRazor => Path.Combine(Root, "baseline", "Divide.Blazor", "App.razor");

    /// <summary>The double-division SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string DivideAnswerKey => Path.Combine(Root, "samples", "Divide", "divide.js");

    public static string DivideIntRazor => Path.Combine(Root, "baseline", "DivideInt.Blazor", "App.razor");

    /// <summary>The integer-division SPEC (decisions 51/87). Never edited to make a gate pass.</summary>
    public static string DivideIntAnswerKey => Path.Combine(Root, "samples", "DivideInt", "divideint.js");

    public static string LoopsRazor => Path.Combine(Root, "baseline", "Loops.Blazor", "App.razor");

    /// <summary>The loop/switch-statement SPEC (decisions 51/102). Never edited to make a gate pass.</summary>
    public static string LoopsAnswerKey => Path.Combine(Root, "samples", "Loops", "loops.js");

    /// <summary>Parent + sibling child (Greeting.razor) — the file Blazor compiles. Static-leaf composition.</summary>
    public static string ComposeRazor => Path.Combine(Root, "baseline", "Compose.Blazor", "App.razor");

    /// <summary>The composition SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string ComposeAnswerKey => Path.Combine(Root, "samples", "Compose", "compose.js");

    /// <summary>Root-level @foreach (a reactive list into #app) — the file Blazor compiles (no drift, like Rows).</summary>
    public static string RootForeachRazor => Path.Combine(Root, "baseline", "RootForeach.Blazor", "App.razor");

    /// <summary>The root-@foreach SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string RootForeachAnswerKey => Path.Combine(Root, "samples", "RootForeach", "rootforeach.js");

    /// <summary>Root-level @if (with a sibling toggle) — the file Blazor compiles (no drift, like Rows).</summary>
    public static string RootIfRazor => Path.Combine(Root, "baseline", "RootIf.Blazor", "App.razor");

    /// <summary>The root-@if SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string RootIfAnswerKey => Path.Combine(Root, "samples", "RootIf", "rootif.js");

    public static string IfMultiBodyRazor => Path.Combine(Root, "baseline", "IfMultiBody.Blazor", "App.razor");

    /// <summary>The multi-node-@if-body SPEC (decisions 51/81). Never edited to make a gate pass.</summary>
    public static string IfMultiBodyAnswerKey => Path.Combine(Root, "samples", "IfMultiBody", "ifmulti.js");

    public static string IfElseMultiBodyRazor => Path.Combine(Root, "baseline", "IfElseMultiBody.Blazor", "App.razor");

    /// <summary>The multi-node-@else-body SPEC (decisions 51/82). Never edited to make a gate pass.</summary>
    public static string IfElseMultiBodyAnswerKey => Path.Combine(Root, "samples", "IfElseMultiBody", "ifelsemulti.js");

    public static string IfNestedRazor => Path.Combine(Root, "baseline", "IfNested.Blazor", "App.razor");

    /// <summary>The nested-@if SPEC (decisions 51/81). Never edited to make a gate pass.</summary>
    public static string IfNestedAnswerKey => Path.Combine(Root, "samples", "IfNested", "ifnested.js");

    /// <summary>Bound-parameter composition (a reactive counter into a child) — the file Blazor compiles.</summary>
    public static string BoundComposeRazor => Path.Combine(Root, "baseline", "BoundCompose.Blazor", "App.razor");

    /// <summary>The bound-composition SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string BoundComposeAnswerKey => Path.Combine(Root, "samples", "BoundCompose", "boundcompose.js");

    /// <summary>Reactive `class` attribute (a counter whose #status class tracks state) — the file Blazor compiles.</summary>
    public static string ReactiveAttrRazor => Path.Combine(Root, "baseline", "ReactiveAttr.Blazor", "App.razor");

    /// <summary>The reactive-attribute SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string ReactiveAttrAnswerKey => Path.Combine(Root, "samples", "ReactiveAttr", "reactiveattr.js");

    /// <summary>Boolean `disabled` attribute (a toggle whose #target disabled tracks state) — the file Blazor compiles.</summary>
    public static string BoolAttrRazor => Path.Combine(Root, "baseline", "BoolAttr.Blazor", "App.razor");

    /// <summary>The boolean-attribute SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string BoolAttrAnswerKey => Path.Combine(Root, "samples", "BoolAttr", "boolattr.js");

    /// <summary>Mixed literal+expression `class` value (a counter whose #status class is composed) — the file Blazor compiles.</summary>
    public static string MixedAttrRazor => Path.Combine(Root, "baseline", "MixedAttr.Blazor", "App.razor");

    /// <summary>The mixed-attribute SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string MixedAttrAnswerKey => Path.Combine(Root, "samples", "MixedAttr", "mixedattr.js");

    /// <summary>Reactive string attribute names (title/href/aria-label on an &lt;a&gt;) — the file Blazor compiles.</summary>
    public static string StringAttrsRazor => Path.Combine(Root, "baseline", "StringAttrs.Blazor", "App.razor");

    /// <summary>The string-attribute-names SPEC (decisions 21/51). Never edited to make a gate pass.</summary>
    public static string StringAttrsAnswerKey => Path.Combine(Root, "samples", "StringAttrs", "stringattrs.js");

    public static string MoreAttrsRazor => Path.Combine(Root, "baseline", "MoreAttrs.Blazor", "App.razor");

    /// <summary>The attribute-allowlist-widening SPEC (decisions 51/97). Never edited to make a gate pass.</summary>
    public static string MoreAttrsAnswerKey => Path.Combine(Root, "samples", "MoreAttrs", "moreattrs.js");

    public static string BindRazor => Path.Combine(Root, "baseline", "Bind.Blazor", "App.razor");

    /// <summary>The two-way-binding SPEC (decisions 51/104). Never edited to make a gate pass.</summary>
    public static string BindAnswerKey => Path.Combine(Root, "samples", "Bind", "bind.js");

    public static string LambdaHandlerRazor => Path.Combine(Root, "baseline", "LambdaHandler.Blazor", "App.razor");

    /// <summary>The lambda-handler SPEC (decisions 51/105). Never edited to make a gate pass.</summary>
    public static string LambdaHandlerAnswerKey => Path.Combine(Root, "samples", "LambdaHandler", "lambdahandler.js");

    public static string ListOpsRazor => Path.Combine(Root, "baseline", "ListOps.Blazor", "App.razor");

    /// <summary>The List.Clear() SPEC (decisions 51/106). Never edited to make a gate pass.</summary>
    public static string ListOpsAnswerKey => Path.Combine(Root, "samples", "ListOps", "listops.js");

    public static string CheckBindRazor => Path.Combine(Root, "baseline", "CheckBind.Blazor", "App.razor");

    /// <summary>The checkbox-@bind SPEC (decisions 51/104). Never edited to make a gate pass.</summary>
    public static string CheckBindAnswerKey => Path.Combine(Root, "samples", "CheckBind", "checkbind.js");

    public static string IntBindRazor => Path.Combine(Root, "baseline", "IntBind.Blazor", "App.razor");

    /// <summary>The int-@bind SPEC (decisions 51/104). Never edited to make a gate pass.</summary>
    public static string IntBindAnswerKey => Path.Combine(Root, "samples", "IntBind", "intbind.js");

    public static string CodeBlockRazor => Path.Combine(Root, "baseline", "CodeBlock.Blazor", "App.razor");

    /// <summary>The root-@{ }-code-block SPEC (decisions 51/89). Never edited to make a gate pass.</summary>
    public static string CodeBlockAnswerKey => Path.Combine(Root, "samples", "CodeBlock", "codeblock.js");

    public static string TryLockRazor => Path.Combine(Root, "baseline", "TryLock.Blazor", "App.razor");

    /// <summary>The try/catch/throw/lock SPEC (decisions 51/110). Never edited to make a gate pass.</summary>
    public static string TryLockAnswerKey => Path.Combine(Root, "samples", "TryLock", "trylock.js");

    public static string PositionalRecordRazor => Path.Combine(Root, "baseline", "PositionalRecord.Blazor", "App.razor");

    /// <summary>The positional-record SPEC (decisions 51/111). Never edited to make a gate pass.</summary>
    public static string PositionalRecordAnswerKey => Path.Combine(Root, "samples", "PositionalRecord", "positionalrecord.js");

    public static string LongCounterRazor => Path.Combine(Root, "baseline", "LongCounter.Blazor", "App.razor");

    /// <summary>The long-via-BigInt SPEC (decisions 51/112). Never edited to make a gate pass.</summary>
    public static string LongCounterAnswerKey => Path.Combine(Root, "samples", "LongCounter", "longcounter.js");

    public static string FloatCounterRazor => Path.Combine(Root, "baseline", "FloatCounter.Blazor", "App.razor");

    /// <summary>The float-via-fround SPEC (decisions 51/113). Never edited to make a gate pass.</summary>
    public static string FloatCounterAnswerKey => Path.Combine(Root, "samples", "FloatCounter", "floatcounter.js");

    public static string DecimalCounterRazor => Path.Combine(Root, "baseline", "DecimalCounter.Blazor", "App.razor");

    /// <summary>The decimal-via-boxed-{m,s} SPEC (decisions 51/114). Never edited to make a gate pass.</summary>
    public static string DecimalCounterAnswerKey => Path.Combine(Root, "samples", "DecimalCounter", "decimalcounter.js");

    public static string DateTimeCounterRazor => Path.Combine(Root, "baseline", "DateTimeCounter.Blazor", "App.razor");

    /// <summary>The DateTime-via-BigInt-ticks SPEC (decisions 51/115). Never edited to make a gate pass.</summary>
    public static string DateTimeCounterAnswerKey => Path.Combine(Root, "samples", "DateTimeCounter", "datetimecounter.js");

    public static string LinqRazor => Path.Combine(Root, "baseline", "Linq.Blazor", "App.razor");

    /// <summary>The LINQ-over-a-List SPEC (decisions 51/116). Never edited to make a gate pass.</summary>
    public static string LinqAnswerKey => Path.Combine(Root, "samples", "Linq", "linq.js");

    public static string ArrayIndexRazor => Path.Combine(Root, "baseline", "ArrayIndex.Blazor", "App.razor");

    /// <summary>The T[]-array SPEC (decisions 51/117). Never edited to make a gate pass.</summary>
    public static string ArrayIndexAnswerKey => Path.Combine(Root, "samples", "ArrayIndex", "arrayindex.js");

    public static string DictLookupRazor => Path.Combine(Root, "baseline", "DictLookup.Blazor", "App.razor");

    /// <summary>The Dictionary-as-Map SPEC (decisions 51/118). Never edited to make a gate pass.</summary>
    public static string DictLookupAnswerKey => Path.Combine(Root, "samples", "DictLookup", "dictlookup.js");

    public static string AsyncClickRazor => Path.Combine(Root, "baseline", "AsyncClick.Blazor", "App.razor");

    /// <summary>The async/await SPEC (decisions 51/119). Never edited to make a gate pass.</summary>
    public static string AsyncClickAnswerKey => Path.Combine(Root, "samples", "AsyncClick", "asyncclick.js");

    public static string IfNestedMixedRazor => Path.Combine(Root, "baseline", "IfNestedMixed.Blazor", "App.razor");

    /// <summary>The mixed-@if-branch SPEC (decisions 51/120). Never edited to make a gate pass.</summary>
    public static string IfNestedMixedAnswerKey => Path.Combine(Root, "samples", "IfNestedMixed", "ifnestedmixed.js");

    public static string LinqAggregateRazor => Path.Combine(Root, "baseline", "LinqAggregate.Blazor", "App.razor");

    /// <summary>The LINQ-aggregate SPEC (decisions 51/121). Never edited to make a gate pass.</summary>
    public static string LinqAggregateAnswerKey => Path.Combine(Root, "samples", "LinqAggregate", "linqaggregate.js");

    public static string SizedArrayRazor => Path.Combine(Root, "baseline", "SizedArray.Blazor", "App.razor");

    /// <summary>The sized-array SPEC (decisions 51/122). Never edited to make a gate pass.</summary>
    public static string SizedArrayAnswerKey => Path.Combine(Root, "samples", "SizedArray", "sizedarray.js");

    public static string AsyncResultRazor => Path.Combine(Root, "baseline", "AsyncResult.Blazor", "App.razor");

    /// <summary>The async-Task&lt;T&gt; SPEC (decisions 51/123). Never edited to make a gate pass.</summary>
    public static string AsyncResultAnswerKey => Path.Combine(Root, "samples", "AsyncResult", "asyncresult.js");

    public static string ForeachArrayRazor => Path.Combine(Root, "baseline", "ForeachArray.Blazor", "App.razor");

    /// <summary>The @foreach-over-array SPEC (decisions 51/124). Never edited to make a gate pass.</summary>
    public static string ForeachArrayAnswerKey => Path.Combine(Root, "samples", "ForeachArray", "foreacharray.js");

    public static string ForeachDictRazor => Path.Combine(Root, "baseline", "ForeachDict.Blazor", "App.razor");

    /// <summary>The @foreach-over-Dictionary SPEC (decisions 51/125). Never edited to make a gate pass.</summary>
    public static string ForeachDictAnswerKey => Path.Combine(Root, "samples", "ForeachDict", "foreachdict.js");

    public static string LinqOrderRazor => Path.Combine(Root, "baseline", "LinqOrder.Blazor", "App.razor");

    /// <summary>The LINQ-ordering SPEC (decisions 51/126). Never edited to make a gate pass.</summary>
    public static string LinqOrderAnswerKey => Path.Combine(Root, "samples", "LinqOrder", "linqorder.js");

    public static string ElementWriteRazor => Path.Combine(Root, "baseline", "ElementWrite.Blazor", "App.razor");

    /// <summary>The element-write SPEC (decisions 51/127). Never edited to make a gate pass.</summary>
    public static string ElementWriteAnswerKey => Path.Combine(Root, "samples", "ElementWrite", "elementwrite.js");

    public static string GroupByRazor => Path.Combine(Root, "baseline", "GroupBy.Blazor", "App.razor");

    /// <summary>The GroupBy SPEC (decisions 51/128). Never edited to make a gate pass.</summary>
    public static string GroupByAnswerKey => Path.Combine(Root, "samples", "GroupBy", "groupby.js");

    public static string JsInteropRazor => Path.Combine(Root, "baseline", "JsInterop.Blazor", "App.razor");

    /// <summary>The JS-interop SPEC (decisions 51/133). Never edited to make a gate pass.</summary>
    public static string JsInteropAnswerKey => Path.Combine(Root, "samples", "JsInterop", "jsinterop.js");

    public static string ElemRefRazor => Path.Combine(Root, "baseline", "ElemRef.Blazor", "App.razor");

    /// <summary>The @ref SPEC (decisions 51/132). Never edited to make a gate pass.</summary>
    public static string ElemRefAnswerKey => Path.Combine(Root, "samples", "ElemRef", "elemref.js");

    public static string FragmentRazor => Path.Combine(Root, "baseline", "Fragment.Blazor", "App.razor");

    /// <summary>The Fragment SPEC (decisions 51/131). Never edited to make a gate pass.</summary>
    public static string FragmentAnswerKey => Path.Combine(Root, "samples", "Fragment", "fragment.js");

    public static string EventCbRazor => Path.Combine(Root, "baseline", "EventCb.Blazor", "App.razor");

    /// <summary>The EventCb SPEC (decisions 51/130). Never edited to make a gate pass.</summary>
    public static string EventCbAnswerKey => Path.Combine(Root, "samples", "EventCb", "eventcb.js");
    public static string Canon => Path.Combine(Root, "tools", "canon.mjs");

    /// <summary>
    /// One .razor per Razor construct that is OUTSIDE Phase 2's subset. Each one must
    /// produce a located FIL0003 and no file; every one of them was silently compiled
    /// into a plausible-looking module before DiagnosticTests existed.
    /// </summary>
    public static string Unsupported => Path.Combine(Root, "tests", "Filament.Generator.Tests", "Unsupported");

    /// <summary>
    /// The other side of <see cref="Unsupported"/>: .razor fixtures that USED to be refused and now
    /// COMPILE (each pinned by a *_NowCompiles / *_CompilesClean test). They were carried out of the
    /// Unsupported dir the moment the capability shipped, so the folder name never lies about a fixture
    /// the generator accepts. Same Code/ + Gate/ substructure as Unsupported, so provenance is preserved.
    /// </summary>
    public static string Supported => Path.Combine(Root, "tests", "Filament.Generator.Tests", "Supported");

    static string Find()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "DECISIONS.md")))
                return d.FullName;
        throw new InvalidOperationException("repo root (the directory holding DECISIONS.md) not found above " + AppContext.BaseDirectory);
    }
}
