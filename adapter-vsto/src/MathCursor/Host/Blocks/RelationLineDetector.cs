namespace MathCursor.Host.Blocks
{
    /// <summary>Résultat de détection : « la ligne commence par un marqueur
    /// de chaîne » — offsets en coordonnées du texte d'entrée.</summary>
    internal sealed class RelationLineMatch
    {
        /// <summary>Marqueur tel que tapé (« &lt;=&gt; », « = »…).</summary>
        public string MarkerTyped { get; set; } = "";
        /// <summary>LaTeX d'affichage du marqueur (« \Leftrightarrow  »…).</summary>
        public string MarkerLatex { get; set; } = "";
        /// <summary>Début du marqueur (après les blancs de tête).</summary>
        public int MarkerStart { get; set; }
        /// <summary>Début du RESTE (après marqueur + blancs).</summary>
        public int RestStart { get; set; }
        /// <summary>Le reste de la ligne (l'expression à analyser par le
        /// moteur). Peut être vide (ligne en cours de frappe : « = »).</summary>
        public string Rest { get; set; } = "";
    }

    /// <summary>
    /// Détection pure « ligne de chaîne » (M1 du chantier multiligne, ADR
    /// 2026-06-10-Feat-multiline-chain-eqarr-architecture) : une ligne dont
    /// le premier token non-blanc est un marqueur de <see cref="RelationMarkers"/>
    /// est candidate à l'alignement avec la ligne précédente. Le moteur ne
    /// reçoit QUE le reste (relation en tête = « erreur » côté moteur).
    /// Pure compute — compilé aussi par MathCursor.Tests.
    /// </summary>
    internal static class RelationLineDetector
    {
        /// <summary>Null si la ligne ne commence pas par un marqueur.</summary>
        public static RelationLineMatch TryDetect(string lineText)
        {
            if (string.IsNullOrEmpty(lineText)) return null;

            int i = 0;
            while (i < lineText.Length && char.IsWhiteSpace(lineText[i])) i++;
            if (i >= lineText.Length) return null;

            var m = RelationMarkers.TryMatch(lineText, i);
            if (m == null) return null;
            var (typed, latex) = m.Value;

            int rest = i + typed.Length;
            while (rest < lineText.Length && lineText[rest] == ' ') rest++;

            return new RelationLineMatch
            {
                MarkerTyped = typed,
                MarkerLatex = latex,
                MarkerStart = i,
                RestStart = rest,
                Rest = lineText.Substring(rest).TrimEnd('\r', '\n'),
            };
        }
    }
}
