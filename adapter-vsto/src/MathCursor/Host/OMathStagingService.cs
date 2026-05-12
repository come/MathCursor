using System;
using System.Diagnostics;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Construit l'XML d'une OMath via le pipeline Word (Insert unicodeMath →
    /// BuildUp → capture WordOpenXML) dans un <b>document fantôme caché</b>,
    /// totalement isolé du doc utilisateur. Zéro mutation du doc actif.
    ///
    /// <para>Le ghost doc est créé lazy au 1er appel et réutilisé pour tous
    /// les commits suivants. Vidé avant chaque utilisation pour borner sa
    /// taille.</para>
    ///
    /// <para>P2.6 du refactor archi (ADR
    /// <c>2026-05-12-Refactor-pure-merger-atomic-insert</c>). Remplace la
    /// précédente <c>BuildOMathXmlIsolated</c> qui mutait temporairement le
    /// doc user (insert + delete à la fin) — fragile sur erreur, polluait
    /// l'undo stack, interagissait avec la session du user.</para>
    /// </summary>
    internal sealed class OMathStagingService : IDisposable
    {
        private readonly Word.Application _app;
        private readonly Action<string> _diagLog;
        private Word.Document _stagingDoc;
        private bool _disposed;

        public OMathStagingService(Word.Application app, Action<string> diagLog = null)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _diagLog = diagLog;
        }

        /// <summary>
        /// Construit le pkg:package WordOpenXML d'une OMath rendue à partir
        /// du <paramref name="latex"/>. Retourne <c>null</c> sur échec
        /// (conversion latex→unicodeMath, BuildUp Word, capture, etc.).
        /// </summary>
        public string BuildOMathXml(string latex)
        {
            if (_disposed) return null;
            if (string.IsNullOrEmpty(latex)) return null;

            string unicodeMath;
            try { unicodeMath = MathCursor.Core.LatexToUnicodeMath.Convert(latex); }
            catch (Exception ex) { _diagLog?.Invoke("staging.l2um_error: " + ex.Message); return null; }
            if (string.IsNullOrEmpty(unicodeMath)) return null;

            if (!EnsureStagingDoc()) return null;

            // Capture l'ActiveDocument utilisateur : les mutations sur
            // _stagingDoc (Delete, Text=, OMaths.Add) RE-activent
            // implicitement le ghost doc côté Word. Sans restauration,
            // _app.Selection pointe sur le ghost après BuildOMathXml →
            // l'utilisateur perd la main dans le doc invisible.
            Word.Document userDoc = null;
            try { userDoc = _app.ActiveDocument; } catch { }
            bool needRestoreActive = userDoc != null && !ReferenceEquals(userDoc, _stagingDoc);

            bool prevScreenUpdating = true;
            try { prevScreenUpdating = _app.ScreenUpdating; } catch { }

            var swTotal = Stopwatch.StartNew();
            try
            {
                try { _app.ScreenUpdating = false; } catch { }

                // Vide le ghost doc (sauf le ¶ mark final obligatoire).
                int end = _stagingDoc.Content.End;
                if (end > 1) _stagingDoc.Range(0, end - 1).Delete();

                // Insert "\r" + unicodeMath + "\r" pour isoler le math
                // comme un ¶ standalone. BuildUp détecte alors le mode
                // display pour les multi-lignes (m:oMathPara) — sans
                // wrapping, certaines formes restent inline et le
                // patcher EnsureDisplayWithLeftJc doit re-wrapper.
                _stagingDoc.Range(0, 0).Text = "\r" + unicodeMath + "\r";
                int mathStart = 1; // saute le \r leading
                int mathEnd = mathStart + unicodeMath.Length;
                var mathRange = _stagingDoc.Range(mathStart, mathEnd);
                mathRange.OMaths.Add(mathRange);
                mathRange.OMaths.BuildUp();

                // Capture le pkg:package du ¶ contenant la nouvelle OMath.
                var omaths = mathRange.OMaths;
                if (omaths == null || omaths.Count < 1)
                {
                    _diagLog?.Invoke("staging.no_omath_after_buildup");
                    return null;
                }
                var omPara = omaths[1].Range.Paragraphs[1];
                string xml = omPara?.Range.WordOpenXML;
                swTotal.Stop();
                _diagLog?.Invoke($"PERF staging.build_omath_xml={swTotal.ElapsedMilliseconds}ms len={xml?.Length ?? 0}");
                return xml;
            }
            catch (Exception ex)
            {
                _diagLog?.Invoke("staging.build_error: " + ex.Message);
                return null;
            }
            finally
            {
                if (needRestoreActive)
                {
                    try { userDoc.Activate(); }
                    catch (Exception ex) { _diagLog?.Invoke("staging.restore_active_error: " + ex.Message); }
                }
                try { _app.ScreenUpdating = prevScreenUpdating; } catch { }
            }
        }

        /// <summary>
        /// Crée le ghost doc lazy si pas encore créé. Le doc est créé
        /// caché (window.Visible=false) après réactivation du doc user.
        /// Retourne <c>false</c> si la création a échoué.
        /// </summary>
        private bool EnsureStagingDoc()
        {
            if (_stagingDoc != null) return true;

            Word.Document userDoc = null;
            try { userDoc = _app.ActiveDocument; } catch { /* peut être null */ }

            bool prevScreenUpdating = true;
            try { prevScreenUpdating = _app.ScreenUpdating; } catch { }

            try
            {
                _app.ScreenUpdating = false;
                _stagingDoc = _app.Documents.Add();
                try { _stagingDoc.ActiveWindow.Visible = false; }
                catch (Exception ex) { _diagLog?.Invoke("staging.hide_window_error: " + ex.Message); }
                userDoc?.Activate();
                _diagLog?.Invoke("staging.doc_created");
                return true;
            }
            catch (Exception ex)
            {
                _diagLog?.Invoke("staging.doc_create_error: " + ex.Message);
                _stagingDoc = null;
                return false;
            }
            finally
            {
                try { _app.ScreenUpdating = prevScreenUpdating; } catch { }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_stagingDoc != null)
            {
                try { _stagingDoc.Close(SaveChanges: false); }
                catch (Exception ex) { _diagLog?.Invoke("staging.close_error: " + ex.Message); }
                _stagingDoc = null;
            }
        }
    }
}
