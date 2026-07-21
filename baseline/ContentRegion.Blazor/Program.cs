using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ContentRegion.Blazor;

// Same minimal host as Counter.Blazor: one screen, no Router/HeadOutlet/HttpClient.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

await builder.Build().RunAsync();
