using System;
using System.Diagnostics;

namespace MathCursor.Host
{
    /// <summary>
    /// Abstraction du provider de texte/XML du paragraphe courant Word.
    /// Permet de tester <see cref="ParaXmlPrefetcher"/> sans Word ouvert.
    /// </summary>
    internal interface IParaXmlSource
    {
        /// <summary>Position de départ + texte brut du ¶ contenant le caret.
        /// <c>false</c> si pas de sélection / pas de doc actif.</summary>
        bool TryReadCurrentParagraph(out int paraStart, out string paraText);

        /// <summary>WordOpenXML (pkg:package) du ¶ courant. <c>null</c>
        /// si lecture impossible.</summary>
        string ReadCurrentParaXml();
    }

    /// <summary>
    /// Cache pré-fetch du <c>paraXml</c> courant. Couche 3/3 du stack perf
    /// (ADR <c>2026-05-12-Perf-commit-pipeline-three-stage-stack</c>).
    /// Extrait en classe dédiée par P2.5 du refactor archi (ADR
    /// <c>2026-05-12-Refactor-pure-merger-atomic-insert</c>).
    ///
    /// <para><see cref="Tick"/> est appelé périodiquement (DispatcherTimer).
    /// Il ne tire le coup qu'en cas d'idle : 2 ticks consécutifs avec le
    /// même texte de ¶ (= user n'a rien tapé entre). Évite la lag pendant
    /// la frappe rapide ; le coût (~60ms WordOpenXML sur gros doc) n'est
    /// payé qu'aux pauses.</para>
    ///
    /// <para><see cref="TryGet"/> est appelé au commit pour servir le
    /// cache hit. Match exact paraStart + hash texte → 0ms, sinon null
    /// (fallback live read côté caller).</para>
    /// </summary>
    internal sealed class ParaXmlPrefetcher
    {
        private readonly IParaXmlSource _source;
        private readonly Action<string> _diagLog;

        private int _cachedParaStart = -1;
        private int _cachedTextHash;
        private string _cachedXml;
        private string _lastSeenText;

        public ParaXmlPrefetcher(IParaXmlSource source, Action<string> diagLog = null)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _diagLog = diagLog;
        }

        /// <summary>
        /// Tick périodique. Refresh le cache si (et seulement si) :
        /// (1) on peut lire le ¶ courant, (2) le texte est STABLE depuis
        /// le tick précédent, (3) le cache courant ne match pas déjà ce ¶.
        /// </summary>
        public void Tick()
        {
            if (!_source.TryReadCurrentParagraph(out int paraStart, out string paraText))
                return;

            int hash = paraText?.GetHashCode() ?? 0;

            // Cache déjà aligné sur ce ¶ : on note le texte vu et on s'arrête.
            if (_cachedParaStart == paraStart && _cachedTextHash == hash)
            {
                _lastSeenText = paraText;
                return;
            }

            // Signal idle : ce tick a le même texte que le précédent.
            // Sinon on attend que ça se stabilise (= user pas en train de taper).
            if (_lastSeenText != paraText)
            {
                _lastSeenText = paraText;
                return;
            }

            // Stable + stale → refresh.
            var sw = Stopwatch.StartNew();
            string xml = _source.ReadCurrentParaXml();
            sw.Stop();
            if (xml == null) return;
            _cachedParaStart = paraStart;
            _cachedTextHash = hash;
            _cachedXml = xml;
            _diagLog?.Invoke($"PERF prefetch.read_para_xml={sw.ElapsedMilliseconds}ms len={xml.Length}");
        }

        /// <summary>
        /// Retourne le XML caché si <paramref name="paraStart"/> +
        /// <paramref name="paraText"/> matchent exactement (paraStart
        /// identique + hash texte identique). <c>null</c> sinon.
        /// </summary>
        public string TryGet(int paraStart, string paraText)
        {
            if (_cachedXml == null) return null;
            if (_cachedParaStart != paraStart) return null;
            int hash = paraText?.GetHashCode() ?? 0;
            if (_cachedTextHash != hash) return null;
            return _cachedXml;
        }

        /// <summary>Force l'invalidation du cache (ex: doc fermé, doc changé).</summary>
        public void Invalidate()
        {
            _cachedParaStart = -1;
            _cachedTextHash = 0;
            _cachedXml = null;
            _lastSeenText = null;
        }
    }
}
