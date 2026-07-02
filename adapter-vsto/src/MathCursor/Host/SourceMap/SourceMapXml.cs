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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace MathCursor.Host.SourceMap
{
    /// <summary>
    /// (Dé)sérialisation de la map hash→source vers le XML de la
    /// CustomXMLPart (une part EST du XML : schéma natif, pas de
    /// JSON-dans-CDATA — pas de double échappement, part lisible dans le
    /// .docx). Porte aussi la politique de la map : upsert
    /// dernier-écrit-gagne sur (K1,K2) et éviction au cap par ParsedAt.
    ///
    /// Schéma v1 :
    /// <code>
    /// &lt;sourceMap xmlns="urn:mathcursor:source-map:v1" v="1"&gt;
    ///   &lt;eq k1=".." k2=".." type="chain" handleId=".." version=".." parsedAt="ISO8601"&gt;
    ///     &lt;steno&gt;…&lt;/steno&gt;&lt;latex&gt;…&lt;/latex&gt;
    ///   &lt;/eq&gt;
    /// &lt;/sourceMap&gt;
    /// </code>
    ///
    /// Pur (pas de Word interop) : testé en xUnit.
    /// </summary>
    internal static class SourceMapXml
    {
        /// <summary>Namespace de la part — sert au SelectByNamespace du store.</summary>
        public const string Ns = "urn:mathcursor:source-map:v1";

        /// <summary>Cap d'entrées : au-delà, éviction des plus anciennes
        /// (ParsedAt). Pas de GC des entrées mortes (équations supprimées) —
        /// quelques centaines d'octets inoffensifs chacune (acté ADR).</summary>
        public const int Cap = 500;

        private static readonly XNamespace X = Ns;

        public static string Serialize(IList<EquationSource> entries)
        {
            var root = new XElement(X + "sourceMap", new XAttribute("v", 1));
            if (entries != null)
                foreach (var e in entries)
                {
                    var eq = new XElement(X + "eq",
                        new XAttribute("k1", e.K1 ?? string.Empty),
                        new XAttribute("k2", e.K2 ?? string.Empty));
                    if (!string.IsNullOrEmpty(e.Type)) eq.Add(new XAttribute("type", e.Type));
                    if (!string.IsNullOrEmpty(e.HandleId)) eq.Add(new XAttribute("handleId", e.HandleId));
                    if (!string.IsNullOrEmpty(e.Version)) eq.Add(new XAttribute("version", e.Version));
                    eq.Add(new XAttribute("parsedAt",
                        e.ParsedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)));
                    eq.Add(new XElement(X + "steno", e.Steno ?? string.Empty));
                    eq.Add(new XElement(X + "latex", e.Latex ?? string.Empty));
                    root.Add(eq);
                }
            return new XDocument(root).ToString(SaveOptions.DisableFormatting);
        }

        /// <summary>Liste vide si XML invalide ou schéma inconnu (la part
        /// repartira de zéro au prochain Record — dégradation douce).</summary>
        public static List<EquationSource> Deserialize(string xml)
        {
            var outp = new List<EquationSource>();
            if (string.IsNullOrEmpty(xml)) return outp;
            XDocument doc;
            try { doc = XDocument.Parse(xml); }
            catch (Exception) { return outp; }
            var root = doc.Root;
            if (root == null || root.Name != X + "sourceMap") return outp;

            foreach (var eq in root.Elements(X + "eq"))
            {
                var e = new EquationSource
                {
                    K1 = (string)eq.Attribute("k1") ?? string.Empty,
                    K2 = (string)eq.Attribute("k2") ?? string.Empty,
                    Type = (string)eq.Attribute("type"),
                    HandleId = (string)eq.Attribute("handleId"),
                    Version = (string)eq.Attribute("version"),
                    Steno = (string)eq.Element(X + "steno") ?? string.Empty,
                    Latex = (string)eq.Element(X + "latex") ?? string.Empty,
                };
                string at = (string)eq.Attribute("parsedAt");
                DateTime parsed;
                if (DateTime.TryParse(at, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out parsed))
                    e.ParsedAt = parsed;
                outp.Add(e);
            }
            return outp;
        }

        /// <summary>
        /// Upsert dernier-écrit-gagne : une entrée existante de même (K1,K2)
        /// est REMPLACÉE (deux équations identiques dans le doc partagent
        /// l'entrée — acté ADR). Puis éviction des plus anciennes (ParsedAt)
        /// au-delà de <see cref="Cap"/>.
        /// </summary>
        public static void Upsert(List<EquationSource> entries, EquationSource e)
        {
            if (entries == null || e == null) return;
            entries.RemoveAll(x => x.K1 == e.K1 && x.K2 == e.K2);
            entries.Add(e);
            if (entries.Count > Cap)
            {
                var evict = entries.OrderBy(x => x.ParsedAt)
                                   .Take(entries.Count - Cap).ToList();
                foreach (var v in evict) entries.Remove(v);
            }
        }
    }
}
