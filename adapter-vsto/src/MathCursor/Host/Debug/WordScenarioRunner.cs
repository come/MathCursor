using System;
using System.Collections.Generic;
using System.Text;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Debug
{
    /// <summary>
    /// Résultat d'un scenario Word d'intégration.
    /// </summary>
    public sealed class WordScenarioResult
    {
        public string Name { get; set; }
        public bool Pass { get; set; }
        public string Details { get; set; }
        public string Diagnostic { get; set; }
    }

    /// <summary>
    /// Lance une suite de scenarios end-to-end qui exercent les fixes
    /// récents touchant l'interop Word. Chaque scenario écrit un séparateur
    /// + titre dans le doc actuel, puis exerce le pipeline et vérifie.
    ///
    /// <para>Travaille sur <c>ActiveDocument</c> — pas de doc temporaire,
    /// le doc reste pollué après la run pour inspection visuelle des
    /// rendus. L'utilisateur scrolle pour voir chaque scenario rendu en
    /// vrai.</para>
    /// </summary>
    internal static class WordScenarioRunner
    {
        private const string Separator = "═══════════════════════════════════════════════════════";

        public static List<WordScenarioResult> RunAll(SuggestionService service, Word.Application app)
        {
            var results = new List<WordScenarioResult>();
            if (service == null || app == null)
            {
                results.Add(Fail("(setup)", "SuggestionService ou Word.Application null."));
                return results;
            }
            var doc = app.ActiveDocument;
            if (doc == null)
            {
                results.Add(Fail("(setup)", "Pas de document actif."));
                return results;
            }

            // Banner de début (séparateur visuel dans le doc).
            AppendHeader(doc, "SUITE DE SCENARIOS — Run "
                + DateTime.Now.ToString("HH:mm:ss"));

            results.Add(Run(doc, "Prose + OMath inline (\"On a f(x)\")",
                () => ScenarioInlineProse(service, doc, "f(x)", "f\\left(x\\right)")));

            results.Add(Run(doc, "Prose + 1/x inline (fraction)",
                () => ScenarioInlineProse(service, doc, "1/x", "\\frac{1}{x}")));

            results.Add(Run(doc, "OMath f(x) seule → Display",
                () => ScenarioAloneDisplay(service, doc, "f(x)", "f\\left(x\\right)")));

            results.Add(Run(doc, "OMath 1/x seule → Display (fraction)",
                () => ScenarioAloneDisplay(service, doc, "1/x", "\\frac{1}{x}")));

            results.Add(Run(doc, "Liste numérotée + f(x) → Inline + CC propre",
                () => ScenarioInList(service, doc, "f(x)", "f\\left(x\\right)")));

            results.Add(Run(doc, "Liste numérotée + 1/x → Inline + CC propre",
                () => ScenarioInList(service, doc, "1/x", "\\frac{1}{x}")));

            results.Add(Run(doc, "Cellule tableau + f(x) → ¶ vide créé",
                () => ScenarioInCell(service, doc, "f(x)", "f\\left(x\\right)")));

            results.Add(Run(doc, "Cellule tableau + 1/x → ¶ vide créé",
                () => ScenarioInCell(service, doc, "1/x", "\\frac{1}{x}")));

            results.Add(Run(doc, "Cellule tableau + cases ({ x=1) → ¶ vide créé",
                () => ScenarioCasesInCell(service, doc)));

            results.Add(Run(doc, "Chaîne équivalences (=, <=>) → cross-merge align*",
                () => ScenarioChainEquivalences(service, doc)));

            results.Add(Run(doc, "Système cases ({ , { ) → cross-merge cases",
                () => ScenarioCasesSystem(service, doc)));

            results.Add(Run(doc, "Tableau 1×2 : système à gauche + équivalences à droite",
                () => ScenarioMixedTableSystemAndEquivalences(service, doc)));

            AppendFooter(doc, FormatSummary(results));

            return results;
        }

        // ─── Helpers communs ──────────────────────────────────────────

        /// <summary>
        /// Exécute le scenario, append le résultat (✓/✗ + détails) en fin de
        /// doc juste sous l'output du scenario. Ajoute aussi le diag si fail.
        /// </summary>
        private static WordScenarioResult Run(Word.Document doc, string name, Func<WordScenarioResult> body)
        {
            WordScenarioResult r;
            try
            {
                r = body();
                r.Name = name;
            }
            catch (Exception ex)
            {
                r = new WordScenarioResult { Name = name, Pass = false, Details = "Exception: " + ex.Message };
            }
            AppendResultLine(doc, r);
            return r;
        }

        /// <summary>Append en fin de doc une ligne « → Résultat : ✓/✗ … ».</summary>
        private static void AppendResultLine(Word.Document doc, WordScenarioResult r)
        {
            try
            {
                var sel = doc.Application.Selection;
                sel.SetRange(doc.Content.End - 1, doc.Content.End - 1);
                sel.TypeParagraph();
                string line = "→ Résultat : " + (r.Pass ? "✓ PASS" : "✗ FAIL") + " — " + (r.Details ?? "");
                sel.TypeText(line);
                if (!string.IsNullOrEmpty(r.Diagnostic))
                {
                    sel.TypeParagraph();
                    sel.TypeText("  diag : " + r.Diagnostic);
                }
            }
            catch { /* best-effort UI; le summary final couvre quand même */ }
        }

        private static WordScenarioResult Pass(string details, string diag = null)
            => new WordScenarioResult { Pass = true, Details = details, Diagnostic = diag };

        private static WordScenarioResult Fail(string name, string details, string diag = null)
            => new WordScenarioResult { Name = name, Pass = false, Details = details, Diagnostic = diag };

        /// <summary>Écrit un header séparateur en fin de doc + ¶ neuf.</summary>
        private static void AppendHeader(Word.Document doc, string title)
        {
            var sel = doc.Application.Selection;
            sel.SetRange(doc.Content.End - 1, doc.Content.End - 1);
            // Si le ¶ actuel n'est pas vide, sauter une ligne avant le séparateur.
            string currentParaText = "";
            try { currentParaText = sel.Paragraphs[1].Range.Text ?? ""; } catch { }
            if (currentParaText.Replace("\r", "").Replace("\a", "").Trim().Length > 0)
                sel.TypeParagraph();
            sel.TypeText(Separator);
            sel.TypeParagraph();
            sel.TypeText("▶ " + title);
            sel.TypeParagraph();
            sel.TypeText(Separator);
            sel.TypeParagraph();
        }

        /// <summary>Écrit un footer avec le résumé final dans le doc.</summary>
        private static void AppendFooter(Word.Document doc, string summary)
        {
            var sel = doc.Application.Selection;
            sel.SetRange(doc.Content.End - 1, doc.Content.End - 1);
            sel.TypeParagraph();
            sel.TypeText(Separator);
            sel.TypeParagraph();
            sel.TypeText("RÉSUMÉ");
            sel.TypeParagraph();
            sel.TypeText(Separator);
            sel.TypeParagraph();
            sel.TypeText(summary);
        }

        /// <summary>
        /// Place le caret en fin de doc, écrit un séparateur + numéro + nom +
        /// explication du scenario, puis ¶ pour démarrer le scenario.
        /// Retourne la position de début du scenario (= où le user va taper).
        /// </summary>
        private static int OpenScenarioRegion(Word.Document doc, string name, string explanation)
        {
            var sel = doc.Application.Selection;
            sel.SetRange(doc.Content.End - 1, doc.Content.End - 1);
            sel.TypeParagraph();
            sel.TypeText("─── " + name + " ───");
            sel.TypeParagraph();
            sel.TypeText("(" + explanation + ")");
            sel.TypeParagraph();
            return sel.Start;
        }

        private static int OMathCountInRange(Word.Document doc, int rangeStart, int rangeEnd)
        {
            try
            {
                var rng = doc.Range(rangeStart, Math.Min(rangeEnd, doc.Content.End));
                return rng.OMaths.Count;
            }
            catch { return -1; }
        }

        private static int CcCountInRange(Word.Document doc, int rangeStart, int rangeEnd, string title)
        {
            try
            {
                var rng = doc.Range(rangeStart, Math.Min(rangeEnd, doc.Content.End));
                int n = 0;
                foreach (Word.ContentControl cc in rng.ContentControls)
                {
                    if (cc.Title == title) n++;
                }
                return n;
            }
            catch { return -1; }
        }

        // ─── Scenarios ────────────────────────────────────────────────

        private static WordScenarioResult ScenarioInlineProse(SuggestionService svc, Word.Document doc, string source, string latex)
        {
            int regionStart = OpenScenarioRegion(doc, "Prose + " + source + " inline",
                "Tape « On a " + source + " » puis convertit. Attendu : Inline.");

            var sel = doc.Application.Selection;
            sel.TypeText("On a ");
            int absStart = sel.Start;
            sel.TypeText(source);
            int absEnd = sel.Start;

            svc.InsertOMathForScenarioTest(absStart, absEnd, latex, source);

            int regionEnd = doc.Content.End;
            int omCount = OMathCountInRange(doc, regionStart, regionEnd);
            if (omCount != 1) return Fail(null, $"Attendu 1 OMath, obtenu {omCount}");

            Word.OMath om = null;
            foreach (Word.OMath o in doc.Range(regionStart, regionEnd).OMaths) { om = o; break; }
            if (om == null) return Fail(null, "OMath introuvable");

            if (om.Type != Word.WdOMathType.wdOMathInline)
                return Fail(null, $"Attendu Inline (prose mixée), obtenu {om.Type}");

            string paraText = "";
            try { paraText = om.Range.Paragraphs[1].Range.Text ?? ""; } catch { }
            if (!paraText.StartsWith("On a "))
                return Fail(null, $"Prose 'On a ' manquante. Para: \"{Escape(paraText)}\"");

            return Pass("1 OMath Inline ✓, prose 'On a ' préservée ✓");
        }

        private static WordScenarioResult ScenarioAloneDisplay(SuggestionService svc, Word.Document doc, string source, string latex)
        {
            int regionStart = OpenScenarioRegion(doc, source + " seule",
                "Tape « " + source + " » seul sur ¶ vide. Attendu : Display.");

            var sel = doc.Application.Selection;
            int absStart = sel.Start;
            sel.TypeText(source);
            int absEnd = sel.Start;

            svc.InsertOMathForScenarioTest(absStart, absEnd, latex, source);

            int regionEnd = doc.Content.End;
            int omCount = OMathCountInRange(doc, regionStart, regionEnd);
            if (omCount != 1) return Fail(null, $"Attendu 1 OMath, obtenu {omCount}");

            Word.OMath om = null;
            foreach (Word.OMath o in doc.Range(regionStart, regionEnd).OMaths) { om = o; break; }
            if (om == null) return Fail(null, "OMath introuvable");

            if (om.Type != Word.WdOMathType.wdOMathDisplay)
                return Fail(null, $"Attendu Display (alone), obtenu {om.Type}");

            return Pass("1 OMath Display ✓");
        }

        private static WordScenarioResult ScenarioInList(SuggestionService svc, Word.Document doc, string source, string latex)
        {
            int regionStart = OpenScenarioRegion(doc, "Liste numérotée + " + source,
                "Active une liste numérotée, tape « " + source + " ». Attendu : Inline + CC propre.");

            var sel = doc.Application.Selection;
            try
            {
                Word.ListGallery gallery = doc.Application.ListGalleries[Word.WdListGalleryType.wdNumberGallery];
                sel.Range.ListFormat.ApplyListTemplate(gallery.ListTemplates[1], false);
            }
            catch (Exception ex)
            {
                return Fail(null, "Activation liste numérotée : " + ex.Message);
            }

            int absStart = sel.Start;
            sel.TypeText(source);
            int absEnd = sel.Start;

            svc.InsertOMathForScenarioTest(absStart, absEnd, latex, source);

            int regionEnd = doc.Content.End;
            int omCount = OMathCountInRange(doc, regionStart, regionEnd);
            if (omCount != 1) return Fail(null, $"Attendu 1 OMath, obtenu {omCount}");

            Word.OMath om = null;
            foreach (Word.OMath o in doc.Range(regionStart, regionEnd).OMaths) { om = o; break; }
            if (om.Type != Word.WdOMathType.wdOMathInline)
                return Fail(null, $"Attendu Inline (en liste), obtenu {om.Type}");

            int ccCount = CcCountInRange(doc, regionStart, regionEnd, MathCursor.Host.CCMeta.MCMetaJson.CcTitle);
            if (ccCount != 1) return Fail(null, $"Attendu 1 anchor CC, obtenu {ccCount}");

            Word.ContentControl cc = null;
            foreach (Word.ContentControl c in doc.Range(regionStart, regionEnd).ContentControls)
            {
                if (c.Title == MathCursor.Host.CCMeta.MCMetaJson.CcTitle) { cc = c; break; }
            }
            int ccLen = cc.Range.End - cc.Range.Start;
            if (ccLen > 5)
                return Fail(null, $"CC anchor bloated (len={ccLen}) → placeholder Word probable",
                    diag: $"cc.Range.Text=\"{Escape(cc.Range.Text ?? "")}\"");

            // Désactive la liste sur le ¶ suivant pour ne pas polluer les scenarios suivants.
            try
            {
                sel.SetRange(doc.Content.End - 1, doc.Content.End - 1);
                sel.TypeParagraph();
                sel.Range.ListFormat.RemoveNumbers();
            }
            catch { }

            return Pass($"1 OMath Inline ✓, anchor CC propre (len={ccLen}) ✓");
        }

        private static WordScenarioResult ScenarioInCell(SuggestionService svc, Word.Document doc, string source, string latex)
        {
            int regionStart = OpenScenarioRegion(doc, "Cellule tableau + " + source,
                "Crée un tableau 2x2, tape « " + source + " » dans la cellule (1,1). Attendu : 1 OMath dans la cellule. Note : pour OMath non-cases, AppendEmptyParagraphAfterOMath n'est PAS appelé — c'est testé séparément par ScenarioCasesInCell.");

            Word.Table table;
            try
            {
                var endRange = doc.Range(doc.Content.End - 1, doc.Content.End - 1);
                table = doc.Tables.Add(endRange, 2, 2);
            }
            catch (Exception ex)
            {
                return Fail(null, "Création tableau : " + ex.Message);
            }

            var cell = table.Cell(1, 1);
            var sel = doc.Application.Selection;
            sel.SetRange(cell.Range.Start, cell.Range.Start);

            int absStart = sel.Start;
            sel.TypeText(source);
            int absEnd = sel.Start;

            svc.InsertOMathForScenarioTest(absStart, absEnd, latex, source);

            // Vérifie que l'OMath est bien DANS la cellule (pas ailleurs).
            int omInCell = 0;
            try { omInCell = table.Cell(1, 1).Range.OMaths.Count; } catch { }
            if (omInCell != 1) return Fail(null, $"Attendu 1 OMath en cellule(1,1), obtenu {omInCell}");

            // Sortir du tableau pour les scenarios suivants.
            try { sel.SetRange(doc.Content.End - 1, doc.Content.End - 1); } catch { }

            return Pass("1 OMath en cellule(1,1) ✓");
        }

        /// <summary>
        /// Scenario dédié au fix « ¶ vide créé en fin de cellule » via
        /// <c>PostCommitLayoutFinalizer.IsLastParaOfTableCell</c>. On commit
        /// un cases single-line (qui passe par cette branche du LayoutImpl)
        /// puis on simule l'appel layout via <c>FinalizeLayoutForCasesScenarioTest</c>.
        /// </summary>
        private static WordScenarioResult ScenarioCasesInCell(SuggestionService svc, Word.Document doc)
        {
            int regionStart = OpenScenarioRegion(doc, "Cellule tableau + cases ({ x=1)",
                "Crée un tableau 2x2, tape « { x=1 » dans cellule(1,1), commit cases. Attendu : ¶ vide créé dans la cellule (= IsLastParaOfTableCell détecte fin de cellule et déclenche InsertParagraphAfter).");

            Word.Table table;
            try
            {
                var endRange = doc.Range(doc.Content.End - 1, doc.Content.End - 1);
                table = doc.Tables.Add(endRange, 2, 2);
            }
            catch (Exception ex)
            {
                return Fail(null, "Création tableau : " + ex.Message);
            }

            var cell = table.Cell(1, 1);
            int paraCountBefore = 0;
            try { paraCountBefore = cell.Range.Paragraphs.Count; } catch { }

            var sel = doc.Application.Selection;
            sel.SetRange(cell.Range.Start, cell.Range.Start);

            int absStart = sel.Start;
            sel.TypeText("{ x=1");
            int absEnd = sel.Start;

            var insertResult = svc.CommitWithMergersForScenarioTest(absStart, absEnd,
                "\\begin{cases} x = 1 \\end{cases}", "{ x=1");

            // Reproduit la branche IsCasesLatex de LayoutImpl : appel direct
            // au finalizer pour déclencher l'append ¶.
            svc.FinalizeLayoutForCasesScenarioTest(insertResult.newStart);

            int omInCell = 0;
            try { omInCell = table.Cell(1, 1).Range.OMaths.Count; } catch { }
            if (omInCell != 1) return Fail(null, $"Attendu 1 OMath cases en cellule(1,1), obtenu {omInCell}");

            int paraCountAfter = 0;
            try { paraCountAfter = table.Cell(1, 1).Range.Paragraphs.Count; } catch { }
            if (paraCountAfter <= paraCountBefore)
                return Fail(null, $"¶ vide PAS créé en cellule (before={paraCountBefore}, after={paraCountAfter})",
                    diag: "IsLastParaOfTableCell n'a pas déclenché InsertParagraphAfter ?");

            // Sortir du tableau.
            try { sel.SetRange(doc.Content.End - 1, doc.Content.End - 1); } catch { }

            return Pass($"1 OMath cases en cellule ✓, ¶ vide créé ({paraCountBefore} → {paraCountAfter}) ✓");
        }

        private static WordScenarioResult ScenarioChainEquivalences(SuggestionService svc, Word.Document doc)
        {
            int regionStart = OpenScenarioRegion(doc, "Chaîne équivalences (=, <=>)",
                "Ligne 1 « f(x)=1 », ligne 2 « <=>x=2 ». Attendu : 1 seule OMath multi-ligne align*.");

            var sel = doc.Application.Selection;
            int l1Start = sel.Start;
            sel.TypeText("f(x)=1");
            int l1End = sel.Start;
            svc.CommitWithMergersForScenarioTest(l1Start, l1End, "f\\left(x\\right)= 1", "f(x)=1");

            int omAfter1 = OMathCountInRange(doc, regionStart, doc.Content.End);
            if (omAfter1 != 1) return Fail(null, $"Après ligne 1, attendu 1 OMath, obtenu {omAfter1}");

            // ¶ neuf pour ligne 2.
            sel.SetRange(doc.Content.End - 1, doc.Content.End - 1);
            sel.TypeParagraph();

            int l2Start = sel.Start;
            sel.TypeText("<=>x=2");
            int l2End = sel.Start;
            svc.CommitWithMergersForScenarioTest(l2Start, l2End, "\\Leftrightarrow x = 2", "<=>x=2");

            int omAfter2 = OMathCountInRange(doc, regionStart, doc.Content.End);
            if (omAfter2 != 1)
                return Fail(null, $"Après ligne 2, attendu 1 OMath (merged align*), obtenu {omAfter2}",
                    diag: "MarkerChainCascadeMerger n'a pas absorbé la ligne 1.");

            Word.OMath om = null;
            foreach (Word.OMath o in doc.Range(regionStart, doc.Content.End).OMaths) { om = o; break; }
            string omText = "";
            try { omText = om.Range.Text ?? ""; } catch { }
            bool hasBoth = omText.Contains("1") && omText.Contains("2");
            if (!hasBoth)
                return Fail(null, "L'OMath mergée ne contient pas les deux lignes",
                    diag: $"om.Range.Text=\"{Escape(omText)}\"");

            return Pass("Cross-merge align* OK : 1 OMath contient les 2 lignes ✓");
        }

        private static WordScenarioResult ScenarioCasesSystem(SuggestionService svc, Word.Document doc)
        {
            int regionStart = OpenScenarioRegion(doc, "Système cases ({ , { )",
                "Ligne 1 « { x+1=0 », ligne 2 « { x+2=0 ». Attendu : 1 seule OMath multi-ligne cases.");

            var sel = doc.Application.Selection;
            int l1Start = sel.Start;
            sel.TypeText("{ x+1=0");
            int l1End = sel.Start;
            svc.CommitWithMergersForScenarioTest(l1Start, l1End, "\\begin{cases} x+1 & = 0 \\end{cases}", "{ x+1=0");

            int omAfter1 = OMathCountInRange(doc, regionStart, doc.Content.End);
            if (omAfter1 != 1) return Fail(null, $"Après ligne 1, attendu 1 OMath cases, obtenu {omAfter1}");

            sel.SetRange(doc.Content.End - 1, doc.Content.End - 1);
            sel.TypeParagraph();

            int l2Start = sel.Start;
            sel.TypeText("{ x+2=0");
            int l2End = sel.Start;
            svc.CommitWithMergersForScenarioTest(l2Start, l2End, "\\begin{cases} x+2 & = 0 \\end{cases}", "{ x+2=0");

            int omAfter2 = OMathCountInRange(doc, regionStart, doc.Content.End);
            if (omAfter2 != 1)
                return Fail(null, $"Après ligne 2, attendu 1 OMath cases mergé, obtenu {omAfter2}",
                    diag: "CasesChainCascadeMerger n'a pas absorbé la ligne 1.");

            Word.OMath om = null;
            foreach (Word.OMath o in doc.Range(regionStart, doc.Content.End).OMaths) { om = o; break; }
            string omText = "";
            try { omText = om.Range.Text ?? ""; } catch { }
            bool hasBoth = omText.Contains("1") && omText.Contains("2");
            if (!hasBoth)
                return Fail(null, "Le cases mergé ne contient pas les 2 lignes",
                    diag: $"om.Range.Text=\"{Escape(omText)}\"");

            return Pass("Cross-merge cases OK : 1 OMath contient les 2 lignes ✓");
        }

        private static WordScenarioResult ScenarioMixedTableSystemAndEquivalences(SuggestionService svc, Word.Document doc)
        {
            int regionStart = OpenScenarioRegion(doc, "Mixed table — cases gauche + équivalences droite",
                "1 tableau 1×2. Gauche : 2 lignes cases. Droite : 2 lignes équivalences. Attendu : 2 OMaths multi-ligne (1 par cellule).");

            Word.Table table;
            try
            {
                var endRange = doc.Range(doc.Content.End - 1, doc.Content.End - 1);
                table = doc.Tables.Add(endRange, NumRows: 1, NumColumns: 2);
            }
            catch (Exception ex)
            {
                return Fail(null, "Création tableau : " + ex.Message);
            }

            // ── Cellule gauche : système cases ─────────────────────────
            var leftCell = table.Cell(1, 1);
            var sel = doc.Application.Selection;
            sel.SetRange(leftCell.Range.Start, leftCell.Range.Start);

            int leftL1Start = sel.Start;
            sel.TypeText("{ x+1=0");
            int leftL1End = sel.Start;
            svc.CommitWithMergersForScenarioTest(leftL1Start, leftL1End,
                "\\begin{cases} x+1 & = 0 \\end{cases}", "{ x+1=0");

            // ¶ neuf DANS la cellule.
            sel.TypeParagraph();

            int leftL2Start = sel.Start;
            sel.TypeText("{ x+2=1");
            int leftL2End = sel.Start;
            svc.CommitWithMergersForScenarioTest(leftL2Start, leftL2End,
                "\\begin{cases} x+2 & = 1 \\end{cases}", "{ x+2=1");

            // ── Cellule droite : chaîne équivalences ──────────────────
            var rightCell = table.Cell(1, 2);
            sel.SetRange(rightCell.Range.Start, rightCell.Range.Start);

            int rightL1Start = sel.Start;
            sel.TypeText("f(x)=1");
            int rightL1End = sel.Start;
            svc.CommitWithMergersForScenarioTest(rightL1Start, rightL1End,
                "f\\left(x\\right)= 1", "f(x)=1");

            sel.TypeParagraph();

            int rightL2Start = sel.Start;
            sel.TypeText("<=>x=2");
            int rightL2End = sel.Start;
            svc.CommitWithMergersForScenarioTest(rightL2Start, rightL2End,
                "\\Leftrightarrow x = 2", "<=>x=2");

            // ── Vérifications ─────────────────────────────────────────
            // Recompte les cells (le ref peut bouger après modifs).
            Word.Cell leftCellNow = table.Cell(1, 1);
            Word.Cell rightCellNow = table.Cell(1, 2);

            int leftOmCount = 0;
            int rightOmCount = 0;
            try { leftOmCount = leftCellNow.Range.OMaths.Count; } catch { }
            try { rightOmCount = rightCellNow.Range.OMaths.Count; } catch { }

            if (leftOmCount != 1)
                return Fail(null, $"Cellule gauche : attendu 1 OMath cases mergé, obtenu {leftOmCount}",
                    diag: "CasesChainCascadeMerger n'a pas absorbé la 1ère ligne ?");
            if (rightOmCount != 1)
                return Fail(null, $"Cellule droite : attendu 1 OMath align* mergé, obtenu {rightOmCount}",
                    diag: "MarkerChainCascadeMerger n'a pas absorbé la 1ère ligne ?");

            // Vérifie que chaque OMath contient les 2 lignes.
            Word.OMath leftOm = null, rightOm = null;
            foreach (Word.OMath o in leftCellNow.Range.OMaths) { leftOm = o; break; }
            foreach (Word.OMath o in rightCellNow.Range.OMaths) { rightOm = o; break; }

            string leftText = "", rightText = "";
            try { leftText = leftOm?.Range.Text ?? ""; } catch { }
            try { rightText = rightOm?.Range.Text ?? ""; } catch { }

            bool leftOk = leftText.Contains("1") && leftText.Contains("2");
            bool rightOk = rightText.Contains("1") && rightText.Contains("2");

            if (!leftOk)
                return Fail(null, "Cellule gauche : cases mergé ne contient pas les 2 lignes",
                    diag: $"left.Text=\"{Escape(leftText)}\"");
            if (!rightOk)
                return Fail(null, "Cellule droite : align* mergé ne contient pas les 2 lignes",
                    diag: $"right.Text=\"{Escape(rightText)}\"");

            // Sortir du tableau pour le scenario suivant.
            try { sel.SetRange(doc.Content.End - 1, doc.Content.End - 1); } catch { }

            return Pass("Tableau mixed OK : cases gauche + align* droite, 1 OMath/cellule ✓");
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c == '\r') sb.Append("\\r");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\t') sb.Append("\\t");
                else if (c == '\a') sb.Append("\\a");
                else if (c < 32) sb.Append($"\\x{(int)c:X2}");
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string FormatSummary(List<WordScenarioResult> results)
        {
            var sb = new StringBuilder();
            int pass = 0, fail = 0;
            foreach (var r in results)
            {
                string icon = r.Pass ? "✓" : "✗";
                sb.AppendLine($"{icon} {r.Name}");
                sb.AppendLine($"    {r.Details}");
                if (!string.IsNullOrEmpty(r.Diagnostic))
                    sb.AppendLine($"    diag: {r.Diagnostic}");
                sb.AppendLine();
                if (r.Pass) pass++; else fail++;
            }
            sb.AppendLine($"TOTAL : {pass} pass, {fail} fail");
            return sb.ToString();
        }
    }
}
