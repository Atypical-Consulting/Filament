using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Compose.Blazor;

// Same minimal host as the other baselines: one screen, no Router, no HttpClient.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

await builder.Build().RunAsync();
