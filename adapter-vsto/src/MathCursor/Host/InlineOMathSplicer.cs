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
    /// <para>Navigation par contenu + parent immédiat (cf. ADR
    /// <c>2026-05-11-Fix-omath-splice-content-based-navigation</c>). Le
    /// <c>&lt;w:p&gt;</c> cible est identifié par "celui dont la queue
    /// match <paramref name="mathSource"/>", peu importe sa profondeur
    /// dans l'arbre (<c>&lt;w:body&gt;</c> direct, cellule de tableau,
    /// SDT, header, footnote). La cross-merge multi-¶ utilise
    /// <see cref="XContainer.ElementsBeforeSelf()"/> + check du
    /// <c>.Parent</c> pour refuser les frontières de conteneur.</para>
    ///
    /// <para>Origine du pattern XML transplant : ADR
    /// <c>2026-05-07-Fix-insert-via-paragraph-xml-splice</c>. L'approche
    /// précédente (<c>textBefore = Range.Text</c> + reconstruction du ¶
    /// depuis BuildUp) était lossy pour les OMaths voisines.</para>
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
        /// dont les derniers <c>&lt;w:r&gt;</c> matchent
        /// <paramref name="mathSource"/> en queue. Navigation par contenu,
        /// pas par index — marche dans <c>&lt;w:body&gt;</c> direct,
        /// <c>&lt;w:tc&gt;</c> de tableau, <c>&lt;w:sdt&gt;</c>, headers,
        /// footnotes, uniformément.
        ///
        /// <para>Si plusieurs <c>&lt;w:p&gt;</c> matchent (rare — la source
        /// brute fraîchement tapée est presque unique), prend le dernier
        /// dans l'ordre document (intuition utilisateur = le plus récent).</para>
        ///
        /// <para>Suppose que la math source est en queue du ¶ (= cas du
        /// commit immédiat où l'utilisateur vient juste de la taper).
        /// Si elle est en milieu, on retourne null pour que le caller
        /// fallback ailleurs.</para>
        /// </summary>
        public static string SpliceOMathInDocXml(
            string fullDocXml, string mathSource, string newOMathXml)
            => SpliceOMathInDocXml(fullDocXml, mathSource, newOMathXml, null);

        /// <summary>
        /// Variante avec absorption inline. <paramref name="absorbedHandles"/>
        /// liste les handles dont les OMaths sont DANS le <w:p> cible et
        /// doivent être retirées en même temps que les runs typés (cas du
        /// merger inline qui absorbe un voisin avant ou après le texte
        /// typé). La span replacée s'étend backward depuis le tail-match
        /// jusqu'au premier <c>&lt;w:bookmarkStart name="mcEq_X"&gt;</c>
        /// trouvé, en englobant l'OMath et le bookmarkEnd associés ainsi
        /// que tout run de "glue" entre eux (typiquement 1 espace).
        /// </summary>
        public static string SpliceOMathInDocXml(
            string fullDocXml, string mathSource, string newOMathXml,
            IReadOnlyList<string> absorbedHandles)
        {
            if (string.IsNullOrEmpty(fullDocXml)) return null;
            if (string.IsNullOrEmpty(mathSource)) return null;
            if (string.IsNullOrEmpty(newOMathXml)) return null;

            XDocument xdoc;
            try { xdoc = XDocument.Parse(fullDocXml); }
            catch { return null; }

            HashSet<string> handleSet = (absorbedHandles != null && absorbedHandles.Count > 0)
                ? new HashSet<string>(absorbedHandles) : null;

            // Scan TOUS les <w:p>. On retient le DERNIER qui match (intuition
            // user = le ¶ qu'il vient de taper, le plus tardif dans l'ordre doc).
            XElement targetPara = null;
            int firstChildIdxToReplace = -1;
            int lastChildIdxToReplace = -1;
            int prefixLen = 0;
            foreach (var para in xdoc.Descendants(W + "p"))
            {
                var match = TryMatchTailRunSequence(para, mathSource, handleSet);
                if (match.HasValue)
                {
                    targetPara = para;
                    firstChildIdxToReplace = match.Value.firstChildIdx;
                    lastChildIdxToReplace = match.Value.lastChildIdx;
                    prefixLen = match.Value.prefixLen;
                }
            }
            if (targetPara == null) return null;

            var children = targetPara.Elements().ToList();

            // Extension backward : englobe les éléments absorbés (bookmark
            // + OMath du handle absorbé) et la "glue" whitespace, pour le
            // cas merge_left (OMath absorbée AVANT le texte typé).
            if (handleSet != null)
            {
                int spanStart = firstChildIdxToReplace;
                for (int j = firstChildIdxToReplace - 1; j >= 0; j--)
                {
                    if (IsAbsorbedElement(children[j], handleSet) ||
                        IsWhitespaceRun(children[j]))
                    {
                        spanStart = j;
                        continue;
                    }
                    break;
                }
                if (spanStart < firstChildIdxToReplace)
                {
                    firstChildIdxToReplace = spanStart;
                    prefixLen = 0;
                }
            }

            XElement newOMath;
            try { newOMath = WrapAndParse(newOMathXml); }
            catch { return null; }
            newOMath = newOMath.Descendants(M + "oMathPara").FirstOrDefault()
                ?? newOMath.Descendants(M + "oMath").FirstOrDefault()
                ?? newOMath;

            var firstChild = children[firstChildIdxToReplace];

            var newChildren = new List<XElement>();
            for (int i = 0; i < firstChildIdxToReplace; i++)
                newChildren.Add(children[i]);
            if (prefixLen > 0 && firstChild.Name == W + "r")
            {
                string keptText = ExtractRunText(firstChild).Substring(0, prefixLen);
                newChildren.Add(BuildPrefixRun(firstChild, keptText));
            }
            newChildren.Add(newOMath);
            for (int i = lastChildIdxToReplace + 1; i < children.Count; i++)
                newChildren.Add(children[i]);

            targetPara.ReplaceNodes(newChildren.Cast<object>().ToArray());

            if (!targetPara.Elements(W + "r").Any())
            {
                WrapStandaloneOMathWithJcLeft(targetPara);
            }

            return xdoc.ToString(SaveOptions.DisableFormatting);
        }

        /// <summary>
        /// True si l'élément est un <c>&lt;w:r&gt;</c> dont tout le texte
        /// est blanc (espaces, tabs). Glue typique entre OMath et texte typé.
        /// </summary>
        private static bool IsWhitespaceRun(XElement el)
            => el.Name == W + "r" && string.IsNullOrWhiteSpace(ExtractRunText(el));

        /// <summary>
        /// True si l'élément fait partie d'un OMath absorbé : bookmarkStart
        /// "mcEq_X" pour un handle X dans <paramref name="handles"/>, son
        /// bookmarkEnd associé, ou le <c>&lt;m:oMath&gt;</c> entre les deux.
        /// On reconnaît m:oMath/oMathPara comme absorbés sans corrélation au
        /// handle parce qu'ils sont entourés des bookmarks et le scan est
        /// monotone.
        /// </summary>
        private static bool IsAbsorbedElement(XElement el, HashSet<string> handles)
        {
            if (el.Name == M + "oMath" || el.Name == M + "oMathPara") return true;
            if (el.Name == W + "bookmarkEnd") return true;
            if (el.Name == W + "bookmarkStart")
            {
                var name = (string)el.Attribute(W + "name");
                if (string.IsNullOrEmpty(name)) return false;
                const string prefix = "mcEq_";
                if (!name.StartsWith(prefix, StringComparison.Ordinal)) return false;
                return handles.Contains(name.Substring(prefix.Length));
            }
            return false;
        }

        /// <summary>
        /// Essaie de matcher <paramref name="source"/> en queue des
        /// <c>&lt;w:r&gt;</c> de <paramref name="para"/>. Retourne
        /// <c>(firstChildIdx, prefixLen)</c> où :
        /// <list type="bullet">
        /// <item><c>firstChildIdx</c> = index du premier <c>&lt;w:r&gt;</c>
        /// à remplacer (les suivants jusqu'à la fin sont également
        /// couverts par <paramref name="source"/>).</item>
        /// <item><c>prefixLen</c> = nombre de chars à garder en début du
        /// premier run remplacé (texte AVANT <paramref name="source"/>).</item>
        /// </list>
        /// Retourne <c>null</c> si :
        /// <list type="bullet">
        /// <item>aucun match de queue (<paramref name="source"/> pas en fin) ;</item>
        /// <item>on rencontre un enfant non-<c>&lt;w:r&gt;</c> en scan
        /// backward avant d'avoir matché (= source pas en queue stricte) ;</item>
        /// <item>les <c>&lt;w:r&gt;</c> accumulés dépassent la longueur
        /// de <paramref name="source"/> sans match exact (= source pas
        /// en queue).</item>
        /// </list>
        /// </summary>
        private static (int firstChildIdx, int lastChildIdx, int prefixLen)? TryMatchTailRunSequence(
            XElement para, string source, HashSet<string> absorbedHandles = null)
        {
            if (string.IsNullOrEmpty(source)) return null;
            var children = para.Elements().ToList();
            if (children.Count == 0) return null;

            // Skip les éléments absorbés en queue (cas merge_right : l'OMath
            // absorbée est APRÈS le texte typé). lastChildIdx pointe sur le
            // dernier élément du span de remplacement (= dernier run typé OU
            // dernier élément absorbé en queue).
            int lastChildIdx = children.Count - 1;
            if (absorbedHandles != null)
            {
                while (lastChildIdx >= 0 &&
                       (IsAbsorbedElement(children[lastChildIdx], absorbedHandles) ||
                        IsWhitespaceRun(children[lastChildIdx])))
                {
                    lastChildIdx--;
                }
                if (lastChildIdx < 0) return null;
            }
            int spanEnd = children.Count - 1; // englobe les absorbés en queue

            var accumulated = new StringBuilder();
            for (int i = lastChildIdx; i >= 0; i--)
            {
                var child = children[i];
                if (child.Name != W + "r") return null;

                accumulated.Insert(0, ExtractRunText(child));

                if (accumulated.Length >= source.Length)
                {
                    string tail = accumulated.ToString(
                        accumulated.Length - source.Length, source.Length);
                    if (tail == source)
                        return (i, spanEnd, accumulated.Length - source.Length);
                    if (accumulated.Length > source.Length) return null;
                }
            }
            return null;
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
        /// Remplace un groupe de <c>&lt;w:p&gt;</c> siblings consécutifs
        /// (dans le même <c>.Parent</c>) par un seul nouveau
        /// <paramref name="newParaWp"/>. Le groupe est identifié par
        /// <paramref name="paragraphSources"/>, qui liste les sources
        /// brutes des paragraphes cibles dans l'ordre du document (haut
        /// en bas).
        ///
        /// <para>Algorithme :</para>
        /// <list type="number">
        /// <item>Trouver le DERNIER <c>&lt;w:p&gt;</c> du doc dont la
        /// queue match <c>paragraphSources[N-1]</c>.</item>
        /// <item>Pour les N-1 précédents (de bas en haut), prendre le
        /// sibling IMMÉDIATEMENT précédent (<see cref="XNode.ElementsBeforeSelf()"/>
        /// dernier) et vérifier qu'il est un <c>&lt;w:p&gt;</c>, dans
        /// le même <c>.Parent</c>, et que sa queue match
        /// <c>paragraphSources[i]</c>.</item>
        /// <item>Refuser (<c>null</c>) si on traverserait un changement
        /// de Parent (frontière cellule/body, cellule/cellule), un
        /// sibling non-<c>&lt;w:p&gt;</c>, ou si une source ne match pas.</item>
        /// <item>Remplacer le groupe par <paramref name="newParaWp"/> in
        /// place dans l'arbre XDocument.</item>
        /// </list>
        ///
        /// <para>Navigation XML pure (XDocument), pas de regex. Marche
        /// dans n'importe quel conteneur (body, cellule, SDT, …) par
        /// construction.</para>
        /// </summary>
        public static string ReplaceParagraphsInDocXml(
            string fullDocXml, IReadOnlyList<string> paragraphSources, string newParaWp)
        {
            if (string.IsNullOrEmpty(fullDocXml)) return null;
            if (paragraphSources == null || paragraphSources.Count == 0) return null;
            if (string.IsNullOrEmpty(newParaWp)) return null;

            XDocument xdoc;
            try { xdoc = XDocument.Parse(fullDocXml); }
            catch { return null; }

            // 1. Trouver le dernier <w:p> dont la queue match la dernière source.
            string lastSrc = paragraphSources[paragraphSources.Count - 1];
            if (string.IsNullOrEmpty(lastSrc)) return null;
            XElement lastPara = null;
            foreach (var para in xdoc.Descendants(W + "p"))
            {
                if (TryMatchTailRunSequence(para, lastSrc).HasValue)
                {
                    lastPara = para;
                }
            }
            if (lastPara == null) return null;

            // 2. Remonter les N-1 paragraphes précédents par siblings
            //    stricts dans le même .Parent.
            var group = new List<XElement> { lastPara };
            var parent = lastPara.Parent;
            var current = lastPara;
            for (int i = paragraphSources.Count - 2; i >= 0; i--)
            {
                // Le sibling IMMÉDIATEMENT précédent (n'importe quel type)
                // doit être un <w:p> dans le même Parent.
                var prevNode = current.ElementsBeforeSelf().LastOrDefault();
                if (prevNode == null) return null;          // pas de sibling avant
                if (prevNode.Name != W + "p") return null;  // sibling non-<w:p>
                if (prevNode.Parent != parent) return null; // changement de parent (par sécurité)

                string srcI = paragraphSources[i];
                if (string.IsNullOrEmpty(srcI)) return null;
                if (!TryMatchTailRunSequence(prevNode, srcI).HasValue) return null;

                group.Insert(0, prevNode);
                current = prevNode;
            }

            // 3. Remplace le groupe par le nouveau <w:p>.
            XElement newParaEl;
            try { newParaEl = WrapAndParse(newParaWp); }
            catch { return null; }
            newParaEl = newParaEl.Descendants(W + "p").FirstOrDefault() ?? newParaEl;

            // Mémorise le sibling avant le groupe (= node après lequel on
            // va insérer) puis remplace le groupe. Note : ReplaceWith peut
            // cloner newParaEl, rendant la référence locale obsolète — on
            // re-localise le <w:p> inséré via le node précédent ou via
            // parent.Elements().
            var parentEl = group[0].Parent;
            var nodeBeforeGroup = group[0].PreviousNode;
            group[0].ReplaceWith(newParaEl);
            for (int i = 1; i < group.Count; i++)
            {
                group[i].Remove();
            }

            // Récupère le <w:p> inséré dans l'arbre (= le node juste
            // après nodeBeforeGroup, ou le premier élément de parent si
            // nodeBeforeGroup est null).
            XElement insertedPara = nodeBeforeGroup != null
                ? nodeBeforeGroup.NodesAfterSelf().OfType<XElement>().FirstOrDefault()
                : parentEl?.Elements().FirstOrDefault();
            if (insertedPara == null) return xdoc.ToString(SaveOptions.DisableFormatting);

            // Garantit qu'il y a un <w:p> sibling après le nouveau ¶ pour
            // que le caret puisse atterrir dedans après l'insertion (sinon
            // en cellule mono-¶ avec multi-ligne, le caret est piégé sur
            // le `\r` final et NudgeCursorOutOfMath / EndKey(wdLine)
            // sort de la cellule). Cf. bug user 2026-05-11 : multi-ligne
            // dans cellule → caret saute à la cellule suivante. Hors
            // cellule, si un <w:p> sibling existe déjà, on ne touche à
            // rien (préserve comportement actuel).
            if (insertedPara.ElementsAfterSelf(W + "p").FirstOrDefault() == null)
            {
                insertedPara.AddAfterSelf(new XElement(W + "p"));
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
