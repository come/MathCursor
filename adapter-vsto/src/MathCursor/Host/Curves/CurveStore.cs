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

using System;
using Office = Microsoft.Office.Core;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Curves
{
    /// <summary>
    /// Specs des images de courbes dans <c>Document.CustomXMLParts</c> : une part
    /// par image, retrouvée par namespace puis par id (le titre de l'image porte
    /// l'id). Même recette que <see cref="SourceMap.SourceMapStore"/> : pas de
    /// mise à jour en place, delete + add. ADR 2026-10-31-Feat-insert-curves.
    /// </summary>
    internal static class CurveStore
    {
        public static CurveSpec Load(Word.Document doc, string id, Action<string> log)
        {
            var part = Find(doc, id, log);
            if (part == null) return null;
            try { return CurveSpec.FromXml(part.XML); }
            catch (Exception ex) { log("curve: lecture part KO: " + ex.Message); return null; }
        }

        public static void Save(Word.Document doc, CurveSpec spec, Action<string> log)
        {
            try
            {
                Find(doc, spec.Id, log)?.Delete();
                doc.CustomXMLParts.Add(spec.ToXml());
            }
            catch (Exception ex) { log("curve: écriture part KO: " + ex.Message); }
        }

        private static Office.CustomXMLPart Find(Word.Document doc, string id, Action<string> log)
        {
            if (doc == null || string.IsNullOrEmpty(id)) return null;
            try
            {
                foreach (Office.CustomXMLPart p in doc.CustomXMLParts.SelectByNamespace(CurveSpec.Ns))
                {
                    var spec = CurveSpec.FromXml(p.XML);
                    if (spec != null && spec.Id == id) return p;
                }
            }
            catch (Exception ex) { log("curve: SelectByNamespace KO: " + ex.Message); }
            return null;
        }
    }
}
