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
                var omathAbsRegions = new List<(int absStart, int absEnd)>();
                try
                {
                    foreach (Word.OMath om in doc.OMaths)
                    {
                        var r = om.Range;
                        if (r.End <= paraStart || r.Start >= paraEnd) continue;
                        omathAbsRegions.Add((r.Start, r.End));
                    }
                }
                catch (Exception ex) { LogDiag("omath_iter_error: " + ex.Message); }

                var (text, regions) = FallbackFromRangeText(rangeText, paraStart, omathAbsRegions);

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
        /// Fallback : reconstruit (text, regions) à partir du Range.Text
        /// Word (avec ses chars de contrôle internes) + Normalize. Utilisé
        /// quand le chemin XML échoue ou que l'invariant 1:1 casse.
        /// Comportement identique au pré-ADR 2026-05-11.
        /// </summary>
        private static (string text, IReadOnlyList<(int start, int end)> regions) FallbackFromRangeText(
            string rangeText, int paraStart, IReadOnlyList<(int absStart, int absEnd)> omathAbsRegions)
        {
            string normalized = AutocorrectNormalizer.Normalize(rangeText ?? "");
            var regions = new List<(int start, int end)>();
            if (normalized.Length == 0) return (normalized, regions);

            var sb = new StringBuilder(normalized);
            foreach (var (absStart, absEnd) in omathAbsRegions)
            {
                int omRelStart = Math.Max(0, absStart - paraStart);
                int omRelEnd = Math.Min(sb.Length, absEnd - paraStart);
                int regionLen = omRelEnd - omRelStart;
                if (regionLen <= 0) continue;

                for (int i = 0; i < regionLen; i++)
                    sb[omRelStart + i] = ' ';

                regions.Add((omRelStart, omRelEnd));
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
