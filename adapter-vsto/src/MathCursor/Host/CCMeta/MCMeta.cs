using System;

namespace MathCursor.Host.CCMeta
{
    /// <summary>
    /// Métadonnée embarquée dans le <c>Tag</c> JSON du ContentControl
    /// qui enveloppe une OMath MathCursor. Phase A POC du pivot
    /// archi "probe locale + backlink natif" (brief 2026-05-18) :
    /// remplace à terme <c>EquationBookmarkRegistry</c> + <c>IEquationStore</c>
    /// CustomXMLPart par stockage in-line dans le CC, accessible en O(1)
    /// via <c>om.Range.ParentContentControl</c>.
    /// </summary>
    internal sealed class MCMeta
    {
        /// <summary>Version du schéma. Incrémenter si on change la structure.</summary>
        public int V { get; set; } = 1;

        /// <summary>HandleId stable de la formule. Clé de l'EquationHandleRegistry
        /// (sidecar in-memory). Généré au commit, persiste dans le Tag (donc
        /// suit copy-paste et reload doc).</summary>
        public string HandleId { get; set; }

        /// <summary>Sténo brute tapée par l'utilisateur (ex: "rac 1 sur x").</summary>
        public string Steno { get; set; }

        /// <summary>LaTeX résolu (ex: "\sqrt{1/x}").</summary>
        public string Latex { get; set; }

        /// <summary>Version de l'add-in MathCursor au moment du commit.</summary>
        public string Version { get; set; }

        /// <summary>SHA1 hex du <c>OMath.Range.WordOpenXML</c> au moment du commit.
        /// Permet de détecter une édition utilisateur ultérieure (hash change → stale).</summary>
        public string OmmlHash { get; set; }

        /// <summary>Timestamp UTC du commit (round-trip ISO 8601).</summary>
        public DateTime ParsedAt { get; set; }
    }
}
