using System;
using System.IO;
using System.Linq;
using MathCursor.Host.CCMeta;
using MathCursor.Host.Detection;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Blocks
{
    /// <summary>Types de bloc (valeur du Tag <see cref="MCMeta.Type"/>).</summary>
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
        private readonly Action<string> _log;

        public ChainController(Word.Application app, OMathInserter inserter, Action<string> log = null)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _inserter = inserter ?? throw new ArgumentNullException(nameof(inserter));
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
            if (above == null || (above.Value.Meta.Type != null && above.Value.Meta.Type != BlockTypes.Chain))
            {
                // Repli autonome (rien à nous au-dessus, ou bloc d'un autre type).
                _log($"chain: repli autonome \"{match.MarkerTyped} {restLatex}\"");
                _inserter.Insert(zStart, zEnd, match.MarkerLatex + restLatex, zone.Text.Trim());
                return true;
            }

            var (om, cc, meta) = above.Value;
            string steno = (meta.Steno ?? "") + "\n" + zone.Text.Trim();
            string latexJoined = (meta.Latex ?? "") + "\n" + (restLatex ?? "");
            var oMath = ChainComposer.ComposeChain(steno.Split('\n'), latexJoined.Split('\n'));

            int repStart = ReplaceStart(doc, cc, om);
            _log($"chain: {(meta.Type == null ? "CRÉATION" : "EXTENSION")} bloc [{repStart},{zEnd}) lignes={steno.Split('\n').Length}");
            int removed = RemoveOldBlock(doc, cc, om, repStart, zStart);
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
        public bool CommitSystemLine(Word.Document doc, ZoneSpan zone, string restLatex)
        {
            if (!zone.TryToInternal(doc, out int zStart, out int zEnd)) return false;

            var above = FindOurEquationAbove(doc, zStart);
            if (above != null && above.Value.Meta.Type == BlockTypes.System)
            {
                var (om, cc, meta) = above.Value;
                string steno = (meta.Steno ?? "") + "\n" + zone.Text.Trim();
                string latexJoined = (meta.Latex ?? "") + "\n" + (restLatex ?? "");
                var oMathExt = ChainComposer.ComposeSystem(latexJoined.Split('\n'));

                int repStart = ReplaceStart(doc, cc, om);
                _log($"system: EXTENSION, total={steno.Split('\n').Length}");
                int removed = RemoveOldBlock(doc, cc, om, repStart, zStart);
                _inserter.InsertBlock(repStart, zEnd - removed, oMathExt, latexJoined, steno, BlockTypes.System);
                return true;
            }

            var oMath = ChainComposer.ComposeSystem(new[] { restLatex ?? "" });
            _log("system: CRÉATION (1 ligne)");
            var (s, e, h) = _inserter.InsertBlock(zStart, zEnd, oMath, restLatex ?? "", zone.Text.Trim(), BlockTypes.System);
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
                var above = FindOurEquationAbove(doc, zStart);
                if (above == null) return null;
                var meta = above.Value.Meta;
                var lines = (meta.Latex ?? "").Split('\n');
                return (meta.Type ?? "", lines);
            }
            catch { return null; }
        }

        // ── Internals ────────────────────────────────────────────────────

        /// <summary>
        /// Supprime l'ancien bloc (anchor CC + ZWSP + OMath + marque de ¶,
        /// = tout <c>[repStart, zoneStart)</c>) par la recette SÉLECTION
        /// validée (word-api-helpers §7 revert, hygiène H1) : <c>sel.SetRange
        /// + sel.Delete()</c> puis <c>cc.Delete(false)</c> pour le wrapper
        /// fantôme. PAS <c>cc.Delete(true)</c> : observé no-op silencieux sur
        /// nos anchors (boucle ZoneCleaner ×20, shift=0) — le ¶ de l'ancien
        /// bloc survivait et le bloc descendait d'une ligne à chaque merge
        /// (retour user 2026-06-10). Renvoie le nombre de positions
        /// réellement supprimées (mesuré via <c>doc.Content.End</c>).
        /// </summary>
        private int RemoveOldBlock(Word.Document doc, Word.ContentControl cc, Word.OMath om,
            int repStart, int zoneStart)
        {
            if (zoneStart <= repStart) return 0;
            try { cc.LockContents = false; } catch { }
            try { cc.LockContentControl = false; } catch { }

            // DÉMASQUER avant de supprimer : le ZWSP anchor est en
            // Font.Hidden et Word REFUSE silencieusement de supprimer du
            // texte caché quand « afficher le texte masqué » est décoché —
            // c'est ce qui faisait no-oper cc.Delete(true) (boucle ×20) puis
            // survivre 2 chars au sel.Delete (removed=22/24 au log, le ¶ de
            // l'ancien bloc restait → ligne fantôme par merge).
            try { doc.Range(repStart, zoneStart).Font.Hidden = 0; } catch { }

            int before = doc.Content.End;
            try
            {
                var sel = _app.Selection;
                sel.SetRange(repStart, zoneStart);
                sel.Delete();
            }
            catch (Exception ex) { _log("chain: remove_old_block_error: " + ex.Message); }
            try { cc.Delete(false); } catch { } // wrapper fantôme éventuel
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

        /// <summary>L'équation/le bloc À NOUS porté par le ¶ qui précède
        /// IMMÉDIATEMENT le ¶ de la zone (adjacence stricte). Null sinon.</summary>
        private (Word.OMath Om, Word.ContentControl Cc, MCMeta Meta)? FindOurEquationAbove(
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

                var (cc, meta) = CcMetaResolver.ResolveAt(last);
                if (cc == null || meta == null) return null;
                return (last, cc, meta);
            }
            catch (Exception ex) { _log("chain_probe_above_error: " + ex.Message); return null; }
        }

        /// <summary>
        /// Début de la plage à remplacer : le DÉBUT DU ¶ du bloc — pas
        /// <c>cc.Range.Start</c>. Preuve paras-diag 2026-06-10 : la frontière
        /// structurelle du sdt vit À <c>paraStart</c>, AVANT
        /// <c>cc.Range.Start</c> ; en supprimant depuis cc.Start, Word garde
        /// un ¶ vide squelette <c>[paraStart, paraStart+1)</c> au-dessus
        /// (« paras 2→2 » malgré la marque de ¶ dans la plage) → le bloc
        /// descendait d'une ligne à chaque merge. Garde-fou : si du contenu
        /// VISIBLE précède l'anchor dans le ¶ (équation inline dans de la
        /// prose), on revient à cc.Start pour ne pas manger la prose.
        /// </summary>
        private int ReplaceStart(Word.Document doc, Word.ContentControl cc, Word.OMath om)
        {
            int ccStart;
            try { ccStart = cc.Range.Start; }
            catch { ccStart = om.Range.Start; }
            try
            {
                int paraStart = om.Range.Paragraphs[1].Range.Start;
                if (paraStart >= ccStart) return ccStart;
                string lead = (doc.Range(paraStart, ccStart).Text ?? "")
                    .Replace("​", "").Replace("\r", "").Replace("\n", "").Trim();
                if (lead.Length == 0) return paraStart; // ¶ entier à nous
                _log($"chain: contenu visible avant l'anchor (\"{lead}\") → remplacement depuis cc.Start");
            }
            catch (Exception ex) { _log("chain: replace_start_probe_error: " + ex.Message); }
            return ccStart;
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
