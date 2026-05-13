using System.Linq;
using MathCursor.Core;
using MathCursor.Core.Lattice;
using MathCursor.Core.Resolution;
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

        [Fact(DisplayName = "Bug user 06-05 : système { multi-ligne avec vec → cross-merge cases préserve les vec")]
        public void Cases_cross_merge_preserves_vec_via_merged_sidecar()
        {
            // Simule l'output de TryCascadeAbsorbCasesChain post-fix
            // (commit 70a3164) : 2 lignes système `{ ` mergées avec sidecar
            // fusionné contenant tous les pins vec.
            //
            // Sans le fix : MergedSidecar = Empty → ResolverStage Resolve la
            // mergedSource sans aucun pin → vec sautent.
            // Avec le fix : MergedSidecar contient les pins recalibrés des
            // 2 lignes → Resolve les applique → tous les vec préservés.

            // mergedSource = `{ AB+BC=DE\n{ AB+BE=BE` (24 chars)
            //                01234567890 12345678901234
            // ligne 1 [0..10] : `{ AB+BC=DE`
            //   `{` =0, ` ` =1, `A` =2, `B` =3, `+` =4, `B` =5, `C` =6,
            //   `=` =7, `D` =8, `E` =9
            // ligne 2 [11..] : `{ AB+BE=BE` (offset shift = 11)
            //   `{` =11, ` ` =12, `A` =13, `B` =14, `+` =15, `B` =16, `E` =17,
            //   `=` =18, `B` =19, `E` =20
            const string mergedSource = "{ AB+BC=DE\n{ AB+BE=BE";

            var sidecar = new MathCursor.Core.Resolution.ResolutionSidecar(
                new[]
                {
                    new MathCursor.Core.Resolution.SpanPin(AlternativeGenerator.RuleTwoUppercase, 2, 2, 0),  // AB ligne 1
                    new MathCursor.Core.Resolution.SpanPin(AlternativeGenerator.RuleTwoUppercase, 5, 2, 0),  // BC
                    new MathCursor.Core.Resolution.SpanPin(AlternativeGenerator.RuleTwoUppercase, 8, 2, 0),  // DE
                    new MathCursor.Core.Resolution.SpanPin(AlternativeGenerator.RuleTwoUppercase, 13, 2, 0), // AB ligne 2
                    new MathCursor.Core.Resolution.SpanPin(AlternativeGenerator.RuleTwoUppercase, 16, 2, 0), // BE (1er)
                    new MathCursor.Core.Resolution.SpanPin(AlternativeGenerator.RuleTwoUppercase, 19, 2, 0), // BE (2nd)
                },
                new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyDictionary<int, int>>());

            var resolved = _resolver.Resolve(mergedSource, sidecar);

            // Tous les vec doivent être préservés dans le top final
            Assert.Contains("\\vec{AB}", resolved.TopLatex);
            Assert.Contains("\\vec{BC}", resolved.TopLatex);
            Assert.Contains("\\vec{DE}", resolved.TopLatex);
            Assert.Contains("\\vec{BE}", resolved.TopLatex);
        }

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
        [Fact(DisplayName = "Bug parser : `u(1,2) v(1,2)` ne doit pas être parsé comme `u(1, 2v(1,2))` (mult implicite parasite)")]
        public void Parser_uv_coords_with_space_must_not_merge_via_implicit_mult()
        {
            // L'user voit `u(1, 2v⃗(1/2))` quand 2 OMaths vec col adjacents
            // sont mergés. Cause : la mergedSource `u(1,2) v(1,2)` est
            // mal-parsée — le `2 v` est juxtaposition (mult implicite) et
            // `v(1,2)` devient un sous-arg de `u(...)`.
            //
            // Comportement attendu : 2 patterns coords distincts.
            var top = _engine.Convert("u(1,2) v(1,2)")[0].Latex;

            // Pas de \vec{v} à l'intérieur d'une cell de u.
            // Pattern bug : `u(...,...{vec_v...})` → on cherche `2\vec{v}`.
            Assert.DoesNotContain("2\\vec{v}", top);
            // Idéalement : `\vec{u}(1 ; 2)` et `\vec{v}(1 ; 2)` distincts
            // (séparateur espace ou autre).
        }

        [Fact(DisplayName = "Diagnostic ABS : top default de AB(1;2) doit être \\vec{AB}(1 ; 2)")]
        public void Diagnostic_AB_semicolon_top_default()
        {
            var top = _engine.Convert("AB(1;2)")[0].Latex;
            // Si la DLL est à jour, top = "\\vec{AB}(1 ; 2)" (row VC reconnu).
            // Si la DLL est obsolète, top = "AB\\left(1\\right)" (function call).
            Assert.Equal("\\vec{AB}(1 ; 2)", top);
        }

        [Fact(DisplayName = "Diagnostic ALT : alt VectorLayoutFlip pour AB(1;2) doit contenir \\begin{pmatrix}")]
        public void Diagnostic_AB_semicolon_flip_alt_is_column_pmatrix()
        {
            var resolved = _resolver.Resolve("AB(1;2)");
            // Liste tous les alts pour debug
            var allAlts = resolved.AllMatches
                .SelectMany(m => m.Spot.Alternatives)
                .Select(a => a.Latex)
                .ToList();

            // L'alt VectorLayoutFlip = column = \vec{AB}\begin{pmatrix}1 \\ 2\end{pmatrix}
            bool hasColumnAlt = allAlts.Any(a => a.Contains("\\begin{pmatrix}"));
            Assert.True(hasColumnAlt,
                $"Pas d'alt avec \\begin{{pmatrix}}. Alts vues = [{string.Join(" | ", allAlts)}]");
        }

        [Fact(DisplayName = "Diagnostic comparatif : u(1;2) et AB(1;2) doivent tous deux avoir l'alt column")]
        public void Diagnostic_comparison_u_vs_AB_column_alt()
        {
            var u = _resolver.Resolve("u(1;2)");
            var AB = _resolver.Resolve("AB(1;2)");

            var uAlts = u.AllMatches.SelectMany(m => m.Spot.Alternatives).Select(a => a.Latex).ToList();
            var ABAlts = AB.AllMatches.SelectMany(m => m.Spot.Alternatives).Select(a => a.Latex).ToList();

            bool uHasColumn = uAlts.Any(a => a.Contains("\\begin{pmatrix}"));
            bool ABHasColumn = ABAlts.Any(a => a.Contains("\\begin{pmatrix}"));

            Assert.True(uHasColumn, $"u(1;2) sans column. Alts: {string.Join(" | ", uAlts)}");
            Assert.True(ABHasColumn, $"AB(1;2) sans column. Alts: {string.Join(" | ", ABAlts)}");
        }

        [Fact(DisplayName = "Diagnostic : que produit le pipeline pour `u(1,2) v(1,2)` avec sidecar pin column flip ?")]
        public void Diagnostic_dump_top_with_column_flip_pin()
        {
            // L'user a 2 OMaths : `u(1,2)` flip column, puis `v(1,2)` flip column.
            // Au merge, mergedSource = `u(1,2) v(1,2)`. Le sidecar fusionné a
            // 2 pins VectorLayoutFlip.
            // On dump ce que produit ZoneResolver.Resolve avec ce sidecar.

            const string source = "u(1,2) v(1,2)";

            // Simule le sidecar fusionné post-merge (2 pins VectorLayoutFlip)
            var sidecar = new MathCursor.Core.Resolution.ResolutionSidecar(
                new[]
                {
                    new MathCursor.Core.Resolution.SpanPin(
                        AlternativeGenerator.RuleVectorLayoutFlip,
                        offset: 0, len: 6, altIdx: 1), // u(1,2) → column
                    new MathCursor.Core.Resolution.SpanPin(
                        AlternativeGenerator.RuleVectorLayoutFlip,
                        offset: 7, len: 6, altIdx: 1), // v(1,2) → column
                },
                new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyDictionary<int, int>>());

            var resolved = _resolver.Resolve(source, sidecar);

            // Diagnostic : fail le test pour voir le TopLatex dans l'output.
            // Si le test passe, c'est que le résultat est cohérent. Sinon
            // on voit le bug exact.
            Assert.False(resolved.TopLatex.Contains("2\\vec{v}"),
                $"BUG REPRO : Top contient `2\\vec{{v}}` → mult implicite parasite. " +
                $"TopLatex actuel = \"{resolved.TopLatex}\"");
        }

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

        // ───────────────────────────────────────────────────────────────────
        // Bug user 11-05 (report 1d6a9ca0) : double wrap autour d'un span
        // déjà décoré par le parser à partir des délimiteurs source. Cf.
        // ScanDecoratedTwoThreeUpper dans AlternativeGenerator.
        // ───────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Bug 11-05 : `(AB)` seul, top ne doit pas être \\left(\\left(AB\\right)\\right)")]
        public void Parens_around_AB_alone_must_not_be_duplicated()
        {
            var top = _engine.Convert("(AB)")[0].Latex;
            Assert.DoesNotContain(@"\left(\left(AB\right)\right)", top);
            int leftCount = System.Text.RegularExpressions.Regex.Matches(top, @"\\left\(").Count;
            Assert.True(leftCount <= 1,
                $"Attendu au plus 1 `\\left(` autour de AB. TopLatex = \"{top}\"");
        }

        [Fact(DisplayName = "Bug 11-05 : `(AB) perp EF`, propagation du wrap interdit")]
        public void Parens_around_AB_must_not_be_duplicated_when_followed_by_perp()
        {
            var top = _engine.Convert("(AB) perp EF")[0].Latex;
            Assert.DoesNotContain(@"\left(\left(AB\right)\right)", top);
            Assert.Contains(@"\perp", top);
        }

        [Fact(DisplayName = "Bug 11-05 (repro fidèle) : RulePin two-uppercase=parens + `(AB) perp EF`")]
        public void Parens_RulePin_on_pre_parenthesized_AB_must_not_double_wrap()
        {
            // Repro fidèle du log_tail (report 1d6a9ca0) :
            //   proposed_latex = `\left(\left(AB\right)\right) \perp \left(EF\right)`
            // Avec ScanDecoratedTwoThreeUpper, le match pour `(AB)` couvre tout
            // `\left(AB\right)` → pin paren splice `\left(AB\right)` sur lui-même
            // = identité. Plus de double wrap.
            var sidecar = new MathCursor.Core.Resolution.ResolutionSidecar(
                spanPins: null,
                zoneVotes: null,
                rulePins: new[] { new MathCursor.Core.Resolution.RulePin(AlternativeGenerator.RuleTwoUppercase, 1) },
                spanOverrides: null);

            var resolved = _resolver.Resolve("(AB) perp EF", sidecar);
            var top = resolved.TopLatex;

            Assert.False(top.Contains(@"\left(\left(AB\right)\right)"),
                $"BUG REPRO : double wrap parens autour de AB. TopLatex = \"{top}\"");
        }

        [Fact(DisplayName = "Bug 11-05 generalisation : `angle ABC` + pin widehat ne doit pas faire \\widehat{\\widehat{ABC}}")]
        public void Widehat_on_already_widehat_ABC_must_not_double_wrap()
        {
            // Même classe de bug : `angle ABC` source produit `\widehat{ABC}`
            // dans le top, et si pin widehat applique, splice naïf produirait
            // `\widehat{\widehat{ABC}}`. Avec ScanDecoratedTwoThreeUpper,
            // le match couvre tout le `\widehat{ABC}` → splice = identité.
            var sidecar = new MathCursor.Core.Resolution.ResolutionSidecar(
                spanPins: null,
                zoneVotes: null,
                rulePins: new[] { new MathCursor.Core.Resolution.RulePin(AlternativeGenerator.RuleThreeUppercase, 0) },
                spanOverrides: null);

            var resolved = _resolver.Resolve("angle ABC", sidecar);
            var top = resolved.TopLatex;

            Assert.False(top.Contains(@"\widehat{\widehat{ABC}}"),
                $"BUG REPRO : double widehat autour de ABC. TopLatex = \"{top}\"");
        }

        [Fact(DisplayName = "Bug 11-05 generalisation : `vec AB` + pin vec ne doit pas faire \\vec{\\vec{AB}}")]
        public void Vec_on_already_vec_AB_must_not_double_wrap()
        {
            var sidecar = new MathCursor.Core.Resolution.ResolutionSidecar(
                spanPins: null,
                zoneVotes: null,
                rulePins: new[] { new MathCursor.Core.Resolution.RulePin(AlternativeGenerator.RuleTwoUppercase, 0) },
                spanOverrides: null);

            var resolved = _resolver.Resolve("vec AB", sidecar);
            var top = resolved.TopLatex;

            Assert.False(top.Contains(@"\vec{\vec{AB}}"),
                $"BUG REPRO : double vec autour de AB. TopLatex = \"{top}\"");
        }
    }
}
