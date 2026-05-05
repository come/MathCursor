using System.Collections.Generic;

namespace MathCursor.Host
{
    /// <summary>
    /// Résultat d'une cascade cases.
    /// </summary>
    internal sealed class CasesCascadeResult
    {
        /// <summary>
        /// Nombre de paragraphes (depuis le bas de <c>paragraphsAbove</c>) qui
        /// ont été absorbés dans la cascade. Permet au caller (côté Word) de
        /// retrouver la position de début de chaîne.
        /// </summary>
        public int AbsorbedCount { get; set; }

        /// <summary>
        /// Source mergé final, lignes séparées par <c>\n</c>, ordre top→bottom :
        /// les paragraphes absorbés EN HAUT, puis le current source EN BAS.
        /// </summary>
        public string MergedSource { get; set; }
    }

    /// <summary>
    /// Logique pure (sans dépendance Word) pour la cascade cross-merge des
    /// systèmes d'équations <c>{</c> (Phase 2 cases, ADR 05-05).
    /// <para>
    /// Extraite de <see cref="SuggestionService"/> pour être unit-testable.
    /// Pattern aligné sur <see cref="RevertedZoneMerger"/>.
    /// </para>
    /// <para>
    /// Sémantique (cf. brief 30-04 §2.1 et §3.4) :
    /// </para>
    /// <list type="bullet">
    /// <item>Le current source DOIT commencer par <c>{ </c> (marker cases).</item>
    /// <item>Itère depuis le ¶ juste au-dessus vers le HAUT, absorbe tant
    /// que les ¶ commencent aussi par <c>{ </c>.</item>
    /// <item>STOP sur ¶ vide / texte non-cases / marker align (pas de mix).</item>
    /// <item>Au moins 1 ¶ absorbé requis pour qu'il y ait cascade — sinon null.</item>
    /// </list>
    /// </summary>
    internal static class CasesCascadeMerger
    {
        /// <summary>
        /// Vérifie si <paramref name="line"/> commence par le marker cases
        /// <c>{ </c>. Règle stricte : <c>{</c> SUIVI d'un espace. Sans espace,
        /// ce n'est pas un système (set en extension <c>{1,2}</c>, set vide
        /// <c>{}</c>, etc.).
        /// </summary>
        public static bool StartsWithCasesMarker(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            string trimmed = line.TrimStart();
            // Doit faire au moins 2 chars : `{` + un espace.
            return trimmed.Length >= 2 && trimmed[0] == '{' && trimmed[1] == ' ';
        }

        /// <summary>
        /// Construit la cascade cases depuis un current source et la liste
        /// des ¶ situés au-dessus (ordre top→bottom : index 0 = ¶ le plus
        /// éloigné en haut, dernier index = ¶ juste au-dessus du current).
        /// </summary>
        /// <param name="paragraphsAbove">Textes des ¶ au-dessus, ordre top→bottom.</param>
        /// <param name="currentSource">Source du ¶ courant, doit commencer par <c>{ </c>.</param>
        /// <returns>
        /// <see cref="CasesCascadeResult"/> si au moins 1 ¶ a été absorbé,
        /// <c>null</c> sinon (current pas un cases, ou rien à absorber).
        /// </returns>
        public static CasesCascadeResult BuildCascade(
            IList<string> paragraphsAbove,
            string currentSource)
        {
            if (currentSource == null) return null;
            if (!StartsWithCasesMarker(currentSource)) return null;
            if (paragraphsAbove == null || paragraphsAbove.Count == 0) return null;

            // Walk de BAS en HAUT, absorbe tant que cases.
            var absorbed = new List<string>();
            for (int i = paragraphsAbove.Count - 1; i >= 0; i--)
            {
                string line = paragraphsAbove[i] ?? string.Empty;
                if (!StartsWithCasesMarker(line)) break;
                absorbed.Add(line);
            }

            if (absorbed.Count == 0) return null;

            // absorbed est en ordre BOTTOM→TOP (on a walké de bas en haut).
            // Le mergedSource est en ordre TOP→BOTTOM : reverser puis ajouter
            // current à la fin.
            absorbed.Reverse();
            absorbed.Add(currentSource);

            return new CasesCascadeResult
            {
                AbsorbedCount = absorbed.Count - 1,  // -1 pour le current
                MergedSource = string.Join("\n", absorbed),
            };
        }
    }
}
