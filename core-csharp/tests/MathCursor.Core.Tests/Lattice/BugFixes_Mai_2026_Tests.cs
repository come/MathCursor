using System.Linq;
using MathCursor.Core;
using MathCursor.Core.Lattice;
using Xunit;

namespace MathCursor.Core.Tests.Lattice
{
    /// <summary>
    /// Régressions remontées par Etienne Velay (LFV Pologne) le 30 avril 2026.
    /// </summary>
    public sealed class BugFixes_Mai_2026_Tests
    {
        private readonly LatticeEngine _engine = new LatticeEngine();
        private readonly ZoneResolver _resolver;

        public BugFixes_Mai_2026_Tests() { _resolver = new ZoneResolver(_engine); }

        // ───────────────────────────────────────────────────────────────────
        // Bug 1 — AB(1,2) et AB(1;2) doivent proposer le vecteur colonne en alt
        // ───────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Bug Etienne 30-04 : AB(1,2) doit proposer flip vec colonne en alt")]
        public void AB_comma_must_propose_column_vec_alternative()
        {
            // Reference du brief vector-coordinates §5.4 + ADR 06-05 sidecar.
            // `AB (1 2)` (espace) → colonne par défaut, alt ligne.
            // `AB(1, 2)` (virgule) → ligne par défaut, alt colonne (CASSE actuellement).
            var resolved = _resolver.Resolve("AB(1,2)");

            // Default = ligne (français : `\vec{AB}(1 ; 2)`)
            Assert.Contains("\\vec{AB}", resolved.TopLatex);
            // Doit y avoir au moins un AmbiguityMatch avec une alt qui produit
            // le rendu colonne `\begin{pmatrix}`.
            bool hasColumnAlt = resolved.AllMatches.Any(m =>
                m.Spot.Alternatives.Any(a => a.Latex.Contains("\\begin{pmatrix}")));
            Assert.True(hasColumnAlt,
                $"Pas d'alt vec colonne pour AB(1,2). TopLatex=\"{resolved.TopLatex}\"; " +
                $"alts vues=[{string.Join("; ", resolved.AllMatches.SelectMany(m => m.Spot.Alternatives.Select(a => a.Latex)))}]");
        }

        [Fact(DisplayName = "Bug Etienne 30-04 : AB(1;2) doit proposer flip vec colonne en alt (séparateur FR)")]
        public void AB_semicolon_must_propose_column_vec_alternative()
        {
            // `;` est le séparateur FR canonique pour les coordonnées (cf.
            // ADR 30-04 french-semicolon-coordinates). Doit fonctionner comme
            // la virgule pour la désambig vec colonne.
            var resolved = _resolver.Resolve("AB(1;2)");

            Assert.Contains("\\vec{AB}", resolved.TopLatex);
            bool hasColumnAlt = resolved.AllMatches.Any(m =>
                m.Spot.Alternatives.Any(a => a.Latex.Contains("\\begin{pmatrix}")));
            Assert.True(hasColumnAlt,
                $"Pas d'alt vec colonne pour AB(1;2). TopLatex=\"{resolved.TopLatex}\"; " +
                $"alts vues=[{string.Join("; ", resolved.AllMatches.SelectMany(m => m.Spot.Alternatives.Select(a => a.Latex)))}]");
        }

        // ───────────────────────────────────────────────────────────────────
        // Bug 2 — u.v produit scalaire : pas de point + espace bizarre
        // ───────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Bug Etienne 30-04 : u.v en alt vec doit produire \\vec{u}\\cdot \\vec{v} propre")]
        public void DotProduct_uv_renders_clean_cdot_no_bare_dot_remaining()
        {
            // L'user désambigue u.v en produit scalaire vectoriel.
            // L'alt doit être bien formée : pas de `\vec{u}. \vec{v}` (point
            // texte qui survit), et l'espacement doit être cohérent
            // (idéalement `\vec{u}\cdot \vec{v}` ou avec espaces des deux côtés).
            var resolved = _resolver.Resolve("u.v");

            // Récupère l'alt vec dot product (RuleVecDotProduct).
            var dotAlt = resolved.AllMatches
                .SelectMany(m => m.Spot.Alternatives)
                .Select(a => a.Latex)
                .FirstOrDefault(l => l.Contains("\\vec{u}") && l.Contains("\\vec{v}"));

            Assert.NotNull(dotAlt);
            // L'alt doit contenir \cdot (le produit scalaire)
            Assert.Contains("\\cdot", dotAlt);
            // L'alt ne doit PAS contenir de point texte non-LaTeX entre les
            // vecteurs (ex: "\vec{u}. \vec{v}" → bug typographique). On
            // vérifie l'absence de `}. ` (caractère `}`, `.`, espace) qui
            // signalerait un point littéral entre 2 commandes.
            Assert.DoesNotContain("}. ", dotAlt);
        }
    }
}
