using System;
using System.Text;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.CCMeta
{
    /// <summary>
    /// Runner step-by-step de la logique d'<c>InsertOMathAt</c>. Permet de
    /// débugger interactivement chaque étape (SetRange → Delete → TypeText
    /// → CC wrap → OMath build → Tag → CcSticky) et d'observer l'état du
    /// doc après chacune dans l'inspecteur.
    ///
    /// <para>State static cross-clicks. <c>Start</c> initialise depuis la
    /// sélection courante, <c>Next</c> avance d'une étape et renvoie un
    /// rapport texte à afficher.</para>
    /// </summary>
    internal static class PocStepRunner
    {
        private static bool _active;
        private static int _stepIndex;
        private static int _inputAbsStart;
        private static int _inputAbsEnd;
        private static string _source;
        private static string _latex;
        private static int _internalStart;
        private static int _internalEnd;
        private static int _srcStart;
        private static int _afterEnd;
        private static Word.ContentControl _cc;
        private static Word.OMath _om;
        private static string _newHandle;

        public static bool IsActive => _active;
        public static int StepIndex => _stepIndex;

        /// <summary>
        /// Initialise depuis la sélection courante. <paramref name="source"/>
        /// et <paramref name="latex"/> sont les valeurs hardcodées pour ce
        /// scénario debug (= simule un commit de cette zone par la popup).
        /// </summary>
        public static string Start(Word.Application app, string source, string latex)
        {
            try
            {
                var sel = app.Selection;
                _inputAbsStart = sel.Start;
                _inputAbsEnd = sel.End;
                _source = source ?? "";
                _latex = latex ?? "";
                _stepIndex = 0;
                _active = true;
                _internalStart = _internalEnd = -1;
                _srcStart = _afterEnd = -1;
                _cc = null;
                _om = null;
                _newHandle = null;

                var sb = new StringBuilder();
                sb.AppendLine("=== POC Step START ===");
                sb.AppendLine($"Range capturé : [{_inputAbsStart}, {_inputAbsEnd}) ({_inputAbsEnd - _inputAbsStart} chars)");
                sb.AppendLine($"source = \"{_source}\"");
                sb.AppendLine($"latex  = \"{_latex}\"");
                sb.AppendLine();
                sb.AppendLine(DumpDocState(app));
                sb.AppendLine();
                sb.AppendLine("→ Clique « POC Step : next » pour la 1ère étape (Normalize).");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                _active = false;
                return "Start ERROR: " + ex.Message;
            }
        }

        /// <summary>
        /// Avance d'une étape. Retourne un rapport texte de ce qui s'est
        /// passé + l'état doc après. Renvoie un message d'erreur si pas
        /// actif.
        /// </summary>
        public static string Next(Word.Application app)
        {
            if (!_active) return "POC Step inactive — clique « POC Step : start » d'abord.";

            var sb = new StringBuilder();
            sb.AppendLine($"=== POC Step {_stepIndex} ===");

            try
            {
                var doc = app.ActiveDocument;
                var sel = app.Selection;

                switch (_stepIndex)
                {
                    case 0:
                        sb.AppendLine("Étape 0 : NORMALIZE (SetRange collapsed pour absStart puis absEnd)");
                        sel.SetRange(_inputAbsStart, _inputAbsStart);
                        _internalStart = sel.Start;
                        sel.SetRange(_inputAbsEnd, _inputAbsEnd);
                        _internalEnd = sel.Start;
                        sb.AppendLine($"  absStart {_inputAbsStart} → internalStart {_internalStart}");
                        sb.AppendLine($"  absEnd   {_inputAbsEnd} → internalEnd   {_internalEnd}");
                        break;

                    case 1:
                        sb.AppendLine("Étape 1 : SetRange ÉTENDU [internalStart, internalEnd)");
                        sb.AppendLine($"  demandé : [{_internalStart}, {_internalEnd})");
                        sel.SetRange(_internalStart, _internalEnd);
                        sb.AppendLine($"  obtenu  : [{sel.Start}, {sel.End})");
                        if (sel.Start != _internalStart || sel.End != _internalEnd)
                            sb.AppendLine($"  ⚠ SNAP : Word a élargi la sélection (sticky-zone d'OMath voisine ?)");
                        break;

                    case 2:
                        sb.AppendLine("Étape 2 : sel.Delete()");
                        sel.Delete();
                        sb.AppendLine($"  sel post-Delete : [{sel.Start}, {sel.End})");
                        break;

                    case 3:
                        sb.AppendLine($"Étape 3 : sel.TypeText(\"{_source}\") (unicodeMath len={_source.Length})");
                        string unicodeMath = MathCursor.Core.LatexToUnicodeMath.Convert(_latex);
                        sb.AppendLine($"  unicodeMath converti : \"{unicodeMath}\" (len={unicodeMath.Length})");
                        sel.TypeText(unicodeMath);
                        _afterEnd = sel.Start;
                        _srcStart = _afterEnd - unicodeMath.Length;
                        sb.AppendLine($"  sel post-TypeText : [{sel.Start}, {sel.End})");
                        sb.AppendLine($"  srcStart={_srcStart} afterEnd={_afterEnd}");
                        break;

                    case 4:
                        sb.AppendLine("Étape 4 : CC wrap sur doc.Range(srcStart, afterEnd)");
                        var typedRange = doc.Range(_srcStart, _afterEnd);
                        _cc = typedRange.ContentControls.Add(
                            Word.WdContentControlType.wdContentControlRichText);
                        _cc.Title = MCMetaJson.CcTitle;
                        try { _cc.Appearance = Word.WdContentControlAppearance.wdContentControlHidden; } catch { }
                        try { _cc.LockContentControl = false; } catch { }
                        try { _cc.LockContents = false; } catch { }
                        sb.AppendLine($"  cc.Range = [{_cc.Range.Start}, {_cc.Range.End})");
                        break;

                    case 5:
                        sb.AppendLine("Étape 5 : cc.Range.OMaths.Add + BuildUp");
                        var inner = _cc.Range;
                        var addedRange = inner.OMaths.Add(inner);
                        addedRange.OMaths.BuildUp();
                        foreach (Word.OMath o in addedRange.OMaths) { _om = o; break; }
                        if (_om != null)
                        {
                            sb.AppendLine($"  om.Range = [{_om.Range.Start}, {_om.Range.End})");
                            sb.AppendLine($"  cc.Range = [{_cc.Range.Start}, {_cc.Range.End})");
                        }
                        else sb.AppendLine("  ⚠ addedRange.OMaths empty");
                        break;

                    case 6:
                        sb.AppendLine("Étape 6 : om.Justification = Left + Tag JSON");
                        if (_om != null)
                        {
                            try { _om.Justification = Word.WdOMathJc.wdOMathJcLeft; sb.AppendLine("  Justification = Left → OK"); }
                            catch (Exception exJ) { sb.AppendLine("  Justification ERROR : " + exJ.Message); }

                            _newHandle = "eq_" + Guid.NewGuid().ToString("N").Substring(0, 12);
                            string hash = Sha1Helper.Compute(_om.Range.WordOpenXML ?? "");
                            var meta = new MCMeta
                            {
                                V = 1,
                                HandleId = _newHandle,
                                Steno = _source,
                                Latex = _latex,
                                Version = "step-debug",
                                OmmlHash = hash,
                                ParsedAt = DateTime.UtcNow,
                            };
                            _cc.Tag = MCMetaJson.Serialize(meta);
                            sb.AppendLine($"  handle = {_newHandle}");
                            sb.AppendLine($"  hash   = {hash.Substring(0, 12)}…");
                        }
                        break;

                    case 7:
                        sb.AppendLine("Étape 7 : CcSticky.EscapeCaretAfter (sort le caret de la sticky-zone)");
                        if (_cc != null)
                        {
                            int beforeEnd = _cc.Range.End;
                            CcSticky.EscapeCaretAfter(app, _cc);
                            sb.AppendLine($"  cc.Range.End avant : {beforeEnd}");
                            sb.AppendLine($"  cc.Range.End après : {_cc.Range.End}");
                            sb.AppendLine($"  sel.Start après    : {sel.Start}");
                        }
                        _active = false;
                        sb.AppendLine();
                        sb.AppendLine("=== FIN — step runner désactivé. ===");
                        sb.AppendLine("Re-clique « POC Step : start » pour relancer.");
                        break;

                    default:
                        sb.AppendLine("Étape inconnue — reset.");
                        _active = false;
                        break;
                }

                _stepIndex++;
                sb.AppendLine();
                sb.AppendLine("État du doc maintenant :");
                sb.AppendLine(DumpDocState(app));
                return sb.ToString();
            }
            catch (Exception ex)
            {
                _active = false;
                return $"Step {_stepIndex} ERROR : {ex.Message}\n\n{DumpDocState(app)}";
            }
        }

        public static void Reset() { _active = false; _stepIndex = 0; }

        private static string DumpDocState(Word.Application app)
        {
            try
            {
                var doc = app.ActiveDocument;
                var sel = app.Selection;
                var sb = new StringBuilder();
                int docEnd = doc.Content.End;
                int omathCount = 0;
                try { omathCount = doc.OMaths.Count; } catch { }
                int ccCount = 0, mcCount = 0;
                try
                {
                    foreach (Word.ContentControl c in doc.ContentControls)
                    {
                        ccCount++;
                        if (c.Title == MCMetaJson.CcTitle) mcCount++;
                    }
                }
                catch { }
                sb.AppendLine($"  doc.Content.End = {docEnd}");
                sb.AppendLine($"  sel = [{sel.Start}, {sel.End})");
                sb.AppendLine($"  doc.OMaths.Count = {omathCount}");
                sb.AppendLine($"  doc.ContentControls.Count = {ccCount}  (MathCursor = {mcCount})");
                try
                {
                    string content = doc.Content.Text ?? "";
                    if (content.Length > 100) content = content.Substring(0, 100) + "…";
                    content = content.Replace("\r", "\\r").Replace("\n", "\\n").Replace("", "[bell]");
                    sb.AppendLine($"  doc.Content.Text = \"{content}\"");
                }
                catch (Exception ex) { sb.AppendLine($"  doc.Content.Text READ ERR : {ex.Message}"); }
                return sb.ToString();
            }
            catch (Exception ex) { return "DumpDocState ERR : " + ex.Message; }
        }
    }
}
