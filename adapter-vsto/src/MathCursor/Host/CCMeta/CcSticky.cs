using System;
using System.IO;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.CCMeta
{
    /// <summary>
    /// Helper pour échapper le caret de la « sticky zone » d'un ContentControl.
    ///
    /// <para>Comportement Word : quand le caret est à <c>cc.Range.End</c>
    /// (= juste après le contenu, avant l'end-marker invisible du CC),
    /// Word le considère toujours « dans » le CC. Les frappes ultérieures
    /// (Enter, texte, paste) sont alors absorbées par le CC qui auto-grow
    /// pour les engloutir.</para>
    ///
    /// <para>Bug reproductible 2026-05-18 : commit f(x) au ¶1, Enter, commit
    /// g(x) au ¶2 → le CC de f(x) absorbe le paragraph mark + g(x). Revert
    /// f(x) supprime tout. Le fix amont = sortir le caret avant que le user
    /// tape la suite.</para>
    ///
    /// <para>Stratégie : poser le caret à <c>cc.Range.End + 1</c> (= un cran
    /// au-delà de la sticky zone). Si cette position n'existe pas (CC en
    /// toute fin de doc), on insère un espace pour avoir une position cible
    /// valide.</para>
    /// </summary>
    internal static class CcSticky
    {
        /// <summary>
        /// Place le caret juste après le CC, hors de sa sticky zone.
        /// Best-effort : silencieux sur erreur Word interop.
        /// </summary>
        public static void EscapeCaretAfter(Word.Application app, Word.ContentControl cc)
        {
            if (app == null || cc == null) return;
            try
            {
                var doc = app.ActiveDocument;
                if (doc == null) return;

                int ccEnd = cc.Range.End;
                int docEnd = doc.Content.End;
                int target = ccEnd + 1;
                int ccEndBeforeSpace = ccEnd;
                bool spaceInserted = false;

                if (target >= docEnd)
                {
                    doc.Range(ccEnd, ccEnd).InsertAfter(" ");
                    target = cc.Range.End + 1;
                    spaceInserted = true;
                }

                int ccEndAfter = cc.Range.End;
                int docEndAfter = doc.Content.End;
                app.Selection.SetRange(target, target);
                int selStart = app.Selection.Start;

                LogDiag($"CcSticky: ccEndBefore={ccEndBeforeSpace} docEndBefore={docEnd} "
                    + $"spaceInserted={spaceInserted} ccEndAfter={ccEndAfter} "
                    + $"docEndAfter={docEndAfter} target={target} sel.Start={selStart}");
            }
            catch
            {
                /* best-effort, jamais propager */
            }
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
                    $"{DateTime.UtcNow:o} sticky {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
