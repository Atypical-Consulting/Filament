using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Counter.Blazor;

// Dropped from the template on purpose:
//  - HeadOutlet: nothing sets <PageTitle>; the title is static in index.html.
//  - HttpClient registration: this app makes no HTTP calls, so registering it
//    would only drag System.Net.Http into the trimmed output for nothing.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

await builder.Build().RunAsync();
