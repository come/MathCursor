using System;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Nettoie structurellement une plage <c>[absStart, absEnd)</c> du
    /// document avant insertion. Retourne la nouvelle position où l'inserter
    /// doit poser son <c>SetRange + TypeText</c> (= start de zone clean).
    ///
    /// <para>Pourquoi pas juste <c>sel.SetRange(...).Delete()</c> :
    /// Word ne supprime PAS structurellement les ContentControls de cette
    /// façon. Le contenu disparaît, mais la CC vide subsiste et Word la
    /// remplit auto avec son placeholder « Cliquez ou appuyez ici… ».
    /// Idem pour les OMaths : un Delete plain peut laisser des wrappers
    /// fantômes.</para>
    ///
    /// <para>Pourquoi tracker un <c>newStart</c> séparé de <c>absStart</c> :
    /// <c>cc.Delete(true)</c> peut shifter PLUS de chars que la <c>cc.Range</c>
    /// reportée (markers structurels Word avant <c>cc.Start</c>). Si le shift
    /// dépasse <c>ccLen</c>, le surplus provient de positions AVANT
    /// <c>ccStart</c> — donc <c>absStart</c> doit shifter d'autant à gauche
    /// pour rester aligné avec ce qui reste après la deletion. Sinon, du
    /// contenu post-cc qui a shifté plus loin que prévu se retrouve avant
    /// <c>absStart</c> et survit (bug observé 2026-05-19 : <c>=F(x)= 1</c>
    /// au lieu de <c>F(x)= 1</c>, shift=8 vs ccLen=6).</para>
    /// </summary>
    internal static class ZoneCleaner
    {
        /// <summary>
        /// Vide la plage <c>[absStart, absEnd)</c>. Retourne la position où
        /// l'inserter doit poser <c>SetRange + TypeText</c> (= <c>newStart</c>
        /// après ajustements pour les shifts Word inattendus).
        /// </summary>
        public static int ClearZone(Word.Document doc, int absStart, int absEnd, Action<string> log = null)
        {
            log = log ?? (_ => { });
            if (doc == null) { log("ZoneCleaner: doc null → noop"); return absStart; }
            if (absEnd <= absStart) { log($"ZoneCleaner: empty range [{absStart},{absEnd}) → noop"); return absStart; }

            int newStart = absStart;
            int currentEnd = absEnd;
            int safety;

            // ── 1. CCs ────────────────────────────────────────────────
            safety = 20;
            while (safety-- > 0)
            {
                Word.ContentControl ccToDelete = null;
                try
                {
                    if (currentEnd <= newStart) break;
                    foreach (Word.ContentControl cc in doc.Range(newStart, currentEnd).ContentControls)
                    { ccToDelete = cc; break; }
                }
                catch (Exception ex) { log("ZoneCleaner: cc_enum_error: " + ex.Message); break; }
                if (ccToDelete == null) break;

                int ccS = -1, ccE = -1;
                try { ccS = ccToDelete.Range.Start; ccE = ccToDelete.Range.End; }
                catch (Exception exB) { log("ZoneCleaner: cc bounds read error: " + exB.Message); break; }
                int ccLen = ccE - ccS;

                // Probe AVANT delete : capture les 5 chars de prose avant cc.
                // Sert à mesurer combien de chars Word va avaler avant ccStart
                // (= des chars structurels uniquement, pas de prose).
                int probeStart = Math.Max(0, ccS - 5);
                string preProbe = "";
                try { preProbe = doc.Range(probeStart, ccS).Text ?? ""; }
                catch (Exception exPP) { log("ZoneCleaner: pre-probe error: " + exPP.Message); }

                int docEndBefore;
                try { docEndBefore = doc.Content.End; }
                catch (Exception exDe) { log("ZoneCleaner: docEnd read error: " + exDe.Message); break; }

                try { ccToDelete.Delete(true); }
                catch (Exception exD)
                {
                    log($"ZoneCleaner: cc_delete_error [{ccS},{ccE}): {exD.Message}");
                    break;
                }

                int docEndAfter;
                try { docEndAfter = doc.Content.End; }
                catch (Exception exDe) { log("ZoneCleaner: docEnd post-delete read error: " + exDe.Message); break; }
                int shift = docEndBefore - docEndAfter;
                int extraShift = shift - ccLen;

                // Probe APRÈS delete : lit la même plage [probeStart, ccS) en
                // coords NEW. Mesure combien de chars de post-cc content ont
                // shifté DANS la zone before-cc (= positions qu'on doit aussi
                // nettoyer car elles contiennent du résidu de zone).
                //
                // Heuristique : `postProbe.Length - preProbe.Length` = nombre
                // de chars apparus dans cette plage post-delete. Marche que
                // Word ait mangé un structural avant cc (= shift dans before-cc)
                // OU rien avant cc (= longueurs égales, shiftBefore=0).
                //
                // Pourquoi pas "longest common prefix" : les markers
                // structurels Word sont zero-width dans `.Text`, donc preProbe
                // peut être plus court que la range Range.Text demandée.
                // Après delete, ces positions zero-width sont remplacées par
                // du contenu visible (= 1 char Text). Le prefix matche
                // toujours (la prose visible est intacte) mais postProbe est
                // PLUS LONG. C'est cette différence de longueur qui révèle
                // le shift, pas la divergence en prefix.
                int shiftBefore = 0;
                if (extraShift > 0)
                {
                    string postProbe = "";
                    try { postProbe = doc.Range(probeStart, ccS).Text ?? ""; }
                    catch (Exception exPo) { log("ZoneCleaner: post-probe error: " + exPo.Message); }

                    shiftBefore = postProbe.Length - preProbe.Length;
                    if (shiftBefore < 0) shiftBefore = 0;
                    // Si shiftBefore = 0 mais extraShift > 0 → tout l'extra
                    // vient de l'after-cc (gap, structural post-cc). Pas de
                    // shift newStart.
                    log($"ZoneCleaner: probe pre=\"{Escape(preProbe)}\" (len={preProbe.Length}) post=\"{Escape(postProbe)}\" (len={postProbe.Length}) shiftBefore={shiftBefore}");
                }

                if (shiftBefore > 0)
                {
                    int adjusted = Math.Max(0, newStart - shiftBefore);
                    log($"ZoneCleaner: post-cc content shifté dans before-cc, newStart {newStart} → {adjusted}");
                    newStart = adjusted;
                }
                currentEnd -= shift;
                if (currentEnd < newStart) currentEnd = newStart;

                log($"ZoneCleaner: deleted CC [{ccS},{ccE}) ccLen={ccLen} actualShift={shift} extraShift={extraShift} shiftBefore={shiftBefore} → newStart={newStart} currentEnd={currentEnd}");
            }

            // ── 2. OMaths résiduels (legacy non wrappées) ────────────
            safety = 20;
            while (safety-- > 0)
            {
                if (currentEnd <= newStart) break;
                Word.OMath omToDelete = null;
                try
                {
                    foreach (Word.OMath om in doc.Range(newStart, currentEnd).OMaths)
                    { omToDelete = om; break; }
                }
                catch (Exception ex) { log("ZoneCleaner: om_enum_error: " + ex.Message); break; }
                if (omToDelete == null) break;

                int omS = -1, omE = -1;
                try { omS = omToDelete.Range.Start; omE = omToDelete.Range.End; }
                catch (Exception exB) { log("ZoneCleaner: om bounds read error: " + exB.Message); break; }
                int omLen = omE - omS;

                int docEndBefore;
                try { docEndBefore = doc.Content.End; }
                catch { break; }

                try { omToDelete.Range.Delete(); }
                catch (Exception exD)
                {
                    log($"ZoneCleaner: om_delete_error [{omS},{omE}): {exD.Message}");
                    break;
                }

                int docEndAfter;
                try { docEndAfter = doc.Content.End; }
                catch { break; }
                int shift = docEndBefore - docEndAfter;

                int extraShift = shift - omLen;
                if (extraShift > 0)
                {
                    int adjusted = Math.Max(0, newStart - extraShift);
                    log($"ZoneCleaner: extraShift={extraShift} avant omStart={omS}, newStart {newStart} → {adjusted}");
                    newStart = adjusted;
                }
                currentEnd -= shift;
                if (currentEnd < newStart) currentEnd = newStart;

                log($"ZoneCleaner: deleted bare OMath [{omS},{omE}) omLen={omLen} actualShift={shift} → newStart={newStart} currentEnd={currentEnd}");
            }

            // ── 3. Plain text résiduel ─────────────────────────────
            if (currentEnd > newStart)
            {
                int docEndBefore = -1, docEndAfter = -1;
                try { docEndBefore = doc.Content.End; } catch { }
                try { doc.Range(newStart, currentEnd).Delete(); }
                catch (Exception exD) { log($"ZoneCleaner: plain_delete_error [{newStart},{currentEnd}): {exD.Message}"); }
                try { docEndAfter = doc.Content.End; } catch { }
                if (docEndBefore >= 0 && docEndAfter >= 0)
                {
                    int shift = docEndBefore - docEndAfter;
                    currentEnd -= shift;
                    if (currentEnd < newStart) currentEnd = newStart;
                    log($"ZoneCleaner: deleted plain text actualShift={shift} → currentEnd={currentEnd}");
                }
            }

            int result = currentEnd > newStart ? currentEnd : newStart;
            log($"ZoneCleaner: done, returned pos = {result} (absStart in={absStart}, newStart out={newStart})");
            return result;
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        }
    }
}
