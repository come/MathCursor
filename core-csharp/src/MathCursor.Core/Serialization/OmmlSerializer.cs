using System.Text;
using MathCursor.Core.Ast;

namespace MathCursor.Core.Serialization;

/// <summary>
/// AST → OMML XML (Office Math Markup Language). Porté depuis
/// archive/officejs-prototype/src/taskpane/conversion/render.ts + omath/helpers.ts.
/// La sortie nue (inner) peut être enveloppée par BuildPackage pour insertOoxml VSTO.
/// </summary>
public static class OmmlSerializer
{
    private const string RFonts = "<w:rFonts w:ascii=\"Cambria Math\" w:hAnsi=\"Cambria Math\"/>";
    private const string Ctrl = "<m:ctrlPr><w:rPr>" + RFonts + "<w:i/></w:rPr></m:ctrlPr>";

    /// <summary>Un run m:r contenant le texte passé, avec la fonte Cambria Math italique.</summary>
    private static string Mr(string text) =>
        "<m:r><w:rPr>" + RFonts + "<w:i/></w:rPr><m:t>" + XmlEscape(text) + "</m:t></m:r>";

    public static string Serialize(MathNode node)
    {
        switch (node)
        {
            case EmptyNode: return "";
            case NumberNode n: return Mr(n.Value);
            case VariableNode v: return Mr(v.Name);
            case BinaryOpNode b: return Serialize(b.Left) + Mr(b.Op) + Serialize(b.Right);
            case UnaryNode u: return Mr(u.Op) + Serialize(u.Child);
            case FractionNode f:
                return "<m:f><m:fPr>" + Ctrl + "</m:fPr>"
                     + "<m:num>" + SerializeFracChild(f.Numerator) + "</m:num>"
                     + "<m:den>" + SerializeFracChild(f.Denominator) + "</m:den></m:f>";
            case SuperscriptNode s:
                return "<m:sSup><m:sSupPr>" + Ctrl + "</m:sSupPr>"
                     + "<m:e>" + Serialize(s.Base) + "</m:e>"
                     + "<m:sup>" + Serialize(s.Exponent) + "</m:sup></m:sSup>";
            case ParenNode p:
                var open = p.OpenChar == "(" ? "(" : "[";
                var close = p.OpenChar == "(" ? ")" : "]";
                return Mr(open) + Serialize(p.Inner) + Mr(close);
            case JuxtapositionNode j:
                var sb = new StringBuilder();
                foreach (var part in j.Parts) sb.Append(Serialize(part));
                return sb.ToString();
            default:
                return Mr("[?]");
        }
    }

    // Dans les fractions, on retire les parens externes pour ne pas alourdir le rendu
    private static string SerializeFracChild(MathNode node)
    {
        if (node is ParenNode p) return Serialize(p.Inner);
        return Serialize(node);
    }

    /// <summary>
    /// Enveloppe un fragment OMML dans un package complet consommable par
    /// Word.Range.InsertXML (VSTO) ou équivalent.
    /// </summary>
    public static string BuildPackage(string ommlInner)
    {
        return "<pkg:package xmlns:pkg=\"http://schemas.microsoft.com/office/2006/xmlPackage\">"
            + "<pkg:part pkg:name=\"/_rels/.rels\" pkg:contentType=\"application/vnd.openxmlformats-package.relationships+xml\">"
            + "<pkg:xmlData><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>"
            + "</Relationships></pkg:xmlData></pkg:part>"
            + "<pkg:part pkg:name=\"/word/document.xml\" pkg:contentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\">"
            + "<pkg:xmlData><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\">"
            + "<w:body><w:p><m:oMath>" + ommlInner + "</m:oMath></w:p></w:body>"
            + "</w:document></pkg:xmlData></pkg:part></pkg:package>";
    }

    private static string XmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
