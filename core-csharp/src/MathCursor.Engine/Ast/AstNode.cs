using System.Collections.Generic;

namespace MathCursor.Engine.Ast
{
    /// <summary>
    /// Nœud AST produit par <see cref="Parsing.StackParser"/> et
    /// <see cref="MathEngine"/>. Immutable. Sous-types :
    /// <see cref="AtomNode"/>, <see cref="InfixNode"/>, <see cref="GroupNode"/>,
    /// <see cref="ListNode"/>, <see cref="MatrixNode"/>, <see cref="LineNode"/>,
    /// <see cref="PlaceholderNode"/>, <see cref="UnaryPrefixNode"/>.
    ///
    /// <para>Les ancres (lim, sum, int, …) ne sont PAS matérialisées dans
    /// l'AST — <see cref="MathEngine.Resolve"/> les détecte via
    /// <see cref="Rules.ShapeMatcher"/> et émet leur LaTeX directement via
    /// <see cref="Emit.TemplateEmitter"/>, court-circuitant l'AST. L'AST ne
    /// voit que les operands flat.</para>
    /// </summary>
    public abstract class AstNode
    {
        public abstract string Kind { get; }
    }

    /// <summary>Atome : variable, nombre, mot littéral.</summary>
    public sealed class AtomNode : AstNode
    {
        public override string Kind => "atom";
        public string Text { get; }
        public string AtomKind { get; }  // "word" | "number"
        public AtomNode(string text, string atomKind) { Text = text; AtomKind = atomKind; }
    }

    /// <summary>Opérateur infixe (<c>a + b</c>, <c>x = y</c>, …).</summary>
    public sealed class InfixNode : AstNode
    {
        public override string Kind => "infix";
        public string Op { get; }
        public AstNode Left { get; }
        public AstNode Right { get; }
        public InfixNode(string op, AstNode left, AstNode right)
        { Op = op; Left = left; Right = right; }
    }

    /// <summary>Groupe parenthésé (<c>(...)</c>, <c>[...]</c>, <c>{...}</c>).</summary>
    public sealed class GroupNode : AstNode
    {
        public override string Kind => "group";
        public string Open { get; }
        public string Close { get; }
        public AstNode? Body { get; }
        public GroupNode(string open, string close, AstNode? body)
        { Open = open; Close = close; Body = body; }
    }

    /// <summary>
    /// Liste produite par le combinateur (<see cref="Parsing.List.ListCombinator"/>).
    /// <c>Sep</c> = type du séparateur utilisé (<c>colsep</c> ou <c>rowsep</c>).
    /// </summary>
    public sealed class ListNode : AstNode
    {
        public override string Kind => "list";
        public string Sep { get; }
        public IReadOnlyList<AstNode> Items { get; }
        public ListNode(string sep, IReadOnlyList<AstNode> items)
        { Sep = sep; Items = items; }
    }

    /// <summary>
    /// Placeholder à rendre comme <c>\square</c> (= sortie partielle pour
    /// les cadres ouverts ou les slots vides). Cf. brief v4 §1.5.
    /// </summary>
    public sealed class PlaceholderNode : AstNode
    {
        public override string Kind => "placeholder";
        public static readonly PlaceholderNode Instance = new PlaceholderNode();
        private PlaceholderNode() { }
    }

    /// <summary>
    /// Opérateur unaire préfixe (<c>+x</c>, <c>-x</c>). Modèle propre pour les
    /// cas où une expression commence par un signe sans opérande gauche.
    /// Émis par <see cref="Parsing.StackParser"/> uniquement pour les
    /// opérateurs whitelistés (= <c>+</c> et <c>-</c>) qui ont une sémantique
    /// math valide en préfixe. Cf. ADR
    /// <c>2026-05-23-Fix-engine-leading-unary-prefix</c>.
    /// </summary>
    public sealed class UnaryPrefixNode : AstNode
    {
        public override string Kind => "unaryPrefix";
        public string Op { get; }
        public AstNode Operand { get; }
        public UnaryPrefixNode(string op, AstNode operand)
        { Op = op; Operand = operand; }
    }
}
