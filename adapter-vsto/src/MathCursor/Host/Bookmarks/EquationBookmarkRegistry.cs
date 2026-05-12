using System;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Bookmarks
{
    /// <summary>
    /// Registre des bookmarks Word qui taggent nos OMaths : pour chaque
    /// équation que nous avons commitée dans le doc, un bookmark
    /// <c>mcEq_&lt;handleId&gt;</c> couvre son range. Sert à :
    /// <list type="bullet">
    /// <item>Identifier qu'un OMath est "à nous" (vs équation Word native),
    /// pour activer le mode édition au caret.</item>
    /// <item>Retrouver le handle (= clé store) à partir d'un OMath donné.</item>
    /// <item>Garantir que les positions de l'équation suivent les édits du
    /// doc (bookmarks Word sont position-tracked par Word).</item>
    /// </list>
    ///
    /// <para>Le préfixe <see cref="Prefix"/> isole nos bookmarks de ceux
    /// créés par l'utilisateur ou d'autres add-ins.</para>
    ///
    /// <para>P2.8 du refactor archi (continuité de l'ADR
    /// <c>2026-05-12-Refactor-pure-merger-atomic-insert</c>) : extrait des
    /// méthodes privées de <c>SuggestionService</c>, regroupé sous un
    /// concept DDD propre (Equation Bookmark = liaison position ↔ identité).</para>
    /// </summary>
    internal sealed class EquationBookmarkRegistry
    {
        public const string Prefix = "mcEq_";

        private readonly Func<Word.Document> _getActiveDoc;
        private readonly Action<string> _diagLog;

        public EquationBookmarkRegistry(Func<Word.Document> getActiveDoc, Action<string> diagLog = null)
        {
            _getActiveDoc = getActiveDoc ?? throw new ArgumentNullException(nameof(getActiveDoc));
            _diagLog = diagLog ?? (s => { });
        }

        /// <summary>
        /// Crée le bookmark <c>mcEq_&lt;handleId&gt;</c> sur [absStart, absEnd].
        /// Écrase un bookmark de même nom s'il existait. Silencieux sur
        /// erreur Word (log diag).
        /// </summary>
        public void Create(string handleId, int absStart, int absEnd)
        {
            try
            {
                var doc = _getActiveDoc();
                if (doc == null) return;
                string name = Prefix + handleId;
                var range = doc.Range(absStart, absEnd);
                if (doc.Bookmarks.Exists(name)) doc.Bookmarks[name].Delete();
                doc.Bookmarks.Add(name, range);
            }
            catch (Exception ex) { _diagLog("bookmark_create_error: " + ex.Message); }
        }

        /// <summary>
        /// Supprime le bookmark <c>mcEq_&lt;handleId&gt;</c> s'il existe.
        /// Critique au merge : sans ça le bookmark fantôme reste et un futur
        /// <see cref="FindHandleForOMath"/> retrouve un handle déjà absorbé.
        /// </summary>
        public void Delete(string handleId)
        {
            try
            {
                var doc = _getActiveDoc();
                if (doc == null) return;
                string name = Prefix + handleId;
                if (doc.Bookmarks.Exists(name))
                {
                    doc.Bookmarks[name].Delete();
                    _diagLog($"bookmark deleted: {name}");
                }
            }
            catch (Exception ex) { _diagLog("bookmark_delete_error: " + ex.Message); }
        }

        /// <summary>
        /// Retourne le handleId du bookmark <c>mcEq_*</c> qui couvre
        /// <paramref name="om"/>, ou <c>null</c> si l'OMath n'est pas à nous
        /// (équation Word native, OMath issu d'un import .docx tiers, etc.).
        ///
        /// <para>Tolérance 1 char en fin pour absorber un éventuel espace
        /// trailing inclus dans le bookmark au moment de la création.</para>
        /// </summary>
        public string FindHandleForOMath(Word.OMath om)
        {
            try
            {
                var doc = _getActiveDoc();
                if (doc == null) return null;
                int omStart = om.Range.Start;
                int omEnd = om.Range.End;
                foreach (Word.Bookmark bm in doc.Bookmarks)
                {
                    if (!bm.Name.StartsWith(Prefix, StringComparison.Ordinal)) continue;
                    var r = bm.Range;
                    if (r.Start <= omStart && r.End >= omEnd - 1)
                        return bm.Name.Substring(Prefix.Length);
                }
            }
            catch { }
            return null;
        }
    }
}
