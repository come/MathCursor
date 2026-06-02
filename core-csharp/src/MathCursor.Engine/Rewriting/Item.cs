using System;
using System.Collections.Generic;
using MathCursor.Engine.Tokenization;

namespace MathCursor.Engine.Rewriting
{
    /// <summary>
    /// Élément de la séquence en cours de rewriting. Soit un
    /// <see cref="TokenItem"/> (= primitive brute du Tokenizer), soit un
    /// <see cref="RewriteItem"/> (= produit d'une règle, porte un LaTeX
    /// émis + une catégorie déclarée + un flag partiel).
    ///
    /// <para>Moteur V2 (2026-05-29).</para>
    /// </summary>
    public abstract class Item
    {
        /// <summary>Catégorie sémantique (= type pour le matching de slots).</summary>
        public abstract Category Category { get; }

        /// <summary>Texte source agrégé (= ce que l'user a tapé pour la zone
        /// couverte par cet Item). Sert au debug et au span-tracking.</summary>
        public abstract string SourceText { get; }

        /// <summary>LaTeX de cet Item. Pour un TokenItem, le texte brut tel
        /// quel ; pour un RewriteItem, le résultat de l'emit.</summary>
        public abstract string Latex { get; }

        /// <summary>True si cet Item provient d'un match partiel (= des slots
        /// manquants rendus en <c>\square</c>). Toujours false pour un
        /// TokenItem. Utilisé en typing flow + scoring.</summary>
        public virtual bool IsPartial => false;

        /// <summary>Lectures ALTERNATIVES de ce même span source (= collisions).
        /// Ex. un corps `f(x)/x+1` porte la variante `\frac{f(x)}{x+1}`. Propagé
        /// à l'émission des règles → les collisions remontent récursivement à
        /// toute profondeur. Vide par défaut. Cf. ADR
        /// 2026-06-02-Feat-recursive-collisions-variants.</summary>
        public virtual IReadOnlyList<Item> Variants { get; set; } = System.Array.Empty<Item>();
    }

    /// <summary>Primitive issue du Tokenizer, pas encore rewriten.</summary>
    public sealed class TokenItem : Item
    {
        public Token Token { get; }

        public TokenItem(Token token)
        {
            Token = token ?? throw new ArgumentNullException(nameof(token));
        }

        public override Category Category => Classify(Token);

        public override string SourceText => Token.Text;
        public override string Latex => Token.Text;
        public override string ToString() => $"Tok[{Token.Kind}:{Token.Text}]";

        /// <summary>Classifie un Token brut en catégorie. Le tokenizer V2 ne
        /// fait QUE du découpage (= pas de reclassement sémantique des
        /// ambigus comme U/V/E — ceux-ci restent Letter/Var, et leur
        /// éventuel sens opérateur vient de règles YAML).</summary>
        private static Category Classify(Token t)
        {
            switch (t.Kind)
            {
                case TokenKind.Number: return Category.Number;
                case TokenKind.Symbol: return Category.Symbol;
                case TokenKind.Glue: return Category.Symbol;
                case TokenKind.OpenDelim: return Category.Delim;
                case TokenKind.CloseDelim: return Category.Delim;
                case TokenKind.Sep: return Category.Sep;
                case TokenKind.Word:
                    return ClassifyWord(t.Text);
                default:
                    return Category.Any;
            }
        }

        /// <summary>Classifie un Word. Les LaTeX cmd <c>\mathbb{…}</c> →
        /// <see cref="Category.Set"/> ; <c>\sin</c>/<c>\cos</c> (= sans
        /// accolade) → <see cref="Category.Function"/> ; lettre seule →
        /// <see cref="Category.Letter"/> ; reste → <see cref="Category.Var"/>.</summary>
        private static Category ClassifyWord(string text)
        {
            if (text.Length == 0) return Category.Var;
            if (text[0] == '\\')
            {
                if (text.StartsWith("\\mathbb{", System.StringComparison.Ordinal))
                    return Category.Set;
                return text.IndexOf('{') < 0 ? Category.Function : Category.Var;
            }
            if (text.Length == 1 && char.IsLetter(text[0]))
                return Category.Letter;
            // 2-3 lettres MAJUSCULES (= AB, ABC) : séquence ambiguë
            // produit/vecteur/groupe. Cf. ADR collision-uppercase-seq.
            if ((text.Length == 2 || text.Length == 3) && IsAllUpperLetters(text))
                return Category.UpperSeq;
            return Category.Var;
        }

        private static bool IsAllUpperLetters(string text)
        {
            foreach (var c in text)
                if (!char.IsLetter(c) || !char.IsUpper(c)) return false;
            return true;
        }
    }

    /// <summary>Produit d'une règle de rewriting : LaTeX émis + catégorie
    /// déclarée (<c>produces:</c>) + flag partiel.</summary>
    public sealed class RewriteItem : Item
    {
        public string RuleId { get; }
        public override Category Category { get; }
        public override string SourceText { get; }
        public override string Latex { get; }
        public override bool IsPartial { get; }

        public RewriteItem(string ruleId, Category category, string sourceText,
            string latex, bool isPartial)
        {
            RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
            Category = category;
            SourceText = sourceText ?? string.Empty;
            Latex = latex ?? string.Empty;
            IsPartial = isPartial;
        }

        public override string ToString()
            => $"Rw[{RuleId}:{Category}{(IsPartial ? ":partial" : "")}]({Latex})";
    }
}
