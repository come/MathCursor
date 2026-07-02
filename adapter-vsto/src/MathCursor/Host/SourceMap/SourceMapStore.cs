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
using System.Linq;
using Office = Microsoft.Office.Core;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.SourceMap
{
    /// <summary>
    /// Store de la map hash→source dans <c>Document.CustomXMLParts</c>
    /// (ADR 2026-06-11-Feat-hash-source-map-no-cc). Une part unique par
    /// document, retrouvée par namespace (<see cref="SourceMapXml.Ns"/>).
    ///
    /// La part n'a pas de remplacement in-place complet dans l'API → recette
    /// delete + re-add (l'Id change à chaque écriture). Cache mémoire
    /// write-through keyé par <c>part.Id</c> : un Id de part stable = cache
    /// valide, sans dépendre de l'identité RCW de l'objet Document.
    ///
    /// Politique de la map (upsert dernier-écrit-gagne, éviction au cap) :
    /// déléguée à <see cref="SourceMapXml"/> (pur, testé).
    /// </summary>
    internal sealed class SourceMapStore
    {
        private readonly Action<string> _log;

        // Cache du contenu de la part — invalidé si l'Id ne matche plus
        // (autre doc actif, part réécrite par une autre instance, reload).
        private string _cachedPartId;
        private List<EquationSource> _cache;

        public SourceMapStore(Action<string> log = null)
        {
            _log = log ?? (_ => { });
        }

        /// <summary>K1 d'une OMath : SHA1 du texte linéaire (cheap, ~1 COM call).</summary>
        public static string ComputeK1(Word.OMath om)
        {
            try { return Sha1Helper.Compute(om?.Range?.Text ?? ""); }
            catch (Exception) { return string.Empty; }
        }

        /// <summary>K2 d'une OMath : SHA1 de l'OMML canonique (chère : lit
        /// WordOpenXML ~60 ms). Null si pas de m:oMath lisible.</summary>
        public static string ComputeK2(Word.OMath om)
        {
            try
            {
                string canonical = OmmlCanonicalizer.Canonicalize(om?.Range?.WordOpenXML);
                return canonical == null ? null : Sha1Helper.Compute(canonical);
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Enregistre (upsert) la source d'une OMath fraîchement insérée.
        /// À appeler POST-SETTLE (après om.Type/Justification) — sinon
        /// store-hash ≠ read-hash. Recalcule K1+K2 ici.
        /// </summary>
        public EquationSource Record(Word.Document doc, Word.OMath om,
            string steno, string latex, string blockType, string handleId)
        {
            if (doc == null || om == null) return null;
            string k2 = ComputeK2(om);
            if (string.IsNullOrEmpty(k2)) { _log("sourcemap: record K2 introuvable, skip"); return null; }
            var e = new EquationSource
            {
                K1 = ComputeK1(om),
                K2 = k2,
                Steno = steno ?? "",
                Latex = latex ?? "",
                Type = blockType,
                HandleId = handleId,
                Version = typeof(SourceMapStore).Assembly.GetName().Version?.ToString() ?? "0",
                ParsedAt = DateTime.UtcNow,
            };
            var entries = Load(doc);
            SourceMapXml.Upsert(entries, e);
            Save(doc, entries);
            return e;
        }

        /// <summary>Entrées dont K1 matche l'OMath (0..n). N'engage que le
        /// hash cheap — confirmer par <see cref="Confirm"/> avant toute
        /// opération destructive (revert) ou en cas d'ambiguïté.</summary>
        public List<EquationSource> LookupCheap(Word.Document doc, Word.OMath om)
        {
            string k1 = ComputeK1(om);
            if (string.IsNullOrEmpty(k1)) return new List<EquationSource>();
            return Load(doc).Where(x => x.K1 == k1).ToList();
        }

        /// <summary>Départage par K2 (calculée UNE fois ici). Null si aucune
        /// entrée ne matche — l'OMath n'est plus « à nous » (éditée à la main,
        /// lien perdu : acté ADR).</summary>
        public EquationSource Confirm(Word.OMath om, List<EquationSource> candidates)
        {
            if (candidates == null || candidates.Count == 0) return null;
            string k2 = ComputeK2(om);
            if (string.IsNullOrEmpty(k2)) return null;
            return candidates.FirstOrDefault(x => x.K2 == k2);
        }

        /// <summary>Contenu de la part (cache si Id stable). Liste vide si pas
        /// de part ou part illisible (dégradation douce).</summary>
        public List<EquationSource> Load(Word.Document doc)
        {
            var part = FindPart(doc);
            if (part == null) { _cachedPartId = null; _cache = null; return new List<EquationSource>(); }

            string id = SafePartId(part);
            if (_cache != null && id != null && id == _cachedPartId)
                return new List<EquationSource>(_cache);

            string xml = null;
            try { xml = part.XML; }
            catch (Exception ex) { _log("sourcemap: lecture part KO: " + ex.Message); }
            var entries = SourceMapXml.Deserialize(xml);
            _cachedPartId = id;
            _cache = new List<EquationSource>(entries);
            return entries;
        }

        /// <summary>Réécrit la part (delete + re-add) et met le cache à jour.</summary>
        public void Save(Word.Document doc, List<EquationSource> entries)
        {
            if (doc == null) return;
            string xml = SourceMapXml.Serialize(entries);
            try
            {
                var old = FindPart(doc);
                if (old != null) old.Delete();
                var fresh = doc.CustomXMLParts.Add(xml);
                _cachedPartId = SafePartId(fresh);
                _cache = new List<EquationSource>(entries);
            }
            catch (Exception ex)
            {
                _log("sourcemap: écriture part KO: " + ex.Message);
                _cachedPartId = null;
                _cache = null;
            }
        }

        private Office.CustomXMLPart FindPart(Word.Document doc)
        {
            try
            {
                var parts = doc.CustomXMLParts.SelectByNamespace(SourceMapXml.Ns);
                foreach (Office.CustomXMLPart p in parts) return p;
            }
            catch (Exception ex) { _log("sourcemap: SelectByNamespace KO: " + ex.Message); }
            return null;
        }

        private static string SafePartId(Office.CustomXMLPart part)
        {
            try { return part?.Id; }
            catch (Exception) { return null; }
        }
    }
}
