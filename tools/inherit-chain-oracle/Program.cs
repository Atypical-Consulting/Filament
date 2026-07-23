using System.Text.Json;
using Bunit;
using InheritChainOracle;

// inherit-chain-oracle -- what BLAZOR renders, initially and after two #inc clicks, for a field and a
// method declared THREE levels up an @inherits chain (App : Mid : Grand). Register A4 / S8.
//
//     dotnet run --project tools/inherit-chain-oracle
//
// Renders App.razor through bUnit's real ComponentBase + Renderer, reads #out, clicks #inc twice (the
// real event dispatch that runs the inherited handler and StateHasChanged), re-reading each time.
// Prints the three texts as JSON. The Filament side (observe-filament.mjs) drives the SAME App.razor's
// emitted module in happy-dom identically; the two JSON blobs must be byte-identical.

using var ctx = new BunitContext();
var cut = ctx.Render<App>();

string Out() => cut.Find("#out").TextContent.Trim();

var initial = Out();
cut.Find("#inc").Click();
var afterFirst = Out();
cut.Find("#inc").Click();
var afterSecond = Out();

var observed = new
{
    initial,
    afterFirst,
    afterSecond,
};

Console.WriteLine(JsonSerializer.Serialize(observed));
return 0;
