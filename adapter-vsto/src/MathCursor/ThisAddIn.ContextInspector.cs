using System;
using Microsoft.Office.Core;
using OfficeTools = Microsoft.Office.Tools;
using MathCursor.UI.Debug;

namespace MathCursor
{
    /// <summary>
    /// Partial dédié au pane debug "Context Inspector" (cf. brief
    /// 2026-05-07-global-context-multi-zoom-ranking). Isolé du fichier
    /// principal pour faciliter le retrait quand on n'aura plus besoin de
    /// l'outil de debug en prod.
    ///
    /// <para>Le pane est ouvert/fermé via un bouton ruban, lazy-init au
    /// premier toggle. Au premier init, s'abonne à
    /// <see cref="MathCursor.Host.SuggestionService.ContextResolved"/> et
    /// met à jour le pane à chaque résolution (via le Dispatcher WPF pour
    /// rester safe sur le thread UI).</para>
    /// </summary>
    public partial class ThisAddIn
    {
        private OfficeTools.CustomTaskPane _inspectorTaskPane;
        private ContextInspectorPaneHost _inspectorHost;

        /// <summary>
        /// Toggle du panneau debug Context Inspector. Lazy-init au premier
        /// appel, flippe juste <c>Visible</c> aux appels suivants.
        /// </summary>
        internal void ToggleContextInspectorPane()
        {
            try
            {
                if (_inspectorTaskPane == null)
                {
                    if (_suggestions == null)
                    {
                        System.Windows.MessageBox.Show(
                            "MathCursor n'est pas démarré (service indisponible).",
                            "Inspecteur — MathCursor",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning);
                        return;
                    }
                    _inspectorHost = new ContextInspectorPaneHost();
                    _suggestions.ContextResolved += OnContextResolvedForInspector;
                    _suggestions.CommitTraced += OnCommitTracedForInspector;
                    Application.WindowSelectionChange += OnSelectionChangeForCaretInspector;

                    _inspectorTaskPane = CustomTaskPanes.Add(
                        _inspectorHost, Strings.ContextInspectorPaneTitle);
                    _inspectorTaskPane.Width = 420;
                    _inspectorTaskPane.DockPosition = MsoCTPDockPosition.msoCTPDockPositionRight;
                }
                _inspectorTaskPane.Visible = !_inspectorTaskPane.Visible;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    "Échec d'ouverture de l'Inspecteur :\n" + ex.Message,
                    "Inspecteur — MathCursor",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void OnContextResolvedForInspector(
            object sender, MathCursor.Host.ContextResolveEventArgs e)
        {
            try
            {
                if (_inspectorHost?.WpfPane == null) return;
                // Skip si pane caché — évite le dispatch UI quand l'utilisateur
                // a fermé le pane (l'event continue d'être émis tant qu'on est
                // abonné, on filtre côté handler).
                if (_inspectorTaskPane == null || !_inspectorTaskPane.Visible) return;

                _inspectorHost.WpfPane.Dispatcher.BeginInvoke(
                    new Action(() => _inspectorHost.WpfPane.Update(
                        e.RawSource, e.Snapshot, e.Hints, e.Resolved)));

                // Refresh aussi le caret state : chaque keypress dans la
                // popup NER déclenche un re-resolve mais ne fait pas
                // toujours fire WindowSelectionChange (= caret non bougé
                // côté Word, seulement le texte tapé). L'utilisateur veut
                // voir l'état caret à chaque frappe.
                var caretInfo = CaretStateSnapper.Snapshot(Application);
                _inspectorHost.WpfPane.Dispatcher.BeginInvoke(
                    new Action(() => _inspectorHost.WpfPane.UpdateCaretState(caretInfo)));
            }
            catch { /* debug pane, jamais propager */ }
        }

        /// <summary>
        /// Reçoit la commit trace émise par <see cref="MathCursor.Host.SuggestionService.CommitTraced"/>
        /// à chaque commit pipeline et la pousse dans le pane debug.
        /// </summary>
        private void OnCommitTracedForInspector(object sender, string trace)
        {
            try
            {
                if (_inspectorHost?.WpfPane == null) return;
                if (_inspectorTaskPane == null || !_inspectorTaskPane.Visible) return;
                _inspectorHost.WpfPane.Dispatcher.BeginInvoke(
                    new Action(() => _inspectorHost.WpfPane.SetCommitTrace(trace)));
            }
            catch { /* debug pane, jamais propager */ }
        }

        /// <summary>
        /// Pousse du texte arbitraire dans le pane debug (= remplace la
        /// commit trace courante). Utilisé par les boutons POC du ruban
        /// pour afficher leur résultat sans bloquer l'utilisateur avec une
        /// MessageBox. Auto-ouvre le pane s'il est fermé.
        /// </summary>
        public void PushDebugTrace(string text)
        {
            try
            {
                if (_inspectorTaskPane == null) ToggleContextInspectorPane();
                if (_inspectorTaskPane != null && !_inspectorTaskPane.Visible) _inspectorTaskPane.Visible = true;
                if (_inspectorHost?.WpfPane == null) return;
                _inspectorHost.WpfPane.Dispatcher.BeginInvoke(
                    new Action(() => _inspectorHost.WpfPane.SetCommitTrace(text)));
            }
            catch { /* debug pane, jamais propager */ }
        }

        /// <summary>
        /// Pousse une trace NER live (= ce qui part au modèle à chaque tick
        /// polling). Affiché dans la section basse du pane. Skip si pane
        /// fermé pour ne pas le forcer à s'ouvrir (= moins intrusif que
        /// PushDebugTrace).
        /// </summary>
        public void PushNerLive(string text)
        {
            try
            {
                if (_inspectorHost?.WpfPane == null) return;
                if (_inspectorTaskPane == null || !_inspectorTaskPane.Visible) return;
                _inspectorHost.WpfPane.Dispatcher.BeginInvoke(
                    new Action(() => _inspectorHost.WpfPane.SetNerLiveTrace(text)));
            }
            catch { /* debug pane, jamais propager */ }
        }

        private void OnSelectionChangeForCaretInspector(Microsoft.Office.Interop.Word.Selection sel)
        {
            try
            {
                if (_inspectorHost?.WpfPane == null) return;
                if (_inspectorTaskPane == null || !_inspectorTaskPane.Visible) return;

                // Snapshot inerte hors Dispatcher (= sur le thread Word) puis
                // push au pane via Dispatcher.
                var info = CaretStateSnapper.Snapshot(Application);
                _inspectorHost.WpfPane.Dispatcher.BeginInvoke(
                    new Action(() => _inspectorHost.WpfPane.UpdateCaretState(info)));
            }
            catch { /* debug pane, jamais propager */ }
        }
    }
}
