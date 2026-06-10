using System.Collections.Generic;
using System.Xml.Linq;
using MathCursor.Serialization;

namespace MathCursor.Host.Blocks
{
    /// <summary>
    /// Composeur des blocs multilignes (ADR 2026-06-10-Feat-multiline-chain-
    /// eqarr-architecture) : (lignes sources, LaTeX par ligne) → l'élément
    /// <c>&lt;m:oMath&gt;</c> du bloc. C'est l'UNIQUE chemin de génération
    /// (création, extension, future édition = toujours une re-composition
    /// complète — principe « la source est la vérité »).
    ///
    /// <list type="bullet">
    /// <item><b>Chaîne</b> : eqArr 3 colonnes [connecteur &amp; lhs &amp; signe+rhs]
    ///   — les connecteurs s'alignent entre eux, les signes entre eux
    ///   (double alignement validé par POC).</item>
    /// <item><b>Système</b> : accolade ouvrante <c>&lt;m:d&gt;</c> (fermante
    ///   invisible) enveloppant un eqArr 2 colonnes [lhs &amp; signe+rhs]
    ///   — les = alignés dans l'accolade (décision user).</item>
    /// </list>
    ///
    /// Les marqueurs sont RE-DÉRIVÉS des lignes sources (détecteur pur,
    /// déterministe) ; les LaTeX par ligne sont REUTILISÉS tels que choisis
    /// dans la popup au commit de chaque ligne (jamais de re-analyse).
    /// Pas de Word — testable (la greffe utilise LatexToOmml, pur).
    /// </summary>
    internal static class ChainComposer
    {
        private static readonly XNamespace M = LatexToOmml.M;

        /// <summary>Bloc CHAÎNE : une ligne par (source, latex-sans-marqueur).</summary>
        public static XElement ComposeChain(IReadOnlyList<string> stenoLines, IReadOnlyList<string> latexLines)
        {
            var eqArr = new XElement(M + "eqArr");
            for (int i = 0; i < latexLines.Count; i++)
            {
                string steno = i < stenoLines.Count ? stenoLines[i] : "";
                string latex = latexLines[i] ?? "";
                var m = RelationLineDetector.TryDetect(steno);

                string conn = "", lhs = "", relRhs = "";
                if (m == null)
                {
                    // 1ʳᵉ ligne (équation complète) ou ligne absorbée : scission
                    // au signe top-level pour l'alignement.
                    var (l, r) = LatexTopLevelSplit.Split(latex);
                    if (r != null) { lhs = l; relRhs = r; }
                    else lhs = latex;
                }
                else if (!m.IsConnector)
                {
                    // Marqueur-RELATION (« = 2x ») : le signe EST l'alignement.
                    relRhs = m.MarkerLatex + latex;
                }
                else
                {
                    // CONNECTEUR (« ⟺ x=3 ») : colonne 1 + équation scindée.
                    conn = m.MarkerLatex;
                    var (l, r) = LatexTopLevelSplit.Split(latex);
                    if (r != null) { lhs = l; relRhs = r; }
                    else lhs = latex;
                }
                eqArr.Add(Row(conn, lhs, relRhs));
            }
            return new XElement(M + "oMath", eqArr);
        }

        /// <summary>Bloc SYSTÈME : accolade + eqArr 2 colonnes.</summary>
        public static XElement ComposeSystem(IReadOnlyList<string> latexLines)
        {
            var eqArr = new XElement(M + "eqArr");
            foreach (var latex in latexLines)
            {
                var (l, r) = LatexTopLevelSplit.Split(latex ?? "");
                var e = new XElement(M + "e");
                Graft(e, l);
                Amp(e);
                if (r != null) Graft(e, r);
                eqArr.Add(e);
            }
            var d = new XElement(M + "d",
                new XElement(M + "dPr",
                    new XElement(M + "begChr", new XAttribute(M + "val", "{")),
                    new XElement(M + "endChr", new XAttribute(M + "val", ""))),
                new XElement(M + "e", eqArr));
            return new XElement(M + "oMath", d);
        }

        // ── Internals ────────────────────────────────────────────────────

        private static XElement Row(string conn, string lhs, string relRhs)
        {
            var e = new XElement(M + "e");
            Graft(e, conn);
            Amp(e);
            Graft(e, lhs);
            Amp(e);
            Graft(e, relRhs);
            return e;
        }

        private static void Graft(XElement e, string latex)
        {
            if (string.IsNullOrEmpty(latex)) return;
            foreach (var el in LatexToOmml.Convert(latex).Elements())
                e.Add(el);
        }

        private static void Amp(XElement e)
            => e.Add(new XElement(M + "r",
                new XElement(M + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), "&")));
    }
}
