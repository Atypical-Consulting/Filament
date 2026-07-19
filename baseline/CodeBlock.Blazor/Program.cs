using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using CodeBlock.Blazor;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

await builder.Build().RunAsync();
