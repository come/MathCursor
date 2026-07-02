// MathCursor: capturing mathematical intent from linear keyboard input.
// Copyright (C) 2026  Côme de Percin
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

// Types pour le port JS réutilisé tel quel (parité verrouillée côté web-demo).
declare module '*/spancomputer.js' {
  interface Zone { start: number; end: number; }
  interface SpanComputerApi {
    computeZone(
      text: string,
      caret: number,
      omathRegions: Array<{ start: number; end: number }> | null,
      delims?: Set<string>
    ): Zone;
    SPAN_DELIMITERS: Set<string>;
    DEMO_DELIMITERS: Set<string>;
  }
  const api: SpanComputerApi;
  export default api;
}
