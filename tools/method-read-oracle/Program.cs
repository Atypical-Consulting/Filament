using System.Text.Json;
using Bunit;
using MethodReadOracle;

// method-read-oracle -- what BLAZOR renders, before and after one #inc click, for a field read only
// through a template-called method (#n) and a directly-read control field (#d). Register A14 / S10.
//
//     dotnet run --project tools/method-read-oracle
//
// Renders App.razor through bUnit's real ComponentBase + Renderer, reads the two spans, clicks #inc
// (the real event dispatch that runs the handler and StateHasChanged), and re-reads them. Prints the
// four texts as JSON. The Filament side (observe-filament.mjs) drives the SAME App.razor's emitted
// module in happy-dom identically; the two JSON blobs must be byte-identical.

using var ctx = new BunitContext();
var cut = ctx.Render<App>();

string Text(string id) => cut.Find("#" + id).TextContent.Trim();

var nBefore = Text("n");
var dBefore = Text("d");

cut.Find("#inc").Click();

var observed = new
{
    n_before = nBefore,
    d_before = dBefore,
    n_after = Text("n"),
    d_after = Text("d"),
};

Console.WriteLine(JsonSerializer.Serialize(observed));
return 0;
