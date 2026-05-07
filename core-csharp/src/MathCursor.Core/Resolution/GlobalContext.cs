using System.Collections.Generic;

namespace MathCursor.Core.Resolution
{
    /// <summary>
    /// État vivant pendant une session de saisie. Hub central qui :
    /// <list type="bullet">
    ///   <item>retient les <see cref="IContextSignal"/>s configurés,</item>
    ///   <item>accumule les résolutions explicites (<see cref="SpanPin"/>) du
    ///     ¶ courant pour alimenter le signal L2,</item>
    ///   <item>expose <see cref="Snapshot"/> pour produire un
    ///     <see cref="ContextSnapshot"/> immutable consommé par le scorer.</item>
    /// </list>
    ///
    /// <para>Cycle de vie : un par session VSTO (entre l'ouverture de la popup
    /// et son commit/Esc). Le côté adapter (SuggestionService) instancie,
    /// configure les signaux, pousse les résolutions et appelle Snapshot
    /// avant chaque résolution de zone.</para>
    ///
    /// <para>Cf. brief <c>2026-05-07-global-context-multi-zoom-ranking</c>.</para>
    /// </summary>
    public sealed class GlobalContext
    {
        // Bornage simple pour éviter une croissance non contrôlée. À ajuster
        // empiriquement — 32 pins couvre un ¶ très chargé en math.
        private const int MaxRecentParagraphPins = 32;

        private readonly List<IContextSignal> _signals = new List<IContextSignal>();
        private readonly List<SpanPin> _recentParagraphPins = new List<SpanPin>();

        /// <summary>Scorer construit lazy à partir des signaux courants.
        /// Recalculé si <see cref="AddSignal"/> a été appelé après le dernier
        /// accès (signaux ajoutés post-init).</summary>
        public ContextScorer Scorer
        {
            get
            {
                if (_scorer == null || _scorerSignalCount != _signals.Count)
                {
                    _scorer = new ContextScorer(_signals.AsReadOnly());
                    _scorerSignalCount = _signals.Count;
                }
                return _scorer;
            }
        }
        private ContextScorer? _scorer;
        private int _scorerSignalCount = -1;

        public void AddSignal(IContextSignal signal)
        {
            if (signal == null) return;
            _signals.Add(signal);
        }

        /// <summary>
        /// Enregistre une résolution explicite (typiquement post-commit popup)
        /// pour alimenter la mémoire du ¶ courant. Ces pins sont visibles aux
        /// signaux L2 via <see cref="ContextSnapshot.RecentParagraphPins"/>.
        /// </summary>
        public void RecordParagraphResolution(SpanPin pin)
        {
            if (pin == null) return;
            _recentParagraphPins.Add(pin);
            // Bornage : on garde les N derniers pins (FIFO).
            while (_recentParagraphPins.Count > MaxRecentParagraphPins)
                _recentParagraphPins.RemoveAt(0);
        }

        /// <summary>Reset des résolutions du ¶ courant. À appeler quand le caret
        /// quitte le ¶ (changement de paragraphe).</summary>
        public void ResetParagraphHistory() => _recentParagraphPins.Clear();

        /// <summary>Reset complet (sortie de session, Esc, reset hard).</summary>
        public void Clear()
        {
            _recentParagraphPins.Clear();
            _signals.Clear();
            _scorer = null;
            _scorerSignalCount = -1;
        }

        /// <summary>Construit un snapshot immutable pour le scoring d'une zone.</summary>
        public ContextSnapshot Snapshot(string? rawSource, ResolutionSidecar? sidecar)
        {
            return new ContextSnapshot(
                rawSource: rawSource,
                sidecar: sidecar,
                recentParagraphPins: new List<SpanPin>(_recentParagraphPins));
        }

        /// <summary>Nombre courant de signaux configurés (pour debug / tests).</summary>
        public int SignalCount => _signals.Count;

        /// <summary>Nombre courant de pins ¶ accumulés (pour debug / tests).</summary>
        public int RecentParagraphPinCount => _recentParagraphPins.Count;
    }
}
