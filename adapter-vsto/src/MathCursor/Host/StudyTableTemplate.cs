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

using System.Collections.Generic;

namespace MathCursor.Host
{
    internal enum StudyTableKind { Sign, Variation }

    internal enum CellVAlign { Center, Top, Bottom }

    /// <summary>Une cellule du gabarit : texte simple (pas d'OMath).</summary>
    internal sealed class StudyTableCell
    {
        public string Text { get; }
        public bool Italic { get; }
        public CellVAlign VAlign { get; }
        /// <summary>Flèche de variation (↗ / ↘) : posée en plus grand corps.</summary>
        public bool IsArrow { get; }

        public StudyTableCell(string text, bool italic = false,
                              CellVAlign vAlign = CellVAlign.Center, bool isArrow = false)
        {
            Text = text ?? "";
            Italic = italic;
            VAlign = vAlign;
            IsArrow = isArrow;
        }
    }

    /// <summary>
    /// Gabarit PUR (zéro interop) d'un tableau de signe / de variation, appliqué
    /// dans Word par <see cref="StudyTableInserter"/>. Colonne 0 = étiquettes ;
    /// colonnes 1..5 alternent valeur / intervalle / valeur / intervalle /
    /// valeur. Pré-rempli d'un exemple cohérent (+ 0 −, maximum au milieu) que
    /// l'élève modifie. Cf. ADR 2026-10-16-Feat-sign-variation-table-insert.
    /// </summary>
    internal sealed class StudyTableTemplate
    {
        public const string ArrowUp = "↗";   // ↗
        public const string ArrowDown = "↘"; // ↘
        public const string ArrowRight = "→"; // → (fonction constante)

        /// <summary>Choix de la palette de flèches, dans l'ordre affiché.
        /// Cf. ADR 2026-10-19-UX-variation-arrow-palette.</summary>
        public static readonly IReadOnlyList<string> ArrowChoices = new[] { ArrowUp, ArrowDown, ArrowRight };

        /// <summary>Style de paragraphe qui marque une case de flèche (porte
        /// aussi la taille des flèches).</summary>
        public const string ArrowStyleName = "MathCursor Flèche";
        public const float ArrowFontSizePt = 20f;
        private const string Minus = "−";
        private const string Prime = "′";

        /// <summary>Nombre de colonnes valeur/intervalle (hors étiquettes).</summary>
        public const int ValueColumnCount = 5;

        public StudyTableKind Kind { get; }
        public IReadOnlyList<IReadOnlyList<StudyTableCell>> Rows { get; }
        /// <summary>Index de la ligne haute (f(x) d'un tableau de variation), -1 sinon.</summary>
        public int TallRowIndex { get; }

        public int ColumnCount => ValueColumnCount + 1;

        private StudyTableTemplate(StudyTableKind kind,
                                   IReadOnlyList<IReadOnlyList<StudyTableCell>> rows,
                                   int tallRowIndex)
        {
            Kind = kind;
            Rows = rows;
            TallRowIndex = tallRowIndex;
        }

        /// <summary>Colonnes impaires (1, 3, 5) = valeurs de x ; paires = intervalles.</summary>
        public static bool IsValueColumn(int column) => column >= 1 && column % 2 == 1;

        public static StudyTableTemplate Build(StudyTableKind kind)
        {
            var xRow = Row(
                new StudyTableCell("x", italic: true),
                new StudyTableCell(Minus + "∞"),
                Empty(), Empty(), Empty(),
                new StudyTableCell("+∞"));

            var signRow = new[]
            {
                Empty(), new StudyTableCell("+"), new StudyTableCell("0"),
                new StudyTableCell(Minus), Empty(),
            };

            if (kind == StudyTableKind.Sign)
            {
                var rows = new List<IReadOnlyList<StudyTableCell>>
                {
                    xRow,
                    Row(new StudyTableCell("f(x)", italic: true), signRow),
                };
                return new StudyTableTemplate(kind, rows, tallRowIndex: -1);
            }

            var variation = new List<IReadOnlyList<StudyTableCell>>
            {
                xRow,
                Row(new StudyTableCell("f" + Prime + "(x)", italic: true), signRow),
                Row(new StudyTableCell("f(x)", italic: true),
                    new StudyTableCell("", vAlign: CellVAlign.Bottom),
                    new StudyTableCell(ArrowUp, isArrow: true),
                    new StudyTableCell("", vAlign: CellVAlign.Top),
                    new StudyTableCell(ArrowDown, isArrow: true),
                    new StudyTableCell("", vAlign: CellVAlign.Bottom)),
            };
            return new StudyTableTemplate(kind, variation, tallRowIndex: 2);
        }

        private static StudyTableCell Empty() => new StudyTableCell("");

        private static IReadOnlyList<StudyTableCell> Row(StudyTableCell label, params StudyTableCell[] values)
        {
            var row = new List<StudyTableCell>(values.Length + 1) { label };
            row.AddRange(values);
            return row;
        }
    }
}
