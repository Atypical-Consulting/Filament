// Dev-only static host: serves wwwroot (index.html + the JS Filament emitted at build).
// This Kestrel host never ships to the browser -- the deployed artifact is the static
// files under wwwroot. "No .NET in the browser" holds; this is the Blazor-WASM dev model.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();   // "/" -> wwwroot/index.html
app.UseStaticFiles();    // App.g.js, filament.js, css served with correct MIME types

app.Run();
