using System;
using System.IO;
using System.Reflection;
using System.Windows;
using MathCursor.Host;
using Microsoft.Office.Core;

namespace MathCursor
{
    /// <summary>
    /// Implémente IRibbonExtensibility pour relier le Ribbon.xml aux actions.
    /// Utilise Globals.ThisAddIn pour accéder au host (qui peut ne pas encore
    /// être initialisé au moment où Word crée cette instance).
    /// </summary>
    [System.Runtime.InteropServices.ComVisible(true)]
    public sealed class RibbonCallback : IRibbonExtensibility
    {
        private IRibbonUI _ribbon;

        public string GetCustomUI(string ribbonID)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "MathCursor.Ribbon.xml";
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        // Log les ressources disponibles pour diagnostic
                        var names = string.Join(", ", assembly.GetManifestResourceNames());
                        LogDebug($"Ressource '{resourceName}' introuvable. Disponibles: [{names}]");
                        return "";
                    }
                    using (var reader = new StreamReader(stream))
                    {
                        var xml = reader.ReadToEnd();
                        LogDebug($"Ribbon XML chargé ({xml.Length} caractères) pour ribbonID={ribbonID}");
                        return xml;
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"GetCustomUI exception: {ex.GetType().Name} {ex.Message}");
                return "";
            }
        }

        private static void LogDebug(string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "mathcursor.log"),
                    $"{DateTime.UtcNow:o} ribbon {message}{Environment.NewLine}");
            }
            catch { /* jamais d'exception depuis le logging */ }
        }

        public void OnRibbonLoad(IRibbonUI ribbon)
        {
            _ribbon = ribbon;
        }

        // ---------- getLabel / getScreentip callbacks (i18n + version) ----------

        /// <summary>Lit l'AssemblyVersion et formate "Major.Minor.Patch".</summary>
        private static string CurrentVersion()
            => Strings.FormatVersion(Assembly.GetExecutingAssembly().GetName().Version);

        public string OnGetToolsGroupLabel(IRibbonControl control)
            => Strings.ToolsGroupLabel(CurrentVersion());

        // ---------- TabHome (duo Convertir/Colonnes) + onglet dédié ----------

        public string OnGetHomeGroupLabel(IRibbonControl control)
            => Strings.HomeGroupLabel(CurrentVersion());

        public string OnGetMathCursorTabLabel(IRibbonControl control)
            => Strings.MathCursorTabLabel;

        public string OnGetInputGroupLabel(IRibbonControl control) => Strings.InputGroupLabel;
        public string OnGetLayoutGroupLabel(IRibbonControl control) => Strings.LayoutGroupLabel;
        public string OnGetConstructionsGroupLabel(IRibbonControl control) => Strings.ConstructionsGroupLabel;
        public string OnGetToolsTabGroupLabel(IRibbonControl control) => Strings.ToolsTabGroupLabel;

        public string OnGetColumnsMenuLabel(IRibbonControl control) => Strings.ColumnsMenuLabel;
        public string OnGetColumnsMenuScreentip(IRibbonControl control) => Strings.ColumnsMenuScreentip;
        public string OnGetColumns1Label(IRibbonControl control) => Strings.Columns1Label;
        public string OnGetColumns2Label(IRibbonControl control) => Strings.Columns2Label;
        public string OnGetColumns3Label(IRibbonControl control) => Strings.Columns3Label;
        public string OnGetColumns4Label(IRibbonControl control) => Strings.Columns4Label;

        public string OnGetCheatsheetButtonLabel(IRibbonControl control) => Strings.CheatsheetButtonLabel;
        public string OnGetCheatsheetButtonScreentip(IRibbonControl control) => Strings.CheatsheetButtonScreentip;
        public bool OnGetCheatsheetEnabled(IRibbonControl control) => false; // pane en pause, cf. ADR pivot

        public string OnGetConstructionSignTableLabel(IRibbonControl control) => Strings.ConstructionSignTableLabel;
        public string OnGetConstructionVariationTableLabel(IRibbonControl control) => Strings.ConstructionVariationTableLabel;
        public string OnGetConstructionCurveLabel(IRibbonControl control) => Strings.ConstructionCurveLabel;
        public string OnGetConstructionFigureLabel(IRibbonControl control) => Strings.ConstructionFigureLabel;
        public string OnGetConstructionComingSoonScreentip(IRibbonControl control) => Strings.ConstructionComingSoonScreentip;
        public bool OnGetConstructionEnabled(IRibbonControl control) => false; // roadmap, grisé

        public string OnGetSettingsButtonLabel(IRibbonControl control) => Strings.SettingsButtonLabel;
        public string OnGetSettingsButtonScreentip(IRibbonControl control) => Strings.SettingsButtonScreentip;

        public string OnGetAboutButtonLabel(IRibbonControl control) => Strings.AboutButtonLabel;
        public string OnGetAboutButtonScreentip(IRibbonControl control) => Strings.AboutButtonScreentip;

        // ---------- getImage (icônes PNG embarquées) ----------

        /// <summary>
        /// Callback générique <c>getImage</c> du Ribbon. Charge le PNG
        /// embarqué correspondant à <see cref="IRibbonControl.Id"/>.
        /// Taille fixée à 32×32 (Office downscale proprement vers 16
        /// pour les boutons <c>size="normal"</c>). PNG générés par
        /// <c>tools/icons/rasterize-ribbon-icons.ps1</c>.
        /// </summary>
        public System.Drawing.Bitmap OnGetButtonImage(IRibbonControl control)
        {
            if (control == null) return null;
            string icon = MapControlIdToIcon(control.Id);
            if (icon == null) return null;
            return LoadEmbeddedIcon(icon, 32);
        }

        /// <summary>
        /// Mappe <c>control.Id</c> du Ribbon vers le slug d'icône PNG.
        /// Liste exhaustive (cf. Ribbon.xml). Retourne null si pas mappé
        /// (Office fallback à pas d'image).
        /// </summary>
        private static string MapControlIdToIcon(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            // Plus spécifiques d'abord (ex. InsertColumns1Button avant
            // un éventuel Contains("Columns")).
            if (id.StartsWith("InsertColumns1", StringComparison.Ordinal)) return "columns-1";
            if (id.StartsWith("InsertColumns2", StringComparison.Ordinal)) return "columns-2";
            if (id.StartsWith("InsertColumns3", StringComparison.Ordinal)) return "columns-3";
            if (id.StartsWith("InsertColumns4", StringComparison.Ordinal)) return "columns-4";
            if (id.Contains("Columns"))                       return "columns-2";   // menu trigger
            if (id.Contains("Cheatsheet"))                    return "cheatsheet";
            if (id.Contains("SignTable"))                     return "sign-table";
            if (id.Contains("VariationTable"))                return "variation-table";
            if (id.Contains("Curve"))                         return "curve";
            if (id.Contains("Figure"))                        return "figure";
            if (id.Contains("Settings"))                      return "settings";
            if (id.Contains("ReportIssue"))                   return "report-bug";
            if (id.Contains("ContextInspector"))              return "inspector";
            if (id.Contains("About"))                         return "about";
            return null;
        }

        /// <summary>Charge un PNG embarqué <c>MathCursor.Resources.ribbon-{name}-{size}.png</c>.</summary>
        private static System.Drawing.Bitmap LoadEmbeddedIcon(string name, int size)
        {
            try
            {
                string resourceName = $"MathCursor.Resources.ribbon-{name}-{size}.png";
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        LogDebug($"ribbon_icon_missing: {resourceName}");
                        return null;
                    }
                    return new System.Drawing.Bitmap(stream);
                }
            }
            catch (Exception ex)
            {
                LogDebug($"ribbon_icon_load_error: {ex.Message}");
                return null;
            }
        }

        // ---------- Actions ----------

        /// <summary>
        /// Insère un tableau N colonnes au curseur (barres séparatrices,
        /// pas de bordures externes). N parsé depuis l'id du bouton
        /// (InsertColumns{1..4}Button ou InsertColumns{1..4}TabButton).
        /// </summary>
        public void OnInsertColumnsClicked(IRibbonControl control)
        {
            try
            {
                int n = ParseColumnCountFromId(control?.Id);
                if (n < 1 || n > 4)
                {
                    LogDebug($"insert_columns_invalid_n id={control?.Id ?? "<null>"}");
                    return;
                }
                var app = Globals.ThisAddIn?.Application;
                if (app == null) return;
                MathCursor.Host.ColumnLayoutInserter.Insert(app, n);
            }
            catch (Exception ex)
            {
                LogDebug("insert_columns_error: " + ex.Message);
                MessageBox.Show(
                    "Impossible d'insérer les colonnes :\n" + ex.Message,
                    "MathCursor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private static int ParseColumnCountFromId(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            // Format attendu : "InsertColumns{N}Button" ou
            // "InsertColumns{N}TabButton". On scan le 1er chiffre.
            foreach (char c in id)
                if (c >= '1' && c <= '9') return c - '0';
            return 0;
        }

        public void OnCheatsheetClicked(IRibbonControl control)
        {
            // Pane en pause (cf. ADR pivot) — bouton grisé via
            // OnGetCheatsheetEnabled, donc onAction ne devrait pas tirer.
            // Safe : no-op.
        }

        public void OnSettingsClicked(IRibbonControl control)
        {
            MessageBox.Show(
                Strings.SettingsComingSoonBody,
                Strings.SettingsButtonLabel,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        public void OnAboutClicked(IRibbonControl control)
        {
            MessageBox.Show(
                Strings.HelpDialogBody(CurrentVersion()),
                Strings.HelpDialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // ---------- Legacy callbacks (existaient avant ADR 11-05) ----------

        public string OnGetReportButtonLabel(IRibbonControl control)
            => Strings.ReportButtonLabel;

        public string OnGetReportButtonScreentip(IRibbonControl control)
            => Strings.ReportButtonScreentip;

        public string OnGetContextInspectorButtonLabel(IRibbonControl control)
            => Strings.ContextInspectorButtonLabel;

        public string OnGetContextInspectorButtonScreentip(IRibbonControl control)
            => Strings.ContextInspectorButtonScreentip;

        /// <summary>
        /// Toggle du pane debug Context Inspector
        /// (cf. brief 2026-05-07-global-context-multi-zoom-ranking).
        /// </summary>
        public void OnContextInspectorClicked(IRibbonControl control)
        {
            try
            {
                Globals.ThisAddIn?.ToggleContextInspectorPane();
            }
            catch (Exception ex)
            {
                LogDebug("context_inspector_toggle_error: " + ex.Message);
                MessageBox.Show(
                    "Impossible d'ouvrir l'Inspecteur :\n" + ex.Message,
                    "MathCursor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Bouton de debug : insère une OMath simple <c>f(x)=1</c> à la
        /// position du curseur via le chemin Word natif minimal
        /// (Selection.TypeText + OMaths.Add + BuildUp), puis place le caret
        /// à la fin. Sert à isoler les bugs d'insertion/caret sans passer
        /// par le pipeline NER + popup + staging.
        /// </summary>
        /// <summary>
        /// Bouton de debug n°2 : recette minimale post-popup. Replace la
        /// sélection (ou les 6 chars avant le caret) par une OMath
        /// <c>f(x)=1</c> alignée à gauche.
        ///
        /// <para>Flow ultra-simple :</para>
        /// <list type="number">
        /// <item><c>doc.Range(srcStart, srcEnd).Text = unicodeMath</c> —
        /// remplace la source par le texte unicode math.</item>
        /// <item><c>OMaths.Add + BuildUp</c> sur la range résultante.</item>
        /// <item><c>om.Justification = wdOMathJcLeft</c> avec try/catch
        /// silencieux (= échec accepté pour les OMath inline).</item>
        /// <item><b>Aucun SetRange / Nudge</b> : Word place le caret.</item>
        /// </list>
        ///
        /// <para>Sert à valider que la recette minimale couvre le cas
        /// commit-popup standard sans aucun artifice (pas de ghost, pas
        /// de splice XML, pas de Policy caret).</para>
        /// </summary>
        public void OnDebugReplaceByOMathClicked(IRibbonControl control)
        {
            // NER déjà désactivé pour toute la session via DebugInProgress=true
            // dans ThisAddIn.Startup (TODO retirer quand user demande remise).
            try
            {
                var app = Globals.ThisAddIn?.Application;
                if (app == null) return;
                var doc = app.ActiveDocument;
                if (doc == null) return;
                var sel = app.Selection;
                if (sel == null) return;

                // Détermine la range source à remplacer :
                // - Sélection étendue → remplace la sélection
                // - Caret simple → remplace les 6 chars avant (= simule
                //   « f(x)=1 » fraîchement tapé par l'utilisateur)
                // Si pas de sélection, étend backward de 6 chars (= simule
                // « f(x)=1 » tapé). NER désactivé pour la session donc pas
                // de re-entrancy sur SetRange.
                if (sel.Start == sel.End)
                    sel.SetRange(Math.Max(0, sel.Start - 6), sel.Start);

                // 1. TypeText : remplace la sélection ET avance le caret à
                //    la fin (= recette qui marche partout, prouvée par le
                //    bouton « Debug : f(x)=1 » avec sélection préalable).
                string unicodeMath = MathCursor.Core.LatexToUnicodeMath.Convert("f(x)=1");
                int srcStart = sel.Start;
                sel.TypeText(unicodeMath);
                int afterEnd = sel.Start;

                // 2. OMaths.Add retourne la Range de la nouvelle OMath.
                var mathRange = doc.Range(srcStart, afterEnd);
                var addedRange = mathRange.OMaths.Add(mathRange);
                addedRange.OMaths.BuildUp();

                // 3. Récupère l'OMath via la range et aligne (silencieux).
                // TODO nettoyer : foreach + break pour récupérer le seul
                // élément (équivalent Item[1] mais tolérant aux collections
                // paresseuses Word).
                Microsoft.Office.Interop.Word.OMath om = null;
                foreach (Microsoft.Office.Interop.Word.OMath o in addedRange.OMaths) { om = o; break; }
                if (om != null)
                {
                    try { om.Justification = Microsoft.Office.Interop.Word.WdOMathJc.wdOMathJcLeft; }
                    catch (Exception exJ) { LogDebug("debug_replace.justification_error: " + exJ.Message); }
                    LogDebug($"debug_replace: om.Range=[{om.Range.Start},{om.Range.End}] sel.Start={sel.Start}");
                }
            }
            catch (Exception ex)
            {
                LogDebug("debug_replace_error: " + ex.Message);
                MessageBox.Show(
                    "Debug replace OMath failed :\n" + ex.Message,
                    "MathCursor — Debug",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// POC Phase A (brief 2026-05-18) — Wrap l'OMath collée au caret
        /// dans un ContentControl Hidden avec Title="MathCursor" + Tag JSON.
        /// Sert à valider :
        ///  - Création du CC sur une OMath existante (pas de COMException)
        ///  - Tag JSON sérialisé correctement
        ///  - Hidden = invisible visuellement dans le doc
        ///  - Backlink <c>om.Range.ParentContentControl</c> trouve le CC
        ///  - Hash OMML calculé sur le WordOpenXML stable
        ///
        /// Probe locale : <c>doc.Range(caret-1, caret).OMaths</c> au lieu
        /// de scanner doc.OMaths complet.
        /// </summary>
        public void OnDebugWrapOMathInCCClicked(IRibbonControl control)
        {
            try
            {
                var app = Globals.ThisAddIn?.Application;
                if (app == null) return;
                var doc = app.ActiveDocument;
                if (doc == null) return;
                var sel = app.Selection;
                if (sel == null) return;

                int caret = sel.Range.Start;
                if (caret <= 0)
                {
                    Globals.ThisAddIn.PushDebugTrace("POC wrap CC — caret en début de doc, pas d'OMath avant.");
                    return;
                }

                // Probe locale (brief §1) : OMath collée juste avant le caret.
                var probe = doc.Range(caret - 1, caret);
                if (probe.OMaths.Count == 0)
                {
                    Globals.ThisAddIn.PushDebugTrace($"POC wrap CC — pas d'OMath collée au caret (probe [caret-1, caret]).\ncaret = {caret}");
                    return;
                }

                Microsoft.Office.Interop.Word.OMath om = null;
                foreach (Microsoft.Office.Interop.Word.OMath o in probe.OMaths) { om = o; break; }
                if (om == null) return;

                // Anti-duplicate : déjà wrappée → no-op.
                if (om.Range.ParentContentControl?.Title == MathCursor.Host.CCMeta.MCMetaJson.CcTitle) return;

                // 1. Wrap d'abord (le wrap modifie l'OOXML — ajoute <w:sdt>).
                var cc = om.Range.ContentControls.Add(
                    Microsoft.Office.Interop.Word.WdContentControlType.wdContentControlRichText);
                cc.Title = MathCursor.Host.CCMeta.MCMetaJson.CcTitle;
                try { cc.Appearance = Microsoft.Office.Interop.Word.WdContentControlAppearance.wdContentControlHidden; }
                catch (Exception exApp) { LogDebug("poc_wrap.appearance_error: " + exApp.Message); }
                try { cc.LockContentControl = false; } catch { }
                try { cc.LockContents = false; } catch { }

                // 2. Hash APRÈS wrap : sinon store-hash et read-hash diffèrent
                //    toujours (le wrap lui-même change l'OOXML lu via
                //    om.Range.WordOpenXML) → stale=True systématique.
                string hash = MathCursor.Host.CCMeta.Sha1Helper.Compute(om.Range.WordOpenXML ?? "");

                // 3. Construit + sérialise la métadonnée + assigne le Tag.
                var meta = new MathCursor.Host.CCMeta.MCMeta
                {
                    V = 1,
                    Steno = "(POC — wrapped from existing OMath)",
                    Latex = "(POC)",
                    Version = typeof(RibbonCallback).Assembly.GetName().Version?.ToString() ?? "0",
                    OmmlHash = hash,
                    ParsedAt = DateTime.UtcNow,
                };
                string tag = MathCursor.Host.CCMeta.MCMetaJson.Serialize(meta);
                cc.Tag = tag;

                LogDebug($"poc_wrap: om=[{om.Range.Start},{om.Range.End}] cc.Range=[{cc.Range.Start},{cc.Range.End}] tag_len={tag.Length} hash={hash.Substring(0, 8)}…");

                Globals.ThisAddIn.PushDebugTrace(
                    $"=== POC wrap CC — OK ===\n"
                    + $"om.Range  = [{om.Range.Start}, {om.Range.End})\n"
                    + $"cc.Range  = [{cc.Range.Start}, {cc.Range.End})\n"
                    + $"Title     = {cc.Title}\n"
                    + $"Appearance = {cc.Appearance}\n"
                    + $"Tag JSON ({tag.Length} chars):\n{tag}");
            }
            catch (Exception ex)
            {
                LogDebug("poc_wrap_error: " + ex.Message);
                Globals.ThisAddIn.PushDebugTrace("=== POC wrap CC — ERROR ===\n" + ex.Message);
            }
        }

        /// <summary>
        /// POC Phase A — Lit le CC parent de l'OMath collée au caret et
        /// affiche son contenu (Title + Tag JSON parsé + détection stale
        /// via hash). Valide le backlink natif O(1).
        /// </summary>
        public void OnDebugReadCCAtCaretClicked(IRibbonControl control)
        {
            try
            {
                var app = Globals.ThisAddIn?.Application;
                if (app == null) return;
                var doc = app.ActiveDocument;
                if (doc == null) return;
                var sel = app.Selection;
                if (sel == null) return;

                int caret = sel.Range.Start;
                int docEnd = doc.Content.End;

                // Cascade de probes, plus large que l'insertion (qui exige
                // l'OMath collée pile derrière le caret) :
                //   1. sel.OMaths       — caret strictement DANS une OMath
                //   2. [caret-1, caret) — collée juste derrière (cas standard)
                //   3. [caret, caret+1) — collée juste devant
                //   4. [caret-5, caret+5) — fenêtre 10 chars autour
                //   5. para.OMaths      — n'importe où dans le ¶ courant
                Microsoft.Office.Interop.Word.OMath om = null;
                string hitFrom = null;
                try
                {
                    if (sel.OMaths != null && sel.OMaths.Count > 0)
                    {
                        foreach (Microsoft.Office.Interop.Word.OMath o in sel.OMaths) { om = o; hitFrom = "sel.OMaths"; break; }
                    }
                }
                catch { }
                if (om == null && caret > 0)
                {
                    try
                    {
                        var p = doc.Range(caret - 1, caret);
                        foreach (Microsoft.Office.Interop.Word.OMath o in p.OMaths) { om = o; hitFrom = "[caret-1,caret)"; break; }
                    }
                    catch { }
                }
                if (om == null && caret < docEnd)
                {
                    try
                    {
                        var p = doc.Range(caret, Math.Min(caret + 1, docEnd));
                        foreach (Microsoft.Office.Interop.Word.OMath o in p.OMaths) { om = o; hitFrom = "[caret,caret+1)"; break; }
                    }
                    catch { }
                }
                if (om == null)
                {
                    try
                    {
                        int lo = Math.Max(0, caret - 5);
                        int hi = Math.Min(docEnd, caret + 5);
                        var p = doc.Range(lo, hi);
                        foreach (Microsoft.Office.Interop.Word.OMath o in p.OMaths) { om = o; hitFrom = $"[{lo},{hi}) ±5"; break; }
                    }
                    catch { }
                }
                if (om == null)
                {
                    try
                    {
                        var paraRange = sel.Paragraphs[1].Range;
                        foreach (Microsoft.Office.Interop.Word.OMath o in paraRange.OMaths) { om = o; hitFrom = "para.OMaths"; break; }
                    }
                    catch { }
                }
                if (om == null)
                {
                    Globals.ThisAddIn.PushDebugTrace(
                        $"=== POC read CC — no OMath ===\n"
                        + $"Aucune OMath trouvée autour du caret (cascade : sel, ±1, ±5, ¶ courant).\n"
                        + $"caret = {caret}");
                    return;
                }

                // Backlink natif principal : depuis la range de l'OMath.
                var cc = om.Range.ParentContentControl;
                string ccFrom = "om.Range.ParentContentControl";

                // Fallbacks : tester ParentContentControl sur des positions
                // alternatives (= différentes Range collapsées) au cas où
                // Word ne remonte pas le CC depuis la range exacte de l'OMath.
                if (cc == null)
                {
                    int mid = (om.Range.Start + om.Range.End) / 2;
                    int[] probes = {
                        om.Range.Start,
                        om.Range.Start + 1,
                        om.Range.End - 1,
                        caret,
                        mid,
                    };
                    foreach (var p in probes)
                    {
                        if (p < 0 || p > docEnd) continue;
                        try
                        {
                            var c = doc.Range(p, p).ParentContentControl;
                            if (c != null) { cc = c; ccFrom = $"doc.Range({p},{p}).ParentContentControl"; break; }
                        }
                        catch { }
                    }
                }

                // Probe inverse : CCs CONTENUES dans la range de l'OMath
                // (cas où om.Range est plus large que cc.Range — display
                // math avec CC en sub-anchor). Pas un backlink natif mais
                // local à la range, pas un scan doc complet.
                if (cc == null)
                {
                    try
                    {
                        foreach (Microsoft.Office.Interop.Word.ContentControl c in om.Range.ContentControls)
                        {
                            if (c.Title == MathCursor.Host.CCMeta.MCMetaJson.CcTitle)
                            {
                                cc = c;
                                ccFrom = "om.Range.ContentControls (inverse probe)";
                                break;
                            }
                        }
                    }
                    catch { }
                }

                if (cc == null)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("=== POC read CC — backlink NULL ===");
                    sb.AppendLine($"OMath trouvée à [{om.Range.Start}, {om.Range.End}) via probe « {hitFrom} ».");
                    sb.AppendLine("Backlink ParentContentControl : NULL sur toutes les variantes :");
                    sb.AppendLine("  - om.Range.ParentContentControl");
                    sb.AppendLine("  - doc.Range(omStart, omStart).ParentContentControl");
                    sb.AppendLine("  - doc.Range(omStart+1, omStart+1).ParentContentControl");
                    sb.AppendLine("  - doc.Range(omEnd-1, omEnd-1).ParentContentControl");
                    sb.AppendLine("  - doc.Range(caret, caret).ParentContentControl");
                    sb.AppendLine();
                    sb.AppendLine("Sanity-check — CCs MathCursor dans le doc :");
                    int total = 0, mc = 0;
                    try
                    {
                        foreach (Microsoft.Office.Interop.Word.ContentControl c in doc.ContentControls)
                        {
                            total++;
                            if (c.Title == MathCursor.Host.CCMeta.MCMetaJson.CcTitle)
                            {
                                mc++;
                                sb.AppendLine($"  • cc.Range=[{c.Range.Start},{c.Range.End}) Title={c.Title} Tag.Length={c.Tag?.Length ?? 0}");
                            }
                        }
                    }
                    catch (Exception exEnum) { sb.AppendLine("  (énum failed: " + exEnum.Message + ")"); }
                    sb.AppendLine();
                    sb.AppendLine($"Total CCs={total}, dont MathCursor={mc}.");
                    sb.AppendLine($"caret={caret}, om.Range=[{om.Range.Start},{om.Range.End})");

                    LogDebug($"poc_read.no_backlink: om=[{om.Range.Start},{om.Range.End}) total_ccs={total} mc={mc}");
                    Globals.ThisAddIn.PushDebugTrace(sb.ToString());
                    return;
                }

                if (cc.Title != MathCursor.Host.CCMeta.MCMetaJson.CcTitle)
                {
                    Globals.ThisAddIn.PushDebugTrace(
                        $"=== POC read CC — wrong Title ===\n"
                        + $"CC trouvé mais Title=\"{cc.Title}\" (≠ MathCursor) → pas à nous.");
                    return;
                }

                var meta = MathCursor.Host.CCMeta.MCMetaJson.TryParse(cc.Tag);
                string currentHash = MathCursor.Host.CCMeta.Sha1Helper.Compute(om.Range.WordOpenXML ?? "");
                bool stale = meta != null && !string.Equals(meta.OmmlHash, currentHash, StringComparison.Ordinal);

                string msg = $"=== POC read CC — OK ===\n"
                    + $"Backlink OK via probe OMath « {hitFrom} » + backlink CC « {ccFrom} ».\n"
                    + $"om.Range = [{om.Range.Start}, {om.Range.End})\n"
                    + $"cc.Range = [{cc.Range.Start}, {cc.Range.End})\n"
                    + $"Title    = {cc.Title}\n";
                if (meta != null)
                {
                    msg += $"v        = {meta.V}\n"
                        + $"steno    = {meta.Steno}\n"
                        + $"latex    = {meta.Latex}\n"
                        + $"version  = {meta.Version}\n"
                        + $"omml_hash stored  = {meta.OmmlHash?.Substring(0, Math.Min(8, meta.OmmlHash?.Length ?? 0))}…\n"
                        + $"omml_hash current = {currentHash.Substring(0, 8)}…\n"
                        + $"stale (user a édité) = {stale}\n"
                        + $"parsedAt = {meta.ParsedAt:o}\n";
                }
                else { msg += "Tag JSON non parseable :\n" + cc.Tag; }

                LogDebug($"poc_read: cc.Range=[{cc.Range.Start},{cc.Range.End}] stale={stale}");
                Globals.ThisAddIn.PushDebugTrace(msg);
            }
            catch (Exception ex)
            {
                LogDebug("poc_read_error: " + ex.Message);
                Globals.ThisAddIn.PushDebugTrace("=== POC read CC — ERROR ===\n" + ex.Message);
            }
        }

        /// <summary>
        /// POC Phase A — Insère <c>g(x)=2</c> au caret en OMath ET enveloppe
        /// dans un CC MathCursor avec Tag JSON. Simule le full flow que
        /// <c>InsertOMathAt</c> aura en Phase B/C.
        ///
        /// <para>Order tenté ici (« CC first ») : TypeText → CC wrap sur le
        /// texte plat → OMaths.Add + BuildUp à l'intérieur du CC →
        /// Justification → hash post-wrap → Tag final. Théorie : faire le CC
        /// AVANT BuildUp évite que Word laisse le caret « catché » dans la
        /// zone math au moment du wrap (le wrap se fait sur du plain text
        /// donc Word ne place pas le caret en math context).</para>
        /// </summary>
        public void OnDebugInsertWrappedOMathClicked(IRibbonControl control)
        {
            try
            {
                var app = Globals.ThisAddIn?.Application;
                if (app == null) return;
                var doc = app.ActiveDocument;
                if (doc == null) return;
                var sel = app.Selection;
                if (sel == null) return;

                int insertPos = sel.Start;
                string unicodeMath = MathCursor.Core.LatexToUnicodeMath.Convert("g(x)=2");

                int caretAfterStep1 = -1, caretAfterStep2 = -1;
                int caretAfterStep3 = -1, caretAfterStep4 = -1;
                Microsoft.Office.Interop.Word.OMath om = null;
                Microsoft.Office.Interop.Word.ContentControl cc = null;
                string hash = null;
                string tag = null;

                // Tout grouper en 1 seule entrée d'undo nommée. Sans ça,
                // BuildUp + CC + TypeText génèrent 3-4 entrées séparées et
                // l'utilisateur doit Ctrl+Z plusieurs fois pour défaire son
                // commit. Évite aussi la désynchro CC/formule en cas d'undo
                // partiel (cc orphelin ou formule sans cc).
                using (var _undo = new MathCursor.Host.UndoRecordScope(app, "Insert g(x)=2 wrappé"))
                {
                    // 1. Type "g(x)=2" au caret.
                    sel.TypeText(unicodeMath);
                    int afterTypeText = sel.Start;
                    caretAfterStep1 = sel.Start;

                    // 2. CC d'abord sur la range plain text.
                    var typedRange = doc.Range(insertPos, afterTypeText);
                    cc = typedRange.ContentControls.Add(
                        Microsoft.Office.Interop.Word.WdContentControlType.wdContentControlRichText);
                    cc.Title = MathCursor.Host.CCMeta.MCMetaJson.CcTitle;
                    try { cc.Appearance = Microsoft.Office.Interop.Word.WdContentControlAppearance.wdContentControlHidden; }
                    catch (Exception exApp) { LogDebug("poc_insert_wrap.appearance_error: " + exApp.Message); }
                    try { cc.LockContentControl = false; } catch { }
                    try { cc.LockContents = false; } catch { }
                    caretAfterStep2 = sel.Start;

                    // 3. OMaths.Add + BuildUp sur la range INTERNE du CC.
                    var innerRange = cc.Range;
                    var addedRange = innerRange.OMaths.Add(innerRange);
                    addedRange.OMaths.BuildUp();
                    caretAfterStep3 = sel.Start;

                    foreach (Microsoft.Office.Interop.Word.OMath o in addedRange.OMaths) { om = o; break; }
                    if (om == null)
                    {
                        Globals.ThisAddIn.PushDebugTrace("=== POC insert+wrap (CC first) — addedRange.OMaths empty ===");
                        return;
                    }

                    try { om.Justification = Microsoft.Office.Interop.Word.WdOMathJc.wdOMathJcLeft; }
                    catch (Exception exJ) { LogDebug("poc_insert_wrap.justification_error: " + exJ.Message); }
                    caretAfterStep4 = sel.Start;

                    // 4. Hash APRÈS wrap pour stable store/read.
                    hash = MathCursor.Host.CCMeta.Sha1Helper.Compute(om.Range.WordOpenXML ?? "");

                    // 5. Tag JSON final.
                    var meta = new MathCursor.Host.CCMeta.MCMeta
                    {
                        V = 1,
                        Steno = "g x = 2",
                        Latex = "g(x)=2",
                        Version = typeof(RibbonCallback).Assembly.GetName().Version?.ToString() ?? "0",
                        OmmlHash = hash,
                        ParsedAt = DateTime.UtcNow,
                    };
                    tag = MathCursor.Host.CCMeta.MCMetaJson.Serialize(meta);
                    cc.Tag = tag;

                    // Sort le caret de la sticky-zone du CC pour que la
                    // prochaine frappe user ne soit pas absorbée.
                    MathCursor.Host.CCMeta.CcSticky.EscapeCaretAfter(app, cc);
                }
                // Sortie du using → l'entrée d'undo « Insert g(x)=2 wrappé »
                // est close. 1 Ctrl+Z défait l'ensemble.

                LogDebug($"poc_insert_wrap (CC first): om=[{om.Range.Start},{om.Range.End}] cc=[{cc.Range.Start},{cc.Range.End}] hash={hash.Substring(0, 8)}");

                Globals.ThisAddIn.PushDebugTrace(
                    $"=== POC insert+wrap g(x)=2 (CC first, undo grouped) — OK ===\n"
                    + $"insertPos    = {insertPos}\n"
                    + $"unicodeMath  = \"{unicodeMath}\" ({unicodeMath.Length} chars)\n"
                    + $"\n"
                    + $"Caret tracking par étape :\n"
                    + $"  step 1 (post TypeText)     : {caretAfterStep1}\n"
                    + $"  step 2 (post CC wrap)      : {caretAfterStep2}\n"
                    + $"  step 3 (post BuildUp)      : {caretAfterStep3}\n"
                    + $"  step 4 (post Justification): {caretAfterStep4}\n"
                    + $"  final sel.Start            : {sel.Start}\n"
                    + $"\n"
                    + $"om.Range  = [{om.Range.Start}, {om.Range.End})\n"
                    + $"cc.Range  = [{cc.Range.Start}, {cc.Range.End})\n"
                    + $"Title     = {cc.Title}\n"
                    + $"Appearance = {cc.Appearance}\n"
                    + $"hash      = {hash.Substring(0, 12)}…\n"
                    + $"\nTag JSON ({tag.Length} chars):\n{tag}");
            }
            catch (Exception ex)
            {
                LogDebug("poc_insert_wrap_error: " + ex.Message);
                Globals.ThisAddIn.PushDebugTrace("=== POC insert+wrap (CC first) — ERROR ===\n" + ex.Message);
            }
        }

        /// <summary>
        /// POC diagnostic — scanne <c>doc.ContentControls</c>, identifie les
        /// CCs MathCursor SANS OMath dedans (= orphelins fantômes laissés par
        /// Word après certaines suppressions). Affiche le diag dans le pane
        /// puis supprime les orphelins.
        /// </summary>
        public void OnDebugCleanupOrphanCCsClicked(IRibbonControl control)
        {
            try
            {
                var app = Globals.ThisAddIn?.Application;
                if (app == null) return;
                var doc = app.ActiveDocument;
                if (doc == null) return;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== POC cleanup orphan CCs ===");

                // Collecte d'abord, supprime ensuite : éviter de muter la
                // collection pendant l'itération (Word interop est fragile).
                var orphans = new System.Collections.Generic.List<Microsoft.Office.Interop.Word.ContentControl>();
                var alive = new System.Collections.Generic.List<Microsoft.Office.Interop.Word.ContentControl>();
                int total = 0;
                foreach (Microsoft.Office.Interop.Word.ContentControl c in doc.ContentControls)
                {
                    total++;
                    if (c.Title != MathCursor.Host.CCMeta.MCMetaJson.CcTitle) continue;
                    int omCount = 0;
                    try { omCount = c.Range.OMaths.Count; } catch { }
                    if (omCount == 0) orphans.Add(c);
                    else alive.Add(c);
                }

                sb.AppendLine($"Total CCs={total}, MathCursor alive={alive.Count}, orphans={orphans.Count}");
                sb.AppendLine();

                if (alive.Count > 0)
                {
                    sb.AppendLine("CCs avec OMath (sains) :");
                    foreach (var c in alive)
                    {
                        sb.AppendLine($"  • cc.Range=[{c.Range.Start},{c.Range.End}) OMaths={c.Range.OMaths.Count}");
                    }
                    sb.AppendLine();
                }

                if (orphans.Count > 0)
                {
                    sb.AppendLine("CCs ORPHELINS (à supprimer) :");
                    foreach (var c in orphans)
                    {
                        sb.AppendLine($"  • cc.Range=[{c.Range.Start},{c.Range.End}) Tag.Length={c.Tag?.Length ?? 0}");
                    }
                    sb.AppendLine();

                    int deleted = 0;
                    foreach (var c in orphans)
                    {
                        try { c.Delete(false); deleted++; } // false = supprime wrapper seul
                        catch (Exception ex) { sb.AppendLine($"  ERROR cc.Delete: {ex.Message}"); }
                    }
                    sb.AppendLine($"→ {deleted} orphelin(s) supprimé(s).");
                }
                else
                {
                    sb.AppendLine("Aucun orphelin trouvé.");
                }

                LogDebug($"poc_cleanup: total={total} alive={alive.Count} orphans={orphans.Count}");
                Globals.ThisAddIn.PushDebugTrace(sb.ToString());
            }
            catch (Exception ex)
            {
                LogDebug("poc_cleanup_error: " + ex.Message);
                Globals.ThisAddIn.PushDebugTrace("=== POC cleanup — ERROR ===\n" + ex.Message);
            }
        }

        /// <summary>Step debug — start. Capture la sélection courante comme zone
        /// d'insertion, hardcode source/latex pour le scénario typique
        /// f(x) + =1. Reset le step runner pour repartir au step 0.</summary>
        public void OnDebugStepStartClicked(IRibbonControl control)
        {
            try
            {
                var app = Globals.ThisAddIn?.Application;
                if (app == null) return;
                var trace = MathCursor.Host.CCMeta.PocStepRunner.Start(app, source: "=1", latex: "= 1");
                Globals.ThisAddIn.PushDebugTrace(trace);
            }
            catch (Exception ex)
            {
                Globals.ThisAddIn.PushDebugTrace("=== POC Step start ERROR ===\n" + ex.Message);
            }
        }

        /// <summary>
        /// POC diagnostic — dump des positions Word de chaque char du ¶
        /// courant via <c>Range.Characters</c>. Pour valider si Word peut
        /// nous donner directement la position interne d'un char visible
        /// (= alternative à un translator string→internal).
        /// </summary>
        public void OnDebugDumpCharPositionsClicked(IRibbonControl control)
        {
            try
            {
                var app = Globals.ThisAddIn?.Application;
                if (app == null) return;
                var doc = app.ActiveDocument;
                if (doc == null) return;
                var sel = app.Selection;
                if (sel == null) return;

                var paraRange = sel.Paragraphs[1].Range;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== POC : dump char positions ===");
                sb.AppendLine($"paragraph.Range = [{paraRange.Start}, {paraRange.End})");
                sb.AppendLine($"paragraph.Range.Text.Length = {(paraRange.Text ?? "").Length}");
                sb.AppendLine($"paragraph.Range.Characters.Count = {paraRange.Characters.Count}");
                sb.AppendLine($"sel = [{sel.Start}, {sel.End})");
                sb.AppendLine();
                sb.AppendLine("Iter Range.Characters :");
                sb.AppendLine("  idx | Start, End  | Text (escaped)");
                sb.AppendLine("  ----+-------------+----------------");

                int i = 0;
                foreach (Microsoft.Office.Interop.Word.Range c in paraRange.Characters)
                {
                    try
                    {
                        string text = c.Text ?? "";
                        string escaped = text.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
                        // Surrogate-pair friendly preview
                        if (escaped.Length > 12) escaped = escaped.Substring(0, 12) + "…";
                        sb.AppendLine($"  {i,3} | [{c.Start,3}, {c.End,3}) | \"{escaped}\" (len={text.Length})");
                    }
                    catch (Exception exC) { sb.AppendLine($"  {i,3} | ERR : {exC.Message}"); }
                    i++;
                    if (i > 80) { sb.AppendLine("  (… troncated, > 80 chars)"); break; }
                }

                sb.AppendLine();
                sb.AppendLine("OMaths dans le ¶ :");
                try
                {
                    int omIdx = 0;
                    foreach (Microsoft.Office.Interop.Word.OMath om in paraRange.OMaths)
                    {
                        omIdx++;
                        string omText = "";
                        try { omText = om.Range.Text ?? ""; } catch { }
                        sb.AppendLine($"  OMath #{omIdx} : Range=[{om.Range.Start},{om.Range.End}) (width={om.Range.End - om.Range.Start}, text.Length={omText.Length})");
                    }
                }
                catch (Exception exO) { sb.AppendLine("  ERR OMaths iter : " + exO.Message); }

                LogDebug($"poc_dump_chars: paragraph=[{paraRange.Start},{paraRange.End}) sel=[{sel.Start},{sel.End}) chars={paraRange.Characters.Count}");
                Globals.ThisAddIn.PushDebugTrace(sb.ToString());
            }
            catch (Exception ex)
            {
                LogDebug("poc_dump_chars_error: " + ex.Message);
                Globals.ThisAddIn.PushDebugTrace("=== POC : dump char positions ERROR ===\n" + ex.Message);
            }
        }

        /// <summary>Step debug — next. Avance d'une étape dans la séquence
        /// InsertOMathAt et affiche l'état du doc post-étape.</summary>
        public void OnDebugStepNextClicked(IRibbonControl control)
        {
            try
            {
                var app = Globals.ThisAddIn?.Application;
                if (app == null) return;
                var trace = MathCursor.Host.CCMeta.PocStepRunner.Next(app);
                Globals.ThisAddIn.PushDebugTrace(trace);
            }
            catch (Exception ex)
            {
                Globals.ThisAddIn.PushDebugTrace("=== POC Step next ERROR ===\n" + ex.Message);
            }
        }

        /// <summary>Helper safe pour lire <c>Selection.OMaths.Count</c>
        /// sans jeter (collection Word peut être null ou indisponible).</summary>
        private static int SafeOMathsCount(Microsoft.Office.Interop.Word.Selection sel)
        {
            try { return sel?.OMaths?.Count ?? -1; }
            catch { return -1; }
        }

        public void OnDebugInsertOMathClicked(IRibbonControl control)
        {
            try
            {
                var app = Globals.ThisAddIn?.Application;
                if (app == null) return;
                var doc = app.ActiveDocument;
                if (doc == null) return;
                var sel = app.Selection;
                if (sel == null) return;

                int insertPos = sel.Start;
                LogDebug($"debug_insert: start at sel.Start={insertPos} docEnd={doc.Content.End}");

                // 1. Type "f(x)=1" à la position du caret.
                sel.TypeText("f(x)=1");

                // 2. OMaths.Add retourne la Range de la nouvelle OMath.
                int afterTypedEnd = insertPos + 6;
                var typedRange = doc.Range(insertPos, afterTypedEnd);
                var addedRange = typedRange.OMaths.Add(typedRange);
                addedRange.OMaths.BuildUp();

                // 3. Récupère l'OMath via la range et aligne (silencieux).
                // TODO nettoyer : foreach + break pour récupérer le seul
                // élément (équivalent Item[1] mais tolérant aux collections
                // paresseuses Word).
                Microsoft.Office.Interop.Word.OMath om = null;
                foreach (Microsoft.Office.Interop.Word.OMath o in addedRange.OMaths) { om = o; break; }
                if (om != null)
                {
                    LogDebug($"debug_insert: om.Range=[{om.Range.Start},{om.Range.End}]");
                    try
                    {
                        om.Justification = Microsoft.Office.Interop.Word.WdOMathJc.wdOMathJcLeft;
                        LogDebug("debug_insert: om.Justification = Left → OK");
                    }
                    catch (Exception exAlign)
                    {
                        LogDebug("debug_insert.align_justification_error: " + exAlign.Message);
                    }
                }

                // 4. Pas de SetRange — Word place le caret naturellement.
            }
            catch (Exception ex)
            {
                LogDebug("debug_insert_error: " + ex.Message);
                MessageBox.Show(
                    "Debug insert OMath failed :\n" + ex.Message,
                    "MathCursor — Debug",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Ouvre la fenêtre WPF "Signaler une erreur" pré-remplie depuis le
        /// dernier <see cref="MathCursor.Host.LastActionSnapshot"/> (saisie /
        /// proposé / inséré). 3 actions dans la fenêtre : Annuler, Copier
        /// dans un mail, Envoyer (POST direct vers /api/v1/report).
        ///
        /// Cf. ADR 2026-04-30-Feat-feedback-form-cloudflare-backend.
        /// </summary>
        public void OnReportIssueClicked(IRibbonControl control)
        {
            try
            {
                var suggestions = Globals.ThisAddIn?.Suggestions;
                if (suggestions == null)
                {
                    MessageBox.Show(
                        Strings.ReportFailedBody(FeedbackBundle.ContactEmail),
                        Strings.ReportFailedTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
                // Capture le screen AVANT de cacher la popup (la popup de
                // suggestion fait partie du contexte du bug et est utile à
                // voir). Ensuite on cache la popup pour ne pas qu'elle se
                // superpose au dialog. Le dialog est ouvert APRÈS capture
                // donc n'apparaît jamais dans le screenshot.
                byte[] preScreenshot = null;
                try { preScreenshot = MathCursor.Host.FeedbackBundle.CaptureScreenshotPng(); } catch { }
                try { suggestions.HidePopup(); } catch { }
                var report = suggestions.BuildFeedbackReport();
                if (preScreenshot != null && preScreenshot.Length > 0)
                    report.ScreenshotPngBase64 = Convert.ToBase64String(preScreenshot);
                var sender = MathCursor.Host.Feedback.FeedbackSenderFactory.Create();
                var dialog = new MathCursor.UI.FeedbackDialog(report, sender);
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                LogDebug("report_dialog_error: " + ex.Message);
                MessageBox.Show(
                    Strings.ReportFailedBody(FeedbackBundle.ContactEmail),
                    Strings.ReportFailedTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}
