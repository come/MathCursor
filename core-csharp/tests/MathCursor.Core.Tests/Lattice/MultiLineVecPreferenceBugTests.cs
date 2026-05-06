using MathCursor.Core;
using MathCursor.Core.Lattice;
using Xunit;

namespace MathCursor.Core.Tests.Lattice
{
    /// <summary>
    /// Cadre le bug user 06-05 (multi-ligne + désambiguïsation vecteur).
    ///
    /// SCÉNARIO USER (en runtime VSTO, simulé ici en pipeline pur) :
    ///   1. User tape `AB+BC=CD`, désambiguïse sur vec → popup affiche
    ///      `\vec{AB}+\vec{BC}=\vec{CD}` puis commit → OMath ligne 1.
    ///   2. User tape `= CH + HD`, désambiguïse sur vec → popup
    ///      `\vec{CH}+\vec{HD}` puis commit. Le marker `=` déclenche le
    ///      cross-merge multi-ligne (cf. SuggestionService.TryCascadeAbsorbMarkerChain) :
    ///      le pipeline reconstruit la source mergée
    ///      `AB+BC=CD\n= CH + HD` et la repasse au LatticeEngine.
    ///   3. RÉSULTAT OBSERVÉ (= bug) : le ré-rendu produit
    ///      `AB+BC=CD\n= CH+HD` sans aucun \vec — les choix vec sont
    ///      perdus à la fusion.
    ///
    /// CAUSE RACINE :
    ///   - Le mécanisme de désambiguïsation `RuleTwoUppercase` (alts `\vec{}`,
    ///     `\left()`, `\left[]`) est implémenté côté UI popup comme une
    ///     **substitution LaTeX** (`AB → \vec{AB}` dans le rendu),
    ///     PAS comme une mutation source (cf. AlternativeGenerator.cs:740-748,
    ///     les alternatives n'ont pas de `Mutation`).
    ///   - Quand le cross-merge re-pipeline la source brute, il appelle
    ///     `ZoneResolver.Resolve` qui n'a pas mémoire des substitutions UI
    ///     précédentes — les `AddPreference` sur `RuleTwoUppercase` ne
    ///     produisent aucune mutation source (`alt.Mutation == null` dans
    ///     `ApplyPreferences`).
    ///   - Conséquence : le top LaTeX rendu reste le default (sans vec).
    ///
    /// CONTRAT À RESTAURER (= ce que les tests vérifient une fois le fix
    /// appliqué — actuellement ils ÉCHOUENT car ils encodent le comportement
    /// SOUHAITÉ) :
    ///   Quand on `AddPreference(RuleTwoUppercase, 0)` (vec) puis qu'on
    ///   `Resolve` une chaîne contenant des paires de majuscules, le top
    ///   LaTeX doit appliquer `\vec{}` à TOUTES les paires détectées.
    ///
    /// Cf. SuggestionService.cs:2470-2540 (TryCascadeAbsorbMarkerChain),
    /// AlternativeGenerator.cs:740 (RuleTwoUppercase),
    /// ZoneResolver.cs:207 (ApplyPreferences).
    /// </summary>
    public sealed class MultiLineVecPreferenceBugTests
    {
        private static ZoneResolver MakeResolver()
            => new ZoneResolver(new LatticeEngine());

        // ─────────────────────────────────────────────────────────────────
        //  ÉTAT ACTUEL : démontre le bug. Sans pref, default sans vec.
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Default sans pref : `AB+BC=CD` rend les paires telles quelles")]
        public void NoPreference_uppercase_pairs_render_default()
        {
            // Confirme que le default = pas de vec (= comportement actuel
            // sans choix utilisateur).
            var r = MakeResolver().Resolve("AB+BC=CD");
            Assert.DoesNotContain("\\vec", r.TopLatex);
            Assert.NotNull(r.Spot); // ambig est bien proposée
            Assert.Equal(AlternativeGenerator.RuleTwoUppercase, r.Spot!.RuleId);
        }

        // ─────────────────────────────────────────────────────────────────
        //  TEST DU BUG : pref vec + cross-merge multi-ligne → \vec perdu.
        //
        //  Ces tests sont marqués `Skip` parce qu'ils encodent le
        //  comportement SOUHAITÉ (que la pref vec se propage au top render),
        //  qui n'est pas le comportement actuel. Retirer le `Skip` quand le
        //  fix sera implémenté.
        // ─────────────────────────────────────────────────────────────────

