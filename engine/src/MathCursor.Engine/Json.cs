using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MathCursor.Engine;

// Mini-lecteur JSON ZÉRO-DÉPENDANCE. Le moteur n'a aucun package NuGet (invariant
// de portabilité / compilation WASM, cf. ADR-001 + portable-engine) ; on ne peut
// donc pas utiliser System.Text.Json (package externe en netstandard2.0). Ce
// lecteur suffit pour la data universelle qu'on contrôle (data/engine/*.json) :
// objets, tableaux, chaînes (échappements \\ \" \/ \b \f \n \r \t \uXXXX),
// nombres, true/false/null. Modèle objet :
//   Dictionary<string, object?> | List<object?> | string | double | bool | null.
internal static class Json
{
    public static object? Parse(string s)
    {
        int i = 0;
        var v = ParseValue(s, ref i);
        SkipWs(s, ref i);
        if (i != s.Length) throw new FormatException($"JSON: contenu après la valeur racine (pos {i})");
        return v;
    }

    private static object? ParseValue(string s, ref int i)
    {
        SkipWs(s, ref i);
        if (i >= s.Length) throw new FormatException("JSON: fin inattendue");
        switch (s[i])
        {
            case '{': return ParseObject(s, ref i);
            case '[': return ParseArray(s, ref i);
            case '"': return ParseString(s, ref i);
            case 't': Expect(s, ref i, "true"); return true;
            case 'f': Expect(s, ref i, "false"); return false;
            case 'n': Expect(s, ref i, "null"); return null;
            default: return ParseNumber(s, ref i);
        }
    }

    private static Dictionary<string, object?> ParseObject(string s, ref int i)
    {
        var o = new Dictionary<string, object?>();
        i++; // {
        SkipWs(s, ref i);
        if (Peek(s, i) == '}') { i++; return o; }
        while (true)
        {
            SkipWs(s, ref i);
            string key = ParseString(s, ref i);
            SkipWs(s, ref i);
            if (Peek(s, i) != ':') throw new FormatException($"JSON: ':' attendu (pos {i})");
            i++;
            o[key] = ParseValue(s, ref i);
            SkipWs(s, ref i);
            char n = Peek(s, i);
            if (n == ',') { i++; continue; }
            if (n == '}') { i++; break; }
            throw new FormatException($"JSON: ',' ou '}}' attendu (pos {i})");
        }
        return o;
    }

    private static List<object?> ParseArray(string s, ref int i)
    {
        var a = new List<object?>();
        i++; // [
        SkipWs(s, ref i);
        if (Peek(s, i) == ']') { i++; return a; }
        while (true)
        {
            a.Add(ParseValue(s, ref i));
            SkipWs(s, ref i);
            char n = Peek(s, i);
            if (n == ',') { i++; continue; }
            if (n == ']') { i++; break; }
            throw new FormatException($"JSON: ',' ou ']' attendu (pos {i})");
        }
        return a;
    }

    private static string ParseString(string s, ref int i)
    {
        if (Peek(s, i) != '"') throw new FormatException($"JSON: '\"' attendu (pos {i})");
        i++;
        var sb = new StringBuilder();
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '"') return sb.ToString();
            if (c == '\\')
            {
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException($"JSON: échappement inconnu \\{e} (pos {i})");
                }
            }
            else sb.Append(c);
        }
        throw new FormatException("JSON: chaîne non terminée");
    }

    private static double ParseNumber(string s, ref int i)
    {
        int start = i;
        while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
        if (i == start) throw new FormatException($"JSON: valeur inattendue (pos {start})");
        return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
    }

    private static void Expect(string s, ref int i, string lit)
    {
        if (i + lit.Length > s.Length || s.Substring(i, lit.Length) != lit)
            throw new FormatException($"JSON: '{lit}' attendu (pos {i})");
        i += lit.Length;
    }

    private static char Peek(string s, int i) => i < s.Length ? s[i] : '\0';

    private static void SkipWs(string s, ref int i)
    {
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
    }
}
