using MathCursor.Core.Ast;

namespace MathCursor.Core.Serialization;

/// <summary>
/// AST → OMML XML (Office Math Markup Language). Utilisé par VSTO pour
/// insérer via Range.InsertXML. À porter depuis :
/// archive/officejs-prototype/src/taskpane/conversion/render.ts
/// </summary>
public static class OmmlSerializer
{
    public static string Serialize(MathNode node)
    {
        // TODO phase B : récursion sur l'AST, produire <m:r>, <m:f>, <m:sSup>, etc.
        return "";
    }
}
