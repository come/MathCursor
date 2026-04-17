using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml.Linq;
using MathCursor.HostContract;
using Office = Microsoft.Office.Core;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Implémentation VSTO de IEquationStore via Document.CustomXMLParts.
    /// Stratégie : on ne mute jamais une part existante (CustomXMLPart.LoadXML
    /// n'est appelable qu'UNE FOIS). À chaque update on supprime la part et
    /// on en recrée une avec le nouveau XML — robuste, pas d'effet de bord.
    /// </summary>
    public sealed class VstoEquationStore : IEquationStore
    {
        private const string StoreNamespace = "http://mathcursor.app/equations/v1";
        private readonly Word.Application _app;

        public VstoEquationStore(Word.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        private Office.CustomXMLPart FindExistingPart()
        {
            var doc = _app.ActiveDocument;
            foreach (Office.CustomXMLPart part in doc.CustomXMLParts)
            {
                if (part.NamespaceURI == StoreNamespace) return part;
            }
            return null;
        }

        private XElement LoadCurrentRoot()
        {
            var part = FindExistingPart();
            XNamespace ns = StoreNamespace;
            if (part == null)
            {
                return new XElement(ns + "equations");
            }
            try
            {
                var parsed = XDocument.Parse(part.XML).Root;
                return parsed ?? new XElement(ns + "equations");
            }
            catch
            {
                return new XElement(ns + "equations");
            }
        }

        private void SaveRoot(XElement newRoot)
        {
            var doc = _app.ActiveDocument;
            var existing = FindExistingPart();
            if (existing != null) existing.Delete();
            doc.CustomXMLParts.Add(newRoot.ToString());
        }

        public Task StoreAsync(EquationHandle handle, string source, EquationMetadata metadata)
        {
            var root = LoadCurrentRoot();
            XNamespace ns = root.Name.Namespace;

            var existing = FindElement(root, handle.Id);
            if (existing != null) existing.Remove();

            root.Add(new XElement(ns + "equation",
                new XAttribute("id", handle.Id),
                new XAttribute("version", metadata.CoreVersion ?? ""),
                new XAttribute("lang", metadata.SourceLanguage ?? ""),
                new XAttribute("createdAt", metadata.CreatedAt.ToString("o")),
                new XElement(ns + "source", source)));

            SaveRoot(root);
            return Task.CompletedTask;
        }

        public Task<StoredEquation> RetrieveAsync(EquationHandle handle)
        {
            var root = LoadCurrentRoot();
            XNamespace ns = root.Name.Namespace;
            var el = FindElement(root, handle.Id);
            if (el == null) return Task.FromResult<StoredEquation>(null);

            var source = el.Element(ns + "source")?.Value ?? "";
            var metadata = new EquationMetadata
            {
                SourceLanguage = el.Attribute("lang")?.Value,
                CoreVersion = el.Attribute("version")?.Value ?? "",
                CreatedAt = DateTimeOffset.TryParse(el.Attribute("createdAt")?.Value, out var dt)
                    ? dt : DateTimeOffset.UtcNow,
            };
            return Task.FromResult(new StoredEquation { Source = source, Metadata = metadata });
        }

        public Task UpdateAsync(EquationHandle handle, string newSource)
        {
            var root = LoadCurrentRoot();
            XNamespace ns = root.Name.Namespace;
            var el = FindElement(root, handle.Id);
            if (el == null) return Task.CompletedTask;
            var src = el.Element(ns + "source");
            if (src != null) src.Value = newSource;
            SaveRoot(root);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(EquationHandle handle)
        {
            var root = LoadCurrentRoot();
            var el = FindElement(root, handle.Id);
            if (el == null) return Task.CompletedTask;
            el.Remove();
            SaveRoot(root);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EquationHandle>> ListAllAsync()
        {
            var root = LoadCurrentRoot();
            XNamespace ns = root.Name.Namespace;
            var list = new List<EquationHandle>();
            foreach (var el in root.Elements(ns + "equation"))
            {
                var id = el.Attribute("id")?.Value;
                if (!string.IsNullOrEmpty(id)) list.Add(new EquationHandle(id));
            }
            return Task.FromResult<IReadOnlyList<EquationHandle>>(list);
        }

        private static XElement FindElement(XElement root, string id)
        {
            XNamespace ns = root.Name.Namespace;
            foreach (var el in root.Elements(ns + "equation"))
            {
                if (el.Attribute("id")?.Value == id) return el;
            }
            return null;
        }
    }
}
