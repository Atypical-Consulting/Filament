// The playground host's entry point does nothing but keep the runtime alive: every capability is a
// [JSExport] on PlaygroundApi, called from the page. The generator pipeline it exposes is the
// UNCHANGED Filament.Generator -- same parse, same gates, same emitter (decision 144).
await Task.Delay(Timeout.Infinite);
