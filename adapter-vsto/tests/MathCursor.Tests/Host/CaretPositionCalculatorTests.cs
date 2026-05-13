using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Tests pour <see cref="CaretPositionCalculator"/>. Couvre le bug user
    /// 05-05 « soit f » Ctrl+Espace → cursor descend : quand l'OMath est en
    /// fin de ¶, on ne doit PAS déborder dans le ¶ suivant.
    /// </summary>
    public sealed class CaretPositionCalculatorTests
    {
        // ─────────────────────────────────────────────────────────────────
        //  Bug 05-05 : OMath en fin de ¶ → clamp à paraContentEnd
        //
        //  Scénario "soit f" : ¶ "Soit f" avec f converti en OMath en position 5,
        //  OMath.End = 6, ¶ mark à position 6, paraContentEnd = 6, ¶ suivant
        //  à position 7. Sans clamp : afterPos = 7 = ¶ suivant → caret descend.
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Bug 05-05 : OMath fin de ¶ → clamp à paraContentEnd, pas de débordement")]
        public void OMathAtEndOfParagraph_ClampsToParaEnd()
        {
            // ¶ "Soit f" : positions 0-5 (6 chars), ¶ mark position 6.
            // Word: paraRange.End = 7, paraContentEnd = 6.
            // OMath remplace `f` à position 5 : OMath.Range = [5, 6), omEnd = 6.
            int omEnd = 6;
            int paraContentEnd = 6;
            int docContentEnd = 100; // assez large

            int got = CaretPositionCalculator.ClampAfterOMathToParagraph(omEnd, paraContentEnd, docContentEnd);

            // Attendu : 6 (= juste avant le ¶ mark, sur la même ligne).
            // PAS 7 (= start du ¶ suivant, "le cursor descend").
            Assert.Equal(6, got);
        }

        // ─────────────────────────────────────────────────────────────────
        //  OMath au milieu du ¶ : pas de clamp, omEnd+1 normal
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "OMath au milieu du ¶ : afterPos = omEnd + 1 (pas de clamp)")]
        public void OMathInMiddleOfParagraph_NoClampNeeded()
        {
            // ¶ "soit f rouge" : positions 0-11, ¶ mark à 12.
            // OMath remplace `f` à position 5 : OMath = [5, 6), omEnd = 6.
            // Texte " rouge" suit l'OMath, paraContentEnd = 11.
            int omEnd = 6;
            int paraContentEnd = 11;
            int docContentEnd = 100;

            int got = CaretPositionCalculator.ClampAfterOMathToParagraph(omEnd, paraContentEnd, docContentEnd);

            // Attendu : 7 = juste après l'OMath, dans le texte " rouge".
            Assert.Equal(7, got);
        }

        // ─────────────────────────────────────────────────────────────────
        //  OMath en fin de doc (dernier ¶) : clamp à docContentEnd
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "OMath en fin de doc : clamp à docContentEnd")]
        public void OMathAtEndOfDocument_ClampsToDocEnd()
        {
            // Dernier ¶ du doc, OMath en fin. Word: doc.Content.End = position
            // après le dernier char. Si omEnd+1 dépasse docContentEnd, on clamp.
            int omEnd = 50;
            int paraContentEnd = 50;  // = ¶ mark final
            int docContentEnd = 50;   // doc finit pile

            int got = CaretPositionCalculator.ClampAfterOMathToParagraph(omEnd, paraContentEnd, docContentEnd);

            Assert.Equal(50, got);
        }

        [Fact(DisplayName = "docContentEnd plus restrictif que paraContentEnd : clamp à doc")]
        public void DocEndStricterThanParaEnd_ClampsToDoc()
        {
            // Cas (peu probable mais défensif) : doc.Content.End < paraContentEnd.
            // On clamp d'abord à doc, puis à ¶, le résultat le plus petit gagne.
            int omEnd = 99;
            int paraContentEnd = 200;
            int docContentEnd = 100;

            int got = CaretPositionCalculator.ClampAfterOMathToParagraph(omEnd, paraContentEnd, docContentEnd);

            // omEnd+1 = 100, déjà = docContentEnd → pas plus loin.
            // paraContentEnd = 200 > 100 → pas de clamp ¶.
            Assert.Equal(100, got);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Cas dégénérés
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "OMath déjà au-delà du ¶ (état corrompu) : retourne paraContentEnd")]
        public void OMathBeyondParaEnd_ReturnsParaEnd()
        {
            // Ne devrait pas arriver mais on protège : si omEnd+1 dépasse
            // paraContentEnd, on rabat à paraContentEnd (= jamais déborder).
            int omEnd = 20;
            int paraContentEnd = 10;
            int docContentEnd = 100;

            int got = CaretPositionCalculator.ClampAfterOMathToParagraph(omEnd, paraContentEnd, docContentEnd);

            Assert.Equal(10, got);
        }

        [Theory]
        [InlineData(0, 0, 0, 0)]    // doc vide
        [InlineData(5, 5, 100, 5)]  // OMath fin ¶ avec doc plus loin
        [InlineData(5, 100, 6, 6)]  // OMath dans ¶ mais doc finit juste après
        public void Plan_VariousEdgeCases(int omEnd, int paraContentEnd, int docContentEnd, int expected)
        {
            int got = CaretPositionCalculator.ClampAfterOMathToParagraph(omEnd, paraContentEnd, docContentEnd);
            Assert.Equal(expected, got);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Cellule de tableau : paraRange = cellule, le clamp ne doit pas
        //  déborder hors cellule.
        //
        //  Validation user-facing demande à 2026-05-13 (« si on est dans
        //  un tableau pareil » — caret doit rester dans la cellule).
        //  Logique pure : la classe ne voit pas qu'on est en cellule,
        //  elle clamp sur paraContentEnd quel qu'il soit. Suffisant tant
        //  que CaretPositioner.ComputeAfterOMath utilise Paragraphs[1]
        //  qui dans une cellule = ¶ de la cellule (pas du body global).
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "OMath dans cellule de tableau : clamp respecte les bornes de cellule")]
        public void OMathInTableCell_ClampsToCellEnd()
        {
            // Cellule : positions [100..120], paraContentEnd = 120 (juste
            // avant le marqueur de cellule), doc continue à 500.
            // OMath occupe la cellule : omEnd = 120.
            int omEnd = 120;
            int paraContentEnd = 120;
            int docContentEnd = 500;

            int got = CaretPositionCalculator.ClampAfterOMathToParagraph(omEnd, paraContentEnd, docContentEnd);

            // Attendu : 120 = reste dans la cellule (pas 121 = cellule suivante).
            Assert.Equal(120, got);
        }
    }
}
