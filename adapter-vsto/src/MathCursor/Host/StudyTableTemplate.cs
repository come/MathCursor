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

namespace MathCursor.Host
{
    internal enum StudyTableKind { Sign, Variation }

    /// <summary>Type d'une ligne, porté par le style de sa case d'étiquette.</summary>
    internal enum StudyRowKind { None, X, Sign, Variation }

    /// <summary>Type d'une colonne, lu sur la ligne x : étiquettes, frontière
    /// (valeur de x, 0, extremum, valeur interdite), intervalle (signe, flèche).</summary>
    internal enum StudyColumnKind { None, Label, Frontier, Interval }

    /// <summary>Rôle d'une case = type de ligne × type de colonne.</summary>
    internal enum StudyCellRole { None, Label, Frontier, ForbiddenFrontier, Interval, Arrow }

    internal enum CellVAlign { Center, Top, Bottom }

    /// <summary>Actions proposées par la popup du tableau.</summary>
    internal enum StudyTableAction
    {
        ArrowUp, ArrowDown, ArrowRight,
        Zero, ToggleForbidden,
        AddValueLeft, AddValueRight, DeleteValue,
        AddSignRow, AddVariationRow, DeleteRow,
    }

    /// <summary>Une cellule du gabarit initial : texte simple, ou équation
    /// (<see cref="IsMath"/> : étiquette de fonction posée en OMath).</summary>
    internal sealed class StudyTableCell
    {
        public string Text { get; }
        public bool Italic { get; }
        public bool IsMath { get; }

        public StudyTableCell(string text, bool italic = false, bool isMath = false)
        {
            Text = text ?? "";
            Italic = italic;
            IsMath = isMath;
        }
    }

    /// <summary>
    /// Modèle PUR (zéro interop) des tableaux de signe / de variation :
    /// gabarit initial, rôles des cases (portés dans Word par des styles de
    /// paragraphe), choix de la popup par rôle, largeurs, plans d'insertion /
    /// suppression de colonnes, alignement des extremums d'après les flèches.
    /// Colonne 0 = étiquettes ; ensuite frontière / intervalle en alternance,
    /// une frontière à chaque bout.
    /// Cf. ADR 2026-10-16-Feat-sign-variation-table-insert,
    /// 2026-10-19-Feat-study-table-cell-roles-contextual-popup.
    /// </summary>
    internal sealed class StudyTableTemplate
    {
        public const string ArrowUp = "↗";   // ↗
        public const string ArrowDown = "↘"; // ↘
        public const string ArrowRight = "→"; // → (fonction constante)
        private const string Minus = "−";
        private const string Prime = "′";

        // ── Styles de rôle ───────────────────────────────────────────────
        public const string LabelXStyle = "MathCursor Ligne x";
        public const string LabelSignStyle = "MathCursor Ligne signe";
        public const string LabelVariationStyle = "MathCursor Ligne variation";
        public const string FrontierStyle = "MathCursor Frontière";
        public const string ForbiddenStyle = "MathCursor Valeur interdite";
        public const string IntervalStyle = "MathCursor Intervalle";
        /// <summary>Ligne x : styles dédiés (gras bleu nuit portés par le style,
        /// cf. passe visuelle 2026-10-20) — ce qu'on y tape en hérite sans
        /// mise en forme directe à réécrire.</summary>
        public const string HeaderFrontierStyle = "MathCursor En-tête valeur";
        public const string HeaderIntervalStyle = "MathCursor En-tête intervalle";
        public const string ArrowStyleName = "MathCursor Flèche";

        public const float BodyFontSizePt = 11f;
        public const float MinFontSizePt = 9f;
        // Flèches fines « manuel » (passe visuelle 2026-10-20) : Cambria Math
        // (livrée avec Office) en gris foncé, plutôt que la police du texte.
        public const float ArrowFontSizePt = 26f;
        public const string ArrowFontName = "Cambria Math";
        /// <summary>Gris foncé 404040, en BGR (format des couleurs Word).</summary>
        public const int ArrowColorBgr = 0x404040;
        /// <summary>En-tête (ligne x) et étiquettes : fond bleu-gris EEF2F8,
        /// texte gras bleu nuit 1F3A5F — valeurs BGR (format Word).</summary>
        public const int HeaderBackgroundBgr = 0xF8F2EE;
        public const int HeaderTextBgr = 0x5F3A1F;
        public const float VariationRowHeightPt = 54f;

