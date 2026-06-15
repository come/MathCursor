using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// Démo web MathCursor — bootstrap Blazor WASM minimal. Pas de composant Razor :
// l'UI est du HTML/JS statique (wwwroot/), le moteur est appelé via Bridge
// ([JSInvokable]). Tout tourne dans le navigateur, rien n'est envoyé nulle part.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
await builder.Build().RunAsync();
