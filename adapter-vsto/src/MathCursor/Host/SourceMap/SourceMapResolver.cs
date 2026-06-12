using System;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.SourceMap
{
    /// <summary>
    /// Résolveur central équation → source, remplaçant de <c>CcMetaResolver</c>
    /// (ADR 2026-06-11-Feat-hash-source-map-no-cc) : plus d'anchor CC, la
    /// correspondance vit dans la map CustomXMLParts, indexée par le CONTENU
    /// de l'OMath (bi-clé K1 cheap / K2 canonique).
    ///
    /// <para>Coûts : <see cref="ResolveAt"/> et <see cref="IsOurs"/> ne
    /// paient que K1 (~1 lecture Range.Text) au cas nominal ; K2 (~60 ms,
    /// WordOpenXML) n'est calculée que sur ambiguïté K1. Le REVERT (seule
    /// opération destructive) passe par <see cref="ResolveConfirmed"/> qui
    /// confirme TOUJOURS par K2 — un faux positif K1 à l'affichage est
    /// bénin, un revert sur la mauvaise source ne l'est pas.</para>
    ///
    /// <para>Équation éditée à la main = hash changé = lookup miss : l'OMath
    /// n'est plus « à nous » (acté ADR — la source ne correspond plus).</para>
    /// </summary>
    internal sealed class SourceMapResolver
    {
        private readonly SourceMapStore _store;

        public SourceMapResolver(SourceMapStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>Source de l'OMath, pour AFFICHAGE (popup edit) : K1 seul
        /// au cas nominal, K2 en départage d'ambiguïté. Null = pas à nous.</summary>
        public EquationSource ResolveAt(Word.Document doc, Word.OMath om)
        {
            if (doc == null || om == null) return null;
            var cands = _store.LookupCheap(doc, om);
            if (cands.Count == 0) return null;
            if (cands.Count == 1) return cands[0];
            return _store.Confirm(om, cands);
        }

        /// <summary>Source CONFIRMÉE par K2 — obligatoire avant toute
        /// opération destructive (revert). Null = pas à nous.</summary>
        public EquationSource ResolveConfirmed(Word.Document doc, Word.OMath om)
        {
            if (doc == null || om == null) return null;
            var cands = _store.LookupCheap(doc, om);
            if (cands.Count == 0) return null;
            return _store.Confirm(om, cands);
        }

        /// <summary>Vrai si l'OMath a une source en map (K1 cheap).</summary>
        public bool IsOurs(Word.Document doc, Word.OMath om)
            => doc != null && om != null && _store.LookupCheap(doc, om).Count > 0;

        /// <summary>
        /// Probe locale : OMath collée juste avant le caret. Retourne
        /// <c>(om, source)</c> — <c>source</c> null si l'OMath n'est pas à
        /// nous. <c>(null, null)</c> si pas d'OMath derrière le caret.
        /// Filtre <c>StoryType == wdMainTextStory</c> (ignore headers/notes).
        /// </summary>
        public (Word.OMath om, EquationSource source) ResolveBehindCaret(Word.Document doc, Word.Selection sel)
        {
            if (doc == null || sel == null) return (null, null);
            try
            {
                if (sel.StoryType != Word.WdStoryType.wdMainTextStory) return (null, null);
                int caret = sel.Range.Start;
                if (caret <= 0) return (null, null);

                var probe = doc.Range(caret - 1, caret);
                if (probe.OMaths.Count == 0) return (null, null);

                Word.OMath om = null;
                foreach (Word.OMath o in probe.OMaths) { om = o; break; }
                if (om == null) return (null, null);

                // Garde : OMath collée pile derrière le caret (pas un
                // chevauchement lointain). Tolérance ±1 char (wrappers Word).
                int omEnd = om.Range.End;
                if (omEnd != caret && omEnd != caret - 1) return (om, null);

                return (om, ResolveAt(doc, om));
            }
            catch { return (null, null); }
        }
    }
}
