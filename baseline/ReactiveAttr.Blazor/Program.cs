using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ReactiveAttr.Blazor;

// Same minimal host as Counter.Blazor/Divide.Blazor: no Router (one screen), no
// HeadOutlet (static title), no HttpClient (no HTTP calls).
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

await builder.Build().RunAsync();