        // ── Largeurs (% de la largeur du tableau) ────────────────────────
        public const float LabelColumnPct = 16f;
        public const float MaxFrontierPct = 10f;
        /// <summary>Part maximale des frontières dans ce qui reste après les
        /// étiquettes : au-delà, les frontières rétrécissent.</summary>
        private const float FrontierShare = 0.45f;
        public const float MaxWidenedFrontierPct = 25f;
        public const float WidenStepPct = 3f;

        /// <summary>Nombre de colonnes du gabarit initial (hors étiquettes).</summary>
        public const int ValueColumnCount = 5;

        public StudyTableKind Kind { get; }
        public IReadOnlyList<StudyRowKind> RowKinds { get; }
        public IReadOnlyList<IReadOnlyList<StudyTableCell>> Rows { get; }

        public int ColumnCount => ValueColumnCount + 1;

        private StudyTableTemplate(StudyTableKind kind, IReadOnlyList<StudyRowKind> rowKinds,
                                   IReadOnlyList<IReadOnlyList<StudyTableCell>> rows)
        {
            Kind = kind;
            RowKinds = rowKinds;
            Rows = rows;
        }

        // ── Gabarit initial ──────────────────────────────────────────────

        public static StudyTableTemplate Build(StudyTableKind kind)
        {
            var xRow = Row(
                new StudyTableCell("x", italic: true),
                new StudyTableCell(Minus + "∞"),
                Empty(), new StudyTableCell("0"), Empty(),
                new StudyTableCell("+∞"));

            var signValues = new[]
            {
                Empty(), new StudyTableCell("+"), new StudyTableCell("0"),
                new StudyTableCell(Minus), Empty(),
            };

            if (kind == StudyTableKind.Sign)
            {
                return new StudyTableTemplate(kind,
                    new[] { StudyRowKind.X, StudyRowKind.Sign },
                    new List<IReadOnlyList<StudyTableCell>>
                    {
                        xRow,
                        Row(new StudyTableCell("f(x)", isMath: true), signValues),
                    });
            }

            return new StudyTableTemplate(kind,
                new[] { StudyRowKind.X, StudyRowKind.Sign, StudyRowKind.Variation },
                new List<IReadOnlyList<StudyTableCell>>
                {
                    xRow,
                    Row(new StudyTableCell("f" + Prime + "(x)", isMath: true), signValues),
                    Row(new StudyTableCell("f(x)", isMath: true),
                        Empty(), new StudyTableCell(ArrowUp), Empty(),
                        new StudyTableCell(ArrowDown), Empty()),
                });
        }

        private static StudyTableCell Empty() => new StudyTableCell("");

        private static IReadOnlyList<StudyTableCell> Row(StudyTableCell label, params StudyTableCell[] values)
        {
            var row = new List<StudyTableCell>(values.Length + 1) { label };
            row.AddRange(values);
            return row;
        }

        // ── Rôles ────────────────────────────────────────────────────────

        /// <summary>Type de colonne du gabarit (index 0-based) : 0 = étiquettes,
        /// impaires = frontières, paires = intervalles.</summary>
        public static StudyColumnKind TemplateColumnKind(int column) =>
            column == 0 ? StudyColumnKind.Label
            : column % 2 == 1 ? StudyColumnKind.Frontier
            : StudyColumnKind.Interval;

        public static bool IsValueColumn(int column) => TemplateColumnKind(column) == StudyColumnKind.Frontier;

        public static StudyCellRole RoleFor(StudyRowKind row, StudyColumnKind column, bool forbidden = false)
        {
            if (row == StudyRowKind.None || column == StudyColumnKind.None) return StudyCellRole.None;
            switch (column)
            {
                case StudyColumnKind.Label: return StudyCellRole.Label;
                case StudyColumnKind.Frontier:
                    return forbidden && row != StudyRowKind.X ? StudyCellRole.ForbiddenFrontier : StudyCellRole.Frontier;
                default:
                    return row == StudyRowKind.Variation ? StudyCellRole.Arrow : StudyCellRole.Interval;
            }
        }

