using System;
using System.Collections.Generic;

namespace MathCursor.Engine;

// lexer.js — chars → tokens. GÉNÉRIQUE : consulte le VOCABULAIRE pour savoir si un
// lexème est infixe/préfixe/n-aire. Ne nomme aucun opérateur.
internal static class Lexer
{
    private const int SplitCap = 4; // ≤ 2^4 flux ; au-delà, on découpe tout (un seul flux)

    private static bool IsAlpha(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    private static bool IsDigit(char c) => c >= '0' && c <= '9';
    private static bool IsUpper(char c) => c >= 'A' && c <= 'Z';

    // L'autocorrection typographique FR de Word insère U+00A0 (et U+202F en
    // versions récentes) avant « : ; ! ? » — pour le lexer ce sont des espaces.
    private static bool IsSpace(char c) => c == ' ' || c == '\u00A0' || c == '\u202F';

    // ── jonction de tokens collés (JOIN) — premier match gagne ───────────────
    private sealed class JoinRule
    {
        public string[] Left = Array.Empty<string>();
        public string[] Right = Array.Empty<string>();
        public string[] Roles = Array.Empty<string>();
        public bool Sticky;
        public bool CrossSpace;
    }

    private static readonly JoinRule[] Join =
    {
        // « x2 » → puissance SEULE (ADR 2026-06-10 sup-only : l'indice se
        // force avec _, les suites s'écrivent u_1 — contre-cas documenté).
        new() { Left = new[] { "name" }, Right = new[] { "num" }, Roles = new[] { "sup" }, Sticky = true },
        new() { Left = new[] { "close" }, Right = new[] { "num" }, Roles = new[] { "sup" }, Sticky = true },
        new() { Left = new[] { "unit" }, Right = new[] { "num" }, Roles = new[] { "sup" }, Sticky = true },
        new() { Left = new[] { "num" }, Right = new[] { "unit" }, Roles = new[] { "unitOp" }, Sticky = true, CrossSpace = true },
        new() { Left = new[] { "name", "unit", "post" }, Right = new[] { "open" }, Roles = new[] { "apply" } },
        new() { Left = new[] { "num", "name", "close", "post" }, Right = new[] { "name", "open", "func" }, Roles = new[] { "implicit" } },
    };

    public static List<Token> Lex(string src, EngineCulture culture, Func<int, bool>? choices = null, List<int>? runsOut = null)
    {
        var toks = new List<Token>();
        int i = 0;
        bool sawSpace = false;
        int brk = 0, brc = 0;

        char Ch(int idx) => idx >= 0 && idx < src.Length ? src[idx] : '\0';
        bool Spaced(int a, int b) => IsSpace(Ch(a)) || IsSpace(Ch(b));
        void Push(Token t) { t.SpaceBefore = sawSpace; toks.Add(t); sawSpace = false; }
        Token? Last() => toks.Count > 0 ? toks[toks.Count - 1] : null;

        bool StartsWithAt(string needle, int at)
        {
            if (at + needle.Length > src.Length) return false;
            return string.CompareOrdinal(src, at, needle, 0, needle.Length) == 0;
        }

        // classe+pousse UN mot (grec / mot-infixe / forme déclarée / mot-unité / atome).
        // Les alias de la culture sont résolus ICI (match exact) : les tokens
        // portent des Sym canoniques, l'aval (parser/score/render) ne voit
        // jamais un mot alias. Les replis « atome littéral » gardent le mot
        // original (« dans » isolé doit rendre « dans », pas « in »).
        void Word(string w, bool sp)
        {
            string k = culture.Canon(w);
            Vocabulary.Vocab.TryGetValue(k, out var v);
            VocabEntry? g = v is { Shape: "atom" } ? v
                : Vocabulary.Vocab.TryGetValue(w.ToLowerInvariant(), out var gl) ? gl : null;

            // Word AutoCorrect capitalise en début de phrase (« sum » → « Sum ») :
            // repli insensible à la casse pour les MOTS-CLÉS non-atomes ≥ 2
            // lettres (alias compris : « Somme » → somme → sum). Les atomes
            // gardent leur sémantique de casse (Pi → \Pi, R = ensemble) — leur
            // chemin (g) prime.
            if (v == null && g is not { Shape: "atom" } && w.Length >= 2)
            {
                var k2 = culture.Canon(w.ToLowerInvariant());
                if (k2 != k && Vocabulary.Vocab.TryGetValue(k2, out var v2)
                    && v2.Shape is not null and not "atom") // opérateurs seulement : ni atomes (casse signifiante) ni unités (Sym = mot original)
                { k = k2; v = v2; }
            }

            if (g is { Shape: "atom", Alts: { } alts })
                Push(new Token { Kind = "atom", Syms = alts, Coh = g.Coh });
            else if (g is { Shape: "atom" })
                Push(new Token { Kind = "atom", Sym = IsUpper(w[0]) ? g.Upper : g.Lower });
            else if (v is { Shape: "infix" })
            {
                var prev = Last();
                bool binaryPos = prev != null && (prev.Kind is "atom" or "rparen" or "bracket" or "bar" or "postfix");
                if (binaryPos) Push(new Token { Kind = "infix", Sym = k, Spaced = sp });
                else if (v.Unary != null) Push(new Token { Kind = "prefix", Sym = v.Unary, Spaced = sp });
                else Push(new Token { Kind = "atom", Sym = w });
            }
            else if (v is { WordSpace: true } && !IsSpace(Ch(i)))
                Push(new Token { Kind = "atom", Sym = w });
            else if (v is { Shape: { } shape })
                Push(new Token { Kind = shape, Sym = k, Spaced = sp });
            else if (v is { UnitWord: true })
                Push(new Token { Kind = "atom", Sym = w, UnitWord = true });
            else
                Push(new Token { Kind = "atom", Sym = w });
        }

        // plus long mot-clé Splittable présent dans str à la position at.
        static string BestSplittableAt(string str, int at)
        {
            string b = "";
            foreach (var k in Vocabulary.Splittable)
                if (k.Length > b.Length && at + k.Length <= str.Length && string.CompareOrdinal(str, at, k, 0, k.Length) == 0)
                    b = k;
            return b;
        }

        List<string> Decompose(string s)
        {
            var outp = new List<string>();
            int j = 0;
            while (j < s.Length)
            {
                string best = BestSplittableAt(s, j);
                // capitale de début de phrase (Word AutoCorrect) : « VecAB »,
                // « Sinx »… — en TÊTE de run seulement (c'est là que Word
                // capitalise), retenter en abaissant la 1re lettre et émettre
                // la clé minuscule (la capitale est involontaire).
                if (best.Length == 0 && j == 0 && IsUpper(s[0]) && s.Length >= 2)
                    best = BestSplittableAt(char.ToLowerInvariant(s[0]) + s.Substring(1), 0);
                if (best.Length > 0) { outp.Add(best); j += best.Length; }
                else if (IsUpper(s[j]))
                {
                    // suite de MAJUSCULES = UN morceau (paire de points géo :
                    // « vecAB » → vec + AB, pas vec·A·B où « A » mot-unité
                    // bloquerait la jonction — ADR split-distance-cost-vec).
                    int st = j;
                    while (j < s.Length && IsUpper(s[j])) j++;
                    outp.Add(s.Substring(st, j - st));
                }
                else { outp.Add(s[j].ToString()); j++; }
            }
            return outp;
        }

        while (i < src.Length)
        {
            char c = src[i];
            if (IsSpace(c)) { sawSpace = true; i++; continue; }
            // antislash LaTeX avalé devant un mot : \sum ≡ sum, \forall ≡
            // forall — le mot se résout ensuite par vocab + alias (les noms
            // divergents sont des alias génériques : \infty → inf, etc.).
            if (c == '\\' && IsAlpha(Ch(i + 1))) { i++; continue; }
            if (c == '(') { Push(new Token { Kind = "lparen" }); i++; continue; }
            if (c == ')') { Push(new Token { Kind = "rparen" }); i++; continue; }
            if (c == '[' || c == ']') { Push(new Token { Kind = "bracket", Sym = c.ToString() }); brk++; i++; continue; }
            if (c == '{') { Push(new Token { Kind = "lbrace" }); brc++; i++; continue; }
            if (c == '}') { Push(new Token { Kind = "rbrace" }); brc--; i++; continue; }
            if (c == '|') { bool norm = Ch(i + 1) == '|'; Push(new Token { Kind = "bar", Sym = norm ? "norm" : "abs" }); i += norm ? 2 : 1; continue; }
            if (Vocabulary.Sep.TryGetValue(c, out var rank)) { Push(new Token { Kind = "sep", Sym = c.ToString(), Rank = rank }); i++; continue; }

            // unité COMPOSÉE juste après un nombre (5 m/s)
            var pv = Last();
            if (pv is { Num: true })
            {
                string best = "";
                foreach (var k in Units.Compound.Keys)
                    if (k.Length > best.Length && StartsWithAt(k, i)) best = k;
                if (best.Length > 0) { Push(new Token { Kind = "atom", Sym = Units.Compound[best], UnitWord = true }); i += best.Length; continue; }
            }

            if (IsAlpha(c)) // run de lettres
            {
                int st = i; var sb = new System.Text.StringBuilder();
                while (i < src.Length && IsAlpha(src[i])) sb.Append(src[i++]);
                string s = sb.ToString();
                bool sp = Spaced(st - 1, i);
                // raccourcis dots « v... » / « d... » / « c... » (et formes
                // autocorrigées « v… ») : le mot absorbe les points qui
                // suivent si l'ALIAS « mot... » existe (table aliasGeneric,
                // résolue via Canon — pas un mot, d'où le lookahead). Repli
                // minuscule pour l'autocapitalisation Word (« V... »).
                int dotsLen = StartsWithAt("...", i) ? 3 : Ch(i) == '…' ? 1 : 0;
                if (dotsLen > 0
                    && (Vocabulary.Vocab.TryGetValue(culture.Canon(s + "..."), out var dotsV)
                        || Vocabulary.Vocab.TryGetValue(culture.Canon(s.ToLowerInvariant() + "..."), out dotsV))
                    && dotsV.Shape == "atom")
                {
                    Push(new Token { Kind = "atom", Sym = dotsV.Lower });
                    i += dotsLen;
                    continue;
                }
                // canonicaliser AUSSI ici : « existe » contient « xi » (ξ,
                // Splittable) et ne survit au découpage que si l'alias le
                // rend « connu » dans la culture courante.
                bool known = Vocabulary.Vocab.ContainsKey(culture.Canon(s))
                    || (Vocabulary.Vocab.TryGetValue(s.ToLowerInvariant(), out var lv) && lv.Shape == "atom")
                    || (s.Length >= 2 && Vocabulary.Vocab.ContainsKey(culture.Canon(s.ToLowerInvariant())));
                if (!known)
                {
                    var pieces = Decompose(s);
                    bool anySplit = pieces.Count >= 2 && pieces.Exists(p => Vocabulary.Splittable.Contains(p));
                    if (anySplit)
                    {
                        runsOut?.Add(st);
                        if (choices?.Invoke(st) ?? true)
                        {
                            for (int k = 0; k < pieces.Count; k++) Word(pieces[k], k == 0 && sp);
                            continue;
                        }
                    }
                }
                Word(s, sp);
                continue;
            }

            if (IsDigit(c)) // run de chiffres
            {
                var sb = new System.Text.StringBuilder();
                while (i < src.Length && IsDigit(src[i])) sb.Append(src[i++]);
                if (Array.IndexOf(culture.DecimalsIn, Ch(i)) >= 0
                    && !(Ch(i) == ',' && brc > 0) && IsDigit(Ch(i + 1)))
                {
                    i++; sb.Append(culture.DecimalTex);
                    while (i < src.Length && IsDigit(src[i])) sb.Append(src[i++]);
                }
                Push(new Token { Kind = "atom", Sym = sb.ToString(), Num = true });
                continue;
            }

            // opérateur infixe / postfixe / symbole-atome : PLUS-LONG-MATCH (3, 2 puis 1)
            bool matched = false;
            foreach (int len in new[] { 3, 2, 1 })
            {
                if (i + len > src.Length) continue;
                string op = src.Substring(i, len);
                if (!Vocabulary.Vocab.TryGetValue(op, out var v)
                    || (v.Shape != "infix" && v.Shape != "postfix" && v.Shape != "atom")) continue;
                // symbole-atome (« ... » 3 chars, « … » autocorrigé par Word →
                // \ldots) : poussé comme un mot-atome, Sym = LaTeX du Lit.
                if (v.Shape == "atom") { Push(new Token { Kind = "atom", Sym = v.Lower }); i += len; matched = true; break; }
                if (v.Shape == "postfix") { Push(new Token { Kind = "postfix", Sym = op }); i += len; matched = true; break; }

                var prev = Last();
                bool binaryPos = prev != null && (prev.Kind is "atom" or "rparen" or "bracket" or "bar" or "postfix");
                char after = Ch(i + len);
                bool detachedR = IsSpace(after) || ")],;".IndexOf(after) >= 0;
                if (len == 1 && v.PostSign != null && binaryPos && !sawSpace && detachedR)
                {
                    Push(new Token { Kind = "infix", Sym = Vocabulary.Role["sup"], Sticky = true });
                    Push(new Token { Kind = "atom", Sym = v.PostSign });
                }
                else if (len == 1 && v.Unary != null && !binaryPos)
                    Push(new Token { Kind = "prefix", Sym = v.Unary, Spaced = Spaced(i - 1, i + 1) });
                else if (v.Alts != null)
                    Push(new Token { Kind = "infix", Syms = v.Alts, Spaced = Spaced(i - 1, i + len) });
                else
                    Push(new Token { Kind = "infix", Sym = op, Spaced = Spaced(i - 1, i + len) });
                i += len; matched = true; break;
            }
            if (matched) continue;
            throw new Exception("caractère inattendu: " + c);
        }

        // saisie en cours : fermer VIRTUELLEMENT les ( non fermées
        int depth = 0;
        foreach (var t in toks) depth += t.Kind == "lparen" ? 1 : t.Kind == "rparen" ? -1 : 0;
        while (depth-- > 0) toks.Add(new Token { Kind = "rparen", SpaceBefore = false, Virtual = true });
        if (brk % 2 == 1) toks.Add(new Token { Kind = "bracket", Sym = "]", SpaceBefore = false, Virtual = true });

        return Juxtapose(toks);
    }

    private static string? Cat(Token? t)
    {
        if (t == null) return null;
        if (t.Kind == "atom")
        {
            if (t.UnitWord) return "unit";
            bool allDigits = t.Sym != null && t.Sym.Length > 0;
            if (allDigits) foreach (var ch in t.Sym!) if (!IsDigit(ch)) { allDigits = false; break; }
            return t.Num || allDigits ? "num" : "name";
        }
        return t.Kind switch
        {
            "lparen" => "open",
            "rparen" => "close",
            "postfix" => "post",
            "prefix" or "nary" => "func",
            _ => "op",
        };
    }

    private static List<Token> Juxtapose(List<Token> toks)
    {
        var outp = new List<Token>();
        for (int i = 0; i < toks.Count; i++)
        {
            var L = toks[i];
            var R = i + 1 < toks.Count ? toks[i + 1] : null;
            outp.Add(L);
            if (R == null) continue;

            var lc = Cat(L); var rc = Cat(R);
            JoinRule? rule = null;
            foreach (var r in Join)
                if (Array.IndexOf(r.Left, lc) >= 0 && Array.IndexOf(r.Right, rc) >= 0) { rule = r; break; }
            if (rule == null || (R.SpaceBefore && !rule.CrossSpace)) continue;

            var syms = new List<string>();
            foreach (var role in rule.Roles) if (Vocabulary.Role.TryGetValue(role, out var sym)) syms.Add(sym);
            if (syms.Count == 0) continue;

            var t = new Token { Kind = "infix", Spaced = rule.CrossSpace && R.SpaceBefore };
            if (syms.Count > 1) t.Syms = syms; else t.Sym = syms[0];
            if (Array.IndexOf(rule.Roles, "implicit") >= 0 || Array.IndexOf(rule.Roles, "apply") >= 0) t.Implicit = true;
            if (rule.Sticky) t.Sticky = true;
            outp.Add(t);
        }
        return outp;
    }

    private static int PopCount(int m) { int c = 0; while (m != 0) { c += m & 1; m >>= 1; } return c; }

    // NIVEAU 2 — lexAll : tous les flux (run entier OU découpé), tagués du nb de découpes.
    public static List<(List<Token> Toks, int Splits)> LexAll(string src, EngineCulture culture)
    {
        var runs = new List<int>();
        var whole = Lex(src, culture, _ => false, runs);
        int n = runs.Count;
        if (n == 0) return new() { (whole, 0) };
        if (n > SplitCap) return new() { (Lex(src, culture, _ => true), n), (whole, 0) };
        var outp = new List<(List<Token>, int)>();
        for (int m = 0; m < (1 << n); m++)
        {
            int mm = m;
            outp.Add((Lex(src, culture, st => (mm & (1 << runs.IndexOf(st))) != 0), PopCount(mm)));
        }
        return outp;
    }
}
