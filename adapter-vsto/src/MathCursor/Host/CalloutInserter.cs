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
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Encadrés « callout » typés (Théorème / Définition / Exemple / Propriété),
    /// calqués sur les blocs colorés d'un poly de cours : barre d'accent gauche
    /// épaisse + fin cadre + fond teinté + titre en petites capitales colorées.
    ///
    /// <para>100 % périphérique (ADR 2026-06-20) : pur style de paragraphe Word
    /// (<c>Range.Borders</c> / <c>Range.Shading</c> / <c>ParagraphFormat</c>),
    /// AUCUNE OMath, AUCUN moteur, AUCUN source-map. Le styling est cosmétique :
    /// chaque sous-étape est en try/catch, on n'échoue jamais l'insertion pour
    /// un réglage de bordure.</para>
    ///
    /// <para>Cible : la sélection si non vide (étendue aux paragraphes entiers),
    /// sinon le paragraphe courant. Un titre est inséré en tête du bloc ; le
    /// curseur est replacé en fin de contenu, prêt à taper.</para>
    /// </summary>
    internal static class CalloutInserter
    {
        internal sealed class CalloutStyle
        {
            public string Key;
            public string LabelFr;
            public string LabelEn;
            public int AccentRgb;   // barre gauche + cadre + titre
            public int FillRgb;     // fond teinté

            public string Label => (Strings.Lang == "fr") ? LabelFr : LabelEn;
        }

        // Couleurs calquées sur le poly ASE203. RGB classiques (la conversion
        // en WdColor BGR est faite par Rgb()).
        public static readonly Dictionary<string, CalloutStyle> Styles =
            new Dictionary<string, CalloutStyle>(StringComparer.OrdinalIgnoreCase)
        {
            ["theorem"] = new CalloutStyle
            {
                Key = "theorem", LabelFr = "Théorème", LabelEn = "Theorem",
                AccentRgb = Rgb(176, 0, 64), FillRgb = Rgb(253, 247, 249),
            },
            ["definition"] = new CalloutStyle
            {
                Key = "definition", LabelFr = "Définition", LabelEn = "Definition",
                AccentRgb = Rgb(36, 78, 168), FillRgb = Rgb(247, 250, 253),
            },
            ["example"] = new CalloutStyle
            {
                Key = "example", LabelFr = "Exemple", LabelEn = "Example",
                AccentRgb = Rgb(33, 130, 88), FillRgb = Rgb(246, 252, 249),
            },
            ["property"] = new CalloutStyle
            {
                Key = "property", LabelFr = "Propriété", LabelEn = "Property",
                AccentRgb = Rgb(190, 46, 110), FillRgb = Rgb(254, 247, 251),
            },
        };

        public static void Insert(Word.Application app, CalloutStyle style)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (style == null) throw new ArgumentNullException(nameof(style));

            var doc = app.ActiveDocument;
            if (doc == null) return;
            var sel = app.Selection;
            if (sel == null) return;

            // Étendre aux paragraphes ENTIERS de la sélection (ou ¶ courant).
            var paras = sel.Paragraphs;
            int contentStart = paras[1].Range.Start;
            int contentEnd = paras[paras.Count].Range.End;

            // Le corps était-il vide (= on crée une boîte vierge à remplir) ?
            // Si oui le curseur reste DANS la boîte ; sinon il sort en dessous
            // (cas « j'ai sélectionné mon théorème, je continue après »).
            bool emptyBody;
            try
            {
                string body = doc.Range(contentStart, contentEnd).Text ?? "";
                // ¶ = \r, fin de cellule = \a : on les ignore pour juger du vide.
                emptyBody = body.Replace("\r", "").Replace("\a", "").Trim().Length == 0;
            }
            catch { emptyBody = false; }

            // Taille du corps AVANT insertion, pour ne pas rapetisser le titre
            // (le corps peut être en 11/12pt). wdUndefined (mélange) → on laisse
            // le titre hériter naturellement (bodySize < 0).
            float bodySize = -1f;
            try
            {
                float s = doc.Range(contentStart, contentEnd).Font.Size;
                if (s > 0 && s < 1000) bodySize = s;
            }
            catch { /* taille corps : on dégrade */ }

            // 1) Titre = nouveau ¶ inséré AVANT le contenu. InsertBefore
            //    rattache le texte au début et décale le contenu d'autant.
            string title = style.Label;
            var titleAnchor = doc.Range(contentStart, contentStart);
            titleAnchor.InsertBefore(title + "\r");
            int shift = title.Length + 1; // titre + marque de ¶
            StyleTitle(doc.Range(contentStart, contentStart + title.Length), style, bodySize);

            // 2) Bloc entier (titre + contenu) = cadre + fond. Les positions
            //    du contenu ont décalé de `shift`.
            int blockEnd = contentEnd + shift;
            var block = doc.Range(contentStart, blockEnd);
            ApplyFrameAndFill(block, style);

            // 3) Sortie de l'encadré : on veut toujours pouvoir cliquer hors de
            //    la boîte, juste en dessous.
            //    - Si un paragraphe suit DÉJÀ la boîte, il n'est pas encadré (on
            //      n'a posé les bordures que sur le bloc) → il sert de sortie,
            //      on n'ajoute rien (pas de ligne vide parasite).
            //    - Si la boîte est en FIN de document (rien en dessous), on crée
            //      un ¶ vide non encadré, sinon on reste piégé dans la boîte.
            //      InsertParagraphAfter gère le ¶ FINAL du doc — ce que
            //      InsertAfter("\r") ne savait pas faire (bug 2026-06-22).
            int tailStart = -1;
            try
            {
                var blockRange = doc.Range(contentStart, blockEnd);
                var lastBlockPara = blockRange.Paragraphs[blockRange.Paragraphs.Count];
                bool nothingBelow = lastBlockPara.Range.End >= doc.Content.End;
                if (nothingBelow)
                {
                    lastBlockPara.Range.InsertParagraphAfter();
                    var newLast = doc.Paragraphs[doc.Paragraphs.Count];
                    ClearCalloutFormatting(newLast.Range);
                    tailStart = newLast.Range.Start;
                }
                else
                {
                    tailStart = blockEnd; // début du ¶ déjà présent sous la boîte
                }
            }
            catch { /* sortie cosmétique : on dégrade proprement */ }

            // 4) Curseur : dans la boîte si corps vide (à remplir), sinon dans
            //    le ¶ de sortie propre (prêt à enchaîner après l'encadré).
            try
            {
                int caretPos = (!emptyBody && tailStart >= 0) ? tailStart : (blockEnd - 1);
                sel.SetRange(caretPos, caretPos);
                int real = sel.Start;      // readback : Word snap les positions
                sel.SetRange(real, real);
            }
            catch { /* placement curseur : non critique */ }
        }

        // ─── Helpers ──────────────────────────────────────────────────

        /// <summary>Titre en petites capitales grasses colorées, à la taille du
        /// corps (jamais plus petit). <paramref name="bodySize"/> &lt; 0 = on
        /// laisse hériter (corps de taille mixte).</summary>
        private static void StyleTitle(Word.Range title, CalloutStyle style, float bodySize)
        {
            try
            {
                var f = title.Font;
                f.Bold = 1;
                f.SmallCaps = 1;
                f.Color = (Word.WdColor)style.AccentRgb;
                if (bodySize > 0) f.Size = bodySize;
                title.ParagraphFormat.SpaceAfter = 2f;
            }
            catch { /* titre cosmétique */ }
        }

        /// <summary>Barre d'accent gauche épaisse + fin cadre de la couleur
        /// d'accent + fond teinté + respiration (espacement, retrait).</summary>
        private static void ApplyFrameAndFill(Word.Range block, CalloutStyle style)
        {
            var accent = (Word.WdColor)style.AccentRgb;

            try
            {
                block.Shading.Texture = Word.WdTextureIndex.wdTextureNone;
                block.Shading.BackgroundPatternColor = (Word.WdColor)style.FillRgb;
            }
            catch { /* fond cosmétique */ }

            try
            {
                // Uniquement la barre gauche épaisse (accent) — pas de cadre
                // complet (cf. poly de cours : simple liseré à gauche).
                SetBorder(block, Word.WdBorderType.wdBorderLeft, accent, Word.WdLineWidth.wdLineWidth225pt);
                foreach (var side in new[]
                {
                    Word.WdBorderType.wdBorderTop,
                    Word.WdBorderType.wdBorderBottom,
                    Word.WdBorderType.wdBorderRight,
                })
                {
                    block.Borders[side].LineStyle = Word.WdLineStyle.wdLineStyleNone;
                }
                // Espace entre le texte et les bordures (en points).
                var b = block.Borders;
                b.DistanceFromLeft = 10;
                b.DistanceFromRight = 10;
                b.DistanceFromTop = 4;
                b.DistanceFromBottom = 4;
            }
            catch { /* cadre cosmétique */ }

            try
            {
                var pf = block.ParagraphFormat;
                pf.SpaceBefore = 6f;
                pf.SpaceAfter = 6f;
            }
            catch { /* espacement cosmétique */ }
        }

        private static void SetBorder(Word.Range range, Word.WdBorderType side,
            Word.WdColor color, Word.WdLineWidth width)
        {
            var border = range.Borders[side];
            border.LineStyle = Word.WdLineStyle.wdLineStyleSingle;
            border.LineWidth = width;
            border.Color = color;
        }

        /// <summary>Remet un ¶ à plat : aucune bordure, aucun fond, espacement
        /// nul, fonte neutre. Sert au paragraphe de sortie sous l'encadré pour
        /// que la boîte se referme et qu'on puisse écrire normalement après.</summary>
        private static void ClearCalloutFormatting(Word.Range r)
        {
            try
            {
                foreach (var side in new[]
                {
                    Word.WdBorderType.wdBorderLeft, Word.WdBorderType.wdBorderTop,
                    Word.WdBorderType.wdBorderBottom, Word.WdBorderType.wdBorderRight,
                })
                {
                    r.Borders[side].LineStyle = Word.WdLineStyle.wdLineStyleNone;
                }
            }
            catch { /* bordures */ }

            try
            {
                r.Shading.Texture = Word.WdTextureIndex.wdTextureNone;
                r.Shading.BackgroundPatternColor = Word.WdColor.wdColorAutomatic;
            }
            catch { /* fond */ }

            try
            {
                var pf = r.ParagraphFormat;
                pf.SpaceBefore = 0f;
                pf.SpaceAfter = 0f;
            }
            catch { /* espacement */ }

            try
            {
                var f = r.Font;
                f.Bold = 0;
                f.SmallCaps = 0;
                f.Color = Word.WdColor.wdColorAutomatic;
            }
            catch { /* fonte */ }
        }

        /// <summary>RGB (0-255) → WdColor (Word encode en BGR : 0x00BBGGRR).</summary>
        private static int Rgb(int r, int g, int b) => r | (g << 8) | (b << 16);
    }
}
