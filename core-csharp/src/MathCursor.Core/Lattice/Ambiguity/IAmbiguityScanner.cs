using System.Collections.Generic;

namespace MathCursor.Core.Lattice.Ambiguity
{
    /// <summary>
    /// Scanner d'ambiguïté : une famille d'<see cref="AmbiguityMatch"/>
    /// détectable par une heuristique propre (AST-based, string-based sur
    /// topLatex, source-based, etc.). Un scanner = un fichier dédié, une
    /// responsabilité.
    ///
    /// <para>Cf. ADR <c>2026-05-13-Refactor-ambiguity-scanners-strategy</c>.
    /// Aligné sur la doctrine projet : <see cref="MathCursor.Core.Lattice.IAstVisitor{TResult}"/>,
    /// <c>IZoneMerger</c>, <c>ICommitStage</c>, <c>IContextSignal</c>.</para>
    ///
    /// <para>Ajouter un scanner = nouveau fichier dans
    /// <c>Lattice/Ambiguity/Scanners/</c> + 1 ligne dans la registration
    /// de <see cref="AmbiguityScannerPipeline"/>. Pas de modification de
    /// l'orchestrateur — Open/Closed.</para>
    /// </summary>
    public interface IAmbiguityScanner
    {
        /// <summary>
        /// Ordre dans la pipeline. Plus petit = plus tôt. L'ordre encode
        /// des dépendances par <c>consumed[]</c> : un scanner précédent
        /// peut réserver des positions topLatex pour empêcher un scanner
        /// ultérieur d'y émettre.
        ///
        /// <para>Convention : 0-9 pour les scanners initiaux ; un nouveau
        /// scanner qui doit s'intercaler choisit un entier intermédiaire
        /// (l'<c>int</c> encode une priorité, pas un index d'array).</para>
        /// </summary>
        int Order { get; }

        /// <summary>
        /// Émet les <see cref="AmbiguityMatch"/> de cette famille dans
        /// <paramref name="output"/>. Le scanner DOIT respecter les
        /// positions déjà réservées via <paramref name="consumed"/>
        /// (skipper les ranges chevauchant) ET marquer les positions
        /// qu'il émet pour empêcher la double-émission par les scanners
        /// ultérieurs.
        /// </summary>
        void Scan(ScanContext ctx, List<AmbiguityMatch> output, bool[] consumed);
    }
}
