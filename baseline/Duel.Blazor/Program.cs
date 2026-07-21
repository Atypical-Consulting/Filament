using Duel.Blazor;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// The Duel is an APP, not a feature witness: routed pages, a form, a keyed list with
// per-row handlers, LINQ. Like Routing.Blazor it needs the Router -- that is part of
// what an app-level comparison must pay for on the Blazor side.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

await builder.Build().RunAsync();