        public static string StyleFor(StudyCellRole role, StudyRowKind row)
        {
            switch (role)
            {
                case StudyCellRole.Label:
                    return row == StudyRowKind.X ? LabelXStyle
                        : row == StudyRowKind.Variation ? LabelVariationStyle
                        : LabelSignStyle;
                case StudyCellRole.Frontier: return row == StudyRowKind.X ? HeaderFrontierStyle : FrontierStyle;
                case StudyCellRole.ForbiddenFrontier: return ForbiddenStyle;
                case StudyCellRole.Interval: return row == StudyRowKind.X ? HeaderIntervalStyle : IntervalStyle;
                case StudyCellRole.Arrow: return ArrowStyleName;
                default: return null;
            }
        }

        public static StudyRowKind RowKindFromLabelStyle(string styleName)
        {
            switch (styleName)
            {
                case LabelXStyle: return StudyRowKind.X;
                case LabelSignStyle: return StudyRowKind.Sign;
                case LabelVariationStyle: return StudyRowKind.Variation;
                default: return StudyRowKind.None;
            }
        }

        /// <summary>Type de colonne d'après le style de sa case sur la ligne x.</summary>
        public static StudyColumnKind ColumnKindFromXRowStyle(string styleName)
        {
            switch (styleName)
            {
                case LabelXStyle: return StudyColumnKind.Label;
                case HeaderFrontierStyle:
                case FrontierStyle: return StudyColumnKind.Frontier;   // ancien style de ligne x
                case HeaderIntervalStyle:
                case IntervalStyle: return StudyColumnKind.Interval;
                default: return StudyColumnKind.None;
            }
        }

        // ── Choix de la popup ────────────────────────────────────────────

        /// <summary>
        /// Choix proposés pour une case. Vide = pas de popup (intervalle de
        /// signe : on tape + / −). <paramref name="canDeleteValue"/> = plus de
        /// deux frontières ; <paramref name="canDeleteRow"/> = ligne autre que
        /// x et pas la dernière ligne sous x. Ligne x : AUCUNE popup — on
        /// ajoute une valeur en la tapant entre deux valeurs, la valeur
        /// interdite se pose depuis les frontières du dessous.
        /// </summary>
        public static IReadOnlyList<StudyTableAction> ChoicesFor(
            StudyCellRole role, StudyRowKind row, bool canDeleteValue, bool canDeleteRow)
        {
            var list = new List<StudyTableAction>();
            switch (role)
            {
                case StudyCellRole.Label:
                    list.Add(StudyTableAction.AddSignRow);
                    list.Add(StudyTableAction.AddVariationRow);
                    if (canDeleteRow && row != StudyRowKind.X) list.Add(StudyTableAction.DeleteRow);
                    break;
                case StudyCellRole.Frontier when row == StudyRowKind.X:
                    break;
                case StudyCellRole.Frontier:
                    list.Add(StudyTableAction.Zero);
                    list.Add(StudyTableAction.ToggleForbidden);
                    break;
                case StudyCellRole.ForbiddenFrontier:
                    list.Add(StudyTableAction.ToggleForbidden);
                    break;
                case StudyCellRole.Arrow:
                    list.Add(StudyTableAction.ArrowUp);
                    list.Add(StudyTableAction.ArrowDown);
                    list.Add(StudyTableAction.ArrowRight);
                    break;
            }
            return list;
        }

        public static string ArrowText(StudyTableAction action) =>
            action == StudyTableAction.ArrowUp ? ArrowUp
            : action == StudyTableAction.ArrowDown ? ArrowDown
            : action == StudyTableAction.ArrowRight ? ArrowRight
            : null;

        // ── Largeurs ─────────────────────────────────────────────────────

        /// <summary>
        /// Largeurs en % : étiquettes fixes, frontières à <see cref="MaxFrontierPct"/>
        /// tant qu'elles n'occupent pas plus de <see cref="FrontierShare"/> du
        /// reste, intervalles se partagent le solde. <paramref name="widened"/>
        /// = frontières élargies (index de frontière → % imposé).
        /// </summary>
        public static float[] ColumnWidths(IReadOnlyList<StudyColumnKind> columns,
                                           IReadOnlyDictionary<int, float> widened = null)
        {
            int frontiers = 0, intervals = 0;
            foreach (var k in columns)
            {
                if (k == StudyColumnKind.Frontier) frontiers++;
                else if (k == StudyColumnKind.Interval) intervals++;
            }
            float rest = 100f - LabelColumnPct;
            float baseFrontier = frontiers == 0 ? 0f
                : Math.Min(MaxFrontierPct, rest * FrontierShare / frontiers);

            var widths = new float[columns.Count];
            float frontierTotal = 0f;
            for (int i = 0; i < columns.Count; i++)
            {
                if (columns[i] != StudyColumnKind.Frontier) continue;
                widths[i] = widened != null && widened.TryGetValue(i, out float w) ? w : baseFrontier;
                frontierTotal += widths[i];
            }
            float interval = intervals == 0 ? 0f : Math.Max(1f, (rest - frontierTotal) / intervals);
            for (int i = 0; i < columns.Count; i++)
            {
                if (columns[i] == StudyColumnKind.Label) widths[i] = LabelColumnPct;
                else if (columns[i] == StudyColumnKind.Interval) widths[i] = interval;
            }
            return widths;
        }

