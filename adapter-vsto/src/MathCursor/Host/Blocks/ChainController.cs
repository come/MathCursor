using System;
using System.IO;
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

            int repStart = ReplaceStart(cc, om);
            _log($"chain: {(meta.Type == null ? "CRÉATION" : "EXTENSION")} bloc [{repStart},{zEnd}) lignes={steno.Split('\n').Length}");
            var (s, e, h) = _inserter.InsertBlock(repStart, zEnd, oMath, latexJoined, steno, BlockTypes.Chain);
            return h != null || s != e;
        }

        /// <summary>Commit d'une ligne « { … » : bloc SYSTÈME d'une ligne
        /// (accolade ouvrante, fermante invisible).</summary>
        public bool CommitSystemOpener(Word.Document doc, ZoneSpan zone, string restLatex)
        {
            if (!zone.TryToInternal(doc, out int zStart, out int zEnd)) return false;
            var oMath = ChainComposer.ComposeSystem(new[] { restLatex ?? "" });
            _log("system: CRÉATION (1 ligne)");
            var (s, e, h) = _inserter.InsertBlock(zStart, zEnd, oMath, restLatex ?? "", zone.Text.Trim(), BlockTypes.System);
            return h != null || s != e;
        }

        /// <summary>
        /// Commit d'une ligne SANS marqueur : si le ¶ au-dessus est un bloc
        /// SYSTÈME à nous → la ligne y est absorbée (+1 ligne, accolade qui
        /// grandit). False = pas un système au-dessus, commit normal.
        /// </summary>
        public bool TryAbsorbIntoSystemAbove(Word.Document doc, ZoneSpan zone, string chosenLatex)
        {
            if (!zone.TryToInternal(doc, out int zStart, out int zEnd)) return false;

            var above = FindOurEquationAbove(doc, zStart);
            if (above == null || above.Value.Meta.Type != BlockTypes.System) return false;

            var (om, cc, meta) = above.Value;
            string steno = (meta.Steno ?? "") + "\n" + zone.Text.Trim();
            string latexJoined = (meta.Latex ?? "") + "\n" + (chosenLatex ?? "");
            var oMath = ChainComposer.ComposeSystem(latexJoined.Split('\n'));

            int repStart = ReplaceStart(cc, om);
            _log($"system: ABSORPTION ligne, total={steno.Split('\n').Length}");
            _inserter.InsertBlock(repStart, zEnd, oMath, latexJoined, steno, BlockTypes.System);
            return true;
        }

        // ── Internals ────────────────────────────────────────────────────

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

        /// <summary>Début de la plage à remplacer : l'anchor CC du bloc (ou
        /// l'OMath si la CC est illisible).</summary>
        private static int ReplaceStart(Word.ContentControl cc, Word.OMath om)
        {
            try { return cc.Range.Start; }
            catch { return om.Range.Start; }
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
