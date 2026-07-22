using System.Text;
using System.Text.Json;
using ErrorBoundaryOracle;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/*
 * error-boundary-oracle — what Blazor's <ErrorBoundary> ACTUALLY catches (decision 164).
 *
 *     dotnet run --project tools/error-boundary-oracle [-- --json]
 *
 * Exit 0 = every witness behaved as decision 164 recorded. Non-zero = Blazor's own behaviour has
 * moved, and the mapping built on it has to be re-argued rather than quietly kept.
 *
 * WHY NOT PLAYWRIGHT, like every other oracle here. Because it is not installable in this
 * environment (BENCH n°69's disclosed reserve), and because it is not needed: the catch is decided
 * by Renderer.HandleExceptionViaErrorBoundary, which is plain .NET. This hosts the real Renderer,
 * the real ErrorBoundary component and real event dispatch. What it cannot see is the BROWSER's
 * half — what a WASM app paints after an unhandled exception — and it does not claim to.
 *
 * THE FINDING THIS EXISTS TO PIN. W1 and W2 differ, and W1 is the shape every author expects a
 * boundary to catch:
 *
 *   W1  a throw from an event handler the PARENT owns, written inside the boundary   NOT caught
 *   W2  a throw from a CHILD COMPONENT's event handler                                   caught
 *   W3  a throw from a CHILD COMPONENT's OnInitialized                                    caught
 *   W4  a throw raised while the parent EVALUATES the content                             caught
 *   W5  the same on RE-RENDER; the latch is STICKY and the outside keeps updating          caught
 *   W6  with no ErrorContent, the default UI is <div class="blazor-error-boundary">        caught
 *
 * A boundary catches what its DESCENDANTS raise. A handler written in the boundary's child content
 * belongs to the component that WROTE the fragment — an ancestor of the boundary — so it is not a
 * descendant and is not caught. W1 IS the control for W2: the harness distinguishes the outcomes,
 * so "caught" is not something it reports unconditionally.
 */

public static class Probe
{
    public static readonly List<string> Log = [];
    public static void Say(string s) => Log.Add(s);
}

/// <summary>ErrorBoundary injects this; WASM's own implementation is internal, so the oracle
/// supplies one and uses it as a second, independent signal that the catch really fired.</summary>
sealed class RecordingBoundaryLogger : IErrorBoundaryLogger
{
    public static int Calls;
    public ValueTask LogErrorAsync(Exception exception)
    {
        Calls++;
        Probe.Say($"logged:{exception.GetType().Name}");
        return ValueTask.CompletedTask;
    }
}

sealed class OracleRenderer(IServiceProvider services, ILoggerFactory lf) : Renderer(services, lf)
{
    public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
    public readonly List<Exception> Escaped = [];
    public readonly List<ulong> Handlers = [];
    public readonly List<string> Frames = [];

    protected override void HandleException(Exception e)
    {
        Probe.Say($"escaped:{e.GetType().Name}");
        Escaped.Add(e);
    }

    protected override Task UpdateDisplayAsync(in RenderBatch batch)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < batch.ReferenceFrames.Count; i++)
        {
            var f = batch.ReferenceFrames.Array[i];
            if (f.FrameType == RenderTreeFrameType.Attribute && f.AttributeEventHandlerId != 0)
                Handlers.Add(f.AttributeEventHandlerId);
            switch (f.FrameType)
            {
                case RenderTreeFrameType.Element: sb.Append($"<{f.ElementName}> "); break;
                case RenderTreeFrameType.Attribute: sb.Append($"[{f.AttributeName}={f.AttributeValue}] "); break;
                case RenderTreeFrameType.Text: sb.Append($"'{f.TextContent}' "); break;
                case RenderTreeFrameType.Markup: sb.Append($"markup'{f.MarkupContent}' "); break;
            }
        }
        if (sb.Length > 0) Frames.Add(sb.ToString().Trim());
        return Task.CompletedTask;
    }

    public Task RenderAsync(Type t)
    {
        var id = AssignRootComponentId(InstantiateComponent(t));
        return Dispatcher.InvokeAsync(() => RenderRootComponentAsync(id));
    }

    public Task ClickAsync(ulong h) =>
        Dispatcher.InvokeAsync(() => DispatchEventAsync(h, new EventFieldInfo(), new MouseEventArgs()));
}

record Witness(string Id, string What, Type Root, int Clicks, bool ExpectCaught, bool ExpectEscaped);

