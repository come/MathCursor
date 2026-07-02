// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme Percin
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

using System.Collections.Generic;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MathCursor.TutorialBuilder.Models;

namespace MathCursor.TutorialBuilder;

/// <summary>
/// Rend une <see cref="TutorialSpec"/> en .docx via objets OpenXML typés.
///
/// Règle dure (anti-MC0001) : aucune manipulation de XML brut, aucune regex.
/// Tout passe par les objets de <c>DocumentFormat.OpenXml.Wordprocessing</c>.
/// </summary>
public static class DocxRenderer
{
    public static void Render(TutorialSpec spec, string outputPath)
    {
        using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        var body = mainPart.Document.Body!;

        AddStyles(mainPart);

        body.AppendChild(BuildHeading(spec.Title, level: 1));
        body.AppendChild(BuildProseParagraph(spec.Intro));
        body.AppendChild(BuildSpacerParagraph());

        var tryHere = string.IsNullOrWhiteSpace(spec.TryHere) ? "↑ Essaie ici ↑" : spec.TryHere!;

        foreach (var section in spec.Sections)
        {
            body.AppendChild(BuildHeading(section.Title, level: 2));
            body.AppendChild(BuildProseParagraph(section.Intro));
            // Section "meta" (items vides) = juste l'intro explicative, pas
            // de tableau d'exercices.
            if (section.Items.Count > 0)
            {
                body.AppendChild(BuildItemsTable(section.Items, tryHere));
            }
            if (!string.IsNullOrWhiteSpace(section.Note))
            {
                body.AppendChild(BuildNoteParagraph(section.Note!));
            }
            body.AppendChild(BuildSpacerParagraph());
        }

        // Définit la taille de page (A4) et les marges pour le doc final.
        body.AppendChild(BuildSectionProperties());
    }

    private static SectionProperties BuildSectionProperties()
    {
        return new SectionProperties(
            // A4 portrait : 11906 × 16838 twips (1/20 point).
            new PageSize { Width = 11906, Height = 16838 },
            // Marges 1500 (~2.6 cm) — un peu plus larges que le default Word
            // pour laisser respirer le tableau.
            new PageMargin
            {
                Top = 1500,
                Right = 1500,
                Bottom = 1500,
                Left = 1500,
                Header = 720,
                Footer = 720,
                Gutter = 0,
            });
    }

    private static void AddStyles(MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles(
            BuildHeadingStyle("Heading1", "Heading 1", fontSizeHalfPt: 40, bold: true,
                spacingBeforeDxa: 480, spacingAfterDxa: 240),
            BuildHeadingStyle("Heading2", "Heading 2", fontSizeHalfPt: 32, bold: true,
                spacingBeforeDxa: 480, spacingAfterDxa: 180));
        stylesPart.Styles.Save();
    }

    private static Style BuildHeadingStyle(
        string id, string name, int fontSizeHalfPt, bool bold,
        int spacingBeforeDxa, int spacingAfterDxa)
    {
        var runProps = new StyleRunProperties(
            new FontSize { Val = fontSizeHalfPt.ToString() });
        if (bold) runProps.AppendChild(new Bold());

        var paraProps = new StyleParagraphProperties(
            new SpacingBetweenLines
            {
                Before = spacingBeforeDxa.ToString(),
                After = spacingAfterDxa.ToString(),
            });

        return new Style(
            new StyleName { Val = name },
            new BasedOn { Val = "Normal" },
            new NextParagraphStyle { Val = "Normal" },
            new UIPriority { Val = 9 },
            new PrimaryStyle(),
            paraProps,
            runProps)
        {
            Type = StyleValues.Paragraph,
            StyleId = id,
        };
    }

    private static Paragraph BuildHeading(string text, int level)
    {
        return new Paragraph(
            new ParagraphProperties(
                new ParagraphStyleId { Val = $"Heading{level}" }),
            new Run(new Text(text)));
    }

    private static Paragraph BuildSpacerParagraph()
    {
        // Paragraphe vide avec un peu de hauteur pour aérer entre sections.
        return new Paragraph(
            new ParagraphProperties(
                new SpacingBetweenLines { After = "200" }));
    }

    private static Paragraph BuildProseParagraph(string text)
    {
        var paragraph = new Paragraph(
            new ParagraphProperties(
                new SpacingBetweenLines { After = "160", Line = "300", LineRule = LineSpacingRuleValues.Auto }));
        foreach (var (segment, isCode) in SplitOnBackticks(text))
        {
            if (segment.Length == 0) continue;
            paragraph.AppendChild(BuildRun(segment, isCode));
        }
        return paragraph;
    }

    /// <summary>Astuce de section, SOUS le tableau : italique gris, garde le
    /// rendu `code` des backticks (ex. `Ctrl+Espace`).</summary>
    private static Paragraph BuildNoteParagraph(string text)
    {
        var paragraph = new Paragraph(
            new ParagraphProperties(
                new SpacingBetweenLines { Before = "80", After = "160", Line = "300", LineRule = LineSpacingRuleValues.Auto }));
        foreach (var (segment, isCode) in SplitOnBackticks(text))
        {
            if (segment.Length == 0) continue;
            var run = BuildRun(segment, isCode);
            run.RunProperties ??= new RunProperties();
            run.RunProperties.AppendChild(new Italic());
            run.RunProperties.AppendChild(new Color { Val = "595959" });
            paragraph.AppendChild(run);
        }
        return paragraph;
    }

    private static Run BuildRun(string text, bool isCode)
    {
        var run = new Run();
        if (isCode)
        {
            run.RunProperties = new RunProperties(
                new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" },
                new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = "F0F0F0" });
        }
        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    private static IEnumerable<(string segment, bool isCode)> SplitOnBackticks(string text)
    {
        var parts = text.Split('`');
        for (var i = 0; i < parts.Length; i++)
        {
            yield return (parts[i], isCode: i % 2 == 1);
        }
    }

