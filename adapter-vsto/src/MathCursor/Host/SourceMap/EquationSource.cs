using System;

namespace MathCursor.Host.SourceMap
{
    /// <summary>
    /// Entrée de la map inversée hash(OMath) → source (expérience
    /// exp-hash-source-no-cc, ADR 2026-06-11-Feat-hash-source-map-no-cc).
    /// Remplace <c>MCMeta</c> : la métadonnée ne vit plus dans le Tag d'un
    /// ContentControl anchor mais dans une CustomXMLPart au niveau document,
    /// indexée par le contenu de l'OMath elle-même.
    /// </summary>
    internal sealed class EquationSource
    {
        /// <summary>Clé d'ACCÈS (cheap) : SHA1 hex de <c>om.Range.Text</c>.
        /// ~0 ms à recalculer au lookup ; peut collisionner entre structures
        /// différentes au texte linéaire identique (x² vs x₂) — d'où K2.</summary>
        public string K1 { get; set; }

        /// <summary>Clé de CONFIRMATION : SHA1 hex de l'OMML canonique
        /// (<see cref="OmmlCanonicalizer"/>). Chère (~60 ms : lit
        /// <c>WordOpenXML</c>) — calculée au commit, et au lookup seulement
        /// pour départager une ambiguïté K1 ou confirmer un revert.</summary>
        public string K2 { get; set; }

        /// <summary>Sténo brute tapée (ex: "rac 1 sur x"). Pour un BLOC :
        /// lignes jointes par '\n', marqueurs compris.</summary>
        public string Steno { get; set; }

        /// <summary>LaTeX résolu (ex: "\sqrt{1/x}"). Pour un BLOC : LaTeX par
        /// ligne (SANS marqueur) joints par '\n'.</summary>
        public string Latex { get; set; }

        /// <summary>null = équation simple, "chain" = chaîne, "system" = système.</summary>
        public string Type { get; set; }

        /// <summary>Identifiant de commit (logging/events). Ne survit PAS au
        /// doublon : deux équations identiques partagent l'entrée,
        /// dernier-écrit-gagne (acté ADR).</summary>
        public string HandleId { get; set; }

        /// <summary>Version de l'add-in au moment du commit.</summary>
        public string Version { get; set; }

        /// <summary>Timestamp UTC du commit — sert de critère d'éviction au cap.</summary>
        public DateTime ParsedAt { get; set; }

        // ── Classification pour l'encadrement (ADR 2026-06-22) ───────────

        /// <summary>Bloc empilé (chaîne / système) : sténo et LaTeX joints par
        /// '\n'. C'est le cas « multiligne » → l'encadrement ne vise que la
        /// dernière ligne.</summary>
        public bool IsBlock =>
            !string.IsNullOrEmpty(Type) || (Steno ?? "").IndexOf('\n') >= 0;

        /// <summary>Équation simple mais à structure « tableau » (matrice / env.
        /// LaTeX) : hors périmètre encadrement v1.</summary>
        public bool IsMatrixLike =>
            !IsBlock && ((Latex ?? "").Contains("\\\\") || (Latex ?? "").Contains("\\begin{"));

        /// <summary>Déjà encadrée (un \boxed quelque part dans le LaTeX).</summary>
        public bool IsAlreadyBoxed => (Latex ?? "").Contains("\\boxed{");

        /// <summary>Encadrable : ni déjà encadrée, ni matrice/env. complexe.</summary>
        public bool CanBox => !IsAlreadyBoxed && !IsMatrixLike;
    }
}
