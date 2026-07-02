// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
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
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace MathCursor.Host.SourceMap
{
    /// <summary>
    /// Projection CANONIQUE d'un <c>WordOpenXML</c> d'OMath, pour servir de
    /// base à la clé K2 (ADR 2026-06-11-Feat-hash-source-map-no-cc).
    ///
    /// Word mute le XML brut post-commit (rsid, re-split des runs, proofErr,
    /// formatage) — cf. word-api-helpers §5 « drift hash WARN-only ». On ne
    /// hash donc JAMAIS le XML brut : on en extrait une projection
    /// structurelle stable :
    ///
    /// - seuls les sous-arbres <c>m:oMath</c> comptent (l'oMathPara et son
    ///   <c>m:jc</c> sont du LAYOUT, pas de l'identité) ;
    /// - éléments hors namespace m: supprimés (w:rPr, w:proofErr, w:rsid…) ;
    /// - <c>m:rPr</c> et <c>m:ctrlPr</c> supprimés (formatage de run/contrôle,
    ///   porteurs de w:rPr) — les AUTRES *Pr (m:dPr, m:naryPr, m:fPr…) sont
    ///   GARDÉS : ils portent la sémantique (begChr, limLoc, type de barre) ;
    /// - attributs : seuls ceux du namespace m: survivent (m:val) ;
    /// - runs <c>m:r</c> adjacents fusionnés (absorbe le re-split au
    ///   save/reopen), leurs <c>m:t</c> concaténés ;
    /// - sérialisation sans indentation.
    ///
    /// XLinq pur (MC0001 : jamais de regex sur du XML). Aucune dépendance
    /// Word : testable en xUnit avec des dumps de WordOpenXML réels.
    /// </summary>
    internal static class OmmlCanonicalizer
    {
        private static readonly XNamespace M =
            "http://schemas.openxmlformats.org/officeDocument/2006/math";

        /// <summary>
        /// Forme canonique des m:oMath contenus dans <paramref name="wordOpenXml"/>
        /// (concaténés dans l'ordre du document), ou null si XML invalide ou
        /// sans m:oMath. L'appelant hash le résultat (K2 = Sha1Helper.Compute).
        /// </summary>
        public static string Canonicalize(string wordOpenXml)
        {
            if (string.IsNullOrEmpty(wordOpenXml)) return null;
            XDocument doc;
            try { doc = XDocument.Parse(wordOpenXml); }
            catch (Exception) { return null; }

            var omaths = doc.Descendants(M + "oMath")
                .Where(e => !e.Ancestors(M + "oMath").Any())   // pas de double compte si imbriqué
                .Select(CanonicalElement)
                .Where(e => e != null)
                .ToList();
            if (omaths.Count == 0) return null;

            return string.Concat(omaths.Select(e => e.ToString(SaveOptions.DisableFormatting)));
        }

        // Copie récursive filtrée d'un élément m: (cf. règles en tête de classe).
        private static XElement CanonicalElement(XElement src)
        {
            if (src.Name.Namespace != M) return null;
            string local = src.Name.LocalName;
            if (local == "rPr" || local == "ctrlPr") return null;

            var dst = new XElement(src.Name);
            foreach (var a in src.Attributes())
                if (a.Name.Namespace == M)
                    dst.Add(new XAttribute(a.Name, a.Value));

            // Texte : seul m:t porte du texte signifiant.
            if (local == "t")
            {
                dst.Add(src.Value);
                return dst;
            }

            var children = new List<XElement>();
            foreach (var child in src.Elements())
            {
                var c = CanonicalElement(child);
                if (c != null) children.Add(c);
            }

            // Un run = UN m:t (un m:r natif peut porter plusieurs m:t ; la
            // fusion de runs adjacents ci-dessous produit aussi un t unique —
            // même forme partout, sinon le hash divergerait).
            if (local == "r")
            {
                string own = string.Concat(children.Where(c => c.Name == M + "t").Select(c => c.Value));
                children.RemoveAll(c => c.Name == M + "t");
                children.Add(new XElement(M + "t", own));
            }

            // Fusion des m:r adjacents : ne garde qu'un run, m:t concaténés.
            var merged = new List<XElement>();
            foreach (var c in children)
            {
                XElement prev = merged.Count > 0 ? merged[merged.Count - 1] : null;
                if (c.Name == M + "r" && prev != null && prev.Name == M + "r")
                {
                    string text = string.Concat(prev.Elements(M + "t").Select(t => t.Value))
                                + string.Concat(c.Elements(M + "t").Select(t => t.Value));
                    prev.Elements(M + "t").Remove();
                    prev.Add(new XElement(M + "t", text));
                }
                else merged.Add(c);
            }
            foreach (var c in merged) dst.Add(c);

            // Un *Pr VIDÉ par le filtrage (il ne portait que du ctrlPr/rPr,
            // ex. m:sSupPr) est lui-même du bruit : Word l'ajoute ou non selon
            // l'historique du run. Les *Pr porteurs de sémantique (m:dPr avec
            // begChr…) gardent leurs enfants et survivent.
            if (local.EndsWith("Pr", StringComparison.Ordinal)
                && !dst.HasElements && !dst.HasAttributes)
                return null;
            return dst;
        }
    }
}
