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
using System.Linq;
using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Modèle des tableaux de signe / variation (ADR 2026-10-16-Feat-sign-
    /// variation-table-insert, 2026-10-19-Feat-study-table-cell-roles-
    /// contextual-popup) : gabarit, rôles, choix de la popup, largeurs, plans
    /// de colonnes, extremums d'après les flèches.
    /// </summary>
    public sealed class StudyTableTemplateTests
    {
        private const string Up = StudyTableTemplate.ArrowUp;
        private const string Down = StudyTableTemplate.ArrowDown;
        private const string Flat = StudyTableTemplate.ArrowRight;

        // F = frontière, I = intervalle, L = étiquettes.
        private static List<StudyColumnKind> Cols(string spec) =>
            spec.Select(ch => ch == 'L' ? StudyColumnKind.Label
                            : ch == 'F' ? StudyColumnKind.Frontier
                            : StudyColumnKind.Interval).ToList();

        // ── Gabarit ──────────────────────────────────────────────────────

        [Theory]
        [InlineData(false, 2)]
        [InlineData(true, 3)]
        public void Gabarit_grille_rectangulaire_etiquettes_plus_cinq_colonnes(bool variation, int rows)
        {
            var t = StudyTableTemplate.Build(variation ? StudyTableKind.Variation : StudyTableKind.Sign);
            Assert.Equal(rows, t.Rows.Count);
            Assert.Equal(rows, t.RowKinds.Count);
            Assert.Equal(StudyRowKind.X, t.RowKinds[0]);
            Assert.All(t.Rows, r => Assert.Equal(t.ColumnCount, r.Count));
        }

        [Fact]
        public void Gabarit_ligne_x_bornes_infinies_et_zero_au_milieu()
        {
            var x = StudyTableTemplate.Build(StudyTableKind.Sign).Rows[0];
            Assert.Equal("x", x[0].Text);
            Assert.True(x[0].Italic);
            Assert.Equal("−∞", x[1].Text);
            Assert.Equal("0", x[3].Text);
            Assert.Equal("+∞", x[5].Text);
        }

        [Fact]
        public void Gabarit_signe_une_ligne_f_de_x_en_equation()
        {
            var t = StudyTableTemplate.Build(StudyTableKind.Sign);
            Assert.Equal(new[] { StudyRowKind.X, StudyRowKind.Sign }, t.RowKinds);
            Assert.False(t.Rows[0][0].IsMath, "x reste du texte");
            Assert.Equal("f(x)", t.Rows[1][0].Text);
            Assert.True(t.Rows[1][0].IsMath, "l'étiquette f(x) est posée en équation");
        }

        [Fact]
        public void Gabarit_variation_derivee_signe_puis_fleches()
        {
            var t = StudyTableTemplate.Build(StudyTableKind.Variation);
            Assert.Equal(new[] { StudyRowKind.X, StudyRowKind.Sign, StudyRowKind.Variation }, t.RowKinds);
            Assert.Equal("f′(x)", t.Rows[1][0].Text);
            Assert.Equal(new[] { "", "+", "0", "−", "" }, t.Rows[1].Skip(1).Select(c => c.Text));
            Assert.Equal(new[] { "", Up, "", Down, "" }, t.Rows[2].Skip(1).Select(c => c.Text));
        }

        [Fact]
        public void Colonnes_du_gabarit_alternent_frontiere_intervalle()
        {
            var kinds = Enumerable.Range(0, 6).Select(StudyTableTemplate.TemplateColumnKind).ToList();
            Assert.Equal(Cols("LFIFIF"), kinds);
        }

        // ── Rôles et styles ──────────────────────────────────────────────

        [Fact]
        public void Role_intervalle_devient_fleche_dans_une_ligne_de_variation()
        {
            Assert.Equal(StudyCellRole.Interval, StudyTableTemplate.RoleFor(StudyRowKind.Sign, StudyColumnKind.Interval));
            Assert.Equal(StudyCellRole.Arrow, StudyTableTemplate.RoleFor(StudyRowKind.Variation, StudyColumnKind.Interval));
        }

        [Fact]
        public void Valeur_interdite_jamais_sur_la_ligne_x()
        {
            Assert.Equal(StudyCellRole.Frontier,
                StudyTableTemplate.RoleFor(StudyRowKind.X, StudyColumnKind.Frontier, forbidden: true));
            Assert.Equal(StudyCellRole.ForbiddenFrontier,
                StudyTableTemplate.RoleFor(StudyRowKind.Sign, StudyColumnKind.Frontier, forbidden: true));
        }

        [Theory]
        [InlineData(StudyTableTemplate.LabelXStyle)]
        [InlineData(StudyTableTemplate.LabelSignStyle)]
        [InlineData(StudyTableTemplate.LabelVariationStyle)]
        public void Style_d_etiquette_aller_retour_type_de_ligne(string style)
        {
            var kind = StudyTableTemplate.RowKindFromLabelStyle(style);
            Assert.NotEqual(StudyRowKind.None, kind);
            Assert.Equal(style, StudyTableTemplate.StyleFor(StudyCellRole.Label, kind));
        }

        [Fact]
        public void Ligne_x_styles_d_en_tete_dedies_et_relus()
        {
            Assert.Equal(StudyTableTemplate.HeaderFrontierStyle,
                StudyTableTemplate.StyleFor(StudyCellRole.Frontier, StudyRowKind.X));
            Assert.Equal(StudyTableTemplate.HeaderIntervalStyle,
                StudyTableTemplate.StyleFor(StudyCellRole.Interval, StudyRowKind.X));
            Assert.Equal(StudyTableTemplate.FrontierStyle,
                StudyTableTemplate.StyleFor(StudyCellRole.Frontier, StudyRowKind.Sign));
            Assert.Equal(StudyColumnKind.Frontier, StudyTableTemplate.ColumnKindFromXRowStyle(StudyTableTemplate.HeaderFrontierStyle));
            Assert.Equal(StudyColumnKind.Interval, StudyTableTemplate.ColumnKindFromXRowStyle(StudyTableTemplate.HeaderIntervalStyle));
        }

        [Fact]
        public void Type_de_colonne_lu_sur_la_ligne_x()
        {
            Assert.Equal(StudyColumnKind.Frontier, StudyTableTemplate.ColumnKindFromXRowStyle(StudyTableTemplate.FrontierStyle));
            Assert.Equal(StudyColumnKind.Interval, StudyTableTemplate.ColumnKindFromXRowStyle(StudyTableTemplate.IntervalStyle));
            Assert.Equal(StudyColumnKind.None, StudyTableTemplate.ColumnKindFromXRowStyle("Normal"));
        }

        // ── Choix de la popup ────────────────────────────────────────────

        [Fact]
        public void Pas_de_popup_dans_un_intervalle_de_signe()
        {
            Assert.Empty(StudyTableTemplate.ChoicesFor(StudyCellRole.Interval, StudyRowKind.Sign, true, true));
        }

        [Fact]
        public void Pas_de_popup_sur_la_ligne_x()
        {
            // On ajoute une valeur en la tapant entre deux valeurs ; la valeur
            // interdite se pose depuis les frontières du dessous.
            Assert.Empty(StudyTableTemplate.ChoicesFor(StudyCellRole.Frontier, StudyRowKind.X, canDeleteValue: true, canDeleteRow: false));
            Assert.Empty(StudyTableTemplate.ChoicesFor(StudyCellRole.Interval, StudyRowKind.X, canDeleteValue: true, canDeleteRow: false));
        }

        [Fact]
        public void Frontiere_sous_x_zero_ou_valeur_interdite()
        {
            Assert.Equal(new[] { StudyTableAction.Zero, StudyTableAction.ToggleForbidden },
                StudyTableTemplate.ChoicesFor(StudyCellRole.Frontier, StudyRowKind.Sign, true, true));
            Assert.Equal(new[] { StudyTableAction.ToggleForbidden },
                StudyTableTemplate.ChoicesFor(StudyCellRole.ForbiddenFrontier, StudyRowKind.Variation, true, true));
        }

        [Fact]
        public void Etiquette_actions_de_lignes_jamais_supprimer_la_ligne_x()
        {
            var sign = StudyTableTemplate.ChoicesFor(StudyCellRole.Label, StudyRowKind.Sign, true, canDeleteRow: true);
            Assert.Equal(new[] { StudyTableAction.AddSignRow, StudyTableAction.AddVariationRow, StudyTableAction.DeleteRow }, sign);
            var x = StudyTableTemplate.ChoicesFor(StudyCellRole.Label, StudyRowKind.X, true, canDeleteRow: true);
            Assert.DoesNotContain(StudyTableAction.DeleteRow, x);
        }

        [Fact]
        public void Fleche_trois_choix()
        {
            var c = StudyTableTemplate.ChoicesFor(StudyCellRole.Arrow, StudyRowKind.Variation, true, true);
            Assert.Equal(new[] { Up, Down, Flat }, c.Select(StudyTableTemplate.ArrowText));
        }

        // ── Largeurs ─────────────────────────────────────────────────────

        [Theory]
        [InlineData("LFIFIF")]
        [InlineData("LFIFIFIFIF")]
        [InlineData("LFIFIFIFIFIFIF")]
        public void Largeurs_totalisent_100(string spec)
        {
            var w = StudyTableTemplate.ColumnWidths(Cols(spec));
            Assert.Equal(100f, w.Sum(), 2);
            Assert.Equal(StudyTableTemplate.LabelColumnPct, w[0]);
            Assert.All(w, x => Assert.True(x > 0));
        }

        [Fact]
        public void Frontieres_retrecissent_quand_il_y_en_a_beaucoup()
        {
            var few = StudyTableTemplate.ColumnWidths(Cols("LFIFIF"));
            var many = StudyTableTemplate.ColumnWidths(Cols("LFIFIFIFIFIFIF"));
            Assert.Equal(StudyTableTemplate.MaxFrontierPct, few[1]);
            Assert.True(many[1] < few[1]);
            Assert.True(many[2] > many[1], "un intervalle reste plus large qu'une frontière");
        }

        [Fact]
        public void Frontiere_elargie_prend_sur_les_intervalles()
        {
            var cols = Cols("LFIFIF");
            var normal = StudyTableTemplate.ColumnWidths(cols);
            var widened = StudyTableTemplate.ColumnWidths(cols, new Dictionary<int, float> { { 3, 16f } });
            Assert.Equal(16f, widened[3]);
            Assert.True(widened[2] < normal[2]);
            Assert.Equal(100f, widened.Sum(), 2);
        }

        // ── Plans de colonnes ────────────────────────────────────────────

        [Fact]
        public void Ajout_a_droite_intervalle_puis_frontiere_apres_la_valeur()
        {
            var (before, first, second) = StudyTableTemplate.PlanAddValue(3, right: true, columnCount: 6);
            Assert.Equal(4, before);
            Assert.Equal(StudyColumnKind.Interval, first);
            Assert.Equal(StudyColumnKind.Frontier, second);
        }

        [Fact]
        public void Ajout_a_gauche_frontiere_puis_intervalle_avant_la_valeur()
        {
            var (before, first, second) = StudyTableTemplate.PlanAddValue(3, right: false, columnCount: 6);
            Assert.Equal(3, before);
            Assert.Equal(StudyColumnKind.Frontier, first);
            Assert.Equal(StudyColumnKind.Interval, second);
        }

        [Fact]
        public void Ajout_a_droite_de_la_derniere_valeur_ajoute_en_fin()
        {
            var (before, _, _) = StudyTableTemplate.PlanAddValue(5, right: true, columnCount: 6);
            Assert.Equal(6, before);
        }

        [Fact]
        public void Suppression_emporte_l_intervalle_de_droite_ou_de_gauche_en_bout()
        {
            var cols = Cols("LFIFIFIF");
            Assert.Equal(new[] { 4, 3 }, StudyTableTemplate.PlanDeleteValue(cols, 3));
            Assert.Equal(new[] { 7, 6 }, StudyTableTemplate.PlanDeleteValue(cols, 7));
        }

        [Fact]
        public void Suppression_refusee_s_il_ne_reste_que_deux_valeurs()
        {
            Assert.Null(StudyTableTemplate.PlanDeleteValue(Cols("LFIF"), 1));
            Assert.Null(StudyTableTemplate.PlanDeleteValue(Cols("LFIFIF"), 2)); // pas une frontière
        }

        // ── Extremums d'après les flèches ────────────────────────────────

        [Fact]
        public void Maximum_en_haut_minimum_en_bas()
        {
            Assert.Equal(CellVAlign.Top, StudyTableTemplate.ExtremumAlign(Up, Down));
            Assert.Equal(CellVAlign.Bottom, StudyTableTemplate.ExtremumAlign(Down, Up));
            Assert.Equal(CellVAlign.Center, StudyTableTemplate.ExtremumAlign(Up, Up));
            Assert.Equal(CellVAlign.Center, StudyTableTemplate.ExtremumAlign(Flat, Down));
        }

        [Fact]
        public void Bouts_du_tableau_suivent_la_seule_fleche()
        {
            Assert.Equal(CellVAlign.Bottom, StudyTableTemplate.ExtremumAlign(null, Up));
            Assert.Equal(CellVAlign.Top, StudyTableTemplate.ExtremumAlign(null, Down));
            Assert.Equal(CellVAlign.Top, StudyTableTemplate.ExtremumAlign(Up, null));
            Assert.Equal(CellVAlign.Bottom, StudyTableTemplate.ExtremumAlign(Down, null));
            Assert.Equal(CellVAlign.Center, StudyTableTemplate.ExtremumAlign(null, ""));
        }
    }
}
