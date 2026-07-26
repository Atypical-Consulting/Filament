using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TextFormatOracle;

// text-format-oracle -- what BLAZOR renders a type-directed value as in TEXT position (the S9 slice;
// register A13/A15).
//
//     dotnet run --project tools/text-format-oracle [-- --raw]
//
// Renders App.razor through the real HtmlRenderer (invariant culture, set in the csproj) and prints
// the text of the four measured spans as JSON. The Filament side (observe-filament.mjs) renders the
// SAME App.razor's emitted module in happy-dom; the two JSON blobs must be byte-identical.
//
// WHY NOT PLAYWRIGHT, like the weight slices. Because it is not installable in this environment (the
// reserve BENCH n°69 / decision 164 disclosed, and the reason tools/error-boundary-oracle exists),
// and because it is not needed here: what C# renders true/0.1f as is decided by Boolean.ToString /
// Single.ToString on the BCL, walked by the real Renderer -- the SAME code a WASM app runs.

var services = new ServiceCollection();
services.AddLogging();
var provider = services.BuildServiceProvider();
var loggerFactory = provider.GetRequiredService<ILoggerFactory>();

await using var htmlRenderer = new HtmlRenderer(provider, loggerFactory);

var html = await htmlRenderer.Dispatcher.InvokeAsync(async () =>
{
    var output = await htmlRenderer.RenderComponentAsync<App>();
    return output.ToHtmlString();
});

if (args.Contains("--raw"))
{
    Console.WriteLine(html);
    return 0;
}

// Blazor sprinkles `<!--!-->` markers through its output; strip them so the id->text extraction sees
// only real nodes.
var clean = Regex.Replace(html, "<!--.*?-->", "", RegexOptions.Singleline);

// A directly-rendered span holds its text as immediate content: <span id="flag_t">True</span>.
string Direct(string id)
{
    var m = Regex.Match(clean, $"id=\"{id}\"[^>]*>(?<t>[^<]*)<");
    return m.Success ? m.Groups["t"].Value.Trim() : $"<MISSING {id}>";
}

// A generic child inlines its own <span> inside the wrapper: <span id="gen_a"><span>0.1</span></span>.
string Boxed(string id)
{
    var m = Regex.Match(clean, $"id=\"{id}\"[^>]*>\\s*<span[^>]*>(?<t>[^<]*)<");
    return m.Success ? m.Groups["t"].Value.Trim() : $"<MISSING {id}>";
}

var observed = new
{
    flag_t = Direct("flag_t"),
    flag_f = Direct("flag_f"),
    gen_a = Boxed("gen_a"),
    gen_b = Boxed("gen_b"),
};

Console.WriteLine(JsonSerializer.Serialize(observed));
return 0;
