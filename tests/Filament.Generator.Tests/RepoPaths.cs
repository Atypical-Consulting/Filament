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
