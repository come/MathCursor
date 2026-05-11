using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace MathCursor.Host
{
    /// <summary>
    /// Logique pure (sans dépendance Word) pour splicer une nouvelle
    /// <c>&lt;m:oMath&gt;</c> dans le <c>&lt;w:p&gt;</c> existant d'un
    /// document, en remplaçant uniquement les <c>&lt;w:r&gt;</c> qui
    /// couvrent la source brute du commit (texte qu'on vient de taper).
    ///
    /// <para>Utilise <see cref="XDocument"/> (LINQ to XML) pour naviguer
    /// dans le doc XML en mode parser, pas regex. Robuste aux variations
    /// de format Word (self-closing tags, attribute order, whitespace,
    /// namespaces, runs imbriqués…).</para>
    ///
    /// <para>Cf. ADR <c>2026-05-07-Fix-insert-via-paragraph-xml-splice</c>.
    /// L'approche précédente (<c>textBefore = Range.Text</c> +
    /// reconstruction du ¶ depuis BuildUp) était lossy pour les OMaths
    /// voisines : <c>Range.Text</c> aplatit la structure d'une OMath voisine
    /// → BuildUp ultérieure dégénère → l'OMath voisine disparaît du ¶.</para>
    /// </summary>
    internal static class InlineOMathSplicer
    {
        // Namespaces standards WordprocessingML / OfficeMath. On les
        // déclare en constantes pour les utiliser dans les XName.
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

        // ─── ExtractOMathElement ──────────────────────────────────────

        /// <summary>
        /// Extrait le seul élément OMath (préfère <c>&lt;m:oMathPara&gt;</c>
        /// s'il existe, sinon <c>&lt;m:oMath&gt;</c> inline). Retourne
        /// <c>null</c> si aucun n'est trouvé.
        /// </summary>
        public static string ExtractOMathElement(string capturedXml)
        {
            if (string.IsNullOrEmpty(capturedXml)) return null;

            XElement parsed;
            try { parsed = WrapAndParse(capturedXml); }
            catch { return null; }

            var paraEl = parsed.Descendants(M + "oMathPara").FirstOrDefault();
            if (paraEl != null) return paraEl.ToString(SaveOptions.DisableFormatting);

            var inlineEl = parsed.Descendants(M + "oMath").FirstOrDefault();
            return inlineEl?.ToString(SaveOptions.DisableFormatting);
        }

        // ─── SpliceOMathInDocXml ──────────────────────────────────────

        /// <summary>
        /// Splice <paramref name="newOMathXml"/> dans le <c>&lt;w:p&gt;</c>
        /// numéro <paramref name="targetParaIdx0"/> du <paramref name="fullDocXml"/>
        /// (= <c>doc.Content.WordOpenXML</c>), à la place des
        /// <c>&lt;w:r&gt;</c> qui couvrent <paramref name="mathSource"/>.
        /// Retourne le <c>fullDocXml</c> modifié, ou <c>null</c> si la
        /// cible n'existe pas / le splice n'a pas trouvé la math source.
        ///
        /// <para>Suppose que la math source est en queue du ¶ (= cas du
        /// commit immédiat où l'utilisateur vient juste de la taper).
        /// Si elle est en milieu, on retourne null pour que le caller
        /// fallback ailleurs.</para>
        /// </summary>
        public static string SpliceOMathInDocXml(
            string fullDocXml, int targetParaIdx0, string mathSource, string newOMathXml)
        {
            if (string.IsNullOrEmpty(fullDocXml)) return null;
            if (targetParaIdx0 < 0) return null;
            if (string.IsNullOrEmpty(mathSource)) return null;
            if (string.IsNullOrEmpty(newOMathXml)) return null;

            XDocument xdoc;
            try { xdoc = XDocument.Parse(fullDocXml); }
            catch { return null; }

            // Localise le <w:body> (peut être plusieurs si pkg:package
            // contient plusieurs <w:document> — on prend le premier qui
            // a des <w:p> direct enfants).
            var bodies = xdoc.Descendants(W + "body").ToList();
            XElement body = bodies.FirstOrDefault(b => b.Elements(W + "p").Any());
            if (body == null) return null;

            // Liste les <w:p> direct enfants du body, dans l'ordre.
            var paras = body.Elements(W + "p").ToList();
            if (targetParaIdx0 >= paras.Count) return null;

            var targetPara = paras[targetParaIdx0];

            // Construit une nouvelle XElement à partir du XML de l'OMath.
            XElement newOMath;
            try { newOMath = WrapAndParse(newOMathXml); }
            catch { return null; }
            // Si on a wrappé pour parser, descendre au vrai oMath/oMathPara.
            newOMath = newOMath.Descendants(M + "oMathPara").FirstOrDefault()
                ?? newOMath.Descendants(M + "oMath").FirstOrDefault()
                ?? newOMath;

            // Cherche backward dans les enfants direct de <w:p> les
            // <w:r> dont les <w:t> concaténés matchent mathSource en
            // queue.
            var children = targetPara.Elements().ToList();
            var accumulated = new StringBuilder();
            int firstChildIdxToReplace = -1;
            int lastChildIdxToReplace = children.Count - 1;

            // On scan backward et on n'accepte de match que si on est en
            // queue (= les enfants traversés AVANT match doivent être
            // tous des <w:r> avec <w:t>). Ça enforce "math source en fin".
            // Si on rencontre un autre type d'enfant (m:oMath, bookmarks,
            // etc.) AVANT d'avoir matché, abandonner.
            for (int i = children.Count - 1; i >= 0; i--)
            {
                var child = children[i];
                if (child.Name != W + "r")
                {
                    // Pas un run : on ne peut pas matcher ici, donc soit
                    // mathSource pas en queue → null.
                    return null;
                }

                string runText = ExtractRunText(child);
                accumulated.Insert(0, runText);

                if (accumulated.Length >= mathSource.Length)
                {
                    string tail = accumulated.ToString(
                        accumulated.Length - mathSource.Length, mathSource.Length);
                    if (tail == mathSource)
                    {
                        firstChildIdxToReplace = i;
                        break;
                    }
                    if (accumulated.Length > mathSource.Length)
                    {
                        // Plus de texte que mathSource sans match → pas en queue.
                        return null;
                    }
                }
            }
            if (firstChildIdxToReplace < 0) return null;

            // prefixLen = chars à garder du PREMIER run remplacé (texte
            // AVANT mathSource). Si > 0, on émet un run préfixe.
            int prefixLen = accumulated.Length - mathSource.Length;
            var firstRun = children[firstChildIdxToReplace];

            // Construit la nouvelle séquence d'enfants.
            var newChildren = new List<XElement>();
            // Tout ce qui était avant le premier run remplacé.
            for (int i = 0; i < firstChildIdxToReplace; i++)
                newChildren.Add(children[i]);
            // Run préfixe si on garde du texte avant mathSource.
            if (prefixLen > 0)
            {
                string keptText = ExtractRunText(firstRun).Substring(0, prefixLen);
                newChildren.Add(BuildPrefixRun(firstRun, keptText));
            }
            // La nouvelle OMath.
            newChildren.Add(newOMath);
            // Tout ce qui était après le dernier run remplacé.
            for (int i = lastChildIdxToReplace + 1; i < children.Count; i++)
                newChildren.Add(children[i]);

            // Remplace les enfants du <w:p>. ReplaceNodes prend des
            // objects, on passe les XElements.
            targetPara.ReplaceNodes(newChildren.Cast<object>().ToArray());

            // Si l'OMath se retrouve seule dans le <w:p> (aucun <w:r>),
            // wrap en <m:oMathPara><m:jc=left> sinon Word centre.
            if (!targetPara.Elements(W + "r").Any())
            {
                WrapStandaloneOMathWithJcLeft(targetPara);
            }

            return xdoc.ToString(SaveOptions.DisableFormatting);
        }

        // ─── Helpers internes ─────────────────────────────────────────

        /// <summary>
        /// Concatène le texte de tous les <c>&lt;w:t&gt;</c> dans un
        /// <c>&lt;w:r&gt;</c>. Habituellement il n'y en a qu'un mais on
        /// gère la généralité.
        /// </summary>
        private static string ExtractRunText(XElement run)
        {
            return string.Concat(run.Elements(W + "t").Select(t => t.Value));
        }

        /// <summary>
        /// Reconstruit un <c>&lt;w:r&gt;</c> avec uniquement le texte
        /// préfixe gardé. Préserve <c>&lt;w:rPr&gt;</c> et autres
        /// éléments non-texte. Ajoute <c>xml:space="preserve"</c> au
        /// <c>&lt;w:t&gt;</c> si le texte commence/finit par un espace.
        /// </summary>
        private static XElement BuildPrefixRun(XElement originalRun, string keptText)
        {
            var newRun = new XElement(W + "r");
            // Copie les enfants non-<w:t> à l'identique (rPr, etc.).
            foreach (var el in originalRun.Elements())
            {
                if (el.Name == W + "t") continue;
                newRun.Add(new XElement(el));
            }
            // Ajoute un <w:t> avec le texte préfixe.
            var t = new XElement(W + "t", keptText);
            // Préserve les espaces en début/fin via xml:space="preserve".
            // Word par défaut strip les whitespace sans cet attribut.
            if (keptText.Length > 0
                && (char.IsWhiteSpace(keptText[0])
                    || char.IsWhiteSpace(keptText[keptText.Length - 1])))
            {
                t.SetAttributeValue(XNamespace.Xml + "space", "preserve");
            }
            newRun.Add(t);
            return newRun;
        }

        /// <summary>
        /// Force jc=left sur le contenu math du <paramref name="para"/>
        /// (sinon Word centre par défaut, cf. bug user 05-05). Deux cas :
        /// <list type="bullet">
        /// <item>Le ¶ a déjà un <c>&lt;m:oMathPara&gt;</c> (BuildUp Word
        /// le pose souvent automatiquement avec jc=centerGroup) → on
        /// patche le jc à "left".</item>
        /// <item>Le ¶ a un <c>&lt;m:oMath&gt;</c> inline tout seul → on
        /// l'enrobe dans un <c>&lt;m:oMathPara&gt;&lt;m:jc=left&gt;</c>.</item>
        /// </list>
        /// </summary>
        private static void WrapStandaloneOMathWithJcLeft(XElement para)
        {
            // Cas 1 : oMathPara déjà présent → patch jc.
            var existingPara = para.Element(M + "oMathPara");
            if (existingPara != null)
            {
                SetJcLeftOnOMathPara(existingPara);
                return;
            }

            // Cas 2 : oMath inline seul → enrobe.
            var inlineOMath = para.Element(M + "oMath");
            if (inlineOMath == null) return;

            var wrapped = new XElement(M + "oMathPara",
                new XElement(M + "oMathParaPr",
                    new XElement(M + "jc", new XAttribute(M + "val", "left"))),
                new XElement(inlineOMath));
            inlineOMath.ReplaceWith(wrapped);
        }

        /// <summary>
        /// Pose <c>m:val="left"</c> sur le <c>&lt;m:jc&gt;</c> du
        /// <c>&lt;m:oMathParaPr&gt;</c>, en créant les éléments
        /// manquants si nécessaire.
        /// </summary>
        private static void SetJcLeftOnOMathPara(XElement oMathPara)
        {
            var pr = oMathPara.Element(M + "oMathParaPr");
            if (pr == null)
            {
                oMathPara.AddFirst(new XElement(M + "oMathParaPr",
                    new XElement(M + "jc", new XAttribute(M + "val", "left"))));
                return;
            }
            var jc = pr.Element(M + "jc");
            if (jc == null)
            {
                pr.AddFirst(new XElement(M + "jc", new XAttribute(M + "val", "left")));
                return;
            }
            jc.SetAttributeValue(M + "val", "left");
        }

        // ─── ReplaceParagraphsInDocXml + ExtractFirstWPElement ────────

        /// <summary>
        /// Extrait le premier <c>&lt;w:p&gt;</c> trouvé dans
        /// <paramref name="xml"/> (= dans une capture issue de
        /// <c>BuildOMathXmlIsolated</c>). Retourne le <c>&lt;w:p&gt;</c>
        /// sérialisé sans formatting, ou <c>null</c>.
        /// </summary>
        public static string ExtractFirstWPElement(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return null;
            XElement parsed;
            try { parsed = WrapAndParse(xml); }
            catch { return null; }
            var p = parsed.Descendants(W + "p").FirstOrDefault();
            return p?.ToString(SaveOptions.DisableFormatting);
        }

        /// <summary>
        /// Remplace <paramref name="targetCount"/> paragraphes consécutifs
        /// à l'index <paramref name="targetIdx0"/> (0-based) du
        /// <paramref name="fullDocXml"/> par le seul nouveau paragraphe
        /// <paramref name="newParaWp"/>. Navigation XML pure (XDocument),
        /// pas de regex.
        /// </summary>
        public static string ReplaceParagraphsInDocXml(
            string fullDocXml, int targetIdx0, int targetCount, string newParaWp)
        {
            if (string.IsNullOrEmpty(fullDocXml)) return null;
            if (string.IsNullOrEmpty(newParaWp)) return null;
            if (targetIdx0 < 0 || targetCount < 1) return null;

            XDocument xdoc;
            try { xdoc = XDocument.Parse(fullDocXml); }
            catch { return null; }

            var bodies = xdoc.Descendants(W + "body").ToList();
            XElement body = bodies.FirstOrDefault(b => b.Elements(W + "p").Any());
            if (body == null) return null;

            var paras = body.Elements(W + "p").ToList();
            if (targetIdx0 + targetCount > paras.Count) return null;

            XElement newParaEl;
            try { newParaEl = WrapAndParse(newParaWp); }
            catch { return null; }
            // Si on a wrappé pour parser, descendre au vrai <w:p>.
            newParaEl = newParaEl.Descendants(W + "p").FirstOrDefault() ?? newParaEl;

            // Remplace [targetIdx0 .. targetIdx0+targetCount) par le
            // nouveau ¶ (en place dans l'arbre XDocument).
            paras[targetIdx0].ReplaceWith(newParaEl);
            for (int i = 1; i < targetCount; i++)
            {
                paras[targetIdx0 + i].Remove();
            }

            return xdoc.ToString(SaveOptions.DisableFormatting);
        }

        /// <summary>
        /// Parse un fragment XML. Si le fragment est un élément seul, le
        /// retourne directement. Sinon (ex. plusieurs roots, déclaration
        /// XML), on wrappe dans un root pour parser puis on descend.
        /// Tolérant : ajoute les namespaces standards si manquants pour
        /// que les fragments produits par Word soient parsables hors
        /// contexte pkg:package.
        /// </summary>
        private static XElement WrapAndParse(string xml)
        {
            // Déclare les namespaces pkg/w/m/xml dans un wrapper, sans
            // les redéclarer s'ils existent déjà. C'est défensif : un
            // fragment <m:oMath> arrivant sans son contexte ancestor
            // <w:document xmlns:m=...> serait un parse error sans wrap.
            const string wrapper =
                "<root"
                + " xmlns:pkg=\"http://schemas.microsoft.com/office/2006/xmlPackage\""
                + " xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\""
                + " xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\""
                + ">{0}</root>";
            // Si l'XML est déjà un doc complet (avec décl <?xml?> ou pkg),
            // parse direct.
            string trimmed = xml.TrimStart();
            if (trimmed.StartsWith("<?xml", StringComparison.Ordinal)
                || trimmed.StartsWith("<pkg:package", StringComparison.Ordinal))
            {
                return XDocument.Parse(xml).Root;
            }
            // Wrapping pour fragments.
            string wrapped = string.Format(wrapper, xml);
            return XDocument.Parse(wrapped).Root;
        }
    }
}
