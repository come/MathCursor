using System.Collections.Generic;
using System.Threading.Tasks;

namespace MathCursor.Core.Abstractions;

/// <summary>
/// Entité nommée extraite d'un texte par un modèle NER. Pour MathCursor,
/// l'usage principal est l'étiquetage B-MATH / I-MATH (zone math vs
/// texte naturel).
/// </summary>
public sealed class NamedEntity
{
    /// <summary>Offset 0-indexé dans le texte source.</summary>
    public int Start { get; }

    /// <summary>Offset exclusif (Start + Length).</summary>
    public int End { get; }

    /// <summary>Étiquette (ex. <c>"MATH"</c>, <c>"O"</c>).</summary>
    public string Label { get; }

    /// <summary>Confiance du modèle dans <c>[0, 1]</c>.</summary>
    public float Confidence { get; }

    public NamedEntity(int start, int end, string label, float confidence)
    {
        Start = start;
        End = end;
        Label = label ?? "O";
        Confidence = confidence;
    }
}

/// <summary>
/// <b>Axe C — Locale d'entrée naturelle (variante NER).</b>
/// Contrat d'un détecteur de zones math via modèle NER. Le modèle peut être
/// multilingue (un seul ONNX couvrant FR + EN + DE — choix actuel via
/// <c>MathNerDetector</c> DistilBERT-multilingual) ou mono-locale (un modèle
/// par langue, plus précis mais plus gros).
///
/// <para>L'abstraction permet de switcher la stratégie modèle sans toucher
/// au reste du pipeline. Le choix mono vs multi-modèle est un ADR à prendre
/// par locale ajoutée.</para>
///
/// <para>Cf. brief <c>MATHCURSOR_ARCHITECTURE_EXTENSIBILITE.md</c> §3.C, et
/// l'implémentation actuelle <c>adapter-vsto/src/MathCursor/Detection/MathNerDetector.cs</c>.</para>
/// </summary>
public interface ILocaleNER
{
    /// <summary>Identifiant BCP 47 de la locale couverte. Pour un modèle
    /// multilingue, on peut renvoyer une chaîne wildcard convenue (ex.
    /// <c>"*"</c>) ou enregistrer la même instance sous plusieurs locales.</summary>
    string LocaleId { get; }

    /// <summary>Extrait les entités math d'un texte. Asynchrone car l'inférence
    /// ONNX peut prendre des dizaines de ms à plusieurs centaines de ms selon
    /// la longueur du texte et le modèle.</summary>
    Task<IReadOnlyList<NamedEntity>> ExtractAsync(string text);
}
