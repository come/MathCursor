using System;
using MathCursor.Engine.Tokenization;

namespace MathCursor.Engine.Rewriting
{
    /// <summary>
    /// Élément de la séquence en cours de rewriting. Soit un
    /// <see cref="TokenItem"/> (= primitive sortie du Tokenizer, encore brut),
    /// soit un <see cref="RewriteItem"/> (= produit d'une règle, porte un
    /// LaTeX déjà émis + une catégorie déclarée).
    ///
    /// <para>Migration Chantier 4 Phase A (2026-05-25) — POC rewriting-based.</para>
    /// </summary>
    public abstract class Item
    {
        /// <summary>Catégorie sémantique. Pour <see cref="TokenItem"/>, dérivée
        /// du <see cref="TokenKind"/> + heuristique simple (= Word 1 char →
        /// <see cref="Category.Letter"/>, sinon <see cref="Category.Var"/>).</summary>
        public abstract Category Category { get; }

        /// <summary>Représentation textuelle « source » (= ce que l'user a tapé
        /// pour un TokenItem, ou l'agrégat des spans absorbés pour un RewriteItem).
        /// Utilisé pour le debug et le span-tracking.</summary>
        public abstract string SourceText { get; }

        /// <summary>LaTeX déjà émis si <see cref="RewriteItem"/> ; pour un
        /// <see cref="TokenItem"/>, c'est le texte brut éligible à être emit
        /// tel quel dans la concat finale (= un Number reste "123", un Letter
        /// reste "x").</summary>
        public abstract string Latex { get; }
    }

    /// <summary>Primitive sortie du Tokenizer, pas encore rewriten.</summary>
    public sealed class TokenItem : Item
    {
        public Token Token { get; }
        public TokenItem(Token token)
        {
            Token = token ?? throw new ArgumentNullException(nameof(token));
        }

        public override Category Category => Token.Kind switch
        {
            TokenKind.Word => ClassifyWord(Token.Text),
            TokenKind.Number => Category.Number,
            TokenKind.Symbol => Category.Symbol,
            TokenKind.Glue => Category.Symbol,
            TokenKind.OpenDelim => Category.Delim,
            TokenKind.CloseDelim => Category.Delim,
            TokenKind.Sep => Category.Sep,
            _ => Category.Any,
        };

        /// <summary>Classifie un Word en catégorie sémantique. Phase D-4 :
        /// les Words LaTeX <b>simples</b> commençant par <c>\</c> sans
        /// accolade (= <c>\sin</c>, <c>\cos</c>, <c>\log</c>, <c>\ln</c>)
        /// sont <see cref="Category.Function"/>. Les Words structurels avec
        /// accolades (= <c>\mathbb{N}</c>, <c>\text{...}</c>) restent
        /// <see cref="Category.Var"/> pour ne pas être traités comme
        /// des fonctions applicables.</summary>
        private static Category ClassifyWord(string text)
        {
            if (text.Length == 0) return Category.Var;
            if (text[0] == '\\')
            {
                // LaTeX command simple (sans `{`) → Function. Avec `{` → Var
                // (= constructeur structurel comme \mathbb{N}, \text{...}).
                if (text.IndexOf('{') < 0) return Category.Function;
                return Category.Var;
            }
            if (text.Length == 1 && char.IsLetter(text[0])) return Category.Letter;
            return Category.Var;
        }

        public override string SourceText => Token.Text;
        public override string Latex => Token.Text;
        public override string ToString() => $"Tok[{Token.Kind}({Token.Text})]";
    }

    /// <summary>Produit d'une règle de rewriting. Porte un LaTeX déjà émis +
    /// la catégorie déclarée par la règle (= <c>produces:</c>).</summary>
    public sealed class RewriteItem : Item
    {
        public string RuleId { get; }
        public override Category Category { get; }
        public override string SourceText { get; }
        public override string Latex { get; }

        public RewriteItem(string ruleId, Category category, string sourceText, string latex)
        {
            RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
            Category = category;
            SourceText = sourceText ?? string.Empty;
            Latex = latex ?? string.Empty;
        }

        public override string ToString() => $"Rw[{RuleId}:{Category}]({Latex})";
    }
}
