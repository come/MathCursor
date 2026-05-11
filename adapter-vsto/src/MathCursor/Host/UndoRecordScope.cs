using System;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Wrapper <see cref="IDisposable"/> autour de
    /// <c>Application.UndoRecord.StartCustomRecord / EndCustomRecord</c>
    /// (Office 2010+, disponible sur toutes les versions Word ciblées).
    ///
    /// <para>Pattern <c>using (var _ = new UndoRecordScope(_app, "name"))</c>
    /// : toutes les opérations Word visibles à l'intérieur du scope sont
    /// regroupées en un seul undo record nommé. Ctrl+Z annule alors le
    /// commit entier d'un coup au lieu d'une étape à la fois.</para>
    ///
    /// <para>Défensif :</para>
    /// <list type="bullet">
    /// <item>Si <c>StartCustomRecord</c> throw (Word vieux, state weird,
    /// ou <c>UndoRecord</c> indispo) → le scope devient no-op silencieux.
    /// Le code à l'intérieur tourne quand même, juste sans regroupement
    /// undo. Pas de dégradation fonctionnelle.</item>
    /// <item><c>Dispose()</c> appelle <c>EndCustomRecord()</c> dans un
    /// try/catch garantissant qu'aucun record half-open n'est laissé
    /// même si une exception se produit dans le scope.</item>
    /// </list>
    ///
    /// <para>Cf. ADR <c>2026-05-11-Fix-commit-grouped-in-single-undo-record</c>.</para>
    /// </summary>
    internal sealed class UndoRecordScope : IDisposable
    {
        private readonly Word.Application _app;
        private readonly bool _started;

        public UndoRecordScope(Word.Application app, string name)
        {
            _app = app;
            _started = false;
            if (_app == null || string.IsNullOrEmpty(name)) return;
            try
            {
                _app.UndoRecord.StartCustomRecord(name);
                _started = true;
            }
            catch
            {
                // UndoRecord indispo ou state Word weird — no-op silencieux,
                // le scope ne fera rien (et Dispose ne fera rien non plus).
                _started = false;
            }
        }

        public void Dispose()
        {
            if (!_started || _app == null) return;
            try { _app.UndoRecord.EndCustomRecord(); }
            catch
            {
                // EndCustomRecord peut échouer si Word a déjà fermé le
                // record (rare). On swallow pour ne pas masquer une
                // exception primaire du scope appelant.
            }
        }
    }
}
