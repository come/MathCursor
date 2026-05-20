namespace MathCursor.Core.Lattice.Ast
{
    /// <summary>
    /// Visiteur typé sur l'AST. Une méthode <c>Visit</c> par type concret —
    /// l'ajout d'un nouveau type AST oblige tous les visiteurs implémenteurs
    /// à fournir leur traitement (compile error sinon).
    ///
    /// <para>Pattern Visitor : remplace les <c>switch (node)</c> exhaustifs
    /// éparpillés dans les sérialiseurs / analyseurs / normaliseurs par un
    /// dispatch virtuel <c>node.Accept(visitor)</c>. Ajouter un node AST = 1
    /// méthode à implémenter dans chaque visiteur, plus aucun risque de drift
    /// silencieux (case manquant dans un switch qui retombe sur le <c>_ =&gt;
    /// default</c>).</para>
    ///
    /// <para>Cf. brief <c>MATHCURSOR_ARCHITECTURE_EXTENSIBILITE.md</c> §3.A et
    /// l'anti-pattern nº 1 (« <c>switch (node.Type)</c> dans le code partagé »).</para>
    ///
    /// <para>Note de portée : <see cref="IAstVisitor{TResult}"/> reste dans
    /// <c>MathCursor.Core/Lattice/Ast/</c> (couplé aux 18 types concrets) et
    /// NON dans <c>MathCursor.Core.Abstractions</c> — une abstraction qui
    /// dépendrait des types Core casserait l'isolation du projet
    /// Abstractions.</para>
    /// </summary>
    /// <typeparam name="TResult">Type de retour de chaque méthode Visit
    /// (souvent <c>string</c> pour les sérialiseurs LaTeX/OMath, <c>int</c>
    /// pour les counters, <c>bool</c> pour les prédicats récursifs, etc.).</typeparam>
    public interface IAstVisitor<out TResult>
    {
        TResult Visit(Atom node);
        TResult Visit(Hole node);
        TResult Visit(Const node);
        TResult Visit(Unary node);
        TResult Visit(Bin node);
        TResult Visit(Sup node);
        TResult Visit(Sub node);
        TResult Visit(Group node);
        TResult Visit(Frac node);
        TResult Visit(Sqrt node);
        TResult Visit(Vec node);
        TResult Visit(Angle node);
        TResult Visit(Func node);
        TResult Visit(Sum node);
        TResult Visit(Lim node);
        TResult Visit(Int node);
        TResult Visit(Interval node);
        TResult Visit(FuncDef node);
        TResult Visit(VectorCoordinates node);
        TResult Visit(MultiLineBlock node);
    }
}
