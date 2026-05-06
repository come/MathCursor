using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MathCursor.Cheatsheet
{
    /// <summary>
    /// Modèle d'une entrée individuelle du pane Exemples : 1 titre concret +
    /// N syntaxes équivalentes (<c>stenos[]</c>) + 1 rendu LaTeX.
    /// Schema v2 (cf. ADR 2026-05-06-Feat-ribbon-pane-examples-pivot).
    /// </summary>
    [DataContract]
    internal sealed class CheatsheetEntry
    {
        [DataMember(Name = "title_fr")]
        public string TitleFr { get; set; }

        [DataMember(Name = "title_en")]
        public string TitleEn { get; set; }

        [DataMember(Name = "stenos")]
        public string[] Stenos { get; set; }

        [DataMember(Name = "rendered_latex")]
        public string RenderedLatex { get; set; }

        [DataMember(Name = "tags")]
        public string[] Tags { get; set; }
    }

    /// <summary>
    /// Catégorie regroupant plusieurs entrées (= section repliable du pane).
    /// </summary>
    [DataContract]
    internal sealed class CheatsheetCategory
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "label_fr")]
        public string LabelFr { get; set; }

        [DataMember(Name = "label_en")]
        public string LabelEn { get; set; }

        [DataMember(Name = "order")]
        public int Order { get; set; }

        [DataMember(Name = "entries")]
        public CheatsheetEntry[] Entries { get; set; }
    }

    /// <summary>
    /// Document racine (schema + collection de catégories).
    /// </summary>
    [DataContract]
    internal sealed class CheatsheetDocument
    {
        [DataMember(Name = "schema_version")]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "categories")]
        public CheatsheetCategory[] Categories { get; set; }
    }

    /// <summary>
    /// Loader + validation de la cheatsheet. Logique pure — pas de WPF, pas de Word.
    /// La désérialisation utilise <see cref="DataContractJsonSerializer"/> built-in
    /// du BCL .NET Framework (zéro NuGet externe, conforme à la doctrine projet
    /// cf. <c>FeedbackJson.cs</c>).
    /// </summary>
    internal static class CheatsheetData
    {
        /// <summary>
        /// Parse un document JSON cheatsheet depuis une chaîne. Retourne le
        /// <see cref="CheatsheetDocument"/> ou lève en cas d'erreur de parse.
        /// </summary>
        public static CheatsheetDocument Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var bytes = Encoding.UTF8.GetBytes(json);
            using (var stream = new MemoryStream(bytes))
            {
                var serializer = new DataContractJsonSerializer(typeof(CheatsheetDocument));
                return (CheatsheetDocument)serializer.ReadObject(stream);
            }
        }

        /// <summary>
        /// Charge le document JSON embarqué dans l'assembly
        /// (<c>MathCursor.Cheatsheet.Resources.cheatsheet.json</c>).
        /// Retourne null si la ressource est introuvable.
        /// </summary>
        public static CheatsheetDocument LoadEmbedded()
        {
            var assembly = typeof(CheatsheetData).Assembly;
            const string resourceName = "MathCursor.Cheatsheet.Resources.cheatsheet.json";
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return null;
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return Parse(reader.ReadToEnd());
                }
            }
        }

        /// <summary>
        /// Vérifie l'intégrité d'un document : schema_version == 2, chaque entrée
        /// a un titre, au moins une syntaxe (<c>stenos[]</c>) et un
        /// <c>rendered_latex</c> non vide. Retourne la liste des problèmes
        /// trouvés (vide = document valide).
        /// </summary>
        public static IList<string> Validate(CheatsheetDocument doc)
        {
            var issues = new List<string>();
            if (doc == null) { issues.Add("document null"); return issues; }
            if (doc.SchemaVersion != 2)
                issues.Add($"schema_version attendue 2, trouvée {doc.SchemaVersion}");
            if (doc.Categories == null || doc.Categories.Length == 0)
            {
                issues.Add("aucune catégorie");
                return issues;
            }
            for (int ci = 0; ci < doc.Categories.Length; ci++)
            {
                var cat = doc.Categories[ci];
                if (cat == null) { issues.Add($"catégorie #{ci} null"); continue; }
                if (string.IsNullOrEmpty(cat.Id)) issues.Add($"catégorie #{ci} : id vide");
                if (string.IsNullOrEmpty(cat.LabelFr)) issues.Add($"catégorie '{cat.Id}' : label_fr vide");
                if (string.IsNullOrEmpty(cat.LabelEn)) issues.Add($"catégorie '{cat.Id}' : label_en vide");
                if (cat.Entries == null || cat.Entries.Length == 0)
                {
                    issues.Add($"catégorie '{cat.Id}' : entries vide");
                    continue;
                }
                for (int ei = 0; ei < cat.Entries.Length; ei++)
                {
                    var e = cat.Entries[ei];
                    if (e == null) { issues.Add($"catégorie '{cat.Id}' entrée #{ei} : null"); continue; }
                    if (string.IsNullOrEmpty(e.TitleFr))
                        issues.Add($"catégorie '{cat.Id}' entrée #{ei} : title_fr vide");
                    if (string.IsNullOrEmpty(e.TitleEn))
                        issues.Add($"catégorie '{cat.Id}' entrée #{ei} : title_en vide");
                    if (e.Stenos == null || e.Stenos.Length == 0)
                        issues.Add($"catégorie '{cat.Id}' entrée #{ei} : stenos vide");
                    else
                    {
                        for (int si = 0; si < e.Stenos.Length; si++)
                        {
                            if (string.IsNullOrEmpty(e.Stenos[si]))
                                issues.Add($"catégorie '{cat.Id}' entrée #{ei} : stenos[{si}] vide");
                        }
                    }
                    if (string.IsNullOrEmpty(e.RenderedLatex))
                        issues.Add($"catégorie '{cat.Id}' entrée #{ei} (title='{e.TitleFr}') : rendered_latex vide");
                }
            }
            return issues;
        }
    }
}
