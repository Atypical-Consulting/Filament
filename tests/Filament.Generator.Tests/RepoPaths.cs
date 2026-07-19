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
    public static string Canon => Path.Combine(Root, "tools", "canon.mjs");

    /// <summary>
    /// One .razor per Razor construct that is OUTSIDE Phase 2's subset. Each one must
    /// produce a located FIL0003 and no file; every one of them was silently compiled
    /// into a plausible-looking module before DiagnosticTests existed.
    /// </summary>
    public static string Unsupported => Path.Combine(Root, "tests", "Filament.Generator.Tests", "Unsupported");

    static string Find()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "DECISIONS.md")))
                return d.FullName;
        throw new InvalidOperationException("repo root (the directory holding DECISIONS.md) not found above " + AppContext.BaseDirectory);
    }
}