        [Fact(
            DisplayName = "BUG 06-05 : pref vec sur RuleTwoUppercase ne mute pas la source",
            Skip = "Bug actif — RuleTwoUppercase alts n'ont pas de Mutation source. " +
                   "Cf. AlternativeGenerator.cs:740 et brief fix multi-line vec preserve.")]
        public void Bug_VecPreference_on_uppercase_pair_does_not_propagate_to_top()
        {
            var resolver = MakeResolver();
            // L'utilisateur a choisi l'alt `\vec{AB}` (altIdx=0 = vec dans la
            // liste {vec, paren, crochet} cf. AlternativeGenerator.cs:744).
            resolver.AddPreference(AlternativeGenerator.RuleTwoUppercase, 0);

            var r = resolver.Resolve("AB");

            // Comportement attendu (post-fix) : le top render utilise l'alt vec.
            Assert.Equal("\\vec{AB}", r.TopLatex);
        }

        [Fact(
            DisplayName = "BUG 06-05 : cross-merge `AB+BC=CD\\n= CH + HD` perd les vec après pref",
            Skip = "Bug actif — cf. test parent, même cause racine.")]
        public void Bug_CrossMerge_multiline_loses_vec_after_pref()
        {
            // Reproduit la phase 3 du scénario user :
            // SuggestionService a construit le mergedSource après le commit
            // ligne 2, et appelle Resolve avec les préférences accumulées.
            var resolver = MakeResolver();
            resolver.AddPreference(AlternativeGenerator.RuleTwoUppercase, 0);

            const string mergedSource = "AB+BC=CD\n= CH + HD";
            var r = resolver.Resolve(mergedSource);

            // Comportement attendu : align* avec \vec sur TOUTES les paires.
            Assert.Contains("\\begin{align*}", r.TopLatex);
            Assert.Contains("\\vec{AB}", r.TopLatex);
            Assert.Contains("\\vec{BC}", r.TopLatex);
            Assert.Contains("\\vec{CD}", r.TopLatex);
            Assert.Contains("\\vec{CH}", r.TopLatex);
            Assert.Contains("\\vec{HD}", r.TopLatex);
        }

        // ─────────────────────────────────────────────────────────────────
        //  PREUVE empirique du bug : ces tests passent (= l'état actuel est
        //  bien buggué). Ils documentent le COMPORTEMENT OBSERVÉ pour qu'un
        //  futur fix les fasse échouer (et qu'on doive les retirer / inverser).
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Preuve du bug : pref vec sur AB → top ignore la pref (= bug)")]
        public void Proof_VecPreference_does_not_apply_to_uppercase_pair()
        {
            var resolver = MakeResolver();
            resolver.AddPreference(AlternativeGenerator.RuleTwoUppercase, 0);

            var r = resolver.Resolve("AB");

            // État actuel : le top reste "AB" sans vec, malgré la pref.
            // Cause : alts RuleTwoUppercase n'ont pas de Mutation source.
            Assert.Equal("AB", r.TopLatex);
            Assert.DoesNotContain("\\vec", r.TopLatex);
            // La source n'est pas mutée non plus.
            Assert.Equal("AB", r.MutedSource);
        }

        [Fact(DisplayName = "Preuve du bug : cross-merge multiligne perd les vec malgré pref")]
        public void Proof_CrossMerge_multiline_strips_vec()
        {
            var resolver = MakeResolver();
            resolver.AddPreference(AlternativeGenerator.RuleTwoUppercase, 0);

            // Source mergée telle que reconstruite par TryCascadeAbsorbMarkerChain
            const string mergedSource = "AB+BC=CD\n= CH + HD";
            var r = resolver.Resolve(mergedSource);

            // État actuel buggué : align* est bien produit (parser multi-ligne
            // marche) mais aucun \vec dedans → toutes les paires sont rendues
            // en mode default.
            Assert.Contains("\\begin{align*}", r.TopLatex);
            Assert.DoesNotContain("\\vec", r.TopLatex);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Sanity : sans la dimension multi-ligne, single-line a le même
        //  problème — la pref ne suffit pas. Confirme que le bug est dans
        //  le mécanisme de pref pour `RuleTwoUppercase`, pas spécifique à
        //  la fusion cross-paragraphe.
        // ─────────────────────────────────────────────────────────────────

        [Fact(
            DisplayName = "BUG 06-05 (sanity) : single-line `AB+BC=CD` + pref vec → vec absent du top",
            Skip = "Bug actif — même cause racine que le test multi-ligne.")]
        public void Bug_SingleLine_uppercase_chain_with_vec_pref()
        {
            var resolver = MakeResolver();
            resolver.AddPreference(AlternativeGenerator.RuleTwoUppercase, 0);

            var r = resolver.Resolve("AB+BC=CD");

            Assert.Contains("\\vec{AB}", r.TopLatex);
            Assert.Contains("\\vec{BC}", r.TopLatex);
            Assert.Contains("\\vec{CD}", r.TopLatex);
        }
    }
}
