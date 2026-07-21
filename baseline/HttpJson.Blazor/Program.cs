using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using HttpJson.Blazor;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

// The standard Blazor WASM registration: an HttpClient whose BaseAddress is the host, so relative
// URLs resolve against the app's own origin -- EXACTLY what fetch(url) does with a relative URL.
// That identity is the erasure's honesty (decision 147).
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Build().RunAsync();
