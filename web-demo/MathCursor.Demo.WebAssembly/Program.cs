// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme Percin
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// Démo web MathCursor — bootstrap Blazor WASM minimal. Pas de composant Razor :
// l'UI est du HTML/JS statique (wwwroot/), le moteur est appelé via Bridge
// ([JSInvokable]). Tout tourne dans le navigateur, rien n'est envoyé nulle part.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
await builder.Build().RunAsync();