static class Oracle
{
    static readonly Witness[] Witnesses =
    [
        new("W1", "a throw from an event handler the PARENT owns, written inside the boundary",
            typeof(W1ParentHandler), 1, ExpectCaught: false, ExpectEscaped: true),
        new("W2", "a throw from a CHILD COMPONENT's event handler",
            typeof(W2ChildHandler), 1, ExpectCaught: true, ExpectEscaped: false),
        new("W3", "a throw from a CHILD COMPONENT's OnInitialized",
            typeof(W3ChildInit), 0, ExpectCaught: true, ExpectEscaped: false),
        new("W4", "a throw raised while the parent EVALUATES the content",
            typeof(W4ParentRender), 0, ExpectCaught: true, ExpectEscaped: false),
        new("W5", "the same on RE-RENDER, with markup outside the boundary still live",
            typeof(W5Rerender), 3, ExpectCaught: true, ExpectEscaped: false),
        new("W6", "no ErrorContent: Blazor's own default error UI",
            typeof(W6Default), 0, ExpectCaught: false, ExpectEscaped: false),
    ];

    static async Task<(bool Ok, object Row)> Run(Witness w)
    {
        Probe.Log.Clear();
        RecordingBoundaryLogger.Calls = 0;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IErrorBoundaryLogger, RecordingBoundaryLogger>();
        var sp = services.BuildServiceProvider();
        using var r = new OracleRenderer(sp, sp.GetRequiredService<ILoggerFactory>());

        try { await r.RenderAsync(w.Root); }
        catch (Exception e) { Probe.Say($"threw-out-of-render:{e.GetType().Name}"); }

        for (var c = 0; c < w.Clicks && r.Handlers.Count > 0; c++)
        {
            try { await r.ClickAsync(r.Handlers[0]); }
            catch (Exception e) { Probe.Say($"threw-out-of-dispatch:{e.GetType().Name}"); }
        }

        var caught = Probe.Log.Any(l => l.StartsWith("error-content", StringComparison.Ordinal));
        var escaped = r.Escaped.Count > 0;
        var ok = caught == w.ExpectCaught && escaped == w.ExpectEscaped;

        // W5 and W6 carry a claim the caught/escaped pair cannot express, so each is checked on its
        // own evidence rather than being taken on trust.
        var extra = "";
        if (w.Id == "W5")
        {
            // STICKY: the FIRST exception is the one ErrorContent keeps showing, and it is logged once.
            var messages = Probe.Log.Where(l => l.StartsWith("error-content:", StringComparison.Ordinal))
                .Select(l => l["error-content:".Length..]).ToList();
            var sticky = messages.Count > 1 && messages.Distinct().Count() == 1;
            var loggedOnce = RecordingBoundaryLogger.Calls == 1;
            // the markup OUTSIDE the boundary went on updating: '1', '2', '3' arrived as text frames
            var outsideLive = r.Frames.Count(f => f is "'1'" or "'2'" or "'3'") == 3;
            ok = ok && sticky && loggedOnce && outsideLive;
            extra = $"sticky={sticky} loggedOnce={loggedOnce} outsideLive={outsideLive}";
        }
        if (w.Id == "W6")
        {
            var isDefaultUi = r.Frames.Any(f => f.Contains("[class=blazor-error-boundary]"));
            ok = ok && isDefaultUi && RecordingBoundaryLogger.Calls == 1;
            extra = $"defaultUi={isDefaultUi}";
        }

        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {w.Id}  {w.What}");
        Console.WriteLine($"         caught={caught} (expected {w.ExpectCaught})  "
                        + $"escaped={escaped} (expected {w.ExpectEscaped})  {extra}");

        return (ok, new
        {
            id = w.Id, what = w.What, caught, escaped,
            expectedCaught = w.ExpectCaught, expectedEscaped = w.ExpectEscaped, ok,
            log = Probe.Log.ToArray(),
        });
    }

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("error-boundary-oracle — what Blazor's <ErrorBoundary> catches (decision 164)");
        Console.WriteLine($"  framework: {typeof(ErrorBoundary).Assembly.GetName().Version}");
        Console.WriteLine();

        var rows = new List<object>();
        var failures = 0;
        foreach (var w in Witnesses)
        {
            var (ok, row) = await Run(w);
            rows.Add(row);
            if (!ok) failures++;
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? "VERDICT: every witness matches decision 164's record."
            : $"VERDICT: {failures} witness(es) DIVERGED. Blazor's behaviour has moved; decision 164's "
              + "mapping rests on these answers and must be re-argued, not patched.");

        if (args.Contains("--json"))
            Console.WriteLine(JsonSerializer.Serialize(new { failures, witnesses = rows },
                new JsonSerializerOptions { WriteIndented = true }));

        return failures == 0 ? 0 : 1;
    }
}
