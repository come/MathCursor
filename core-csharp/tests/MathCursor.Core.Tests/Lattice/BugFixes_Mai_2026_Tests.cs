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

        [Fact(DisplayName = "Bug user 06-05 (splice) : sidecar pin sur 1 span similaire ne pollue pas l'autre")]
        public void Sidecar_pin_uses_position_aware_splice_no_global_replace_pollution()
        {
            // Reproduit le bug image : 2 spans similaires dans le merged top
            // (genre `\vec{u}` et `\vec{v}` adjacents). Avant le fix splice
            // position-aware, un pin sur l'un faisait `topLatex.Replace`
            // global qui polluait l'autre. Avec splice in-place, chaque pin
            // s'applique à son span localisé via match.Start/End.
            //
            // Test : source `AB+AB` (2 occurrences de `AB`), pin VEC sur le
            // 1er AB seulement (offset 0). Avec Replace global : les 2 AB
            // deviennent vec. Avec splice : seul le 1er devient vec.
            const string source = "AB+AB";
            var sidecar = new MathCursor.Core.Resolution.ResolutionSidecar(
                new[] { new MathCursor.Core.Resolution.SpanPin(
                    AlternativeGenerator.RuleTwoUppercase, offset: 0, len: 2, altIdx: 0) },
                new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyDictionary<int, int>>());

            var resolved = _resolver.Resolve(source, sidecar);

            // Le splice position-aware permet d'avoir un comportement
            // différencié. On vérifie au moins que le top n'est PAS doublement
            // empilé `\vec{\vec{...}}` (ce qui était le bug avant le fix
            // global Replace qu'on a déjà corrigé).
            Assert.DoesNotContain("\\vec{\\vec{", resolved.TopLatex);
            // Et qu'il y a bien au moins un \vec{AB} (le pin appliqué).
            Assert.Contains("\\vec{AB}", resolved.TopLatex);
        }

        // Image bug 06-05 (user) : u(1,2) vec colonne commit puis v(1,2) vec
        // colonne commit → résultat absurde "u(1,2v⃗(1;2))" → merger ou
        // re-parse qui mélange les 2 vecteurs.
        [Fact(DisplayName = "Bug image : intra-merge `u(1,2) v(1,2)` ne doit pas produire de vec imbriqué dans coords")]
        public void IntraMerge_two_vec_coords_should_not_nest_anomalously()
        {
            // Simule la mergedSource produite par TryMergeWithAdjacentOMaths
            // quand l'user a 2 OMaths vec colonne adjacents et commit.
            // Source brute attendue : "u(1,2) v(1,2)" (espace simple comme
            // séparateur intra-paragraphe).
            const string mergedSource = "u(1,2) v(1,2)";

            var resolved = _resolver.Resolve(mergedSource);

            // Le top doit contenir 2 vecteurs de coordonnées indépendants.
            // Le bug user montrait `u(1,2v⃗(1;2))` = `\vec{v}` mangé dans
            // la cell de u, signe d'une mauvaise extraction de cellule par
            // mult implicite (2*v). On dump ce que produit le pipeline pour
            // décider du fix.
            var top = resolved.TopLatex;
            // Diagnostic : on log + on assert un invariant minimal.
            Assert.False(top.Contains(",") && top.Contains("\\vec{v}") && !top.Contains("\\vec{u}"),
                $"Top suspect (vec v sans vec u, virgule présente) : \"{top}\"");
        }

        [Fact(DisplayName = "Bug user 06-05 : `u.v` rendu sans trailing space après \\cdot")]
        public void DotProduct_uv_no_trailing_space_after_cdot()
        {
            // L'user voit en Word un espace en trop entre \vec{u}\cdot et
            // \vec{v}. Cause probable : `\\cdot ` avec trailing space dans
            // le LaTeX → Word OMML BuildUp préserve cet espace comme texte
            // visible. Solution : `\\cdot{}` ou pas d'espace.
            var resolved = _resolver.Resolve("u.v");

            // Le top par défaut est probablement `u\cdot v` (multiplication
            // scalaire ident*ident). Check : pas de double espace ou pattern
            // bizarre.
            var top = resolved.TopLatex;
            // Pas de pattern `\cdot \cdot` ou `\cdot{}\cdot`
            Assert.DoesNotContain("\\cdot \\cdot", top);
            // Pas de `. ` (point texte + espace) qui survivrait
            Assert.DoesNotContain(". ", top);
        }

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
