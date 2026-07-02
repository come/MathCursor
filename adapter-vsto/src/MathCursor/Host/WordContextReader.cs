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
using System.Collections.Generic;
using System.IO;
using System.Text;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Résultat de lecture du paragraphe : le texte reconstruit (OMaths
    /// remplacés par leur source si elle existe, sinon espaces) + la position
    /// du caret + la liste des régions dans le texte où se trouvent les OMaths.
    /// Ces régions servent à filtrer les zones NER qui chevauchent des équations
    /// déjà posées (on ne re-propose pas ce qui est déjà converti).
    /// </summary>
    internal sealed class ParagraphRead
    {
        public string Text { get; set; } = "";
        public int CaretOffset { get; set; }
        public IReadOnlyList<(int start, int end)> OMathRegions { get; set; } = Array.Empty<(int, int)>();
        public int ParagraphAbsStart { get; set; }
    }

    /// <summary>
    /// Lecture du paragraphe courant pour alimenter le NER + popup.
    ///
    /// Les régions OMath sont MASQUÉES (remplacées par des espaces de même
    /// longueur) — le NER est entraîné sur du texte brut, pas sur du math
    /// rendu, et substituer la source au milieu du paragraphe le confond
    /// (il ne détecte plus les expressions adjacentes). Le masquage fait
    /// "disparaître" l'équation sans décaler les positions.
    ///
    /// Les zones OMath sont quand même remontées dans <c>OMathRegions</c>
    /// pour que SuggestionService filtre les zones NER qui tomberaient dans
    /// cette région (si jamais le NER spike dessus).
    ///
    /// La source brute des équations reste récupérable via bookmark mcEq_
    /// → CustomXMLPart côté <c>SuggestionService.TryEnterEditMode</c>.
    /// </summary>
    internal sealed class WordContextReader
    {
        private readonly Word.Application _app;

        public WordContextReader(Word.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        public ParagraphRead ReadCurrentParagraph()
        {
            var empty = new ParagraphRead();
            var doc = _app.ActiveDocument;
            if (doc == null) return empty;
            try
            {
                var sel = _app.Selection;
                int caretPos = sel.Start;
                var paraRange = sel.Paragraphs[1].Range;
                int paraStart = paraRange.Start;
                int paraEnd = paraRange.End;
                if (paraStart >= paraEnd) return empty;

                // REVERT 2026-05-11 : refactor XML reader désactivé suite
                // à crash Word + caret blink en prod (cf. ADR du même jour,
                // section "Risque perf"). Le helper ParagraphTextExtractor
                // reste utilisé par les tests xUnit et sera réactivé une
                // fois qu'on aura un cache pré-appel pour éviter de
                // sérialiser le XML à chaque tick _pollTimer.
                var rangeText = doc.Range(paraStart, paraEnd).Text ?? "";

                // Pour chaque OMath, calcule sa position ET sa largeur en
                // chars STRING (pas en positions internes Word — les deux
                // espaces ne sont PAS alignés à cause des wrappers cachés).
                // Bug 2026-05-18 : positions internes utilisées comme STRING
                // → mask trop large qui mange les chars user après l'OMath.
                var omathStringRegions = new List<(int stringStart, int stringLen)>();
                try
                {
                    // Itère uniquement les OMaths du ¶ courant via Range.OMaths
                    // (pas doc.OMaths qui scanne tout le doc à chaque tick — sur
                    // 200 pages × 2000 OMaths × 5 Hz polling c'est inutilisable).
                    foreach (Word.OMath om in doc.Range(paraStart, paraEnd).OMaths)
                    {
                        var r = om.Range;
                        if (r.End <= paraStart || r.Start >= paraEnd) continue;

                        // Position string = nb de chars du préfixe entre
                        // paraStart et om.Range.Start (Word fait le mapping).
                        int stringStart;
                        int stringLen;
                        try
                        {
                            stringStart = (doc.Range(paraStart, r.Start).Text ?? "").Length;
                            stringLen = (r.Text ?? "").Length;
                        }
                        catch (Exception ex) { LogDiag("omath_string_pos_error: " + ex.Message); continue; }

                        omathStringRegions.Add((stringStart, stringLen));
                    }
                }
                catch (Exception ex) { LogDiag("omath_iter_error: " + ex.Message); }

                var (text, regions) = MaskOMathsInString(rangeText, omathStringRegions);

                if (regions.Count > 0)
                    LogDiag($"paragraph reconstructed (OMaths={regions.Count}, len={text.Length}) → \"{Preview(text)}\"");

                return new ParagraphRead
                {
                    Text = text,
                    CaretOffset = Math.Max(0, Math.Min(caretPos - paraStart, text.Length)),
                    OMathRegions = regions,
                    ParagraphAbsStart = paraStart,
                };
            }
            catch
            {
                return empty;
            }
        }

        /// <summary>
        /// Reconstruit (text, regions) à partir de <paramref name="rangeText"/> et
        /// des régions OMath exprimées en <b>positions STRING</b> (pas Word
        /// internes). Masque chaque région avec des espaces de même longueur
        /// → préserve l'alignement de positions caret/zones du NER, sans
        /// masquer accidentellement les chars user après l'OMath.
        /// </summary>
        private static (string text, IReadOnlyList<(int start, int end)> regions) MaskOMathsInString(
            string rangeText, IReadOnlyList<(int stringStart, int stringLen)> omathStringRegions)
        {
            string normalized = AutocorrectNormalizer.Normalize(rangeText ?? "");
            var regions = new List<(int start, int end)>();
            if (normalized.Length == 0) return (normalized, regions);

            var sb = new StringBuilder(normalized);
            foreach (var (stringStart, stringLen) in omathStringRegions)
            {
                int start = Math.Max(0, stringStart);
                int end = Math.Min(sb.Length, stringStart + stringLen);
                int len = end - start;
                if (len <= 0) continue;

                for (int i = 0; i < len; i++) sb[start + i] = ' ';

                regions.Add((start, end));
            }
            return (sb.ToString(), regions);
        }

        /// <summary>
        /// Normalise les caractères "joliifiés" par l'AutoCorrect Office en
        /// leur équivalent ASCII, **1 char pour 1 char** (les offsets entre
        /// Word doc et notre texte interne restent alignés). Sans ça :
        ///   - `–` (en-dash, U+2013) inséré à la place de `-` casse la
        ///     détection NER (corpus tokenisé sur `-` ASCII) et le lex
        ///     (token unknown) → l'expression n'est pas vue comme math.
        ///   - Idem pour les autres "smart" caractères.
        /// On garde le NBSP traité côté Lexer comme avant pour ne pas changer
        /// le comportement sur les espaces insécables.
        /// </summary>
        // NormalizeAutocorrect a été extrait dans AutocorrectNormalizer.cs
        // pour rester testable sans dépendance Word interop.

        private static string Preview(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
            return s.Length > 120 ? s.Substring(0, 120) + "…" : s;
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
                    $"{DateTime.UtcNow:o} ctx {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