        // ── Plans d'insertion / suppression de colonnes ──────────────────

        /// <summary>
        /// Ajout d'une valeur de x à côté de la frontière <paramref name="frontier"/>
        /// (0-based). Retourne l'index 0-based AVANT lequel insérer deux
        /// colonnes (= <paramref name="columnCount"/> pour ajouter en fin) et
        /// leurs types dans l'ordre : à droite → intervalle puis frontière ;
        /// à gauche → frontière puis intervalle.
        /// </summary>
        public static (int insertBefore, StudyColumnKind first, StudyColumnKind second)
            PlanAddValue(int frontier, bool right, int columnCount)
        {
            if (frontier < 1 || frontier >= columnCount) throw new ArgumentOutOfRangeException(nameof(frontier));
            return right
                ? (frontier + 1, StudyColumnKind.Interval, StudyColumnKind.Frontier)
                : (frontier, StudyColumnKind.Frontier, StudyColumnKind.Interval);
        }

        /// <summary>
        /// Suppression de la frontière <paramref name="frontier"/> (0-based) :
        /// elle part avec l'intervalle de droite, ou celui de gauche si c'est
        /// la dernière. Retourne les deux index 0-based, le plus grand
        /// d'abord (ordre de suppression sûr). Null si impossible (moins de
        /// trois frontières).
        /// </summary>
        public static int[] PlanDeleteValue(IReadOnlyList<StudyColumnKind> columns, int frontier)
        {
            if (frontier < 0 || frontier >= columns.Count || columns[frontier] != StudyColumnKind.Frontier) return null;
            int frontiers = 0;
            foreach (var k in columns) if (k == StudyColumnKind.Frontier) frontiers++;
            if (frontiers <= 2) return null;

            int neighbor = frontier + 1 < columns.Count && columns[frontier + 1] == StudyColumnKind.Interval
                ? frontier + 1
                : frontier - 1;
            if (neighbor < 0 || columns[neighbor] != StudyColumnKind.Interval) return null;
            return new[] { Math.Max(frontier, neighbor), Math.Min(frontier, neighbor) };
        }

        // ── Extremums d'après les flèches ────────────────────────────────

        /// <summary>
        /// Alignement vertical d'une frontière d'une ligne de variation, d'après
        /// les flèches voisines (texte des intervalles de gauche et de droite,
        /// null en bout de tableau) : ↗ puis ↘ = maximum (haut), ↘ puis ↗ =
        /// minimum (bas) ; en bout, la flèche unique décide ; sinon milieu.
        /// </summary>
        public static CellVAlign ExtremumAlign(string leftArrow, string rightArrow)
        {
            int l = Slope(leftArrow), r = Slope(rightArrow);
            bool hasLeft = leftArrow != null, hasRight = rightArrow != null;
            if (hasLeft && hasRight)
            {
                if (l > 0 && r < 0) return CellVAlign.Top;
                if (l < 0 && r > 0) return CellVAlign.Bottom;
                return CellVAlign.Center;
            }
            if (hasRight)  // début de tableau : on part d'en bas si ça monte
                return r > 0 ? CellVAlign.Bottom : r < 0 ? CellVAlign.Top : CellVAlign.Center;
            if (hasLeft)   // fin de tableau : on arrive en haut si ça monte
                return l > 0 ? CellVAlign.Top : l < 0 ? CellVAlign.Bottom : CellVAlign.Center;
            return CellVAlign.Center;
        }

        private static int Slope(string arrow)
        {
            string a = (arrow ?? "").Trim();
            return a == ArrowUp ? 1 : a == ArrowDown ? -1 : 0;
        }
    }
}
