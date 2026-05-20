using System;
using MathCursor.Host.CCMeta;
using Xunit;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Tests.Host.WordIntegration
{
    /// <summary>
    /// Reproducer du bug user 2026-05-18 :
    ///   « f(x) convert au ¶ 1, g(x) convert au ¶ 2, caret sur f(x),
    ///     revert → g(x) disparaît, avalé par le retour à la saisie ».
    ///
    /// Test bypass SuggestionService et exerce directement les ops Word
    /// interop. Tout en Range (pas Selection) car Visible=false rend
    /// <c>Application.Selection</c> null dans le harness.
    /// </summary>
    [Trait("Category", "WordIntegration")]
    [Collection("WordIntegration")]
    public sealed class RevertNeighborParagraphTests : IClassFixture<WordIntegrationFixture>
    {
        private readonly WordIntegrationFixture _fixture;

        public RevertNeighborParagraphTests(WordIntegrationFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact(DisplayName = "Revert OMath au ¶1 ne doit PAS supprimer OMath au ¶2")]
        public void Revert_first_paragraph_OMath_keeps_next_paragraph_OMath_intact()
        {
            var doc = _fixture.CreateBlankDoc();
            // ContentControls.Add exige une fenêtre active sinon COMException.
            doc.Activate();

            // ── Setup : insère f(x) au ¶ 1 ──────────────────────────
            var fxCc = InsertOMathWithCc(doc, atPos: doc.Content.Start, source: "f(x)", steno: "f de x");
            int afterFx = fxCc.Range.End;

            // Ajoute un paragraph mark juste après le CC du ¶ 1.
            doc.Range(afterFx, afterFx).InsertParagraphAfter();

            // ── Setup : insère g(x) au ¶ 2 ──────────────────────────
            // doc.Content.End - 1 = position avant la marque de ¶ final
            int para2Start = afterFx + 1;
            var gxCc = InsertOMathWithCc(doc, atPos: para2Start, source: "g(x)", steno: "g de x");

            // Sanity-check : on a bien 2 OMaths après setup.
            Assert.Equal(2, doc.OMaths.Count);

            // Diag : positions / ranges avant revert.
            int fxOmStart = -1, fxOmEnd = -1, fxCcStart = -1, fxCcEnd = -1;
            int gxOmStart = -1, gxOmEnd = -1, gxCcStart = -1, gxCcEnd = -1;
            foreach (Word.OMath om in doc.OMaths)
            {
                var (c, m) = CcMetaResolver.ResolveAt(om);
                if (m?.Steno == "f de x") { fxOmStart = om.Range.Start; fxOmEnd = om.Range.End; fxCcStart = c.Range.Start; fxCcEnd = c.Range.End; }
                if (m?.Steno == "g de x") { gxOmStart = om.Range.Start; gxOmEnd = om.Range.End; gxCcStart = c.Range.Start; gxCcEnd = c.Range.End; }
            }
            string diagBefore = $"BEFORE: fx om=[{fxOmStart},{fxOmEnd}) cc=[{fxCcStart},{fxCcEnd}) | gx om=[{gxOmStart},{gxOmEnd}) cc=[{gxCcStart},{gxCcEnd})";

            // ── Acte : applique la séquence de revert sur l'OMath du ¶ 1 ─
            var fxOm = FirstOMath(fxCc.Range);
            Assert.NotNull(fxOm);
            var (cc, meta) = CcMetaResolver.ResolveAt(fxOm);
            Assert.NotNull(cc);
            Assert.NotNull(meta);
            Assert.Equal("f de x", meta.Steno);

            // Fix : selStart = cc.Range.Start (capture l'ouverture wrapper)
            //       selEnd   = om.Range.End  (clamp à l'OMath, pas au-delà)
            // Puis cc.Delete(false) pour retirer le wrapper sans toucher au
            // contenu post-revert (= notre plain text + voisins absorbés).
            int selStart = cc.Range.Start;
            int selEnd = fxOm.Range.End;
            string diagRevert = $"REVERT: range=[{selStart},{selEnd}) replace with \"{meta.Steno}\"";

            var replaceRange = doc.Range(selStart, selEnd);
            replaceRange.Text = meta.Steno;

            // Dispose le wrapper CC restant (= maintenant autour de notre
            // plain text + ce qui était dans le CC après l'OMath, si il y a
            // eu auto-grow). cc.Delete(false) = wrapper-only, contenu garde.
            try { cc.Delete(false); } catch { /* peut être déjà mort */ }

            // ── Diag : état après revert ─────────────────────────────
            int omathsAfter = doc.OMaths.Count;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(diagBefore);
            sb.AppendLine(diagRevert);
            sb.AppendLine($"AFTER : doc.OMaths.Count = {omathsAfter}");
            sb.AppendLine($"        doc text = \"{(doc.Content.Text ?? "").Replace("\r", "\\r").Replace("\n", "\\n")}\"");
            int idx = 0;
            foreach (Word.OMath om in doc.OMaths)
            {
                idx++;
                var (c, m) = CcMetaResolver.ResolveAt(om);
                sb.AppendLine($"        OMath#{idx} range=[{om.Range.Start},{om.Range.End}) cc=[{c?.Range.Start},{c?.Range.End}) steno=\"{m?.Steno}\"");
            }
            int ccIdx = 0;
            foreach (Word.ContentControl c in doc.ContentControls)
            {
                ccIdx++;
                int omc = 0; try { omc = c.Range.OMaths.Count; } catch { }
                var m = MCMetaJson.TryParse(c.Tag);
                sb.AppendLine($"        CC#{ccIdx} range=[{c.Range.Start},{c.Range.End}) Title={c.Title} OMaths={omc} steno=\"{m?.Steno}\"");
            }

            // Cible critique : g(x) doit survivre, peu importe les ghost
            // OMaths cosmétiques. Word peut laisser une structure OMath
            // résiduelle au ¶ 1 (contenu remplacé en math italic), c'est
            // non-destructif et le user verra du texte.
            bool gxSurvived = false;
            foreach (Word.OMath om in doc.OMaths)
            {
                var (_, m) = CcMetaResolver.ResolveAt(om);
                if (m?.Steno == "g de x") { gxSurvived = true; break; }
            }
            Assert.True(gxSurvived, $"g(x) doit survivre au revert. State:\n{sb}");
        }

        // ── Helpers (Range-only, no Selection) ───────────────────────

        /// <summary>
        /// Réplique en mini ce que <c>InsertOMathAt</c> fait pour 1 commit
        /// — en pur Range (pas de Selection, donc utilisable en harness
        /// Visible=false).
        /// </summary>
        private static Word.ContentControl InsertOMathWithCc(Word.Document doc, int atPos, string source, string steno)
        {
            var insertRange = doc.Range(atPos, atPos);
            insertRange.Text = source;

            int textStart = atPos;
            int afterText = atPos + source.Length;

            var typedRange = doc.Range(textStart, afterText);
            var cc = typedRange.ContentControls.Add(
                Word.WdContentControlType.wdContentControlRichText);
            cc.Title = MCMetaJson.CcTitle;
            try { cc.Appearance = Word.WdContentControlAppearance.wdContentControlHidden; } catch { }
            try { cc.LockContentControl = false; } catch { }
            try { cc.LockContents = false; } catch { }

            var inner = cc.Range;
            var addedRange = inner.OMaths.Add(inner);
            addedRange.OMaths.BuildUp();

            var meta = new MCMeta
            {
                V = 1,
                HandleId = "eq_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                Steno = steno,
                Latex = source,
                Version = "test",
                OmmlHash = Sha1Helper.Compute(cc.Range.WordOpenXML ?? ""),
                ParsedAt = DateTime.UtcNow,
            };
            cc.Tag = MCMetaJson.Serialize(meta);
            return cc;
        }

        private static Word.OMath FirstOMath(Word.Range range)
        {
            foreach (Word.OMath om in range.OMaths) return om;
            return null;
        }
    }
}
