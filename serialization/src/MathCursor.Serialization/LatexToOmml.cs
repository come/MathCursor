// MathCursor: capturing mathematical intent from linear keyboard input.
// Copyright (C) 2026  Côme de Percin
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

#nullable disable
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace MathCursor.Serialization
{
    /// <summary>
    /// Convertit notre LaTeX en <b>OMML</b> (OfficeMath) — la STRUCTURE native
    /// que Word stocke pour ses équations. Contrairement à l'ancien chemin
    /// UnicodeMath (ligne linéaire que Word re-parsait au BuildUp, avec des
    /// bugs de précédence : lim happait le numérateur, \in avant \int, etc. —
    /// code mort supprimé, ADR 2026-06-22), l'OMML est inséré tel quel via
    /// <c>Range.InsertXML</c> et Word ne re-devine RIEN.
    ///
    /// <para>Retourne l'élément <c>&lt;m:oMath&gt;</c> (namespace math). À
    /// insérer dans un <c>&lt;w:p&gt;</c>. Cf. ADR 2026-06-02-Feat-omml-insertion
    /// + POC validé (backlink CC OK).</para>
    /// </summary>
    public static class LatexToOmml
    {
        public static readonly XNamespace M =
            "http://schemas.openxmlformats.org/officeDocument/2006/math";

        /// <summary>Convertit un LaTeX en élément <c>&lt;m:oMath&gt;</c>.</summary>
        public static XElement Convert(string latex)
        {
            var oMath = new XElement(M + "oMath");
            if (!string.IsNullOrEmpty(latex))
            {
                int i = 0;
                foreach (var el in ParseSeq(latex, ref i, stopAtBrace: false))
                    oMath.Add(el);
            }
            return oMath;
        }

        /// <summary>Sérialise l'<c>&lt;m:oMath&gt;</c> en string (tests/debug).</summary>
        public static string ConvertToString(string latex)
            => Convert(latex).ToString(SaveOptions.DisableFormatting);

        // ── Parsing récursif : LaTeX → liste d'éléments OMML ──────────────
        // Émet une séquence de runs (<m:r>) et de structures (<m:f>, <m:rad>,
        // <m:sSup>, <m:nary>, <m:func>, <m:d>, <m:m>, <m:acc>). Le texte courant
        // est accumulé puis flush en <m:r> quand une structure arrive.

        private static List<XElement> ParseSeq(string s, ref int i, bool stopAtBrace)
        {
            var outEls = new List<XElement>();
            var text = new StringBuilder();

            void Flush()
            {
                if (text.Length > 0) { outEls.Add(Run(CollapseSpaces(text.ToString()))); text.Clear(); }
            }

            while (i < s.Length)
            {
                char c = s[i];
                if (stopAtBrace && c == '}') break;

                if (c == '\\')
                {
                    // Commande : lit le nom (lettres).
                    int j = i + 1;
                    while (j < s.Length && char.IsLetter(s[j])) j++;
                    string cmd = s.Substring(i + 1, j - (i + 1));
                    if (cmd.Length == 0)
                    {
                        // Commandes d'espacement : ÉMETTRE l'espace (le code
                        // hérité les avalait → « 1 cm » rendu collé « 1cm »
                        // dans Word, bug user 2026-06-10). \, = espace fine
                        // U+2009 (unités : 1\,\mathrm{cm}, intégrales \, dx) ;
                        // \; \: = espace moyenne U+2005 ; "\ " = espace pleine.
                        // \! (négative) reste ignorée.
                        char next = j < s.Length ? s[j] : '\0';
                        if (next == ',') { text.Append(' '); i = j + 1; continue; }
                        if (next == ';' || next == ':') { text.Append(' '); i = j + 1; continue; }
                        if (next == ' ') { text.Append(' '); i = j + 1; continue; }
                        if (next == '!') { i = j + 1; continue; }
                        if (next == '{' || next == '}') { text.Append(next); i = j + 1; continue; }
                        i = j; continue;
                    }
                    i = j;
                    var st = ParseCommand(cmd, s, ref i, text, Flush, outEls);
                    if (st != null) { Flush(); outEls.Add(st); }
                    continue;
                }

                if (c == '^' || c == '_')
                {
                    // Exposant/indice : s'applique au texte courant (dernier
                    // atome). Si le buffer texte est VIDE (le token précédent
                    // était une STRUCTURE déjà émise, ex. \mathrm{cm}^{3} —
                    // bug « carrés » 2026-06-10 : base vide → Word affiche sa
                    // boîte de saisie), la base = dernier élément OMML émis.
                    string baseAtom = PopLastAtom(text);
                    Flush();
                    XElement poppedBase = null;
                    if (baseAtom.Length == 0 && outEls.Count > 0)
                    {
                        poppedBase = outEls[outEls.Count - 1];
                        outEls.RemoveAt(outEls.Count - 1);
                    }
                    var script = BuildScript(baseAtom, poppedBase, s, ref i);
                    if (script != null) outEls.Add(script);
                    continue;
                }

                if (c == '{') { i++; continue; }   // groupe nu : transparent
                if (c == '}') { i++; continue; }

                text.Append(c);
                i++;
            }
            Flush();
            return outEls;
        }

        // Exp/indice (+ combiné) attaché à une base. La base est soit le
        // dernier atome texte (baseAtom), soit un élément OMML déjà construit
        // (poppedBase — ex. le <m:r>cm</m:r> issu de \mathrm{cm}).
        private static XElement BuildScript(string baseAtom, XElement poppedBase, string s, ref int i)
        {
            bool sup = s[i] == '^';
            i++;
            var first = ReadArg(s, ref i);
            // Combiné a_b^c ou a^b_c ?
            string supTxt = null, subTxt = null;
            List<XElement> firstEls = ParseFragment(first);
            if (sup) supTxt = first; else subTxt = first;
            List<XElement> secondEls = null;
            if (i < s.Length && (s[i] == '^' || s[i] == '_'))
            {
                bool sup2 = s[i] == '^';
                i++;
                var second = ReadArg(s, ref i);
                secondEls = ParseFragment(second);
                if (sup2) supTxt = second; else subTxt = second;
            }
            var bEls = poppedBase != null
                ? new List<XElement> { poppedBase }
                : ParseFragment(baseAtom);
            if (supTxt != null && subTxt != null)
                return new XElement(M + "sSubSup",
                    new XElement(M + "e", bEls),
                    new XElement(M + "sub", ParseFragment(subTxt)),
                    new XElement(M + "sup", ParseFragment(supTxt)));
            if (sup)
                return new XElement(M + "sSup",
                    new XElement(M + "e", bEls),
                    new XElement(M + "sup", firstEls));
            return new XElement(M + "sSub",
                new XElement(M + "e", bEls),
                new XElement(M + "sub", firstEls));
        }

        // Dispatch des commandes structurelles. Retourne l'élément OMML, ou
        // null si la commande a déjà été traitée (texte/symbole accumulé).
        private static XElement ParseCommand(string cmd, string s, ref int i,
            StringBuilder text, Action flush, List<XElement> outEls)
        {
            switch (cmd)
            {
                case "frac": case "dfrac": case "tfrac":
                {
                    var a = ReadArg(s, ref i); var b = ReadArg(s, ref i);
                    return new XElement(M + "f",
                        new XElement(M + "num", ParseFragment(a)),
                        new XElement(M + "den", ParseFragment(b)));
                }
                case "sqrt":
                {
                    string deg = null;
                    if (i < s.Length && s[i] == '[')
                    {
                        int close = s.IndexOf(']', i + 1);
                        if (close > i) { deg = s.Substring(i + 1, close - i - 1); i = close + 1; }
                    }
                    var arg = ReadArg(s, ref i);
                    if (deg != null)
                        return new XElement(M + "rad",
                            new XElement(M + "deg", ParseFragment(deg)),
                            new XElement(M + "e", ParseFragment(arg)));
                    return new XElement(M + "rad",
                        new XElement(M + "radPr", new XElement(M + "degHide", new XAttribute(M + "val", "1"))),
                        new XElement(M + "deg"),
                        new XElement(M + "e", ParseFragment(arg)));
                }
                case "boxed":
                {
                    // \boxed{x} → <m:borderBox> (cadre 4 bords) ; pas de borderBoxPr
                    // = défaut Word (tous bords visibles, aucune barre).
                    var arg = ReadArg(s, ref i);
                    return new XElement(M + "borderBox",
                        new XElement(M + "e", ParseFragment(arg)));
                }
                case "vec": case "overrightarrow":
                    return Accent(ReadArg(s, ref i), "⃗");
                case "hat": case "widehat":
                    return Accent(ReadArg(s, ref i), "̂");
                case "bar": case "overline":
                    return Accent(ReadArg(s, ref i), "̅");
                case "dot": return Accent(ReadArg(s, ref i), "̇");
                case "ddot": return Accent(ReadArg(s, ref i), "̈");
                case "tilde": case "widetilde": return Accent(ReadArg(s, ref i), "̃");
                case "mathbb": case "mathcal": case "mathrm": case "mathbf":
                case "mathsf": case "operatorname": case "text":
                {
                    var arg = ReadArg(s, ref i);
                    if (cmd == "mathbb" && arg.Length == 1 && SetLetter.TryGetValue(arg, out var sc))
                    { text.Append(sc); return null; }
                    // Parse le contenu comme math (préserve \cdot, exposants… des unités
                    // composées \mathrm{m\cdot s^{-1}}) plutôt que de le dumper en texte brut.
                    flush();
                    outEls.AddRange(ParseFragment(arg));
                    return null;
                }
                case "lim": case "limsup": case "liminf":
                case "max": case "min": case "sup": case "inf":
                    return LimitFunc(cmd, s, ref i);
                case "left":
                {
                    // Délimiteur auto-sizé <m:d> : lit le délim ouvrant, le
                    // corps jusqu'au \right correspondant (imbrication gérée),
                    // puis le délim fermant. '.' = délim invisible (val="").
                    string beg = ReadDelim(s, ref i);
                    string body = ReadUntilMatchingRight(s, ref i);
                    string end = ReadDelim(s, ref i);
                    return new XElement(M + "d",
                        new XElement(M + "dPr",
                            new XElement(M + "begChr", new XAttribute(M + "val", beg)),
                            new XElement(M + "endChr", new XAttribute(M + "val", end))),
                        new XElement(M + "e", ParseFragment(body)));
                }
                case "right":
                    // \right orphelin (LaTeX mal formé) : consomme le délim, ignore.
                    ReadDelim(s, ref i);
                    return null;
                case "sum": return Nary("∑", s, ref i, "undOvr");
                case "prod": return Nary("∏", s, ref i, "undOvr");
                // Intégrales : bornes en indice/exposant À DROITE (subSup), pas
                // empilées au-dessus/dessous — convention typo + Word natif.
                case "int": return Nary("∫", s, ref i, "subSup");
                case "iint": return Nary("∬", s, ref i, "subSup");
                case "iiint": return Nary("∭", s, ref i, "subSup");
                case "oint": return Nary("∮", s, ref i, "subSup");
                case "binom":
                {
                    // coefficient binomial : <m:d>( … )</m:d> avec fraction SANS barre.
                    var a = ReadArg(s, ref i); var b = ReadArg(s, ref i);
                    var frac = new XElement(M + "f",
                        new XElement(M + "fPr", new XElement(M + "type", new XAttribute(M + "val", "noBar"))),
                        new XElement(M + "num", ParseFragment(a)),
                        new XElement(M + "den", ParseFragment(b)));
                    return Delimited("(", ")", frac);
                }
                case "begin":
                {
                    // \begin{pmatrix} … \end{pmatrix} → <m:d>( <m:m> … )</m:d>.
                    var env = ReadArg(s, ref i);
                    string endTok = "\\end{" + env + "}";
                    int endPos = s.IndexOf(endTok, i, StringComparison.Ordinal);
                    string body = endPos >= 0 ? s.Substring(i, endPos - i) : s.Substring(i);
                    i = endPos >= 0 ? endPos + endTok.Length : s.Length;
                    return MatrixEnv(env, body);
                }
                case "end":
                    // \end orphelin (mal formé) : consomme {env}, ignore.
                    ReadArg(s, ref i);
                    return null;
                case "mathbin":
                {
                    // \mathbin{…} : transparent — l'argument est rendu en math inline.
                    var arg = ReadArg(s, ref i);
                    flush();
                    outEls.AddRange(ParseFragment(arg));
                    return null;
                }
                case "not":
                {
                    // \not\subset → ⊄, etc. ; sinon ¬<symbole>.
                    if (i < s.Length && s[i] == '\\')
                    {
                        int j = i + 1;
                        while (j < s.Length && char.IsLetter(s[j])) j++;
                        string nxt = s.Substring(i + 1, j - (i + 1));
                        i = j;
                        if (i < s.Length && s[i] == ' ') i++; // espace délimiteur de nom
                        if (NegSymbols.TryGetValue(nxt, out var neg)) { text.Append(neg); return null; }
                        text.Append('¬');
                        if (Symbols.TryGetValue(nxt, out var rep2)) text.Append(rep2); else text.Append(nxt);
                        return null;
                    }
                    text.Append('¬'); return null;
                }
                default:
                    // Symbole littéral (grec, opérateur, relation, fonction trig).
                    if (Symbols.TryGetValue(cmd, out var rep)) text.Append(rep);
                    else text.Append(cmd); // inconnu : nom brut
                    // Règle LaTeX : l'espace qui SUIT une commande-nom est un
                    // délimiteur de nom, pas un espace visuel (`x\leq y` = x≤y).
                    // Le garder mettait des espaces parasites dans Word
                    // (découvert par LatexToOmmlTests, 2026-06-10).
                    if (i < s.Length && s[i] == ' ') i++;
                    return null;
            }
        }

        // lim/sup/… : <m:func><m:fName><m:limLow>{nom}{indice}</m:limLow></m:fName><m:e>{opérande}</m:e></m:func>
        private static XElement LimitFunc(string cmd, string s, ref int i)
        {
            string name = cmd; // lim, sup, …
            XElement fName;
            if (i < s.Length && s[i] == '_')
            {
                i++;
                var sub = ReadArg(s, ref i);
                fName = new XElement(M + "fName",
                    new XElement(M + "limLow",
                        new XElement(M + "e", Run(name)),
                        new XElement(M + "lim", ParseFragment(sub))));
            }
            else fName = new XElement(M + "fName", Run(name));
            // opérande = reste jusqu'à fin (le moteur a déjà scopé le corps).
            var bodyEls = ParseSeq(s, ref i, stopAtBrace: true);
            return new XElement(M + "func", fName, new XElement(M + "e", bodyEls));
        }

        // ∑/∫/… : <m:nary> avec chr, sub (from), sup (to), e (corps).
        // limLoc = "undOvr" (bornes empilées : ∑, ∏) ou "subSup" (bornes à
        // droite : ∫). Cf. ADR 2026-06-22-Fix-integrales-bornes-subsup.
        private static XElement Nary(string chr, string s, ref int i, string limLoc)
        {
            string sub = null, sup = null;
            while (i < s.Length && (s[i] == '_' || s[i] == '^'))
            {
                bool isSup = s[i] == '^'; i++;
                var arg = ReadArg(s, ref i);
                if (isSup) sup = arg; else sub = arg;
            }
            var naryPr = new XElement(M + "naryPr",
                new XElement(M + "chr", new XAttribute(M + "val", chr)),
                new XElement(M + "limLoc", new XAttribute(M + "val", limLoc)),
                new XElement(M + "subHide", new XAttribute(M + "val", sub == null ? "1" : "0")),
                new XElement(M + "supHide", new XAttribute(M + "val", sup == null ? "1" : "0")));
            var body = ParseSeq(s, ref i, stopAtBrace: true);
            TrimRunEnds(body); // pas d'espace de tête/queue collé à l'opérateur
            return new XElement(M + "nary",
                naryPr,
                new XElement(M + "sub", sub == null ? null : ParseFragment(sub)),
                new XElement(M + "sup", sup == null ? null : ParseFragment(sup)),
                new XElement(M + "e", body));
        }

        // Retire l'espace de TÊTE du 1er run texte et de QUEUE du dernier d'une
        // séquence. Pour le corps d'un n-aire : le template « \int_{a}^{b} f … »
        // a un espace littéral avant l'opérande → sinon Word colle un blanc
        // entre l'intégrale et son corps (retour user 2026-06-22). Les espaces
        // INTERNES (« f \, dx ») restent — seuls les bords sont rognés.
        private static void TrimRunEnds(List<XElement> els)
        {
            if (els.Count == 0) return;
            var firstT = els[0].Name == M + "r" ? els[0].Element(M + "t") : null;
            if (firstT != null)
            {
                int a = 0; var v = firstT.Value;
                while (a < v.Length && IsSpace(v[a])) a++;
                firstT.Value = v.Substring(a);
            }
            var lastT = els[els.Count - 1].Name == M + "r" ? els[els.Count - 1].Element(M + "t") : null;
            if (lastT != null)
            {
                var v = lastT.Value; int b = v.Length;
                while (b > 0 && IsSpace(v[b - 1])) b--;
                lastT.Value = v.Substring(0, b);
            }
        }

        private static XElement Accent(string arg, string combining)
            => new XElement(M + "acc",
                new XElement(M + "accPr", new XElement(M + "chr", new XAttribute(M + "val", combining))),
                new XElement(M + "e", ParseFragment(arg)));

        // Délimiteur auto-sizé <m:d> autour d'un contenu déjà construit.
        private static XElement Delimited(string beg, string end, params object[] content)
            => new XElement(M + "d",
                new XElement(M + "dPr",
                    new XElement(M + "begChr", new XAttribute(M + "val", beg)),
                    new XElement(M + "endChr", new XAttribute(M + "val", end))),
                new XElement(M + "e", content));

        // Environnement matriciel : \begin{pmatrix} a & b \\ c & d \end{pmatrix}
        // → <m:d>( <m:m><m:mr><m:e>a</m:e><m:e>b</m:e></m:mr>…</m:m> )</m:d>.
        // Lignes séparées par \\ (deux backslashes), colonnes par &.
        private static XElement MatrixEnv(string env, string body)
        {
            string beg = "(", end = ")";
            switch (env)
            {
                case "bmatrix": beg = "["; end = "]"; break;
                case "Bmatrix": beg = "{"; end = "}"; break;
                case "vmatrix": beg = "|"; end = "|"; break;
                case "Vmatrix": beg = "‖"; end = "‖"; break;
            }
            var m = new XElement(M + "m");
            foreach (var rowStr in body.Split(new[] { "\\\\" }, StringSplitOptions.None))
            {
                if (rowStr.Trim().Length == 0) continue;
                var mr = new XElement(M + "mr");
                foreach (var cell in rowStr.Split('&'))
                    mr.Add(new XElement(M + "e", ParseFragment(cell.Trim())));
                m.Add(mr);
            }
            return Delimited(beg, end, m);
        }

        private static XElement Run(string t) =>
            new XElement(M + "r", new XElement(M + "t",
                new XAttribute(XNamespace.Xml + "space", "preserve"), t));

        private static bool IsSpace(char c) => c == ' ' || c == ' ' || c == ' ';

        // Collapse les espaces CONSÉCUTIFS d'un run en un seul. En math un
        // littéral et un \, qui se suivent ne valent qu'UN espace ; sans ça
        // « \int f \, dx » cumulait espace + fine (U+2009) + espace = 3 espaces
        // avant dx dans Word (retour user 2026-06-22 ; WpfMath les ignore déjà
        // côté aperçu). On garde l'espace fine si le run en contient une (rendu
        // intégrale propre), sinon l'espace normal. Les espaces simples (un
        // seul, ex. « 1\,cm ») sont inchangés.
        private static string CollapseSpaces(string s)
        {
            var sb = new StringBuilder(s.Length);
            int k = 0;
            while (k < s.Length)
            {
                if (IsSpace(s[k]))
                {
                    char keep = ' ';
                    while (k < s.Length && IsSpace(s[k])) { if (s[k] == ' ') keep = ' '; k++; }
                    sb.Append(keep);
                }
                else sb.Append(s[k++]);
            }
            return sb.ToString();
        }

        // Parse un fragment isolé (argument) en liste d'éléments.
        private static List<XElement> ParseFragment(string frag)
        {
            int k = 0;
            return ParseSeq(frag ?? "", ref k, stopAtBrace: false);
        }

        // Lit un argument : {…} (équilibré) ou un seul caractère / \cmd.
        private static string ReadArg(string s, ref int i)
        {
            if (i >= s.Length) return "";
            if (s[i] == '{')
            {
                int depth = 1, start = ++i;
                while (i < s.Length && depth > 0)
                {
                    if (s[i] == '\\' && i + 1 < s.Length) { i += 2; continue; }
                    if (s[i] == '{') depth++;
                    else if (s[i] == '}') { depth--; if (depth == 0) break; }
                    i++;
                }
                string inner = s.Substring(start, i - start);
                if (i < s.Length) i++; // skip }
                return inner;
            }
            if (s[i] == '\\')
            {
                int j = i + 1;
                while (j < s.Length && char.IsLetter(s[j])) j++;
                string cmd = s.Substring(i, j - i);
                i = j;
                return cmd; // ex. \infty
            }
            return s[i++].ToString();
        }

        // Lit un délimiteur après \left / \right. Retourne le caractère OMML
        // (val de begChr/endChr). '.' → "" (délimiteur invisible).
        private static string ReadDelim(string s, ref int i)
        {
            if (i >= s.Length) return "";
            if (s[i] == '\\')
            {
                int j = i + 1;
                while (j < s.Length && char.IsLetter(s[j])) j++;
                if (j == i + 1) // \{ \} \| : un seul char non-lettre après \
                {
                    string one = s[i + 1].ToString(); i += 2;
                    return one == "|" ? "‖" : DelimChar(one); // \| = norme (double barre)
                }
                string cmd = s.Substring(i + 1, j - i - 1); i = j;
                return DelimChar(cmd);
            }
            char ch = s[i++];
            return ch == '.' ? "" : ch.ToString();
        }

        // Mappe un token de délimiteur (litéral ou \cmd) vers son caractère.
        private static string DelimChar(string tok)
        {
            switch (tok)
            {
                case ".": return "";
                case "{": case "lbrace": return "{";
                case "}": case "rbrace": return "}";
                case "|": case "vert": case "lvert": case "rvert": return "|";
                case "Vert": case "lVert": case "rVert": return "‖";
                case "langle": return "⟨";
                case "rangle": return "⟩";
                case "lfloor": return "⌊";
                case "rfloor": return "⌋";
                case "lceil": return "⌈";
                case "rceil": return "⌉";
                default: return tok; // ( ) [ ] etc. (ou inconnu : brut)
            }
        }

        // Consomme s à partir de i jusqu'au \right qui matche le \left courant
        // (imbrication \left/\right gérée), retourne le corps (sans le \right).
        // i est laissé juste après "\right" (sur le délim fermant).
        private static string ReadUntilMatchingRight(string s, ref int i)
        {
            int start = i, depth = 0;
            while (i < s.Length)
            {
                if (s[i] == '\\')
                {
                    if (MatchKw(s, i, "left")) { depth++; i += 5; continue; }
                    if (MatchKw(s, i, "right"))
                    {
                        if (depth == 0) { string body = s.Substring(start, i - start); i += 6; return body; }
                        depth--; i += 6; continue;
                    }
                }
                i++;
            }
            return s.Substring(start); // \right manquant (mal formé) : tout le reste
        }

        // True si s[at]=='\\' et \kw suit, terminé par un non-lettre (ou fin).
        private static bool MatchKw(string s, int at, string kw)
        {
            if (at + 1 + kw.Length > s.Length) return false;
            if (string.CompareOrdinal(s, at + 1, kw, 0, kw.Length) != 0) return false;
            int after = at + 1 + kw.Length;
            return after >= s.Length || !char.IsLetter(s[after]);
        }

        // Retire et retourne le dernier "atome" du buffer texte (pour base de ^/_).
        private static string PopLastAtom(StringBuilder text)
        {
            if (text.Length == 0) return "";
            char last = text[text.Length - 1];
            text.Remove(text.Length - 1, 1);
            return last.ToString();
        }

        private static readonly Dictionary<string, string> SetLetter = new Dictionary<string, string>
        {
            { "R", "ℝ" }, { "N", "ℕ" }, { "Z", "ℤ" }, { "Q", "ℚ" }, { "C", "ℂ" },
            { "K", "𝕂" }, { "P", "ℙ" }, { "F", "𝔽" },
        };

        // \not<rel> → relation barrée (négation). Cf. ParseCommand case "not".
        private static readonly Dictionary<string, string> NegSymbols = new Dictionary<string, string>
        {
            {"subset","⊄"},{"supset","⊅"},{"subseteq","⊈"},{"supseteq","⊉"},{"in","∉"},{"equiv","≢"},
        };

        // Symboles littéraux (grec, relations, opérateurs, fonctions).
        private static readonly Dictionary<string, string> Symbols = new Dictionary<string, string>
        {
            // ajouts portage forest : délimiteurs/relations/placeholder
            {"colon",":"},{"mid","∣"},{"langle","⟨"},{"rangle","⟩"},
            {"lfloor","⌊"},{"rfloor","⌋"},{"lceil","⌈"},{"rceil","⌉"},
            {"ast","∗"},{"cong","≅"},{"nexists","∄"},{"bmod","mod"},{"square","□"},
            {"infty","∞"},{"to","→"},{"mapsto","↦"},{"times","×"},{"cdot","⋅"},
            {"ldots","…"},{"cdots","⋯"},{"vdots","⋮"},{"ddots","⋱"},
            {"div","÷"},{"pm","±"},{"mp","∓"},{"leq","≤"},{"geq","≥"},{"neq","≠"},
            {"approx","≈"},{"equiv","≡"},{"sim","∼"},{"propto","∝"},
            {"in","∈"},{"notin","∉"},{"subset","⊂"},{"subseteq","⊆"},{"supset","⊃"},{"supseteq","⊇"},
            {"cup","∪"},{"cap","∩"},{"setminus","∖"},{"emptyset","∅"},
            {"forall","∀"},{"exists","∃"},{"Rightarrow","⇒"},{"Leftrightarrow","⇔"},
            {"iff","⇔"},{"implies","⇒"},{"wedge","∧"},{"vee","∨"},{"land","∧"},{"lor","∨"},{"neg","¬"},
            {"partial","∂"},{"nabla","∇"},{"circ","∘"},{"perp","⊥"},{"parallel","∥"},
            {"alpha","α"},{"beta","β"},{"gamma","γ"},{"delta","δ"},{"epsilon","ε"},{"varepsilon","ε"},
            {"zeta","ζ"},{"eta","η"},{"theta","θ"},{"iota","ι"},{"kappa","κ"},{"lambda","λ"},
            {"mu","μ"},{"nu","ν"},{"xi","ξ"},{"pi","π"},{"rho","ρ"},{"sigma","σ"},{"tau","τ"},
            {"upsilon","υ"},{"phi","ϕ"},{"varphi","φ"},{"chi","χ"},{"psi","ψ"},{"omega","ω"},
            {"Gamma","Γ"},{"Delta","Δ"},{"Theta","Θ"},{"Lambda","Λ"},{"Xi","Ξ"},{"Pi","Π"},
            {"Sigma","Σ"},{"Upsilon","Υ"},{"Phi","Φ"},{"Psi","Ψ"},{"Omega","Ω"},
            {"sin","sin"},{"cos","cos"},{"tan","tan"},{"ln","ln"},{"log","log"},{"exp","exp"},
        };
    }
}
