using System.Collections.Generic;

namespace MathCursor.Core.Patterns
{
    /// <summary>
    /// Registre des <see cref="IPatternTemplate"/> indexés par
    /// <see cref="IPatternTemplate.TemplateId"/>. Utilisé pour la composition
    /// parent↔enfant via <see cref="PatternRefSlot"/> : quand un slot d'un
    /// template parent référence un autre pattern par nom, le pipeline
    /// résout l'instance via <see cref="Get"/> au moment de l'expansion.
    ///
    /// <para>Cf. ADR <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>,
    /// étape P2.</para>
    /// </summary>
    public sealed class PatternRegistry
    {
        private readonly Dictionary<string, IPatternTemplate> _byId;

        public PatternRegistry(IEnumerable<IPatternTemplate> templates)
        {
            if (templates == null) throw new System.ArgumentNullException(nameof(templates));
            _byId = new Dictionary<string, IPatternTemplate>(System.StringComparer.Ordinal);
            foreach (var template in templates)
            {
                if (template == null) continue;
                if (_byId.ContainsKey(template.TemplateId))
                    throw new System.ArgumentException(
                        $"Duplicate TemplateId '{template.TemplateId}' in registry.",
                        nameof(templates));
                _byId[template.TemplateId] = template;
            }
        }

        /// <summary>Retourne le template enregistré pour <paramref name="templateId"/>,
        /// ou <c>null</c> si aucun.</summary>
        public IPatternTemplate? Get(string templateId)
        {
            if (string.IsNullOrEmpty(templateId)) return null;
            return _byId.TryGetValue(templateId, out var t) ? t : null;
        }

        /// <summary>Tente de récupérer le template enregistré pour
        /// <paramref name="templateId"/>. Retourne <c>true</c> si trouvé.</summary>
        public bool TryGet(string templateId, out IPatternTemplate? template)
        {
            if (string.IsNullOrEmpty(templateId))
            {
                template = null;
                return false;
            }
            return _byId.TryGetValue(templateId, out template);
        }

        /// <summary>Nombre de templates enregistrés.</summary>
        public int Count => _byId.Count;
    }
}
