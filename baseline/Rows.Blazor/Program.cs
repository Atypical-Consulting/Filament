using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Rows.Blazor;

// Single-page app: no Router, no layout, no HeadOutlet, no HttpClient.
// RowsApp is mounted directly as the root component. This is stock, documented
// Blazor hosting API - nothing here is a framework hack, it is just what you
// write when the app has exactly one screen and no navigation.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<RowsApp>("#app");

await builder.Build().RunAsync();
