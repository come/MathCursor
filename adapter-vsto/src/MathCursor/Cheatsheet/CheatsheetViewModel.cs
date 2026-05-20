using System;
using System.Collections.Generic;
using System.Linq;

namespace MathCursor.Cheatsheet
{
    /// <summary>
    /// Vue d'une catégorie filtrée — résultat d'un cycle de recherche / collapse.
    /// </summary>
    internal sealed class CategoryView
    {
        public string Id { get; set; }
        public string Label { get; set; }            // déjà localisé (fr ou en)
        public bool IsExpanded { get; set; }
        public IReadOnlyList<CheatsheetEntry> VisibleEntries { get; set; }
    }

    /// <summary>
    /// ViewModel pure (sans WPF/Word) pour le pane Exemples (schema v2) :
    /// <list type="bullet">
    /// <item><b>Recherche temps réel</b> : filtre case-insensitive sur title
    /// + stenos[*] + tags (par entrée) + label catégorie (auto-expand de la
    /// catégorie qui matche).</item>
    /// <item><b>Collapse state</b> : état expand/collapse par catégorie,
    /// persistable. Pendant une recherche active, les catégories qui matchent
    /// sont forcées expanded ; à la sortie de search, l'état saved revient.</item>
    /// <item><b>i18n</b> : label catégorie résolu selon <c>lang</c> ("fr" / "en").</item>
    /// </list>
    /// </summary>
    internal sealed class CheatsheetViewModel
    {
        private readonly CheatsheetDocument _doc;
        private readonly string _lang;
        // État explicite saved par l'user. Si une catégorie n'est PAS dans ce
        // dict, son état est "expanded par défaut". Stocké en sparse pour ne
        // sérialiser que les overrides côté IsolatedStorage.
        private readonly Dictionary<string, bool> _savedCollapse = new Dictionary<string, bool>(StringComparer.Ordinal);
        private string _searchQuery = "";

        public CheatsheetViewModel(CheatsheetDocument doc, string lang)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _lang = lang ?? "fr";
        }

        /// <summary>
        /// Levé quand l'état persistable change (collapse explicite par l'user).
        /// Permet à l'add-in de déclencher une sauvegarde IsolatedStorage.
        /// </summary>
        public event Action StateChanged;

        /// <summary>
        /// Texte de recherche (TrimStart/End appliqué au get). Setter
        /// déclenche un recalcul des catégories visibles.
        /// </summary>
        public string SearchQuery
        {
            get => _searchQuery;
            set => _searchQuery = (value ?? string.Empty).Trim();
        }

        /// <summary>
        /// Catégories visibles après application du filtre. Recalculé à chaque
        /// accès (peu coûteux : 8 cats × 3 entrées = 24 comparaisons string).
        /// </summary>
        public IReadOnlyList<CategoryView> VisibleCategories
        {
            get
            {
                var query = _searchQuery;
                bool hasQuery = !string.IsNullOrEmpty(query);

                var result = new List<CategoryView>();
                foreach (var cat in (_doc.Categories ?? Array.Empty<CheatsheetCategory>())
                                    .OrderBy(c => c.Order))
                {
                    string label = LabelFor(cat);
                    bool labelMatches = hasQuery && label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

                    IReadOnlyList<CheatsheetEntry> visibleEntries;
                    if (!hasQuery || labelMatches)
                    {
                        visibleEntries = cat.Entries ?? Array.Empty<CheatsheetEntry>();
                    }
                    else
                    {
                        // Filter par steno + tags
                        visibleEntries = (cat.Entries ?? Array.Empty<CheatsheetEntry>())
                            .Where(e => EntryMatches(e, query))
                            .ToList();
                        if (visibleEntries.Count == 0) continue; // catégorie cachée
                    }

                    bool expanded = hasQuery
                        ? true                        // auto-expand pendant search
                        : SavedExpanded(cat.Id);     // sinon, état saved

                    result.Add(new CategoryView
                    {
                        Id = cat.Id,
                        Label = label,
                        IsExpanded = expanded,
                        VisibleEntries = visibleEntries,
                    });
                }
                return result;
            }
        }

        /// <summary>
        /// État explicite (toggle) d'une catégorie. Default = expanded (true)
        /// si l'user n'a jamais touché.
        /// </summary>
        public bool IsExpanded(string categoryId) => SavedExpanded(categoryId);

        /// <summary>
        /// Toggle utilisateur sur le header d'une catégorie.
        /// </summary>
        public void SetExpanded(string categoryId, bool expanded)
        {
            if (string.IsNullOrEmpty(categoryId)) return;
            bool changed;
            if (expanded)
            {
                // Default = true → on retire l'override pour rester sparse
                changed = _savedCollapse.Remove(categoryId);
            }
            else
            {
                changed = !_savedCollapse.ContainsKey(categoryId) || _savedCollapse[categoryId] != false;
                _savedCollapse[categoryId] = false;
            }
            if (changed) StateChanged?.Invoke();
        }

        /// <summary>
        /// Retourne le state collapse "non-default" pour persistance externe
        /// (IsolatedStorage). Sparse : seulement les catégories explicitement
        /// collapsed apparaissent.
        /// </summary>
        public IDictionary<string, bool> GetCollapseState()
            => new Dictionary<string, bool>(_savedCollapse);

        /// <summary>
        /// Restaure l'état collapse depuis une persistance. IDs inconnus
        /// (catégorie supprimée d'une version à l'autre) ignorés.
        /// </summary>
        public void SetCollapseState(IDictionary<string, bool> state)
        {
            _savedCollapse.Clear();
            if (state == null) return;
            var validIds = new HashSet<string>(
                (_doc.Categories ?? Array.Empty<CheatsheetCategory>()).Select(c => c.Id),
                StringComparer.Ordinal);
            foreach (var kv in state)
            {
                if (!validIds.Contains(kv.Key)) continue;
                if (!kv.Value) _savedCollapse[kv.Key] = false;
            }
        }

        /// <summary>
        /// Titre localisé d'une entrée selon la langue active de la VM.
        /// </summary>
        public string TitleFor(CheatsheetEntry e)
        {
            if (e == null) return "";
            return _lang == "en" ? (e.TitleEn ?? e.TitleFr ?? "") : (e.TitleFr ?? e.TitleEn ?? "");
        }

        // ─── helpers privés ──────────────────────────────────────────────

        private string LabelFor(CheatsheetCategory cat)
            => _lang == "en" ? (cat.LabelEn ?? cat.LabelFr ?? "") : (cat.LabelFr ?? cat.LabelEn ?? "");

        private bool SavedExpanded(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId)) return true;
            return !_savedCollapse.TryGetValue(categoryId, out bool collapsed) || collapsed;
        }

        private bool EntryMatches(CheatsheetEntry e, string query)
        {
            if (e == null) return false;
            // Title (langue active)
            string title = _lang == "en" ? (e.TitleEn ?? e.TitleFr ?? "") : (e.TitleFr ?? e.TitleEn ?? "");
            if (!string.IsNullOrEmpty(title)
                && title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            // Toutes les syntaxes
            if (e.Stenos != null)
            {
                for (int i = 0; i < e.Stenos.Length; i++)
                {
                    var steno = e.Stenos[i];
                    if (!string.IsNullOrEmpty(steno)
                        && steno.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            if (e.Tags != null)
            {
                for (int i = 0; i < e.Tags.Length; i++)
                {
                    var tag = e.Tags[i];
                    if (!string.IsNullOrEmpty(tag)
                        && tag.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            return false;
        }
    }
}
