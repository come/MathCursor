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
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Mode physique-chimie PAR DOCUMENT (ADR 2026-10-16-Feat-physics-chemistry-mode) : mémorisé
    /// dans une variable de document (Document.Variables), donc enregistré dans
    /// le .docx et invisible. Absente ou illisible = désactivé.
    /// </summary>
    internal static class DocumentMode
    {
        private const string PhysChemVariable = "MathCursor.PhysChem";

        /// <summary>Unités désactivées pour ce document (ADR 2026-11-02-Feat-units-toggle).
        /// On mémorise l'EXCEPTION : variable absente = unités activées (défaut coché),
        /// donc aucun document existant ne change de comportement.</summary>
        private const string UnitsOffVariable = "MathCursor.UnitsOff";

        public static bool IsPhysChem(Word.Document doc)
        {
            if (doc == null) return false;
            try
            {
                foreach (Word.Variable v in doc.Variables)
                    if (v.Name == PhysChemVariable) return v.Value == "1";
            }
            catch { }
            return false;
        }

        public static void SetPhysChem(Word.Document doc, bool physChem)
        {
            if (doc == null) return;
            foreach (Word.Variable v in doc.Variables)
                if (v.Name == PhysChemVariable)
                {
                    if (physChem) v.Value = "1"; else v.Delete();
                    return;
                }
            if (physChem) doc.Variables.Add(PhysChemVariable, "1");
        }

        /// <summary>Reconnaissance des unités voulue pour ce document (vrai par défaut).</summary>
        public static bool IsUnits(Word.Document doc)
        {
            if (doc == null) return true;
            try
            {
                foreach (Word.Variable v in doc.Variables)
                    if (v.Name == UnitsOffVariable) return v.Value != "1";
            }
            catch { }
            return true;
        }

        public static void SetUnits(Word.Document doc, bool units)
        {
            if (doc == null) return;
            foreach (Word.Variable v in doc.Variables)
                if (v.Name == UnitsOffVariable)
                {
                    if (units) v.Delete(); else v.Value = "1";
                    return;
                }
            if (!units) doc.Variables.Add(UnitsOffVariable, "1");
        }

        /// <summary>Réglage « Unités » du document actif (vrai sans document). Le moteur
        /// les force de toute façon en mode physique-chimie (EngineCulture.UnitsActive).</summary>
        public static bool IsUnitsActive(Word.Application app)
        {
            try { return app == null || app.Documents.Count == 0 || IsUnits(app.ActiveDocument); }
            catch { return true; }
        }

        /// <summary>Mode physique-chimie du document actif (faux sans document).</summary>
        public static bool IsPhysChemActive(Word.Application app)
        {
            try { return app != null && app.Documents.Count > 0 && IsPhysChem(app.ActiveDocument); }
            catch { return false; }
        }
    }
}
