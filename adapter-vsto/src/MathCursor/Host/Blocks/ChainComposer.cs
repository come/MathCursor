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

        /// <summary>Bloc CHAÎNE : une ligne par (source, latex-sans-marqueur).
        /// Layout ADAPTATIF, chaque forme prouvée EMPIRIQUEMENT (docx de
        /// variantes V1-V5, user 2026-06-10) :
        /// <list type="bullet">
        /// <item>AUCUN connecteur (suite de « = ») → 2 colonnes, UN « &amp; »
        ///   par ligne devant le signe (<c>[lhs &amp; =rhs]</c>) — V. POC
        ///   « simple » + bissection B-series : aligné.</item>
        /// <item>Au moins un ⟺/⟹ → 3 colonnes, DEUX « &amp; » par ligne
        ///   (<c>[conn &amp; lhs &amp; =rhs]</c>) — variantes V4/V5 : aligné
        ///   (la forme single-&amp; désalignait les lignes à connecteur,
        ///   variantes V1-V3).</item>
        /// </list>
        /// Le jc=left posé par Word à la promotion display est conservé
        /// (V5 : aligné ET à gauche, validé).</summary>
        public static XElement ComposeChain(IReadOnlyList<string> stenoLines, IReadOnlyList<string> latexLines)
        {
            var matches = new RelationLineMatch[latexLines.Count];
            bool anyConnector = false;
            for (int i = 0; i < latexLines.Count; i++)
            {
                string steno = i < stenoLines.Count ? stenoLines[i] : "";
                matches[i] = RelationLineDetector.TryDetect(steno);
                if (matches[i] != null && matches[i].IsConnector) anyConnector = true;
            }

            var eqArr = new XElement(M + "eqArr");
            for (int i = 0; i < latexLines.Count; i++)
            {
                string latex = latexLines[i] ?? "";
                var m = matches[i];

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
                    // CONNECTEUR (« ⟺ x=3 ») : colonne 1, équation scindée.
                    conn = m.MarkerLatex;
                    var (l, r) = LatexTopLevelSplit.Split(latex);
                    if (r != null) { lhs = l; relRhs = r; }
                    else lhs = latex;
                }
                eqArr.Add(anyConnector ? Row3(conn, lhs, relRhs) : Row2(lhs, relRhs));
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

        /// <summary>Ligne 2 colonnes (chaîne sans connecteur) — forme du
        /// POC « simple » : <c>[f(x) &amp; =2x+2-2]</c>, <c>[&amp; =2x]</c>.</summary>
        private static XElement Row2(string lhs, string relRhs)
        {
            var e = new XElement(M + "e");
            Graft(e, lhs);
            Amp(e);
            Graft(e, relRhs);
            return e;
        }

        /// <summary>Ligne 3 colonnes (chaîne AVEC connecteur) — forme V4/V5 :
        /// <c>[⇔ &amp; f(x)-1 &amp; =2+4]</c>, colonne 1 vide sur les lignes
        /// sans connecteur.</summary>
        private static XElement Row3(string conn, string lhs, string relRhs)
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