    private static Table BuildItemsTable(IReadOnlyList<TutorialItem> items, string tryHere)
    {
        var table = new Table(
            new TableProperties(
                BuildBorders(),
                new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                new TableLayout { Type = TableLayoutValues.Fixed }),
            new TableGrid(
                new GridColumn { Width = "5500" },
                new GridColumn { Width = "4500" }));

        foreach (var item in items)
        {
            table.AppendChild(BuildItemRow(item, tryHere));
        }
        return table;
    }

    private static TableBorders BuildBorders()
    {
        const uint size = 4u;
        return new TableBorders(
            new TopBorder { Val = BorderValues.Single, Size = size, Color = "BFBFBF" },
            new BottomBorder { Val = BorderValues.Single, Size = size, Color = "BFBFBF" },
            new LeftBorder { Val = BorderValues.Single, Size = size, Color = "BFBFBF" },
            new RightBorder { Val = BorderValues.Single, Size = size, Color = "BFBFBF" },
            new InsideHorizontalBorder { Val = BorderValues.Single, Size = size, Color = "BFBFBF" },
            new InsideVerticalBorder { Val = BorderValues.Single, Size = size, Color = "BFBFBF" });
    }

    private static TableRow BuildItemRow(TutorialItem item, string tryHere)
    {
        return new TableRow(
            new TableRowProperties(
                // Hauteur minimum ~2.5 cm pour offrir un vrai espace de saisie
                // côté droit, même si la consigne est courte.
                new TableRowHeight { Val = 1500, HeightType = HeightRuleValues.AtLeast },
                new CantSplit()),
            BuildInstructionCell(item),
            BuildTryCell(tryHere));
    }

    private static TableCellProperties CellProperties(int widthDxa, TableVerticalAlignmentValues vAlign)
    {
        return new TableCellProperties(
            new TableCellWidth { Width = widthDxa.ToString(), Type = TableWidthUnitValues.Dxa },
            // Padding interne généreux pour aérer.
            new TableCellMargin(
                new TopMargin { Width = "180", Type = TableWidthUnitValues.Dxa },
                new BottomMargin { Width = "180", Type = TableWidthUnitValues.Dxa },
                new LeftMargin { Width = "200", Type = TableWidthUnitValues.Dxa },
                new RightMargin { Width = "200", Type = TableWidthUnitValues.Dxa }),
            new TableCellVerticalAlignment { Val = vAlign });
    }

    private static TableCell BuildInstructionCell(TutorialItem item)
    {
        var cell = new TableCell(
            CellProperties(5500, TableVerticalAlignmentValues.Center),
            BuildProseParagraph(item.Instruction));
        // Astuce optionnelle SOUS la consigne (gris italique, ex. « ou : @p »).
        if (!string.IsNullOrWhiteSpace(item.Tip))
            cell.AppendChild(BuildTipParagraph(item.Tip!));
        // 2e ligne grise SOUS le tip (ex. « (note : Ctrl+Espace…) ») — même style.
        if (!string.IsNullOrWhiteSpace(item.Note))
            cell.AppendChild(BuildTipParagraph(item.Note!));
        return cell;
    }

    /// <summary>Ligne d'astuce sous la consigne : gris italique, plus petite,
    /// garde le rendu `code` des backticks. Display only (non validé moteur).</summary>
    private static Paragraph BuildTipParagraph(string text)
    {
        var paragraph = new Paragraph(
            new ParagraphProperties(
                new SpacingBetweenLines { Before = "60", After = "0" }));
        foreach (var (segment, isCode) in SplitOnBackticks(text))
        {
            if (segment.Length == 0) continue;
            var run = BuildRun(segment, isCode);
            run.RunProperties ??= new RunProperties();
            run.RunProperties.AppendChild(new Italic());
            run.RunProperties.AppendChild(new Color { Val = "7A7A7A" });
            run.RunProperties.AppendChild(new FontSize { Val = "18" });   // 9 pt
            paragraph.AppendChild(run);
        }
        return paragraph;
    }

    /// <summary>
    /// Cellule de droite : paragraphe vide en haut (= le caret s'y pose
    /// naturellement au clic, Word focus sur le 1er paragraphe d'une cellule)
    /// + hint <c>↑ Essaie ici ↑</c> en gris italique centré en bas. Le hint
    /// reste affiché tant que l'utilisateur écrit dans le paragraphe du dessus.
    /// </summary>
    private static TableCell BuildTryCell(string tryHere)
    {
        var hintRun = new Run(
            new RunProperties(
                new Italic(),
                new Color { Val = "BFBFBF" }),
            new Text(tryHere) { Space = SpaceProcessingModeValues.Preserve });

        var hintParagraph = new Paragraph(
            new ParagraphProperties(
                new Justification { Val = JustificationValues.Center },
                new SpacingBetweenLines { Before = "0", After = "0" }),
            hintRun);

        var cell = new TableCell(CellProperties(4500, TableVerticalAlignmentValues.Top));

        // Plusieurs paragraphes vides empilés : un clic n'importe où dans
        // la zone de saisie tombe dans le paragraphe le plus proche, pas
        // dans le hint en bas. Hauteur totale ~5 lignes ≈ 2 cm.
        for (var i = 0; i < 5; i++)
        {
            cell.AppendChild(new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines { After = "0", Line = "300", LineRule = LineSpacingRuleValues.Auto })));
        }
        cell.AppendChild(hintParagraph);
        return cell;
    }
}
