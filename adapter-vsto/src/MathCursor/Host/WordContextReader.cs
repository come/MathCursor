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
                var rawText = doc.Range(paraStart, paraEnd).Text ?? "";

                var regions = new List<(int start, int end)>();
                string text = rawText;

                if (rawText.Length > 0)
                {
                    var sb = new StringBuilder(rawText);
                    try
                    {
                        foreach (Word.OMath om in doc.OMaths)
                        {
                            var r = om.Range;
                            if (r.End <= paraStart || r.Start >= paraEnd) continue;
                            int omRelStart = Math.Max(0, r.Start - paraStart);
                            int omRelEnd = Math.Min(sb.Length, r.End - paraStart);
                            int regionLen = omRelEnd - omRelStart;
                            if (regionLen <= 0) continue;

                            // Masquage : on remplace chaque caractère de la région OMath
                            // par un espace. Deux motifs :
                            // 1) Le NER est entraîné sur du texte brut, pas sur du math
                            //    rendu — substituer la source brute au milieu d'un
                            //    paragraphe le confond parfois (il ne détecte plus les
                            //    expressions adjacentes). Masquer le fait "disparaître".
                            // 2) La source reste accessible via bookmark→store côté
                            //    TryEnterEditMode quand le caret entre dans l'équation.
                            for (int i = 0; i < regionLen; i++)
                                sb[omRelStart + i] = ' ';

                            regions.Add((omRelStart, omRelEnd));
                        }
                    }
                    catch (Exception ex) { LogDiag("omath_iter_error: " + ex.Message); }
                    text = sb.ToString();
                }

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
