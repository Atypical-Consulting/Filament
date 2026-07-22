using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RouteParams.Blazor;

// Like Routing.Blazor, this baseline needs a real Router: routing is the thing being measured.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

await builder.Build().RunAsync();
