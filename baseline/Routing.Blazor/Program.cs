using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Routing.Blazor;

// This baseline DOES need a Router, unlike every other one -- routing is the thing being measured.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

await builder.Build().RunAsync();
