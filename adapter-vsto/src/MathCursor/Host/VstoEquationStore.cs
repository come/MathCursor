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
    /// Le source de chaque équation MathCursor est stocké dans une CustomXMLPart
    /// dédiée, invisible à l'utilisateur et persistée avec le .docx.
    /// </summary>
    public sealed class VstoEquationStore : IEquationStore
    {
        private const string StoreNamespace = "http://mathcursor.app/equations/v1";
        private readonly Word.Application _app;

        public VstoEquationStore(Word.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        private Office.CustomXMLPart GetOrCreatePart()
        {
            var doc = _app.ActiveDocument;
            foreach (Office.CustomXMLPart part in doc.CustomXMLParts)
            {
                if (part.NamespaceURI == StoreNamespace) return part;
            }
            var created = doc.CustomXMLParts.Add(
                "<equations xmlns=\"" + StoreNamespace + "\"/>");
            return created;
        }

        public Task StoreAsync(EquationHandle handle, string source, EquationMetadata metadata)
        {
            var part = GetOrCreatePart();
            var root = XDocument.Parse(part.XML).Root;
            XNamespace ns = StoreNamespace;

            var existing = FindElement(root, handle.Id);
            if (existing != null) existing.Remove();

            root.Add(new XElement(ns + "equation",
                new XAttribute("id", handle.Id),
                new XAttribute("version", metadata.CoreVersion ?? ""),
                new XAttribute("lang", metadata.SourceLanguage ?? ""),
                new XAttribute("createdAt", metadata.CreatedAt.ToString("o")),
                new XElement(ns + "source", source)));

            ReplaceXml(part, root);
            return Task.CompletedTask;
        }

        public Task<StoredEquation> RetrieveAsync(EquationHandle handle)
        {
            var part = GetOrCreatePart();
            var root = XDocument.Parse(part.XML).Root;
            var el = FindElement(root, handle.Id);
            if (el == null) return Task.FromResult<StoredEquation>(null);

            XNamespace ns = StoreNamespace;
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
            var part = GetOrCreatePart();
            var root = XDocument.Parse(part.XML).Root;
            XNamespace ns = StoreNamespace;
            var el = FindElement(root, handle.Id);
            if (el == null) return Task.CompletedTask;
            var src = el.Element(ns + "source");
            if (src != null) src.Value = newSource;
            ReplaceXml(part, root);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(EquationHandle handle)
        {
            var part = GetOrCreatePart();
            var root = XDocument.Parse(part.XML).Root;
            var el = FindElement(root, handle.Id);
            if (el == null) return Task.CompletedTask;
            el.Remove();
            ReplaceXml(part, root);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EquationHandle>> ListAllAsync()
        {
            var part = GetOrCreatePart();
            var root = XDocument.Parse(part.XML).Root;
            XNamespace ns = StoreNamespace;
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

        private static void ReplaceXml(Office.CustomXMLPart part, XElement newRoot)
        {
            // CustomXMLPart n'expose pas directement "replace XML" ; on supprime
            // la partie et on en crée une nouvelle (simple et robuste).
            var doc = part.OwnerPart; // récupère Document parent
            // Plus simple : utiliser LoadXML pour remplacer tout le contenu
            part.LoadXML(newRoot.ToString());
        }
    }
}
