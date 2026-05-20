using System;
using System.Collections.Generic;
using System.IO;
using System.IO.IsolatedStorage;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MathCursor.Cheatsheet
{
    /// <summary>
    /// État persistable du panneau Cheatsheet (largeur, ouvert/fermé,
    /// catégories explicitement collapsed).
    /// </summary>
    [DataContract]
    internal sealed class CheatsheetState
    {
        [DataMember(Name = "schema_version")]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "pane_open")]
        public bool PaneOpen { get; set; }

        [DataMember(Name = "pane_width")]
        public int PaneWidth { get; set; }

        /// <summary>
        /// Liste des IDs de catégories explicitement collapsed (sparse :
        /// seulement les overrides, pas les catégories par défaut expanded).
        /// </summary>
        [DataMember(Name = "collapsed_categories")]
        public string[] CollapsedCategories { get; set; }

        public static CheatsheetState Default() => new CheatsheetState
        {
            SchemaVersion = 1,
            PaneOpen = false,
            PaneWidth = 380,
            CollapsedCategories = Array.Empty<string>(),
        };
    }

    /// <summary>
    /// Sérialisation + persistance IsolatedStorage de l'état du pane Cheatsheet.
    /// Logique de (de)sérialisation séparée des I/O pour rester testable.
    /// </summary>
    internal static class CheatsheetPersistence
    {
        private const string FileName = "cheatsheet-state.json";

        /// <summary>Sérialise un state en chaîne JSON.</summary>
        public static string Serialize(CheatsheetState state)
        {
            if (state == null) return null;
            var serializer = new DataContractJsonSerializer(typeof(CheatsheetState));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, state);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        /// <summary>
        /// Désérialise un state depuis une chaîne JSON. Retourne <c>Default()</c>
        /// si null/empty/parse error (jamais null en sortie pour simplifier les
        /// callers).
        /// </summary>
        public static CheatsheetState Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json)) return CheatsheetState.Default();
            try
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                using (var ms = new MemoryStream(bytes))
                {
                    var serializer = new DataContractJsonSerializer(typeof(CheatsheetState));
                    var state = (CheatsheetState)serializer.ReadObject(ms);
                    if (state == null) return CheatsheetState.Default();
                    if (state.CollapsedCategories == null)
                        state.CollapsedCategories = Array.Empty<string>();
                    return state;
                }
            }
            catch
            {
                return CheatsheetState.Default();
            }
        }

        /// <summary>
        /// Construit un state depuis le VM courant + valeurs UI (width / open).
        /// </summary>
        public static CheatsheetState Capture(CheatsheetViewModel vm, bool paneOpen, int paneWidth)
        {
            var collapsed = new List<string>();
            if (vm != null)
            {
                foreach (var kv in vm.GetCollapseState())
                {
                    if (!kv.Value) collapsed.Add(kv.Key);
                }
            }
            return new CheatsheetState
            {
                SchemaVersion = 1,
                PaneOpen = paneOpen,
                PaneWidth = paneWidth,
                CollapsedCategories = collapsed.ToArray(),
            };
        }

        /// <summary>
        /// Applique un state sur le VM (collapse). La largeur et l'open state
        /// sont appliqués côté caller (sur le CustomTaskPane).
        /// </summary>
        public static void ApplyToViewModel(CheatsheetState state, CheatsheetViewModel vm)
        {
            if (state == null || vm == null) return;
            var dict = new Dictionary<string, bool>();
            if (state.CollapsedCategories != null)
            {
                foreach (var id in state.CollapsedCategories)
                {
                    if (!string.IsNullOrEmpty(id)) dict[id] = false;
                }
            }
            vm.SetCollapseState(dict);
        }

        // ─── I/O IsolatedStorage (non testé directement, ciblé manual) ────

        /// <summary>
        /// Charge le state persisté depuis IsolatedStorage. Retourne
        /// <c>Default()</c> si le fichier n'existe pas ou est corrompu.
        /// </summary>
        public static CheatsheetState Load()
        {
            try
            {
                using (var iso = IsolatedStorageFile.GetUserStoreForAssembly())
                {
                    if (!iso.FileExists(FileName)) return CheatsheetState.Default();
                    using (var stream = new IsolatedStorageFileStream(FileName, FileMode.Open, FileAccess.Read, iso))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return Deserialize(reader.ReadToEnd());
                    }
                }
            }
            catch
            {
                return CheatsheetState.Default();
            }
        }

        /// <summary>
        /// Sauvegarde le state dans IsolatedStorage. Échec silencieux (logging
        /// best-effort) — on ne doit jamais crasher l'add-in pour une persistance.
        /// </summary>
        public static void Save(CheatsheetState state)
        {
            if (state == null) return;
            try
            {
                var json = Serialize(state);
                using (var iso = IsolatedStorageFile.GetUserStoreForAssembly())
                using (var stream = new IsolatedStorageFileStream(FileName, FileMode.Create, FileAccess.Write, iso))
                using (var writer = new StreamWriter(stream, Encoding.UTF8))
                {
                    writer.Write(json);
                }
            }
            catch
            {
                // best-effort, jamais propager
            }
        }
    }
}
