using System.Globalization;

namespace MathCursor.Core
{
    /// <summary>
    /// Options de rendu LaTeX configurables par l'hôte (adapter VSTO),
    /// agnostiques de Windows (le core ne lit pas le Registry).
    ///
    /// L'adapter lit ses sources de config (Registry HKCU\Software\MathCursor\
    /// Rendering, fichier de prefs, etc.) et configure
    /// <see cref="MathCursor.Core.Lattice.LatexRenderer.GlobalOptions"/> au
    /// démarrage.
    ///
    /// Default culture-aware : <see cref="CultureInfo.CurrentUICulture"/>
    /// résout le symbole approprié pour la culture courante. FR utilise
    /// `\times` (convention lycée français), les autres cultures gardent
    /// `\cdot` (convention universitaire / anglo-saxonne).
    ///
    /// Cf. ADR 2026-04-30-Feat-explicit-mult-times-vs-cdot et le brief
    /// associé.
    /// </summary>
    public sealed class RenderOptions
    {
        /// <summary>
        /// Symbole rendu pour la multiplication explicite via `*`. Format
        /// LaTeX (avec espace final pour la juxtaposition correcte).
        /// Valeurs typiques : <c>"\\times "</c> ou <c>"\\cdot "</c>.
        /// Cas qui IGNORENT cette valeur (forcés `\cdot`) :
        /// - Vec * Vec (produit scalaire vectoriel, convention math)
        /// - Cascade RuleVecDotProduct (alt `\vec{a} \cdot \vec{b}`)
        /// </summary>
        public string MultSymbol { get; set; } = ResolveCultureDefault();

        /// <summary>
        /// Résout le symbole par défaut depuis la culture courante. Appelée
        /// lors de l'init de <see cref="MultSymbol"/>. L'adapter peut
        /// override après pour appliquer un setting Registry explicite.
        /// </summary>
        public static string ResolveCultureDefault()
        {
            var iso = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return iso == "fr" ? "\\times " : "\\cdot ";
        }
    }
}
