using System;
using System.Linq;
using System.Xml.Linq;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.UI.Debug
{
    /// <summary>
    /// Lit la Selection courante de Word et produit un <see cref="CaretStateInfo"/>
    /// inerte. Best-effort : toute exception Word interop est catchée et
    /// notée dans <see cref="CaretStateInfo.ErrorMessage"/>.
    /// </summary>
    internal static class CaretStateSnapper
    {
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

        public static CaretStateInfo Snapshot(Word.Application app)
        {
            var info = new CaretStateInfo();
            if (app == null) { info.ErrorMessage = "app null"; return info; }

            Word.Selection sel;
            try { sel = app.Selection; }
            catch (Exception ex) { info.ErrorMessage = "selection: " + ex.Message; return info; }
            if (sel == null) { info.ErrorMessage = "selection is null"; return info; }

            try { info.SelStart = sel.Start; info.SelEnd = sel.End; } catch { }
            try { info.SelOMathsCount = sel.OMaths?.Count ?? 0; } catch { }

            // ¶ parent
            Word.Range paraRange = null;
            try { paraRange = sel.Paragraphs[1].Range; }
            catch (Exception ex) { info.ErrorMessage = "paragraphs[1]: " + ex.Message; }
            if (paraRange != null)
            {
                try
                {
                    info.ParaStart = paraRange.Start;
                    info.ParaEnd = paraRange.End;
                    string text = paraRange.Text ?? "";
                    info.ParaTextPreview = Truncate(text.Replace('\r', '↵').Replace('\a', '⌐').Replace('\v', '↧'), 60);
                }
                catch { }
            }

            // Tableau ?
            try
            {
                info.InTable = (bool)sel.Information[Word.WdInformation.wdWithInTable];
                if (info.InTable)
                {
                    info.TableRow = (int)sel.Information[Word.WdInformation.wdStartOfRangeRowNumber];
                    info.TableCol = (int)sel.Information[Word.WdInformation.wdStartOfRangeColumnNumber];
                    try
                    {
                        var cells = sel.Cells;
                        if (cells != null && cells.Count > 0)
                        {
                            var cellRange = cells[1].Range;
                            info.CellStart = cellRange.Start;
                            info.CellEnd = cellRange.End;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // OMath englobante ?
            try
            {
                if (sel.OMaths != null && sel.OMaths.Count > 0)
                {
                    var omRange = sel.OMaths[1].Range;
                    info.OMathStart = omRange.Start;
                    info.OMathEnd = omRange.End;
                }
            }
            catch { }

            // Siblings via paraXml (parse XDocument)
            if (paraRange != null)
            {
                try
                {
                    string xml = paraRange.WordOpenXML;
                    if (!string.IsNullOrEmpty(xml))
                    {
                        var doc = XDocument.Parse(xml);
                        var wp = doc.Descendants(W + "p").FirstOrDefault();
                        if (wp != null)
                        {
                            int idxApprox = 0;
                            int caretRelative = info.SelStart - info.ParaStart;
                            foreach (var child in wp.Elements())
                            {
                                string kind = child.Name.LocalName;
                                // Skip pPr (paragraph props) — bruit visuel
                                if (kind == "pPr") continue;

                                string preview = ExtractTextPreview(child);
                                int approxLen = ApproxRenderLength(child);

                                var sib = new CaretSiblingInfo
                                {
                                    Kind = kind,
                                    TextPreview = preview,
                                    ContainsCaret = caretRelative >= idxApprox
                                                 && caretRelative < idxApprox + approxLen,
                                };
                                info.Siblings.Add(sib);
                                idxApprox += approxLen;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (string.IsNullOrEmpty(info.ErrorMessage))
                        info.ErrorMessage = "siblings: " + ex.Message;
                }
            }

            return info;
        }

        private static string ExtractTextPreview(XElement el)
        {
            // Extrait le texte aggregé des descendants <w:t> et <m:t>.
            var texts = el.Descendants().Where(d =>
                d.Name.LocalName == "t").Select(t => t.Value);
            string joined = string.Concat(texts);
            return Truncate(joined, 40);
        }

        private static int ApproxRenderLength(XElement el)
        {
            // Approximation Word : 1 char par caractère texte. OMath = compté
            // comme la longueur de son contenu texte (Word ne range pas un
            // OMath comme 1 char monolithique, mais ses descendants comptent).
            string s = string.Concat(
                el.Descendants().Where(d => d.Name.LocalName == "t").Select(t => t.Value));
            // Markers structurels (bookmarkStart, fldChar, etc.) ont une
            // longueur "virtuelle" 0 côté Range.Text.
            return s.Length;
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max - 1) + "…";
        }
    }
}
