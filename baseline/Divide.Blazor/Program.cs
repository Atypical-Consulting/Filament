using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Divide.Blazor;

// Same minimal host as Counter.Blazor: no Router (one screen), no HeadOutlet
// (static title), no HttpClient (no HTTP calls).
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

await builder.Build().RunAsync();
