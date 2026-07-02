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
using System.IO;
using System.Linq;
using MathCursor.Host.Detection;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Blocks
{
    /// <summary>Types de bloc (valeur de <c>EquationSource.Type</c> en map).</summary>
    internal static class BlockTypes
    {
        public const string Chain = "chain";
        public const string System = "system";
    }

    /// <summary>
    /// Exécuteur Word des blocs multilignes (M2, ADR 2026-06-10-Feat-
    /// multiline-chain-eqarr-architecture). Toute opération suit le même
    /// chemin : lire les sources/LaTeX du Tag → ajouter la ligne →
    /// <see cref="ChainComposer"/> re-compose le bloc ENTIER →
    /// <see cref="OMathInserter.InsertBlock"/> remplace l'ancien
    /// (ZoneCleaner nettoie CC + OMath + marque de ¶ + ligne tapée — les
    /// deux ¶ fusionnent). Jamais de chirurgie d'OMath.
    ///
    /// Adjacence stricte : le ¶ PRÉCÉDANT immédiatement la ligne committée
    /// doit porter l'équation/le bloc — une ligne vide casse la chaîne
    /// (décision user : « double entrée nickel c'est naturel »).
    /// </summary>
    internal sealed class ChainController
    {
        private readonly Word.Application _app;
        private readonly OMathInserter _inserter;
        private readonly SourceMap.SourceMapResolver _resolver;
        private readonly Action<string> _log;

        public ChainController(Word.Application app, OMathInserter inserter, Action<string> log = null)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _inserter = inserter ?? throw new ArgumentNullException(nameof(inserter));
            _resolver = new SourceMap.SourceMapResolver(inserter.SourceMap);
            _log = log ?? LogDiag;
        }

        /// <summary>
        /// Commit d'une ligne MARQUÉE (« ⟺ x=3 », « = 2x »…). Crée ou étend
        /// le bloc chaîne si le ¶ au-dessus porte une équation/chaîne à nous ;
        /// sinon repli : équation autonome avec le marqueur RENDU (décision
        /// user : « si l'utilisateur l'a écrit il veut le voir »).
        /// </summary>
        public bool CommitChainLine(Word.Document doc, ZoneSpan zone, RelationLineMatch match, string restLatex)
        {
            if (!zone.TryToInternal(doc, out int zStart, out int zEnd)) return false;

            var above = FindOurEquationAbove(doc, zStart);
            if (!IsAloneOnItsLine(doc, zStart)) above = null;   // ligne mixte → repli autonome
            if (above == null || (above.Value.Source.Type != null && above.Value.Source.Type != BlockTypes.Chain))
            {
                // Repli autonome (rien à nous au-dessus, ou bloc d'un autre type).
                _log($"chain: repli autonome \"{match.MarkerTyped} {restLatex}\"");
                _inserter.Insert(zStart, zEnd, match.MarkerLatex + restLatex, zone.Text.Trim());
                return true;
            }

            var (om, source) = above.Value;
            string steno = (source.Steno ?? "") + "\n" + zone.Text.Trim();
            string latexJoined = (source.Latex ?? "") + "\n" + (restLatex ?? "");
            var oMath = ChainComposer.ComposeChain(steno.Split('\n'), latexJoined.Split('\n'));

            int repStart = ReplaceStart(doc, om);
            _log($"chain: {(source.Type == null ? "CRÉATION" : "EXTENSION")} bloc [{repStart},{zEnd}) lignes={steno.Split('\n').Length}");
            int removed = RemoveOldBlock(doc, repStart, zStart);
            var (s, e, h) = _inserter.InsertBlock(repStart, zEnd - removed, oMath, latexJoined, steno, BlockTypes.Chain);
            return h != null || s != e;
        }

        /// <summary>
        /// Commit d'une ligne « { … » : si le ¶ au-dessus est un bloc
        /// SYSTÈME à nous → EXTENSION (+1 ligne, accolade qui grandit) ;
        /// sinon CRÉATION d'un système 1 ligne. Règle user 2026-06-10 :
        /// « il faut un { sur la ligne courante ET un { sur la ligne du
        /// dessus pour merger » — plus d'absorption des lignes nues.
        /// </summary>
        /// <summary>SYSTÈME (modèle matrice, ADR 2026-06-29) : insère le bloc DÉJÀ
        /// COMPOSÉ (préfixe + toutes les lignes) sur la zone entière, EN UN COUP —
        /// plus d'incrémental (create-or-extend), plus de probe-au-dessus. Calqué
        /// sur l'ancien chemin CRÉATION (mêmes bornes zone + InsertBlock walker).</summary>
        public bool CommitSystem(Word.Document doc, ZoneSpan zone,
            System.Xml.Linq.XElement oMath, string latexJoined, string steno)
        {
            if (!zone.TryToInternal(doc, out int zStart, out int zEnd)) return false;
            _log($"system: composition matrice (un coup) [{zStart},{zEnd})");
            var (s, e, h) = _inserter.InsertBlock(zStart, zEnd, oMath, latexJoined ?? "", steno ?? "", BlockTypes.System);
            return h != null || s != e;
        }

        /// <summary>
        /// Sonde NON-mutante pour la popup : le ¶ au-dessus de la zone
        /// porte-t-il une cible de merge ? Renvoie (type, latex par ligne)
        /// — type "" pour une équation simple à nous, lignes lues du Tag.
        /// Null si rien à merger.
        /// </summary>
        public (string Type, string[] LatexLines)? ProbeMergeAbove(Word.Document doc, ZoneSpan zone)
        {
            try
            {
                if (!zone.TryToInternal(doc, out int zStart, out _)) return null;
                if (!IsAloneOnItsLine(doc, zStart)) return null;   // aperçu cohérent avec le commit
                var above = FindOurEquationAbove(doc, zStart);
                if (above == null) return null;
                var source = above.Value.Source;
                var lines = (source.Latex ?? "").Split('\n');
                return (source.Type ?? "", lines);
            }
            catch { return null; }
        }

        // ── Internals ────────────────────────────────────────────────────

        /// <summary>
        /// Supprime l'ancien bloc (OMath + marque de ¶, = tout
        /// <c>[repStart, zoneStart)</c>) par la recette SÉLECTION validée :
        /// <c>sel.SetRange + sel.Delete()</c>. Plus de CC ni de ZWSP caché
        /// (ADR hash-source-map) — les pathologies « cc.Delete no-op » et
        /// « texte masqué insupprimable » sont caduques par construction ;
        /// les diagnostics removed=N/N sont GARDÉS (preuve paras-diag).
        /// L'entrée map de l'ancien bloc devient une entrée morte (politique
        /// ADR : pas de GC, cap+éviction). Renvoie le nombre de positions
        /// réellement supprimées (mesuré via <c>doc.Content.End</c>).
        /// </summary>
        private int RemoveOldBlock(Word.Document doc, int repStart, int zoneStart)
        {
            if (zoneStart <= repStart) return 0;

            int before = doc.Content.End;
            try
            {
                var sel = _app.Selection;
                sel.SetRange(repStart, zoneStart);
                sel.Delete();
            }
            catch (Exception ex) { _log("chain: remove_old_block_error: " + ex.Message); }
            int removed = before - doc.Content.End;
            int expected = zoneStart - repStart;
            _log($"chain: ancien bloc supprimé [{repStart},{zoneStart}) removed={removed}/{expected}");

            // Diagnostic : si des positions survivent encore, logger LEURS
            // CODES — c'est la donnée qui manque pour identifier le résidu.
            if (removed < expected)
            {
                try
                {
                    string s = doc.Range(repStart, Math.Min(doc.Content.End, repStart + (expected - removed) + 2)).Text ?? "";
                    var codes = string.Join(",", s.Select(ch => ((int)ch).ToString("X4")));
                    _log($"chain: résidu non supprimé, codes=[{codes}]");
                }
                catch { }
            }
            return removed;
        }

        // NB : le strip « ¶ résiduel au-dessus » porté de DocMath a été
        // remplacé par l'anti-split DANS OMathInserter.InsertCore (étape 9) :
        // le ¶ vide vient du SPLIT à l'InsertXML (promotion oMathPara des
        // eqArr), pas d'un résidu de suppression — preuve docx sautdeligne
        // 2026-06-10 (suppression amont removed=N/N parfaite, et pourtant
        // un <w:p> vide sans run au-dessus du bloc).

        /// <summary>
        /// Le marqueur de chaîne ne FUSIONNE que depuis une ligne AUTONOME
        /// (retour user 2026-06-12) : de la prose ou une OMath AVANT la zone
        /// dans le MÊME ¶ = ligne mixte → repli autonome (marqueur rendu).
        /// La fusion étant destructive (l'ancien bloc est remplacé), le
        /// doute (probe KO) répond FAUX — l'autonome est toujours bénin.
        /// </summary>
        private bool IsAloneOnItsLine(Word.Document doc, int zStart)
        {
            try
            {
                var para = doc.Range(zStart, zStart).Paragraphs[1].Range;
                int paraStart = para.Start;
                if (zStart <= paraStart) return true;
                var before = doc.Range(paraStart, zStart);
                if (before.OMaths.Count > 0)
                {
                    _log($"chain: OMath avant la zone dans le ¶ [{paraStart},{zStart}) → ligne mixte");
                    return false;
                }
                string lead = (before.Text ?? "").Replace("\r", "").Replace("\n", "").Trim();
                if (lead.Length > 0)
                {
                    _log($"chain: prose avant la zone (\"{lead}\") → ligne mixte");
                    return false;
                }
                return true;
            }
            catch (Exception ex) { _log("chain: alone_probe_error: " + ex.Message); return false; }
        }

        /// <summary>L'équation/le bloc À NOUS porté par le ¶ qui précède
        /// IMMÉDIATEMENT le ¶ de la zone (adjacence stricte). Identification
        /// par la map (resolver bi-clé). Null sinon.</summary>
        private (Word.OMath Om, SourceMap.EquationSource Source)? FindOurEquationAbove(
            Word.Document doc, int zoneStart)
        {
            try
            {
                var para = doc.Range(zoneStart, zoneStart).Paragraphs[1];
                int paraStart = para.Range.Start;
                if (paraStart - 1 <= doc.Content.Start) return null;

                var prevPara = doc.Range(paraStart - 1, paraStart - 1).Paragraphs[1];
                Word.OMath last = null;
                foreach (Word.OMath o in prevPara.Range.OMaths) last = o;
                if (last == null) return null;

                var source = _resolver.ResolveAt(doc, last);
                if (source == null) return null;
                return (last, source);
            }
            catch (Exception ex) { _log("chain_probe_above_error: " + ex.Message); return null; }
        }

        /// <summary>
        /// Début de la plage à remplacer : le DÉBUT DU ¶ du bloc si rien de
        /// visible ne précède l'OMath (¶ entier à nous — évite le ¶ squelette,
        /// preuve paras-diag 2026-06-10), sinon <c>om.Range.Start</c> (équation
        /// inline dans de la prose : ne pas manger la prose).
        /// </summary>
        private int ReplaceStart(Word.Document doc, Word.OMath om)
        {
            int omStart;
            try { omStart = om.Range.Start; } catch { return 0; }
            try
            {
                int paraStart = om.Range.Paragraphs[1].Range.Start;
                if (paraStart >= omStart) return omStart;
                string lead = (doc.Range(paraStart, omStart).Text ?? "")
                    .Replace("​", "").Replace("\r", "").Replace("\n", "").Trim();
                if (lead.Length == 0) return paraStart; // ¶ entier à nous
                _log($"chain: contenu visible avant le bloc (\"{lead}\") → remplacement depuis om.Start");
            }
            catch (Exception ex) { _log("chain: replace_start_probe_error: " + ex.Message); }
            return omStart;
        }

        private static void LogDiag(string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "mathcursor.log"),
                    $"{DateTime.UtcNow:o} chain {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
