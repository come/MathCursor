// Pont WASM↔JS de l'host VSCode. Expose LE même ForestEngine que Word / la démo
// web (cf. web-demo/.../Bridge.cs) via [JSExport]. On s'arrête au LaTeX : VSCode
// n'a pas besoin d'OMML (Word-only). JSON construit à la main pour ne dépendre
// d'aucune réflexion (robuste au trimming WASM).

using System.Runtime.InteropServices.JavaScript;
using System.Text;
using MathCursor.Engine;

// Main no-op : le moteur est invoqué à la demande via Bridge.Analyze.
return;

internal static partial class Bridge
{
    /// <summary>
    /// Analyse <paramref name="input"/> avec la culture math demandée
    /// ("us" → point décimal + matrices [], sinon FR). Retourne un JSON
    /// {"decision","hasNote","ranked":[{"latex","cost"}]}. Jamais d'exception
    /// vers JS : tout échec retombe sur decision="erreur".
    /// </summary>
    [JSExport]
    internal static string Analyze(string input, string culture)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "{\"decision\":\"erreur\",\"hasNote\":false,\"ranked\":[]}";

        try
        {
            var cult = string.Equals(culture, "us", System.StringComparison.OrdinalIgnoreCase)
                ? EngineCulture.Us
                : EngineCulture.Fr;

            var r = ForestEngine.Analyze(input, cult);

            var sb = new StringBuilder(256);
            sb.Append("{\"decision\":\"").Append(Esc(r.Decision)).Append("\",");
            sb.Append("\"hasNote\":").Append(r.HasNote ? "true" : "false").Append(',');
            sb.Append("\"ranked\":[");
            for (int i = 0; i < r.Ranked.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"latex\":\"").Append(Esc(r.Ranked[i].Latex)).Append("\",");
                sb.Append("\"cost\":").Append(r.Ranked[i].Cost.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }
        catch
        {
            return "{\"decision\":\"erreur\",\"hasNote\":false,\"ranked\":[]}";
        }
    }

    private static string Esc(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
