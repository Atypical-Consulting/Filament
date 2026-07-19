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
