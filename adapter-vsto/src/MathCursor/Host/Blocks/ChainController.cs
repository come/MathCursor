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

    /// <summary>Cible de fusion d'une ligne MARQUÉE (« = 2 »), décidée en UN
    /// seul endroit (<c>FindMergeTarget</c>) pour le commit ET l'aperçu.
    /// Cf. ADR 2026-10-14-Feat-chain-same-line-merge.</summary>
    internal enum MergeTargetKind
    {
        /// <summary>Rien à fusionner : équation autonome, marqueur rendu.</summary>
        None,
        /// <summary>Équation simple à nous juste à gauche sur le même ¶ → une seule formule.</summary>
        SameLine,
        /// <summary>Ligne seule sous une équation/chaîne à nous → bloc chaîne multiligne.</summary>
        Above,
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
        /// Commit d'une ligne MARQUÉE (« ⟺ x=3 », « = 2x »…) selon la cible de
        /// <see cref="FindMergeTarget"/> : <c>SameLine</c> → une seule formule
        /// avec l'équation de gauche ; <c>Above</c> → crée/étend le bloc chaîne ;
        /// <c>None</c> → équation autonome avec le marqueur RENDU (décision
        /// user : « si l'utilisateur l'a écrit il veut le voir »). Les deux
        /// fusions suivent la même recette : suppression de l'ancien puis
        /// insertion sur la zone tapée. Renvoie la cible appliquée.
        /// </summary>
        public MergeTargetKind CommitChainLine(Word.Document doc, ZoneSpan zone, RelationLineMatch match, string restLatex)
        {
            if (!zone.TryToInternal(doc, out int zStart, out int zEnd)) return MergeTargetKind.None;
            string typed = zone.Text.Trim();

            var (kind, om, source) = FindMergeTarget(doc, zStart);
            switch (kind)
            {
                case MergeTargetKind.SameLine:
                {
                    // Une formule : sténo/LaTeX de gauche + ligne tapée. Coupe au
                    // 1er caractère tapé (les blancs entre les deux partent avec
                    // l'ancienne OMath — sinon blanc résiduel en tête).
                    int omStart = om.Range.Start;
                    int cut = zStart + (zone.Text.Length - zone.Text.TrimStart().Length);
                    string steno = (source.Steno ?? "") + " " + typed;
                    string latex = (source.Latex ?? "") + match.MarkerLatex + restLatex;
                    _log($"chain: FUSION même ligne [{omStart},{zEnd})");
                    int removed = RemoveOldBlock(doc, omStart, cut);
                    _inserter.Insert(omStart, zEnd - removed, latex, steno);
                    return kind;
                }
                case MergeTargetKind.Above:
                {
                    string steno = (source.Steno ?? "") + "\n" + typed;
                    string latexJoined = (source.Latex ?? "") + "\n" + (restLatex ?? "");
                    var oMath = ChainComposer.ComposeChain(steno.Split('\n'), latexJoined.Split('\n'));

                    int repStart = ReplaceStart(doc, om);
                    _log($"chain: {(source.Type == null ? "CRÉATION" : "EXTENSION")} bloc [{repStart},{zEnd}) lignes={steno.Split('\n').Length}");
                    int removed = RemoveOldBlock(doc, repStart, zStart);
                    _inserter.InsertBlock(repStart, zEnd - removed, oMath, latexJoined, steno, BlockTypes.Chain);
                    return kind;
                }
                default:
                    _log($"chain: repli autonome \"{match.MarkerTyped} {restLatex}\"");
                    _inserter.Insert(zStart, zEnd, match.MarkerLatex + restLatex, typed);
                    return kind;
            }
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
        /// Sonde NON-mutante pour la popup : MÊME décision que le commit
        /// (<see cref="FindMergeTarget"/>). Renvoie la cible et le LaTeX par
        /// ligne de l'équation/du bloc visé (null si <c>None</c>).
        /// </summary>
        public (MergeTargetKind Kind, string[] LatexLines) ProbeMerge(Word.Document doc, ZoneSpan zone)
        {
            try
            {
                if (!zone.TryToInternal(doc, out int zStart, out _)) return (MergeTargetKind.None, null);
                var (kind, _, source) = FindMergeTarget(doc, zStart);
                return kind == MergeTargetKind.None
                    ? (kind, null)
                    : (kind, (source.Latex ?? "").Split('\n'));
            }
            catch { return (MergeTargetKind.None, null); }
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
        /// LA décision de fusion d'une ligne marquée (ADR 2026-10-14-Feat-
        /// chain-same-line-merge), partagée par commit et aperçu :
        /// <list type="bullet">
        /// <item>Contenu AVANT la zone dans le ¶ (ligne mixte) : <c>SameLine</c>
        ///   seulement si c'est une équation SIMPLE à nous (résolution
        ///   CONFIRMÉE, destructif) juste à gauche, blancs seuls entre ; sinon
        ///   <c>None</c>. Jamais de merge avec le ¶ du dessus depuis une ligne
        ///   mixte (retour user 2026-06-12).</item>
        /// <item>Ligne seule : <c>Above</c> si le ¶ du dessus porte une
        ///   équation/chaîne à nous, sinon <c>None</c>.</item>
        /// </list>
        /// Fusion destructive → le doute (probe KO) répond <c>None</c>,
        /// l'autonome est toujours bénin.
        /// </summary>
        private (MergeTargetKind Kind, Word.OMath Om, SourceMap.EquationSource Source) FindMergeTarget(
            Word.Document doc, int zStart)
        {
            var none = (MergeTargetKind.None, (Word.OMath)null, (SourceMap.EquationSource)null);
            try
            {
                int paraStart = doc.Range(zStart, zStart).Paragraphs[1].Range.Start;
                if (zStart > paraStart)
                {
                    var before = doc.Range(paraStart, zStart);
                    Word.OMath left = null;
                    foreach (Word.OMath o in before.OMaths) left = o;
                    if (left != null)
                    {
                        string gap = doc.Range(left.Range.End, zStart).Text ?? "";
                        var source = IsBlank(gap) ? _resolver.ResolveConfirmed(doc, left) : null;
                        if (source != null && source.Type == null)
                            return (MergeTargetKind.SameLine, left, source);
                        _log($"chain: OMath avant la zone dans le ¶ [{paraStart},{zStart}) non fusionnable → ligne mixte");
                        return none;
                    }
                    string lead = (before.Text ?? "").Replace("\r", "").Replace("\n", "").Trim();
                    if (lead.Length > 0)
                    {
                        _log($"chain: prose avant la zone (\"{lead}\") → ligne mixte");
                        return none;
                    }
                }

                var above = FindOurEquationAbove(doc, zStart);
                if (above == null) return none;
                var (om, src) = above.Value;
                if (src.Type != null && src.Type != BlockTypes.Chain) return none;   // bloc d'un autre type
                return (MergeTargetKind.Above, om, src);
            }
            catch (Exception ex) { _log("chain: merge_target_error: " + ex.Message); return none; }
        }

        private static bool IsBlank(string s) =>
            s.Replace("​", "").Replace("\r", "").Replace("\n", "").Trim().Length == 0;

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
