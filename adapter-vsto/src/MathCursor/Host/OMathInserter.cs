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
using MathCursor.Host.SourceMap;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Insertion d'UNE équation OMath dans Word — pipeline « propre » de
    /// l'expérience hash-source-map (ADR 2026-06-11 + amendement 2026-06-12) :
    ///
    /// <list type="number">
    /// <item>Pré-validation <see cref="OmmlToOMathBuilder.IsSupported"/> :
    ///   pas de repli — un arbre inconstructible est un bug de whitelist
    ///   détecté par le test de couverture corpus, l'insertion s'abstient
    ///   (la sténo reste intacte).</item>
    /// <item>Normalisation des bornes en positions internes Word
    ///   (<c>SetRange</c> + readback — Word snap silencieusement).</item>
    /// <item><see cref="ZoneCleaner.ClearZone"/> : vide structurellement la
    ///   plage (OMaths à re-committer, CCs étrangères, plain text).</item>
    /// <item><see cref="OmmlToOMathBuilder"/> : construction native
    ///   (OMaths.Add sur seed + Functions.Add, AUCUN InsertXML) — le record
    ///   undo reste intact : 1 Ctrl+Z = tout le commit.</item>
    /// <item><see cref="DecideOMathTyping"/> : Display si seule dans son
    ///   contexte, Inline sinon ; alignée à gauche. Blocs : Display forcé,
    ///   pas de Justification (acquis eqArr).</item>
    /// <item>Échappement caret <c>MoveRight</c> (clôt la saisie math).</item>
    /// <item>Source en MAP différée : <see cref="FlushPendingRecord"/> à
    ///   appeler APRÈS la fermeture du UndoRecordScope (la lecture
    ///   WordOpenXML du Record ferme le record custom — mesuré 2026-06-12 ;
    ///   la map n'est pas annulable de toute façon).</item>
    /// </list>
    ///
    /// Plus de ZWSP, plus de ContentControl anchor, plus de Tag JSON, plus
    /// d'InsertXML — zéro caractère caché dans le document.
    /// </summary>
    internal sealed class OMathInserter
    {
        private readonly Word.Application _app;
        private readonly Action<string> _log;
        private readonly SourceMapStore _sourceMap;

        // Record différé (posé par InsertCore, consommé par FlushPendingRecord).
        private (int omStart, int omEnd, string steno, string latex, string blockType, string handle)? _pending;

        public OMathInserter(Word.Application app, Action<string> log = null)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _log = log ?? (_ => { });
            _sourceMap = new SourceMapStore(_log);
        }

        /// <summary>Store partagé (resolvers edit-mode / chaînes).</summary>
        public SourceMapStore SourceMap => _sourceMap;

        /// <summary>Génère un handle id unique (logging/events).</summary>
        public static string NewHandleId() => "eq_" + Guid.NewGuid().ToString("N").Substring(0, 12);

        /// <summary>
        /// Remplace <c>[absStart, absEnd)</c> par l'OMath rendant
        /// <paramref name="latex"/>. La source est enregistrée en map au
        /// prochain <see cref="FlushPendingRecord"/>. Retourne les bornes de
        /// l'OMath inséré + son handle (null si échec).
        /// </summary>
        public (int newStart, int newEnd, string newHandle) Insert(
            int absStart, int absEnd, string latex, string source)
        {
            System.Xml.Linq.XElement oMathEl;
            try { oMathEl = MathCursor.Serialization.LatexToOmml.Convert(latex); }
            catch (Exception ex) { _log("insert_l2o_error: " + ex.Message); return (absStart, absEnd, null); }
            return InsertCore(absStart, absEnd, oMathEl, latex, source, blockType: null);
        }

        /// <summary>
        /// Variante BLOC (chaînes/systèmes) : l'OMML est PRÉ-CONSTRUIT par
        /// <c>ChainComposer</c>. <paramref name="latexJoined"/> et
        /// <paramref name="sourceJoined"/> = LaTeX/sources par ligne joints
        /// par '\n' ; <paramref name="blockType"/> = "chain" | "system".
        /// </summary>
        public (int newStart, int newEnd, string newHandle) InsertBlock(
            int absStart, int absEnd, System.Xml.Linq.XElement oMath,
            string latexJoined, string sourceJoined, string blockType)
            => InsertCore(absStart, absEnd, oMath, latexJoined, sourceJoined, blockType);

        /// <summary>
        /// Écrit la source en map — à appeler APRÈS la fermeture du
        /// UndoRecordScope du commit (lecture WordOpenXML = tueur de record).
        /// No-op s'il n'y a rien en attente.
        /// </summary>
        public void FlushPendingRecord()
        {
            if (_pending == null) return;
            var p = _pending.Value;
            _pending = null;
            try
            {
                var doc = _app.ActiveDocument;
                if (doc == null) return;
                Word.OMath om = null;
                int probeEnd = Math.Min(doc.Content.End, p.omEnd + 1);
                foreach (Word.OMath o in doc.Range(Math.Max(0, p.omStart - 1), probeEnd).OMaths) { om = o; break; }
                if (om == null) { _log("record_flush: OMath introuvable au re-probe, source non enregistrée"); try { _app.StatusBar = Strings.SourceNotRecorded; } catch { } return; }
                _sourceMap.Record(doc, om, p.steno, p.latex, p.blockType, p.handle);
            }
            catch (Exception ex) { _log("record_flush_error: " + ex.Message); try { _app.StatusBar = Strings.SourceNotRecorded; } catch { } }
        }

        private (int newStart, int newEnd, string newHandle) InsertCore(
            int absStart, int absEnd, System.Xml.Linq.XElement oMathEl,
            string latex, string source, string blockType)
        {
            var doc = _app.ActiveDocument;
            _log($"OMathInserter: IN [{absStart},{absEnd}) latex=\"{Preview(latex)}\"");
            if (doc == null) return (absStart, absEnd, null);

            // 0. Pré-validation — pas de repli (amendement ADR 2026-06-12) :
            //    inconstructible = bug de couverture (test corpus), on
            //    S'ABSTIENT, la sténo de l'utilisateur reste intacte.
            if (!OmmlToOMathBuilder.IsSupported(oMathEl, out string why))
            {
                _log("insert_unsupported_BUG: " + why + " — couverture whitelist à corriger, insertion abandonnée");
                return (absStart, absEnd, null);
            }

            // Clamp + trim whitespaces aux bords.
            int docStart = doc.Content.Start, docEnd = doc.Content.End;
            if (absStart < docStart) absStart = docStart;
            if (absEnd > docEnd) absEnd = docEnd;
            if (absEnd <= absStart) return (absStart, absEnd, null);
            while (absStart < absEnd && IsWhitespaceCharAt(doc, absStart)) absStart++;
            while (absEnd > absStart && IsWhitespaceCharAt(doc, absEnd - 1)) absEnd--;
            if (absEnd <= absStart) return (absStart, absEnd, null);

            var sel = _app.Selection;
            if (sel == null) return (absStart, absEnd, null);

            // 1. Normalisation des bornes en positions internes Word.
            int internalStart, internalEnd;
            try
            {
                sel.SetRange(absStart, absStart);
                internalStart = sel.Start;
                sel.SetRange(absEnd, absEnd);
                internalEnd = sel.Start;
            }
            catch (Exception ex) { _log("insert_normalize_error: " + ex.Message); return (absStart, absEnd, null); }

            UndoRecordScope.Probe(_app, "avant ClearZone");

            // 2. Cleanup structurel (OMaths à re-committer + CCs étrangères + texte).
            int afterCleanupPos;
            try { afterCleanupPos = ZoneCleaner.ClearZone(doc, internalStart, internalEnd, _log); }
            catch (Exception ex) { _log("insert_clearzone_error: " + ex.Message); return (absStart, absEnd, null); }
            UndoRecordScope.Probe(_app, "après ClearZone");

            // 3. SetRange collapsed sur la position post-cleanup.
            try { sel.SetRange(afterCleanupPos, afterCleanupPos); }
            catch (Exception ex) { _log("insert_setrange_error: " + ex.Message); return (absStart, absEnd, null); }

            // 3b. Détection liste : Inline forcé (une display dans une puce
            //     casse la ligne de liste — acquis 2026-05-20).
            bool isInList = false;
            try
            {
                var listFormat = doc.Range(afterCleanupPos, afterCleanupPos).Paragraphs[1].Range.ListFormat;
                isInList = listFormat != null && listFormat.ListType != Word.WdListType.wdListNoNumbering;
            }
            catch (Exception exL) { _log("insert_list_probe_error: " + exL.Message); }

            // 4. Construction NATIVE par le walker (zéro InsertXML).
            Word.OMath om;
            try { om = OmmlToOMathBuilder.Build(doc, afterCleanupPos, oMathEl, _log); }
            catch (Exception ex) { _log("insert_walker_error: " + ex.Message); om = null; }
            UndoRecordScope.Probe(_app, "après walker Build");

            // ROLLBACK : le texte source a déjà été détruit par ZoneCleaner —
            // on le RETAPE pour ne jamais laisser le document mutilé.
            if (om == null)
            {
                try
                {
                    sel.SetRange(afterCleanupPos, afterCleanupPos);
                    sel.TypeText(source ?? "");
                    _log("OMathInserter: ROLLBACK — texte source restauré");
                }
                catch (Exception exRb) { _log("insert_rollback_error: " + exRb.Message); }
                return (afterCleanupPos, afterCleanupPos + (source?.Length ?? 0), null);
            }

            int newStart = om.Range.Start, newEnd = om.Range.End;

            // 5. Typage Display/Inline + alignement.
            if (blockType != null)
            {
                // BLOCS (chaîne ET système) : Display + alignement GAUCHE, uniforme.
                try
                {
                    if (om.Type != Word.WdOMathType.wdOMathDisplay)
                        om.Type = Word.WdOMathType.wdOMathDisplay;
                }
                catch (Exception exT) { _log("insert_block_display_error: " + exT.Message); }

                // Gauche pour TOUS les blocs (pas de cas par type). Le setter jette
                // sur un eqArr frais top-level (chaîne) → on l'avale, le défaut Word
                // y est DÉJÀ à gauche (acquis V5) ; il marche sur un <m:d> top-level
                // (système), que Word centrerait sinon.
                try { om.Justification = Word.WdOMathJc.wdOMathJcLeft; }
                catch (Exception exJc) { _log("insert_block_jc_error: " + exJc.Message); }
            }
            else
            {
                var (omType, omJc) = isInList
                    ? (Word.WdOMathType.wdOMathInline, Word.WdOMathJc.wdOMathJcLeft)
                    : DecideOMathTyping(om, source, _log);

                Word.WdOMathType currentType;
                try { currentType = om.Type; }
                catch { currentType = Word.WdOMathType.wdOMathInline; }
                if (currentType != omType)
                {
                    try { om.Type = omType; }
                    catch (Exception exType) { _log("insert_omath_type_error: " + exType.Message); }
                }
                // Justification : seulement en display (sans objet en inline,
                // et le setter y jette « Impossible de définir l'alignement »).
                if (omType == Word.WdOMathType.wdOMathDisplay)
                {
                    try { om.Justification = omJc; }
                    catch (Exception exJc) { _log("insert_omath_jc_error: " + exJc.Message); }
                }
            }

            // 5b. Police math (préréglage utilisateur) — cosmétique, JAMAIS
            //     bloquant pour le commit. Cf. ADR 2026-06-22-Feat-math-font-selector.
            try
            {
                var mathFont = Settings.SettingsStore.Current?.MathFont;
                if (!string.IsNullOrEmpty(mathFont))
                    MathFontApplier.ApplyToRange(om.Range, mathFont);
            }
            catch (Exception exF) { _log("insert_mathfont_error: " + exF.Message); }

            try { newStart = om.Range.Start; newEnd = om.Range.End; } catch { }

            // 6. Caret APRÈS l'OMath + MoveRight : franchit la frontière et
            //    CLÔT la saisie math (sinon frappe suivante en italique math).
            try
            {
                sel.SetRange(om.Range.End, om.Range.End);
                sel.MoveRight(Word.WdUnits.wdCharacter, 1, Word.WdMovementType.wdMove);

                // Post-condition LISTE (litest.docx 2026-06-12) : équation
                // dernier contenu de la puce → le MoveRight peut laisser le
                // caret sur la frontière INTÉRIEURE de la zone — Entrée y
                // PROLONGE alors la zone math sur la puce suivante (le « f( »
                // tapé naissait dans un oMath neuf). Re-saute tant que la
                // sélection rapporte une OMath (cap 3, hops loggés).
                // TODO(escape-liste) : FONCTIONNE (validé user 2026-06-12)
                // mais à réévaluer — une alternative à MoveRight (type
                // MoveEnd) avait été essayée dans cette version d'après
                // l'utilisateur ; retrouver ce banc et comparer avant de
                // considérer cette boucle comme définitive.
                if (isInList)
                {
                    int hops = 0;
                    while (hops < 3)
                    {
                        bool inMath = false;
                        try { inMath = sel.OMaths != null && sel.OMaths.Count > 0; } catch { }
                        if (!inMath) break;
                        sel.MoveRight(Word.WdUnits.wdCharacter, 1, Word.WdMovementType.wdMove);
                        hops++;
                    }
                    if (hops > 0) _log($"caret_escape_liste: +{hops} hop(s) pour sortir de la zone math");
                }
            }
            catch { }

            // 7. Source en map, DIFFÉRÉE post-scope undo.
            string newHandle = NewHandleId();
            _pending = (newStart, newEnd, source ?? "", latex ?? "", blockType, newHandle);

            UndoRecordScope.Probe(_app, "fin InsertCore (record différé)");
            _log($"OMathInserter: OUT range=[{newStart},{newEnd}) handle={newHandle}");
            return (newStart, newEnd, newHandle);
        }

        /// <summary>
        /// Display si l'OMath est seule dans son contexte structurel (¶ vide,
        /// cellule vide), Inline sinon ou si la source commence par un espace
        /// (override user explicite). Toujours alignée à gauche.
        /// </summary>
        private static (Word.WdOMathType type, Word.WdOMathJc justification)
            DecideOMathTyping(Word.OMath om, string source, Action<string> log)
        {
            var fallback = (Word.WdOMathType.wdOMathInline, Word.WdOMathJc.wdOMathJcLeft);
            if (om == null) return fallback;
            try
            {
                if (!string.IsNullOrEmpty(source) && source.StartsWith(" ")) return fallback;

                string paraText, omText;
                try
                {
                    paraText = om.Range.Paragraphs[1].Range.Text ?? "";
                    omText = om.Range.Text ?? "";
                }
                catch { return fallback; }

                // Strip OMath + chars structurels Word (\r \n \v \a \t \f + ZWSP user).
                string remaining = paraText.Replace(omText, "")
                    .Replace("\r", "").Replace("\n", "")
                    .Replace("\v", "").Replace("\a", "")
                    .Replace("\t", "").Replace("\f", "")
                    .Replace("​", "")
                    .Trim();

                if (string.IsNullOrEmpty(remaining))
                    return (Word.WdOMathType.wdOMathDisplay, Word.WdOMathJc.wdOMathJcLeft);
                return fallback;
            }
            catch (Exception ex)
            {
                log?.Invoke("decide_typing_error: " + ex.Message);
                return fallback;
            }
        }

        private static bool IsWhitespaceCharAt(Word.Document doc, int pos)
        {
            try
            {
                var t = doc.Range(pos, pos + 1).Text;
                return !string.IsNullOrEmpty(t) && char.IsWhiteSpace(t[0]);
            }
            catch { return false; }
        }

        private static string Preview(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
            return s.Length > 120 ? s.Substring(0, 120) + "…" : s;
        }
    }
}
