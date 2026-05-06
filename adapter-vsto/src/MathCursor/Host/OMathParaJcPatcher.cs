using System;
using System.Text.RegularExpressions;

namespace MathCursor.Host
{
    /// <summary>
    /// Logique pure (sans dépendance Word) pour garantir que les OMaths qu'on
    /// insère sont en display avec <c>m:jc=left</c> — sinon Word centre par
    /// défaut. Cf. ADR <c>2026-05-05-Limit-omath-jc-stripped-on-fusion</c>
    /// pour la limite Word qu'on documente.
    /// </summary>
    internal static class OMathParaJcPatcher
    {
        private static readonly Regex _rxJcVal = new Regex(
            @"(<m:oMathParaPr[^>]*>(?:(?!</m:oMathParaPr>).)*?<m:jc\s+m:val="")[^""]*("")",
            RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex _rxParaPrSelfClosing = new Regex(
            @"<m:oMathParaPr\s*/>",
            RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex _rxParaPrNoJc = new Regex(
            @"<m:oMathParaPr\s*>(?!\s*<m:jc)",
            RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex _rxParaNoParaPr = new Regex(
            @"<m:oMathPara\s*>(?!\s*<m:oMathParaPr)",
            RegexOptions.Singleline | RegexOptions.Compiled);

        // <m:oMath>...</m:oMath> inline (avec attrs optionnels). Lookahead
        // exclut <m:oMathPara> (qui matche aussi "<m:oMath" en préfixe).
        private static readonly Regex _rxOMathInline = new Regex(
            @"<m:oMath(?=\s|>)(?:\s[^>]*)?>(?:(?!</m:oMath>).)*?</m:oMath>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// Garantit que le contenu math du <c>&lt;w:p&gt;</c> est dans
        /// <c>&lt;m:oMathPara&gt;&lt;m:oMathParaPr&gt;&lt;m:jc m:val="left"/&gt;</c>
        /// pour empêcher Word d'auto-centrer (default display math).
        /// <para>
        /// 2 cas couverts :
        /// </para>
        /// <list type="bullet">
        /// <item>Captured XML a déjà <c>&lt;m:oMathPara&gt;</c> (display) →
        /// délègue à <see cref="Patch"/> pour injecter/remplacer m:jc.</item>
        /// <item>Captured XML a juste <c>&lt;m:oMath&gt;</c> inline (pas de
        /// wrapper) → enrobe pré-emptivement, parce que Word auto-promeut
        /// le standalone-in-¶ en display sans m:jc → centré (cf. bug user
        /// 05-05 « formules une ligne s'auto-centrent »).</item>
        /// </list>
        /// </summary>
        /// <summary>
        /// Variante générique : garantit que le contenu math est en oMathPara
        /// avec le m:jc demandé (left/center/centerGroup/right). Si déjà
        /// oMathPara → patch m:jc. Sinon (m:oMath inline) → enrobe.
        /// </summary>
        public static string EnsureDisplayWithJc(string xml, string jc, out bool changed)
        {
            changed = false;
            if (string.IsNullOrEmpty(xml) || string.IsNullOrEmpty(jc)) return xml;

            // Déjà display ? → patch m:jc seul
            if (xml.IndexOf("<m:oMathPara", StringComparison.Ordinal) >= 0)
            {
                return Patch(xml, jc, out changed);
            }

            // Pas de display wrapper, peut-être inline pur. Cherche
            // <m:oMath>...</m:oMath> et enrobe avec un display + m:jc demandé.
            var match = _rxOMathInline.Match(xml);
            if (!match.Success) return xml;

            string omath = match.Value;
            string wrapped =
                "<m:oMathPara><m:oMathParaPr><m:jc m:val=\"" + jc + "\"/></m:oMathParaPr>"
                + omath
                + "</m:oMathPara>";
            changed = true;
            return xml.Substring(0, match.Index) + wrapped + xml.Substring(match.Index + match.Length);
        }

        /// <summary>Cas particulier (préservé pour rétrocompat des call sites
        /// existants) : EnsureDisplayWithJc avec jc=left.</summary>
        public static string EnsureDisplayWithLeftJc(string xml, out bool changed)
            => EnsureDisplayWithJc(xml, "left", out changed);

        /// <summary>
        /// Patch les attributs de justification OMathPara dans l'OOXML.
        /// 4 cas : (1) m:jc existe → remplace val ; (2) oMathParaPr auto-fermant
        /// → remplace par bloc complet ; (3) oMathParaPr ouvert sans m:jc →
        /// injecte ; (4) oMathPara sans oMathParaPr → injecte tout (cas par
        /// défaut Word).
        /// </summary>
        public static string Patch(string xml, string targetVal, out bool changed)
        {
            changed = false;
            if (string.IsNullOrEmpty(xml) || string.IsNullOrEmpty(targetVal)) return xml;

            // Cas 1 : m:jc existe déjà — remplace le val (s'il diffère)
            if (_rxJcVal.IsMatch(xml))
            {
                bool needsChange = false;
                string updated = _rxJcVal.Replace(xml, m =>
                {
                    string current = m.Value;
                    int valStart = current.IndexOf("m:val=\"", StringComparison.Ordinal) + 7;
                    int valEnd = current.IndexOf('"', valStart);
                    string currentVal = current.Substring(valStart, valEnd - valStart);
                    if (currentVal != targetVal) needsChange = true;
                    return m.Groups[1].Value + targetVal + m.Groups[2].Value;
                });
                changed = needsChange;
                return needsChange ? updated : xml;
            }

            string injection = "<m:oMathParaPr><m:jc m:val=\"" + targetVal + "\"/></m:oMathParaPr>";

            // Cas 2 : <m:oMathParaPr/> auto-fermant
            if (_rxParaPrSelfClosing.IsMatch(xml))
            {
                changed = true;
                return _rxParaPrSelfClosing.Replace(xml, injection, 1);
            }

            // Cas 3 : <m:oMathParaPr> ouvert sans m:jc — injecte m:jc juste après le tag
            if (_rxParaPrNoJc.IsMatch(xml))
            {
                changed = true;
                return _rxParaPrNoJc.Replace(xml, "<m:oMathParaPr><m:jc m:val=\"" + targetVal + "\"/>", 1);
            }

            // Cas 4 : <m:oMathPara> sans m:oMathParaPr du tout (default Word)
            if (_rxParaNoParaPr.IsMatch(xml))
            {
                changed = true;
                return _rxParaNoParaPr.Replace(xml, "<m:oMathPara>" + injection, 1);
            }

            return xml;
        }
    }
}
