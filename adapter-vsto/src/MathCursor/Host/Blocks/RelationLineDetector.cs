namespace MathCursor.Host.Blocks
{
    /// <summary>Résultat de détection : « la ligne commence par un marqueur
    /// de chaîne (ou un ouvreur de système) » — offsets en coordonnées du
    /// texte d'entrée.</summary>
    internal sealed class RelationLineMatch
    {
        /// <summary>Marqueur tel que tapé (« &lt;=&gt; », « = », « { »…).</summary>
        public string MarkerTyped { get; set; } = "";
        /// <summary>LaTeX d'affichage du marqueur (« \Leftrightarrow  »…).</summary>
        public string MarkerLatex { get; set; } = "";
        /// <summary>Connecteur logique (⟺, ⟹ — colonne 1 du bloc) vs
        /// relation (=, ≤ — le signe aligné, colonne 3).</summary>
        public bool IsConnector { get; set; }
        /// <summary>Début du marqueur (après les blancs de tête).</summary>
        public int MarkerStart { get; set; }
        /// <summary>Début du RESTE (après marqueur + blancs).</summary>
        public int RestStart { get; set; }
        /// <summary>Le reste de la ligne (l'expression à analyser par le
        /// moteur). Peut être vide (ligne en cours de frappe : « = »).</summary>
        public string Rest { get; set; } = "";
    }

    /// <summary>
    /// Détection pure « ligne de chaîne » / « ouvreur de système » (M1-M2 du
    /// chantier multiligne, ADR 2026-06-10-Feat-multiline-chain-eqarr-
    /// architecture). Le moteur ne reçoit QUE le reste (relation ou `{` en
    /// tête = « erreur » côté moteur). Pure compute — compilé aussi par
    /// MathCursor.Tests.
    /// </summary>
    internal static class RelationLineDetector
    {
        /// <summary>Null si la ligne ne commence pas par un marqueur de chaîne.</summary>
        public static RelationLineMatch TryDetect(string lineText)
        {
            if (string.IsNullOrEmpty(lineText)) return null;

            int i = 0;
            while (i < lineText.Length && char.IsWhiteSpace(lineText[i])) i++;
            if (i >= lineText.Length) return null;

            var m = RelationMarkers.TryMatch(lineText, i);
            if (m == null) return null;
            var (typed, latex, isConnector) = m.Value;

            return Build(lineText, typed, latex, isConnector, i);
        }

        /// <summary>Null si la ligne ne commence pas par « { » (ouvreur de
        /// SYSTÈME d'équations — accolade qui regroupera les lignes).</summary>
        public static RelationLineMatch TryDetectSystemOpener(string lineText)
        {
            if (string.IsNullOrEmpty(lineText)) return null;

            int i = 0;
            while (i < lineText.Length && char.IsWhiteSpace(lineText[i])) i++;
            if (i >= lineText.Length || lineText[i] != '{') return null;

            return Build(lineText, "{", "\\{", isConnector: false, markerStart: i);
        }

        private static RelationLineMatch Build(string lineText, string typed, string latex,
            bool isConnector, int markerStart)
        {
            int rest = markerStart + typed.Length;
            while (rest < lineText.Length && lineText[rest] == ' ') rest++;

            return new RelationLineMatch
            {
                MarkerTyped = typed,
                MarkerLatex = latex,
                IsConnector = isConnector,
                MarkerStart = markerStart,
                RestStart = rest,
                Rest = lineText.Substring(rest).TrimEnd('\r', '\n'),
            };
        }
    }
}
