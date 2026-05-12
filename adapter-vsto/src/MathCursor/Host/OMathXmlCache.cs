using System;
using System.Collections.Generic;

namespace MathCursor.Host
{
    /// <summary>
    /// Cache LRU borné qui mappe une expression LaTeX vers son rendu
    /// <c>&lt;m:oMath&gt;</c> (élément extrait via
    /// <see cref="InlineOMathSplicer.ExtractOMathElement"/>). Évite de
    /// re-faire <c>BuildOMathXmlIsolated</c> (~70ms) sur formules
    /// répétées au fil d'une session.
    ///
    /// <para>Couche 2/3 du stack perf défini dans ADR
    /// <c>2026-05-12-Perf-commit-pipeline-three-stage-stack</c>. Extrait
    /// de <c>SuggestionService</c> par P2.4 du refactor architectural
    /// (ADR <c>2026-05-12-Refactor-pure-merger-atomic-insert</c>).</para>
    ///
    /// <para>Implémentation : <c>Dictionary</c> + <c>LinkedList</c>
    /// d'ordre LRU, pas de NuGet (contrainte CLAUDE.md "pas de
    /// dépendances lourdes"). Pure et thread-unsafe : appelé uniquement
    /// depuis le UI thread Word.</para>
    /// </summary>
    internal sealed class OMathXmlCache
    {
        private readonly int _capacity;
        private readonly Dictionary<string, string> _byKey;
        private readonly LinkedList<string> _lru;

        public OMathXmlCache(int capacity = 32)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "capacity must be > 0");
            _capacity = capacity;
            _byKey = new Dictionary<string, string>(StringComparer.Ordinal);
            _lru = new LinkedList<string>();
        }

        public int Count => _byKey.Count;
        public int Capacity => _capacity;

        /// <summary>
        /// Retourne le XML caché pour <paramref name="latex"/>, ou
        /// <c>null</c> si pas en cache. Met à jour la position LRU sur
        /// hit (déplace l'entrée en tête).
        /// </summary>
        public string TryGet(string latex)
        {
            if (string.IsNullOrEmpty(latex)) return null;
            if (!_byKey.TryGetValue(latex, out string xml)) return null;
            _lru.Remove(latex);
            _lru.AddFirst(latex);
            return xml;
        }

        /// <summary>
        /// Insère / écrase l'entrée pour <paramref name="latex"/>.
        /// Évince l'entrée la moins récente si capacité atteinte.
        /// Inputs null/vides sont ignorés silencieusement.
        /// </summary>
        public void Set(string latex, string xml)
        {
            if (string.IsNullOrEmpty(latex) || string.IsNullOrEmpty(xml)) return;
            if (_byKey.ContainsKey(latex))
            {
                _byKey[latex] = xml;
                _lru.Remove(latex);
                _lru.AddFirst(latex);
                return;
            }
            if (_byKey.Count >= _capacity)
            {
                var oldest = _lru.Last;
                if (oldest != null)
                {
                    _byKey.Remove(oldest.Value);
                    _lru.RemoveLast();
                }
            }
            _byKey[latex] = xml;
            _lru.AddFirst(latex);
        }

        public void Clear()
        {
            _byKey.Clear();
            _lru.Clear();
        }
    }
}
