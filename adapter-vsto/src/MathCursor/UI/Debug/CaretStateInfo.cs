using System.Collections.Generic;

namespace MathCursor.UI.Debug
{
    /// <summary>
    /// Snapshot inerte de l'état du caret pour l'inspecteur debug. Aucun
    /// objet COM Word — calculé une fois lors du snapshot, safe à
    /// passer cross-thread / cross-frame.
    /// </summary>
    public sealed class CaretStateInfo
    {
        public int SelStart { get; set; }
        public int SelEnd { get; set; }
        public int SelOMathsCount { get; set; }

        /// <summary>Range [start, end) du ¶ contenant le caret. -1 si lecture échouée.</summary>
        public int ParaStart { get; set; } = -1;
        public int ParaEnd { get; set; } = -1;
        /// <summary>Preview tronqué du texte du ¶ (max 60 chars).</summary>
        public string ParaTextPreview { get; set; }

        public bool InTable { get; set; }
        public int? TableRow { get; set; }
        public int? TableCol { get; set; }
        public int? CellStart { get; set; }
        public int? CellEnd { get; set; }

        /// <summary>Range [start, end) de l'OMath englobant le caret. Null si caret hors OMath.</summary>
        public int? OMathStart { get; set; }
        public int? OMathEnd { get; set; }

        /// <summary>Enfants directs du &lt;w:p&gt; parent (runs, omaths, omathPara, etc.) dans l'ordre du document. Vide si parsing échoué.</summary>
        public List<CaretSiblingInfo> Siblings { get; set; } = new List<CaretSiblingInfo>();

        public string ErrorMessage { get; set; }
    }

    /// <summary>Un enfant direct du ¶ parent (= sibling structurel du caret).</summary>
    public sealed class CaretSiblingInfo
    {
        /// <summary>Tag local XML (ex: "r", "oMath", "oMathPara", "bookmarkStart").</summary>
        public string Kind { get; set; }
        /// <summary>Preview du contenu texte de l'enfant (max 40 chars). Vide pour markers structurels.</summary>
        public string TextPreview { get; set; }
        /// <summary>Vrai si le caret est positionné DANS ce sibling (= caret au milieu de ce run / OMath).</summary>
        public bool ContainsCaret { get; set; }
    }
}
