using System.Collections.Generic;
using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Span du Ctrl+Espace manuel (ADR 2026-06-18-Fix-input-autocorrect-fraction-
    /// factorial). Régression clé : `!` retiré des délimiteurs → un caret juste
    /// après `n!` capte bien `n!` au lieu d'une span vide (= pas de popup).
    /// </summary>
    public sealed class SpanComputerTests
    {
        private static readonly IReadOnlyList<(int, int)> NoOMath = new List<(int, int)>();

        // Reproduit le flux de ConversionController.Trigger : span brute puis
        // trim des blancs aux deux bouts.
        private static string Span(string text, int caret)
        {
            int s = SpanComputer.ComputeSpanStart(text, caret, NoOMath);
            int e = SpanComputer.ComputeSpanEnd(text, caret, NoOMath);
            while (s < e && char.IsWhiteSpace(text[s])) s++;
            while (e > s && char.IsWhiteSpace(text[e - 1])) e--;
            return text.Substring(s, e - s);
        }

        [Fact]
        public void Factorielle_au_bout_est_captee()
        {
            // LE bug : caret juste après "!" donnait une span vide → pas de popup.
            Assert.Equal("n!", Span("n!", 2));
        }

        [Fact]
        public void Factorielle_apres_un_stopword()
        {
            const string t = "soit n!";
            Assert.Equal("n!", Span(t, t.Length));
        }

        [Fact]
        public void Factorielle_a_droite_d_un_egal()
        {
            // "=" borne toujours la span : côté droit "n!" seul.
            const string t = "a=n!";
            Assert.Equal("n!", Span(t, t.Length));
        }

        [Fact]
        public void Point_reste_un_delimiteur()
        {
            const string t = "fin. x+1";
            Assert.Equal("x+1", Span(t, t.Length));
        }

        [Fact]
        public void Expression_simple_entiere()
        {
            Assert.Equal("1/x+1", Span("1/x+1", 5));
        }

        // ── Matrices en cours de frappe : parenthèse non fermée → la zone
        //    englobe tout le groupe, les ; et , internes ne coupent pas
        //    (ADR 2026-06-19-Fix-spancomputer-unclosed-bracket-matrix). ──

        [Fact]
        public void Matrice_non_fermee_virgules_captee_entiere()
        {
            // LE bug rapporté : ne proposait que "e,f".
            const string t = "(a,b,c,d ;e,f";
            Assert.Equal("(a,b,c,d ;e,f", Span(t, t.Length));
        }

        [Fact]
        public void Matrice_non_fermee_espaces_captee_entiere()
        {
            // LE bug rapporté : "e f" seul → moteur erreur → rien.
            const string t = "(a b c d; e f";
            Assert.Equal("(a b c d; e f", Span(t, t.Length));
        }

        [Fact]
        public void Matrice_non_fermee_caret_au_milieu()
        {
            // Caret replacé après "c" (au milieu) → tout le groupe capté.
            const string t = "(a,b;c,d";
            Assert.Equal("(a,b;c,d", Span(t, 6)); // après le 2e ',' -> "(a,b;c|,d"
        }

        [Fact]
        public void Crochet_non_ferme_intervalle()
        {
            const string t = "[0;1";
            Assert.Equal("[0;1", Span(t, t.Length));
        }

        [Fact]
        public void Decimale_dans_parenthese_non_fermee()
        {
            // Le '.' ne doit pas arrêter la détection du groupe (séparateur décimal).
            const string t = "(1.5 ;2.5";
            Assert.Equal("(1.5 ;2.5", Span(t, t.Length));
        }

        [Fact]
        public void Matrice_fermee_inchangee()
        {
            const string t = "(a,b,c;d,e,f)";
            Assert.Equal("(a,b,c;d,e,f)", Span(t, t.Length));
        }

        [Fact]
        public void Point_virgule_hors_parenthese_coupe_toujours()
        {
            // Chaîne de raisonnement multi-zones : hors parenthèse, ';' borne
            // (isolé du '=' qui bornerait plus tôt) — le fix matrices ne doit
            // PAS désactiver le ';' séparateur hors groupe.
            const string t = "a ; b";
            Assert.Equal("b", Span(t, t.Length));
        }
    }
}
