// MathCursor: capturing mathematical intent from linear keyboard input.
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
    /// <summary>Structure d'un tableau MathCursor lue sur ses styles de rôle.</summary>
    internal sealed class StudyTableStructure
    {
        public List<StudyRowKind> Rows { get; } = new List<StudyRowKind>();
        public List<StudyColumnKind> Columns { get; } = new List<StudyColumnKind>();
        /// <summary>Colonnes (0-based) dont la frontière est une valeur interdite.</summary>
        public HashSet<int> Forbidden { get; } = new HashSet<int>();
        /// <summary>Colonnes (0-based) avec une séparation : simple ligne
        /// verticale sous x, posée par un « 0 » sous x ou par une valeur de x
        /// écrite (hors première / dernière valeur).</summary>
        public HashSet<int> SeparatorLines { get; } = new HashSet<int>();

        public int FrontierCount
        {
            get
            {
                int n = 0;
                foreach (var k in Columns) if (k == StudyColumnKind.Frontier) n++;
                return n;
            }
        }
    }

    /// <summary>Ce qu'il faut savoir d'une case pour sa popup (lecture légère).</summary>
    internal sealed class StudyCellInfo
    {
        public StudyRowKind RowKind;
        public StudyColumnKind ColumnKind;
        public bool Forbidden;
        public int RowCount;
        public int ColumnCount;
        public int FrontierCount;
    }

    /// <summary>
    /// Mise en forme Word des tableaux de signe / variation à partir des
    /// rôles de cases (styles de paragraphe). <see cref="Reapply"/> remet tout
    /// d'aplomb après une action de structure : Word recopie le style d'une
    /// colonne voisine mais ni l'alignement, et rétrécit toutes les colonnes ;
    /// une ligne insérée recopie sa voisine (hauteur comprise). Idempotent :
    /// n'écrit que ce qui diffère. Résultats du POC dans l'ADR
    /// 2026-10-19-Feat-study-table-cell-roles-contextual-popup.
    /// </summary>
    internal static class StudyTableFormatter
    {
        private const float ForbiddenGapPt = 2.5f;
        private const float SignRowLinePt = 16f;
        /// <summary>Word dessine une bordure gauche de paragraphe ~1,6 pt à
        /// gauche du retrait (mesuré sur le PDF du spike A) : on compense pour
        /// centrer la simple ligne sous la valeur de x.</summary>
        private const float SingleLineOffsetPt = 1.6f;
        private const float ZeroSegmentSignPt = 3f;
        private const float ZeroLineSignPt = 12f;
        private const float ZeroLineVariationPt = 14f;
        private const int MaxWidenIterations = 8;

        /// <summary>Journal des tableaux (%APPDATA%\MathCursor\logs\mathcursor.log,
        /// préfixe « table ») : chaque écriture dans Word est tracée pour
        /// diagnostiquer les retours d'usage.</summary>
        public static void Log(string message)
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "mathcursor.log"),
                    $"{DateTime.UtcNow:o} table {message}{Environment.NewLine}");
            }
            catch { }
        }

        private static string Short(string text)
        {
            text = (text ?? "").Replace("\r", "\\r").Replace("\a", "");
            return text.Length <= 24 ? text : text.Substring(0, 24) + "…";
        }

        // ── Styles ───────────────────────────────────────────────────────

        public static void EnsureStyles(Word.Document doc)
        {
            foreach (var name in new[] { StudyTableTemplate.FrontierStyle, StudyTableTemplate.IntervalStyle })
                EnsureStyle(doc, name, StudyTableTemplate.BodyFontSizePt, null);
            foreach (var name in new[]
            {
                StudyTableTemplate.LabelXStyle, StudyTableTemplate.LabelSignStyle,
                StudyTableTemplate.LabelVariationStyle, StudyTableTemplate.HeaderFrontierStyle,
                StudyTableTemplate.HeaderIntervalStyle,
            })
            {
                EnsureStyle(doc, name, StudyTableTemplate.BodyFontSizePt, null);
                UpdateHeaderStyle(doc, name);
            }

            EnsureStyle(doc, StudyTableTemplate.ArrowStyleName, StudyTableTemplate.ArrowFontSizePt, null);
            UpdateArrowStyle(doc);
            EnsureStyle(doc, StudyTableTemplate.ForbiddenStyle, StudyTableTemplate.BodyFontSizePt, s =>
            {
                foreach (var b in new[] { Word.WdBorderType.wdBorderLeft, Word.WdBorderType.wdBorderRight })
                {
                    var border = s.ParagraphFormat.Borders[b];
                    border.LineStyle = Word.WdLineStyle.wdLineStyleSingle;
                    border.LineWidth = Word.WdLineWidth.wdLineWidth050pt;
                }
                s.ParagraphFormat.Borders.DistanceFromLeft = 0;
                s.ParagraphFormat.Borders.DistanceFromRight = 0;
            });
        }

        /// <summary>Police, taille et couleur des flèches portées par leur
        /// style : réappliquées à chaque insertion, les tableaux déjà créés
        /// (anciennes flèches) en profitent aussi.</summary>
        private static void UpdateArrowStyle(Word.Document doc)
        {
            try
            {
                var font = doc.Styles[StudyTableTemplate.ArrowStyleName].Font;
                if (font.Name != StudyTableTemplate.ArrowFontName) font.Name = StudyTableTemplate.ArrowFontName;
                if (Math.Abs(font.Size - StudyTableTemplate.ArrowFontSizePt) > 0.05f)
                    font.Size = StudyTableTemplate.ArrowFontSizePt;
                if ((int)font.Color != StudyTableTemplate.ArrowColorBgr)
                    font.Color = (Word.WdColor)StudyTableTemplate.ArrowColorBgr;
            }
            catch (Exception ex) { Log("style_flèche_error: " + ex.Message); }
        }

        /// <summary>Gras bleu nuit de l'en-tête porté par le STYLE (ligne x,
        /// étiquettes) : ce qu'on tape en hérite, rien à réécrire à la sortie
        /// de case (sinon une entrée d'annulation de plus à chaque saisie).
        /// Imposé aussi aux styles déjà présents dans le document.</summary>
        private static void UpdateHeaderStyle(Word.Document doc, string name)
        {
            try
            {
                var font = doc.Styles[name].Font;
                if (font.Bold != 1) font.Bold = 1;
                if ((int)font.Color != StudyTableTemplate.HeaderTextBgr)
                    font.Color = (Word.WdColor)StudyTableTemplate.HeaderTextBgr;
            }
            catch (Exception ex) { Log($"style_en-tête_error {name}: {ex.Message}"); }
        }

        /// <summary>
        /// En-tête (ligne x) et colonne d'étiquettes : fond bleu-gris (le gras
        /// et la couleur sont portés par leurs styles). Les autres cases n'ont
        /// pas de fond (une colonne / ligne insérée par Word peut avoir recopié
        /// celui de sa voisine). <paramref name="onlyCol"/> > 0 = une seule
        /// colonne. N'écrit que ce qui diffère.
        /// </summary>
        private static void ApplyHeaderEmphasis(Word.Table table, StudyTableStructure s, int onlyCol = 0)
        {
            var background = (Word.WdColor)StudyTableTemplate.HeaderBackgroundBgr;
            for (int r = 1; r <= s.Rows.Count; r++)
                for (int c = 1; c <= s.Columns.Count; c++)
                {
                    if (onlyCol > 0 && c != onlyCol) continue;
                    try
                    {
                        var cell = table.Cell(r, c);
                        bool header = r == 1 || c == 1;
                        if (!header)
                        {
                            if (cell.Shading.BackgroundPatternColor != Word.WdColor.wdColorAutomatic)
                                cell.Shading.BackgroundPatternColor = Word.WdColor.wdColorAutomatic;
                            continue;
                        }
                        if (cell.Shading.BackgroundPatternColor != background)
                            cell.Shading.BackgroundPatternColor = background;
                    }
                    catch (Exception ex) { Log($"en-tête_error r{r}c{c}: {ex.Message}"); }
                }
        }

        private static void EnsureStyle(Word.Document doc, string name, float size, Action<Word.Style> extra)
        {
            try { _ = doc.Styles[name]; return; }
            catch { /* absent : on le crée */ }
            try
            {
                var style = doc.Styles.Add(name, Word.WdStyleType.wdStyleTypeParagraph);
                style.Font.Size = size;
                style.Font.Italic = 0;
                var pf = style.ParagraphFormat;
                pf.Alignment = Word.WdParagraphAlignment.wdAlignParagraphCenter;
                pf.SpaceBefore = 2f;
                pf.SpaceAfter = 2f;
                pf.LineSpacingRule = Word.WdLineSpacing.wdLineSpaceSingle;
                pf.LeftIndent = 0f;
                pf.RightIndent = 0f;
                pf.FirstLineIndent = 0f;
                style.QuickStyle = false;
                extra?.Invoke(style);
            }
            catch { /* sans style : le tableau reste utilisable, sans popup */ }
        }

        public static string StyleNameOf(Word.Range range)
        {
            try { return (range.Paragraphs[1].get_Style() as Word.Style)?.NameLocal; }
            catch { return null; }
        }

        public static void SetStyle(Word.Cell cell, string name)
        {
            if (name == null) return;
            string current = StyleNameOf(cell.Range);
            if (current == name) return;
            try
            {
                object st = name;
                cell.Range.set_Style(ref st);
                Log($"style r{cell.RowIndex}c{cell.ColumnIndex} '{current}' -> '{name}'");
            }
            catch (Exception ex) { Log($"style_error r{cell.RowIndex}c{cell.ColumnIndex}: {ex.Message}"); }
        }

        /// <summary>Vrai si la plage est dans un tableau MathCursor (case (1,1)
        /// au style « Ligne x »). Une seule lecture de style.</summary>
        public static bool IsInStudyTable(Word.Range range)
        {
            try
            {
                if (range == null || !(bool)range.get_Information(Word.WdInformation.wdWithInTable)) return false;
                return StyleNameOf(range.Tables[1].Cell(1, 1).Range) == StudyTableTemplate.LabelXStyle;
            }
            catch { return false; }
        }

        /// <summary>Vrai si la plage est dans une case d'ÉTIQUETTE (1re colonne)
        /// d'un tableau MathCursor : une formule convertie y ouvre les actions
        /// de ligne, pas la popup d'édition.</summary>
        public static bool IsInStudyTableLabel(Word.Range range)
        {
            try
            {
                return IsInStudyTable(range) && range.Cells.Count >= 1 && range.Cells[1].ColumnIndex == 1;
            }
            catch { return false; }
        }

        /// <summary>Équations (display) du tableau centrées : dans le tableau,
        /// et seulement là, les formules MathCursor sont centrées.</summary>
        private static void CenterMath(Word.Table table) => CenterMath(table.Range);

        private static void CenterMath(Word.Range range)
        {
            try
            {
                foreach (Word.OMath om in range.OMaths)
                {
                    try
                    {
                        if (om.Type != Word.WdOMathType.wdOMathDisplay) continue;
                        if (om.Justification == Word.WdOMathJc.wdOMathJcCenter) continue;
                        Log($"équation centrée '{Short(om.Range.Text)}' ({om.Justification} -> centre)");
                        om.Justification = Word.WdOMathJc.wdOMathJcCenter;
                    }
                    catch (Exception ex) { Log("centrage_error: " + ex.Message); }
                }
            }
            catch { }
        }

        // ── Lecture de structure ─────────────────────────────────────────

        /// <summary>Null si ce n'est pas un tableau MathCursor (case (1,1)
        /// sans le style « Ligne x »).</summary>
        public static StudyTableStructure Read(Word.Table table)
        {
            if (table == null) return null;
            try
            {
                if (StyleNameOf(table.Cell(1, 1).Range) != StudyTableTemplate.LabelXStyle) return null;
                var s = new StudyTableStructure();
                int rows = table.Rows.Count, cols = table.Columns.Count;
                var valued = new List<int>();
                for (int c = 1; c <= cols; c++)
                {
                    string xStyle = StyleNameOf(table.Cell(1, c).Range);
                    var kind = StudyTableTemplate.ColumnKindFromXRowStyle(xStyle);
                    if (kind == StudyColumnKind.None && c > 1)
                    {
                        // Style perdu sur la ligne x : on reprend l'alternance.
                        kind = s.Columns[c - 2] == StudyColumnKind.Frontier
                            ? StudyColumnKind.Interval : StudyColumnKind.Frontier;
                        Log($"read: style x-row c{c} inconnu '{xStyle}' -> déduit {kind}");
                    }
                    s.Columns.Add(kind);
                    if (kind != StudyColumnKind.Frontier) continue;
                    bool forbidden = false, zero = false;
                    for (int r = 2; r <= rows; r++)
                    {
                        var cell = table.Cell(r, c);
                        if (StyleNameOf(cell.Range) == StudyTableTemplate.ForbiddenStyle) forbidden = true;
                        else if (CellText(cell) == "0") zero = true;
                    }
                    if (forbidden) s.Forbidden.Add(c - 1);
                    else if (zero) s.SeparatorLines.Add(c - 1);
                    else if (CellText(table.Cell(1, c)).Length > 0) valued.Add(c - 1);
                }
                // Une valeur de x écrite = une séparation sous elle, sauf aux
                // deux bouts (la ligne doublerait le bord du tableau). Un 0 la
                // déclenche partout.
                int firstFrontier = s.Columns.IndexOf(StudyColumnKind.Frontier);
                int lastFrontier = s.Columns.LastIndexOf(StudyColumnKind.Frontier);
                foreach (int c0 in valued)
                    if (c0 != firstFrontier && c0 != lastFrontier) s.SeparatorLines.Add(c0);
                for (int r = 1; r <= rows; r++)
                {
                    string labelStyle = StyleNameOf(table.Cell(r, 1).Range);
                    var kind = StudyTableTemplate.RowKindFromLabelStyle(labelStyle);
                    if (kind == StudyRowKind.None && r > 1)
                    {
                        // Style d'étiquette perdu : la ligne se reconnaît à ses intervalles.
                        int interval = s.Columns.IndexOf(StudyColumnKind.Interval) + 1;
                        string inner = interval > 0 ? StyleNameOf(table.Cell(r, interval).Range) : null;
                        kind = inner == StudyTableTemplate.ArrowStyleName ? StudyRowKind.Variation : StudyRowKind.Sign;
                        Log($"read: style étiquette r{r} inconnu '{labelStyle}' -> déduit {kind}");
                    }
                    s.Rows.Add(kind);
                }
                return s;
            }
            catch { return null; }
        }

        // ── Réapplication complète ───────────────────────────────────────

        /// <summary>
        /// Contenu d'une case changé (flèche, 0) sans changement de structure :
        /// seuls les alignements de la ligne bougent (extremums d'après les
        /// flèches). Ne touche ni la ligne x, ni les largeurs, ni les polices.
        /// </summary>
        public static void ReapplyRowAlignments(Word.Table table, int row, string reason)
        {
            var s = Read(table);
            if (s == null) return;
            Log($"reapply_row r{row} ({reason})");
            ApplyAlignments(table, s, row, row);
        }

        /// <summary>
        /// Sortie d'une case modifiée sans changement de structure : seule sa
        /// colonne est remise en forme (police, élargissement, séparation,
        /// alignements, équations centrées). Les autres colonnes ne sont
        /// retouchées que si celle-ci s'élargit (les retraits des lignes
        /// dépendent des largeurs). Bien moins coûteux que <see cref="Reapply"/>
        /// sur un grand tableau.
        /// </summary>
        public static void ReapplyColumn(Word.Table table, int col, string reason)
        {
            var s = Read(table);
            if (s == null || col < 1 || col > s.Columns.Count) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Log($"reapply_col c{col} ({reason}) séparations=[{string.Join(",", s.SeparatorLines)}]");
            bool widthsChanged = false;
            if (s.Columns[col - 1] == StudyColumnKind.Frontier)
            {
                var widened = CurrentWidenedFrontiers(table, s);
                widthsChanged = FitColumn(table.Range.Document, table, s, col, widened);
            }
            ApplyAlignments(table, s, 1, s.Rows.Count, col);
            ApplyFrontierGeometry(table, s, widthsChanged ? 0 : col);
            for (int r = 1; r <= s.Rows.Count; r++)
            {
                try { CenterMath(table.Cell(r, col).Range); } catch { }
            }
            ApplyHeaderEmphasis(table, s, col);
            Log($"reapply_col c{col} fini en {sw.ElapsedMilliseconds} ms");
        }

        /// <summary>
        /// Lecture LÉGÈRE pour une case (à chaque clic) : type de sa ligne et de
        /// sa colonne, valeur interdite, nombre de lignes et de frontières.
        /// Quelques lectures de style au lieu de tout le tableau ; repli sur
        /// <see cref="Read"/> si un style de rôle manque. Null si ce n'est pas
        /// un tableau MathCursor.
        /// </summary>
        public static StudyCellInfo ReadCell(Word.Table table, int row, int col)
        {
            try
            {
                if (StyleNameOf(table.Cell(1, 1).Range) != StudyTableTemplate.LabelXStyle) return null;
                int rows = table.Rows.Count, cols = table.Columns.Count;
                var rowKind = StudyTableTemplate.RowKindFromLabelStyle(StyleNameOf(table.Cell(row, 1).Range));
                var colKind = StudyTableTemplate.ColumnKindFromXRowStyle(StyleNameOf(table.Cell(1, col).Range));
                if (rowKind == StudyRowKind.None || colKind == StudyColumnKind.None)
                {
                    var s = Read(table);
                    if (s == null) return null;
                    return new StudyCellInfo
                    {
                        RowKind = s.Rows[row - 1], ColumnKind = s.Columns[col - 1],
                        Forbidden = s.Forbidden.Contains(col - 1),
                        RowCount = rows, ColumnCount = cols, FrontierCount = s.FrontierCount,
                    };
                }
                bool forbidden = colKind == StudyColumnKind.Frontier && rows >= 2
                    && StyleNameOf(table.Cell(row > 1 ? row : 2, col).Range) == StudyTableTemplate.ForbiddenStyle;
                return new StudyCellInfo
                {
                    RowKind = rowKind, ColumnKind = colKind, Forbidden = forbidden,
                    RowCount = rows, ColumnCount = cols,
                    FrontierCount = cols / 2,  // étiquettes + F I F … F
                };
            }
            catch { return null; }
        }

        /// <summary>
        /// « 0 » posé ou retiré dans une frontière : seule la colonne change
        /// (simple ligne sur toute la colonne, alignements de ses cases).
        /// </summary>
        public static void ReapplyColumnShapes(Word.Table table, int col, string reason)
        {
            var s = Read(table);
            if (s == null) return;
            Log($"reapply_col c{col} ({reason}) séparations=[{string.Join(",", s.SeparatorLines)}]");
            ApplyAlignments(table, s, 1, s.Rows.Count);
            ApplyFrontierGeometry(table, s, col);
        }

        /// <summary>
        /// Remet tout le tableau d'aplomb. <paramref name="keepWidened"/> :
        /// structure inchangée (sortie de case) → les frontières déjà
        /// élargies gardent leur largeur. Après un ajout / une suppression de
        /// colonne, Word a redimensionné toutes les colonnes : on repart des
        /// largeurs de base.
        /// </summary>
        public static void Reapply(Word.Table table, string reason, bool keepWidened = false)
        {
            var s = Read(table);
            if (s == null) { Log($"reapply ({reason}) : pas un tableau MathCursor"); return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var doc = table.Range.Document;
            int rows = s.Rows.Count, cols = s.Columns.Count;
            Log($"reapply ({reason}) {rows}x{cols} rows=[{string.Join(",", s.Rows)}] " +
                $"cols=[{string.Join(",", s.Columns)}] interdites=[{string.Join(",", s.Forbidden)}] séparations=[{string.Join(",", s.SeparatorLines)}]");

            for (int r = 1; r <= rows; r++)
            {
                var rowKind = s.Rows[r - 1];
                for (int c = 1; c <= cols; c++)
                {
                    var role = StudyTableTemplate.RoleFor(rowKind, s.Columns[c - 1], s.Forbidden.Contains(c - 1));
                    SetStyle(table.Cell(r, c), StudyTableTemplate.StyleFor(role, rowKind));
                }
                ApplyRowHeight(table.Rows[r], rowKind);
            }

            ApplyBorders(table, rows);
            var widened = keepWidened ? CurrentWidenedFrontiers(table, s) : new Dictionary<int, float>();
            ApplyWidths(table, s, widened);
            ApplyAlignments(table, s, 1, rows);
            FitFrontiers(doc, table, s, widened);
            ApplyFrontierGeometry(table, s);
            CenterMath(table);
            ApplyHeaderEmphasis(table, s);
            Log($"reapply ({reason}) fini en {sw.ElapsedMilliseconds} ms");
        }

        /// <summary>
        /// Frontières déjà élargies (largeur actuelle au-dessus de la largeur de
        /// base) : on les garde telles quelles plutôt que de les ramener à la
        /// base puis de les réélargir (le tableau « sautait » à chaque action).
        /// </summary>
        private static Dictionary<int, float> CurrentWidenedFrontiers(Word.Table table, StudyTableStructure s)
        {
            var widened = new Dictionary<int, float>();
            var baseWidths = StudyTableTemplate.ColumnWidths(s.Columns);
            for (int c = 1; c <= s.Columns.Count; c++)
            {
                if (s.Columns[c - 1] != StudyColumnKind.Frontier) continue;
                try
                {
                    var cell = table.Cell(1, c);
                    if (cell.PreferredWidthType != Word.WdPreferredWidthType.wdPreferredWidthPercent) continue;
                    float w = cell.PreferredWidth;
                    if (w > baseWidths[c - 1] + 0.05f && w <= StudyTableTemplate.MaxWidenedFrontierPct + 0.05f)
                        widened[c - 1] = w;
                }
                catch { }
            }
            if (widened.Count > 0)
                Log("frontières déjà élargies : " + string.Join(", ", ToStrings(widened)));
            return widened;
        }

        private static IEnumerable<string> ToStrings(Dictionary<int, float> widened)
        {
            foreach (var kv in widened) yield return $"c{kv.Key + 1}={kv.Value:0.#}%";
        }

        private static void ApplyRowHeight(Word.Row row, StudyRowKind kind)
        {
            try
            {
                if (kind == StudyRowKind.Variation)
                {
                    if (row.HeightRule != Word.WdRowHeightRule.wdRowHeightAtLeast)
                        row.HeightRule = Word.WdRowHeightRule.wdRowHeightAtLeast;
                    if (Math.Abs(row.Height - StudyTableTemplate.VariationRowHeightPt) > 0.5f)
                        row.Height = StudyTableTemplate.VariationRowHeightPt;
                }
                else if (row.HeightRule != Word.WdRowHeightRule.wdRowHeightAuto)
                    row.HeightRule = Word.WdRowHeightRule.wdRowHeightAuto;
            }
            catch { }
        }

        /// <summary>Cadre + séparateurs horizontaux + trait après les
        /// étiquettes ; pas de trait entre colonnes de valeurs.</summary>
        private static void ApplyBorders(Word.Table table, int rows)
        {
            try
            {
                foreach (var b in new[]
                {
                    Word.WdBorderType.wdBorderTop, Word.WdBorderType.wdBorderBottom,
                    Word.WdBorderType.wdBorderLeft, Word.WdBorderType.wdBorderRight,
                    Word.WdBorderType.wdBorderHorizontal,
                })
                    Line(table.Borders[b]);
                table.Borders[Word.WdBorderType.wdBorderVertical].LineStyle = Word.WdLineStyle.wdLineStyleNone;
                for (int r = 1; r <= rows; r++)
                    Line(table.Cell(r, 1).Borders[Word.WdBorderType.wdBorderRight]);
            }
            catch { }
        }

        private static void Line(Word.Border border)
        {
            border.LineStyle = Word.WdLineStyle.wdLineStyleSingle;
            border.LineWidth = Word.WdLineWidth.wdLineWidth075pt;
        }

        private static void ApplyWidths(Word.Table table, StudyTableStructure s, IReadOnlyDictionary<int, float> widened)
        {
            try
            {
                table.AllowAutoFit = false;
                table.PreferredWidthType = Word.WdPreferredWidthType.wdPreferredWidthPercent;
                table.PreferredWidth = 100f;
                var widths = StudyTableTemplate.ColumnWidths(s.Columns, widened);
                foreach (Word.Cell cell in table.Range.Cells)
                {
                    float w = widths[cell.ColumnIndex - 1];
                    if (cell.PreferredWidthType != Word.WdPreferredWidthType.wdPreferredWidthPercent)
                        cell.PreferredWidthType = Word.WdPreferredWidthType.wdPreferredWidthPercent;
                    if (Math.Abs(cell.PreferredWidth - w) > 0.05f)
                    {
                        if (cell.RowIndex == 1) Log($"largeur c{cell.ColumnIndex} {cell.PreferredWidth:0.#}% -> {w:0.#}%");
                        cell.PreferredWidth = w;
                    }
                }
            }
            catch { }
        }

        /// <summary>Tout centré, sauf les frontières des lignes de variation :
        /// en haut / en bas selon les flèches voisines.</summary>
        private static void ApplyAlignments(Word.Table table, StudyTableStructure s, int fromRow, int toRow,
                                            int onlyCol = 0)
        {
            for (int r = fromRow; r <= toRow; r++)
                for (int c = 1; c <= s.Columns.Count; c++)
                {
                    if (onlyCol > 0 && c != onlyCol) continue;
                    // Valeur interdite / simple ligne : alignement posé par ApplyFrontierGeometry.
                    if (r > 1 && (s.Forbidden.Contains(c - 1) || s.SeparatorLines.Contains(c - 1))
                        && ShapeOf(s, r, c, CellText(table.Cell(r, c))) != FrontierShape.Normal) continue;
                    try
                    {
                        var align = CellVAlign.Center;
                        if (s.Rows[r - 1] == StudyRowKind.Variation && s.Columns[c - 1] == StudyColumnKind.Frontier)
                        {
                            string left = c - 1 >= 2 ? CellText(table.Cell(r, c - 1)) : null;
                            string right = c + 1 <= s.Columns.Count ? CellText(table.Cell(r, c + 1)) : null;
                            align = StudyTableTemplate.ExtremumAlign(left, right);
                        }
                        var target = align == CellVAlign.Top ? Word.WdCellVerticalAlignment.wdCellAlignVerticalTop
                            : align == CellVAlign.Bottom ? Word.WdCellVerticalAlignment.wdCellAlignVerticalBottom
                            : Word.WdCellVerticalAlignment.wdCellAlignVerticalCenter;
                        var cell = table.Cell(r, c);
                        if (cell.VerticalAlignment != target)
                        {
                            Log($"alignement r{r}c{c} {cell.VerticalAlignment} -> {target}");
                            cell.VerticalAlignment = target;
                        }
                    }
                    catch { }
                }
        }

        /// <summary>
        /// Valeur interdite = bordures de paragraphe G+D (portées par le style)
        /// resserrées au centre par des retraits, interligne exact = hauteur
        /// de la ligne, marges haut/bas nulles. Les autres frontières
        /// reviennent à un paragraphe normal. Recalculé après chaque
        /// changement de largeur.
        /// </summary>
        private enum FrontierShape { Normal, Forbidden, ZeroOnLine, FullLine }

        /// <summary>
        /// Forme d'une frontière sous x : double barre (valeur interdite),
        /// simple ligne si la colonne a une séparation (interrompue par le 0 dans sa
        /// case, pleine dans les cases vides ; une autre valeur, ex. extremum,
        /// reste sans ligne), sinon normale. Cf. ADR 2026-10-19-Feat-study-
        /// table-cell-roles-contextual-popup, « Les 0 posés sur une simple ligne ».
        /// </summary>
        private static FrontierShape ShapeOf(StudyTableStructure s, int row, int col, string text)
        {
            if (row <= 1 || s.Columns[col - 1] != StudyColumnKind.Frontier) return FrontierShape.Normal;
            if (s.Forbidden.Contains(col - 1)) return FrontierShape.Forbidden;
            if (!s.SeparatorLines.Contains(col - 1)) return FrontierShape.Normal;
            if (text == "0") return FrontierShape.ZeroOnLine;
            if (text.Length == 0) return FrontierShape.FullLine;
            return FrontierShape.Normal;
        }

        /// <summary>
        /// Valeur interdite = bordures de paragraphe G+D (portées par le style)
        /// resserrées au centre par des retraits. Simple ligne = bordure gauche
        /// de paragraphe au milieu de la case ; une case « 0 » = trois
        /// paragraphes [bout de ligne / 0 / bout de ligne]. Interligne exact
        /// (= longueur de ligne), marges haut/bas nulles. Les autres frontières
        /// reviennent à un paragraphe normal. Recalculé après chaque
        /// changement de largeur ; <paramref name="onlyCol"/> > 0 = une seule
        /// colonne (1-based). N'écrit que ce qui diffère.
        /// </summary>
        private static void ApplyFrontierGeometry(Word.Table table, StudyTableStructure s, int onlyCol = 0)
        {
            for (int r = 2; r <= s.Rows.Count; r++)
                for (int c = 1; c <= s.Columns.Count; c++)
                {
                    if (s.Columns[c - 1] != StudyColumnKind.Frontier) continue;
                    if (onlyCol > 0 && c != onlyCol) continue;
                    try
                    {
                        var cell = table.Cell(r, c);
                        var shape = ShapeOf(s, r, c, CellText(cell));
                        bool variation = s.Rows[r - 1] == StudyRowKind.Variation;
                        float inner = cell.Width - table.LeftPadding - table.RightPadding;
                        switch (shape)
                        {
                            case FrontierShape.Forbidden: ApplyForbidden(table, cell, r, c, inner, variation); break;
                            case FrontierShape.FullLine: ApplyFullLine(cell, r, c, inner, variation); break;
                            case FrontierShape.ZeroOnLine: ApplyZeroOnLine(cell, r, c, inner, variation); break;
                            default: RevertToNormal(table, cell, r, c); break;
                        }
                    }
                    catch (Exception ex) { Log($"géométrie_error r{r}c{c}: {ex.Message}"); }
                }
        }

        private static float LineLength(bool variation) =>
            variation ? StudyTableTemplate.VariationRowHeightPt : SignRowLinePt;

        private static void ApplyForbidden(Word.Table table, Word.Cell cell, int r, int c, float inner, bool variation)
        {
            var pf = cell.Range.ParagraphFormat;
            float indent = Math.Max(0f, (inner - ForbiddenGapPt) / 2f);
            float line = LineLength(variation);
            if (IsTopNoPadding(cell) && SameExact(pf, indent, indent, line)) return;
            Log($"valeur interdite r{r}c{c} retrait={indent:0.#} interligne={line}");
            SetTopNoPadding(cell);
            SetExact(pf, indent, indent, line);
        }

        /// <summary>Case vide d'une colonne à séparation : ligne pleine.</summary>
        private static void ApplyFullLine(Word.Cell cell, int r, int c, float inner, bool variation)
        {
            if (cell.Range.Paragraphs.Count > 1) SetCellText(cell, "");
            var p = cell.Range.Paragraphs[1];
            float center = inner / 2f + SingleLineOffsetPt;
            float line = LineLength(variation);
            if (IsTopNoPadding(cell) && SameExact(p.Format, center, 0f, line) && HasLeftLine(p.Format)) return;
            Log($"ligne pleine r{r}c{c} milieu={center:0.#} interligne={line}");
            SetTopNoPadding(cell);
            SetSegment(p, center, line);
        }

        /// <summary>Case « 0 » d'une colonne à séparation : bout / 0 / bout.</summary>
        private static void ApplyZeroOnLine(Word.Cell cell, int r, int c, float inner, bool variation)
        {
            var paras = cell.Range.Paragraphs;
            bool shaped = paras.Count == 3
                && ParaText(paras[1]) == "" && ParaText(paras[2]) == "0" && ParaText(paras[3]) == "";
            float center = inner / 2f + SingleLineOffsetPt;
            float zeroLine = variation ? ZeroLineVariationPt : ZeroLineSignPt;
            float segment = variation
                ? (StudyTableTemplate.VariationRowHeightPt - zeroLine) / 2f
                : ZeroSegmentSignPt;
            if (shaped && IsTopNoPadding(cell)
                && SameExact(paras[1].Format, center, 0f, segment) && HasLeftLine(paras[1].Format)
                && SameExact(paras[3].Format, center, 0f, segment) && HasLeftLine(paras[3].Format)
                && SameExact(paras[2].Format, 0f, 0f, zeroLine) && !HasLeftLine(paras[2].Format))
                return;

            Log($"0 sur la ligne r{r}c{c} milieu={center:0.#} bouts={segment:0.#}");
            if (!shaped) SetCellText(cell, "\r0\r");
            paras = cell.Range.Paragraphs;
            SetTopNoPadding(cell);
            SetSegment(paras[1], center, segment);
            var zero = paras[2].Format;
            zero.Borders[Word.WdBorderType.wdBorderLeft].LineStyle = Word.WdLineStyle.wdLineStyleNone;
            SetExact(zero, 0f, 0f, zeroLine);
            zero.Alignment = Word.WdParagraphAlignment.wdAlignParagraphCenter;
            SetSegment(paras[3], center, segment);
        }

        /// <summary>Bout de ligne : paragraphe à bordure gauche au milieu de la case.</summary>
        private static void SetSegment(Word.Paragraph p, float center, float length)
        {
            var pf = p.Format;
            SetExact(pf, center, 0f, length);
            pf.Alignment = Word.WdParagraphAlignment.wdAlignParagraphLeft;
            var b = pf.Borders[Word.WdBorderType.wdBorderLeft];
            b.LineStyle = Word.WdLineStyle.wdLineStyleSingle;
            b.LineWidth = Word.WdLineWidth.wdLineWidth050pt;
            pf.Borders.DistanceFromLeft = 0;
        }

        /// <summary>Frontière qui n'a plus de ligne : un seul paragraphe
        /// normal (les bouts de ligne vides sont retirés, sauf s'il y a une
        /// équation dans la case), bordure directe retirée.</summary>
        private static void RevertToNormal(Word.Table table, Word.Cell cell, int r, int c)
        {
            var paras = cell.Range.Paragraphs;
            bool styled = paras.Count > 1;
            foreach (Word.Paragraph p in paras)
            {
                var pf = p.Format;
                if (pf.LeftIndent != 0f || pf.LineSpacingRule == Word.WdLineSpacing.wdLineSpaceExactly
                    || HasLeftLine(pf))
                { styled = true; break; }
            }
            if (!styled) return;

            Log($"frontière r{r}c{c} redevient normale");
            if (paras.Count > 1 && cell.Range.OMaths.Count == 0) SetCellText(cell, CellText(cell));
            cell.TopPadding = table.TopPadding;
            cell.BottomPadding = table.BottomPadding;
            foreach (Word.Paragraph p in cell.Range.Paragraphs)
            {
                var pf = p.Format;
                pf.Borders[Word.WdBorderType.wdBorderLeft].LineStyle = Word.WdLineStyle.wdLineStyleNone;
                pf.LeftIndent = 0f;
                pf.RightIndent = 0f;
                pf.FirstLineIndent = 0f;
                pf.SpaceBefore = 2f;
                pf.SpaceAfter = 2f;
                pf.LineSpacingRule = Word.WdLineSpacing.wdLineSpaceSingle;
                pf.Alignment = Word.WdParagraphAlignment.wdAlignParagraphCenter;
            }
        }

        private static string ParaText(Word.Paragraph p) =>
            (p.Range.Text ?? "").Replace("\r", "").Replace("\a", "").Trim();

        private static bool HasLeftLine(Word.ParagraphFormat pf) =>
            pf.Borders[Word.WdBorderType.wdBorderLeft].LineStyle != Word.WdLineStyle.wdLineStyleNone;

        private static bool SameExact(Word.ParagraphFormat pf, float left, float right, float line) =>
            Math.Abs(pf.LeftIndent - left) < 0.1f && Math.Abs(pf.RightIndent - right) < 0.1f
            && pf.LineSpacingRule == Word.WdLineSpacing.wdLineSpaceExactly
            && Math.Abs(pf.LineSpacing - line) < 0.1f;

        private static void SetExact(Word.ParagraphFormat pf, float left, float right, float line)
        {
            pf.SpaceBefore = 0f;
            pf.SpaceAfter = 0f;
            pf.FirstLineIndent = 0f;
            pf.LeftIndent = left;
            pf.RightIndent = right;
            pf.LineSpacingRule = Word.WdLineSpacing.wdLineSpaceExactly;
            pf.LineSpacing = line;
        }

        private static bool IsTopNoPadding(Word.Cell cell) =>
            cell.TopPadding == 0f && cell.BottomPadding == 0f
            && cell.VerticalAlignment == Word.WdCellVerticalAlignment.wdCellAlignVerticalTop;

        private static void SetTopNoPadding(Word.Cell cell)
        {
            cell.TopPadding = 0f;
            cell.BottomPadding = 0f;
            cell.VerticalAlignment = Word.WdCellVerticalAlignment.wdCellAlignVerticalTop;
        }

        // ── Ajustement de la police ──────────────────────────────────────

        /// <summary>
        /// Chaque frontière (hors valeurs interdites) doit tenir sur une
        /// ligne : police réduite jusqu'à <see cref="StudyTableTemplate.MinFontSizePt"/>,
        /// puis colonne élargie par pas jusqu'au plafond.
        /// </summary>
        private static void FitFrontiers(Word.Document doc, Word.Table table, StudyTableStructure s,
                                         Dictionary<int, float> widened)
        {
            for (int c = 1; c <= s.Columns.Count; c++)
                if (s.Columns[c - 1] == StudyColumnKind.Frontier) FitColumn(doc, table, s, c, widened);
        }

        /// <summary>
        /// Police ajustée dans une colonne de frontière ; si une case ne tient
        /// pas même à la taille plancher, la colonne s'élargit par pas.
        /// True si les largeurs du tableau ont changé.
        /// </summary>
        private static bool FitColumn(Word.Document doc, Word.Table table, StudyTableStructure s, int c,
                                      Dictionary<int, float> widened)
        {
            bool widthsChanged = false;
            for (int iter = 0; iter < MaxWidenIterations; iter++)
            {
                bool allFit = true;
                int maxLines = 1;
                for (int r = 1; r <= s.Rows.Count; r++)
                {
                    // Valeur interdite, 0 sur la ligne, ligne pleine : pas de police à ajuster.
                    if (r > 1 && (s.Forbidden.Contains(c - 1) || s.SeparatorLines.Contains(c - 1))
                        && ShapeOf(s, r, c, CellText(table.Cell(r, c))) != FrontierShape.Normal) continue;
                    if (!FitCell(doc, table.Cell(r, c), out int lines))
                    {
                        allFit = false;
                        maxLines = Math.Max(maxLines, lines);
                    }
                }
                if (allFit) break;
                var widths = StudyTableTemplate.ColumnWidths(s.Columns, widened);
                float current = widened.TryGetValue(c - 1, out float w) ? w : widths[c - 1];
                if (current >= StudyTableTemplate.MaxWidenedFrontierPct)
                {
                    Log($"fit c{c} : ne tient pas même élargie à {current:0.#}%");
                    break;
                }
                // Saut d'après le débordement mesuré (3 lignes → ~3 fois plus
                // large) au lieu de pas fixes : chaque pas redimensionne tout le
                // tableau et remesure. Les pas fixes ne servent qu'à finir.
                float target = Math.Max(current + StudyTableTemplate.WidenStepPct, current * maxLines);
                widened[c - 1] = Math.Min(StudyTableTemplate.MaxWidenedFrontierPct, target);
                Log($"fit c{c} : élargie {current:0.#}% -> {widened[c - 1]:0.#}%");
                ApplyWidths(table, s, widened);
                widthsChanged = true;
            }
            return widthsChanged;
        }

        /// <summary>
        /// Ajuste la taille d'une case pour que son contenu tienne sur une
        /// ligne, en partant de la taille ACTUELLE : on descend tant que ça
        /// déborde, on remonte vers la taille normale tant que ça tient.
        /// N'écrit rien si la taille actuelle est déjà la bonne. True si le
        /// contenu tient. Détection : numéro de ligne (et position verticale)
        /// au début et à la fin du contenu (POC : fiable pour texte et OMath).
        /// </summary>
        public static bool FitCell(Word.Document doc, Word.Cell cell) => FitCell(doc, cell, out _);

        /// <param name="lines">Lignes occupées à la taille finale (1 si ça tient).</param>
        public static bool FitCell(Word.Document doc, Word.Cell cell, out int lines)
        {
            lines = 1;
            string where = "?";
            try
            {
                where = $"r{cell.RowIndex}c{cell.ColumnIndex}";
                var content = cell.Range;
                content.MoveEnd(Word.WdUnits.wdCharacter, -1);
                if (content.Start >= content.End) return true;

                float body = StudyTableTemplate.BodyFontSizePt, min = StudyTableTemplate.MinFontSizePt;
                float start = content.Font.Size;
                if (start > body || start < min) start = body;  // taille mixte (9999999) ou hors bornes
                float size = start;
                if (Math.Abs(content.Font.Size - size) > 0.05f) content.Font.Size = size;

                bool wraps = Wraps(doc, content, out string m0, out lines);
                if (wraps)
                {
                    while (wraps && size > min)
                    {
                        size -= 1f;
                        content.Font.Size = size;
                        wraps = Wraps(doc, content, out m0, out lines);
                    }
                }
                else
                {
                    while (size < body)
                    {
                        content.Font.Size = size + 1f;
                        if (Wraps(doc, content, out _, out _)) { content.Font.Size = size; break; }
                        size += 1f;
                    }
                }
                if (Math.Abs(size - start) > 0.05f || wraps)
                    Log($"fit {where} '{Short(content.Text)}' {start}pt -> {size}pt {(wraps ? "DÉBORDE" : "ok")} ({m0})");
                return !wraps;
            }
            catch (Exception ex)
            {
                Log($"fit_error {where}: {ex.Message}");
                return true;
            }
        }

        private static bool Wraps(Word.Document doc, Word.Range content, out string measure, out int lines)
        {
            var a = doc.Range(content.Start, content.Start);
            var b = doc.Range(content.End, content.End);
            int la = Convert.ToInt32(a.get_Information(Word.WdInformation.wdFirstCharacterLineNumber));
            int lb = Convert.ToInt32(b.get_Information(Word.WdInformation.wdFirstCharacterLineNumber));
            float ya = Convert.ToSingle(a.get_Information(Word.WdInformation.wdVerticalPositionRelativeToPage));
            float yb = Convert.ToSingle(b.get_Information(Word.WdInformation.wdVerticalPositionRelativeToPage));
            measure = $"lignes {la}-{lb} y {ya:0.#}-{yb:0.#}";
            lines = Math.Max(1, lb - la + 1);
            return la != lb || Math.Abs(yb - ya) > 2f;
        }

        // ── Utilitaires ──────────────────────────────────────────────────

        public static string CellText(Word.Cell cell)
        {
            // Sans les marques de paragraphe : une case « 0 sur la ligne » est
            // faite de trois paragraphes (bout / 0 / bout).
            try { return (cell.Range.Text ?? "").Replace("\r", "").Replace("\a", "").Trim(); }
            catch { return ""; }
        }

        /// <summary>Remplace le contenu d'une case (hors marque de fin) et
        /// retourne la plage écrite.</summary>
        public static Word.Range SetCellText(Word.Cell cell, string text)
        {
            var content = cell.Range;
            content.MoveEnd(Word.WdUnits.wdCharacter, -1);
            content.Text = text ?? "";
            return content;
        }
    }
}
