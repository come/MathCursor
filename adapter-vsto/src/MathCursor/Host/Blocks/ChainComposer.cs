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
        /// <item>Au moins un ⟺/⟹ → 4 colonnes, TROIS « &amp; » par ligne
        ///   (<c>[pad &amp; conn &amp; lhs &amp; =rhs]</c>) — la colonne pad vide
        ///   en tête corrige la parité d'alignement de l'eqArr natif Word
        ///   (alternance droite/gauche) pour que le signe retombe en colonne
        ///   gauche-alignée. La forme 2-&amp; mettait le signe en colonne
        ///   droite-alignée → désaligné via le walker natif (bug 2026-06-22) ;
        ///   la forme single-&amp; désalignait déjà les lignes à connecteur.</item>
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

        /// <summary>Ligne 4 colonnes / 3 marques &amp; (chaîne AVEC connecteur).
        /// Une colonne PAD vide en tête décale la PARITÉ d'alignement de l'eqArr
        /// natif Word — qui alterne [droite][gauche][droite][gauche]… : en 2-&amp;
        /// le signe tombait en colonne DROITE-alignée (les signes ne s'alignaient
        /// plus, via le walker natif — bug 2026-06-22) ; en 3-&amp; il revient en
        /// colonne GAUCHE-alignée. Colonnes : <c>[pad &amp; conn &amp; lhs &amp; =rhs]</c>,
        /// colonnes conn/lhs vides sur les lignes sans connecteur / marqueur-relation.</summary>
        private static XElement Row3(string conn, string lhs, string relRhs)
        {
            var e = new XElement(M + "e");
            Amp(e);            // colonne pad vide → corrige la parité d'alignement
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
