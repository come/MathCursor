using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using MathCursor.Core.Lattice;

namespace MathCursor.Core.Patterns.Templates
{
    /// <summary>
    /// Pattern <c>"matrix"</c> : matrice mathématique avec 3 modes de saisie
    /// + désambig auto-layout. Hérite d'<see cref="ArgListPatternBase"/> pour
    /// le head detection + helpers.
    ///
    /// <para><b>3 modes d'entrée utilisateur</b> :</para>
    /// <list type="number">
    ///   <item><b>Auto-detect</b> : <c>mat a b c d</c> → N args espace-séparés.
    ///   Le template énumère tous les couples (cols, rows) tels que
    ///   cols×rows = N, et émet UNE PatternCompletion par layout
    ///   (= désambig retournée à la popup pour choix user). Ex 6 args
    ///   → [1×6, 6×1, 2×3, 3×2].</item>
    ///   <item><b>Explicit séparateurs</b> : <c>mat 1, 2 ; 3, 4</c>.
    ///   Virgule sépare les cols d'une ligne, <c>;</c> sépare les lignes.
    ///   Dimension figée par l'utilisateur → 1 PatternCompletion.</item>
    ///   <item><b>Head paramétré</b> : <c>mat3x4 a b c ...</c> → dimension
    ///   figée dans le head (3 lignes × 4 colonnes = 12 cells attendues).
    ///   Si N args &lt; 12 → carrés `\square` pour les manquants dans
    ///   HintLatex. Plus rapide quand l'user sait sa dim.</item>
    /// </list>
    ///
    /// <para>Heads supportés : <c>mat</c>, <c>Mat</c>, <c>matrice</c> (FR),
    /// <c>matrix</c> (EN). Tous mutés vers <c>mat</c> keyword.</para>
    ///
    /// <para>Notation LaTeX culture-aware via
    /// <see cref="RenderOptions.MatrixDelim"/> : <c>pmatrix</c> (parenthèses,
    /// FR) ou <c>bmatrix</c> (crochets, autres).</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Feat-matrix-pattern</c> (P9f).</para>
    /// </summary>
    public sealed class MatrixTemplate : ArgListPatternBase
    {
        public override string TemplateId => "matrix";

        private static readonly QuantifierVariant[] _variants = new[]
        {
            new QuantifierVariant("mat",     "\\mathrm{mat}", "mat", weight: 100),
            new QuantifierVariant("Mat",     "\\mathrm{mat}", "mat", weight: 100),
            new QuantifierVariant("matrice", "\\mathrm{mat}", "mat", weight: 90),
            new QuantifierVariant("matrix",  "\\mathrm{mat}", "mat", weight: 85),
        };

        protected override IReadOnlyList<QuantifierVariant> Heads => _variants;

        // Regex pour détecter mat<rows>x<cols> dans le head (mode 3)
        private static readonly Regex _dimSuffixRegex = new Regex(
            @"^(\d+)x(\d+)", RegexOptions.Compiled);

        public override PatternMatch? TryMatchHead(PatternScanContext ctx)
        {
            // Override complet : MatrixTemplate accepte le head soit suivi de
            // boundary normale (espace/EOF), soit suivi d'un suffixe
            // <digits>x<digits> tight (mode 3 paramétré). Le base
            // TryMatchHead rejette tout suffix digit/letter, ce qui bloque
            // "mat3x4". Donc on réimplémente.
            if (ctx == null) return null;
            var src = ctx.Source;
            if (string.IsNullOrEmpty(src)) return null;

            for (int i = ctx.StartPos; i < src.Length; i++)
            {
                foreach (var variant in _variants)
                {
                    if (!StartsWithAt(src, i, variant.Head)) continue;
                    int end = i + variant.Head.Length;

                    // Boundary gauche : pas une lettre/digit
                    if (i > 0 && char.IsLetterOrDigit(src[i - 1])) continue;

                    // Lookahead suffix <digits>x<digits> tight
                    int? rows = null, cols = null;
                    int dimEnd = end;
                    if (end < src.Length && char.IsDigit(src[end]))
                    {
                        var trailing = src.Substring(end);
                        var m = _dimSuffixRegex.Match(trailing);
                        if (m.Success)
                        {
                            // Suffix valide ssi pas suivi de lettre (= éviter mat3x4y)
                            int afterDim = end + m.Length;
                            if (afterDim >= src.Length || !char.IsLetter(src[afterDim]))
                            {
                                rows = int.Parse(m.Groups[1].Value);
                                cols = int.Parse(m.Groups[2].Value);
                                dimEnd = afterDim;
                            }
                        }
                    }

                    // Boundary droite : si pas de dim suffix, alors end doit
                    // être EOF ou non-letter/non-digit (cas normal).
                    if (rows == null && dimEnd < src.Length
                        && char.IsLetterOrDigit(src[dimEnd])) continue;
                    // Si on a un dim suffix, dimEnd est déjà placé après les
                    // digits — pas besoin d'autre check.

                    var slots = new Dictionary<string, SlotValue>(rows.HasValue ? 3 : 1)
                    {
                        ["polarity"] = new FilledSlotAtom(variant.Head, i, end),
                    };
                    if (rows.HasValue && cols.HasValue)
                    {
                        slots["explicit_rows"] = new FilledSlotAtom(
                            rows.Value.ToString(), end, dimEnd);
                        slots["explicit_cols"] = new FilledSlotAtom(
                            cols.Value.ToString(), end, dimEnd);
                    }

                    return new PatternMatch(
                        templateId: TemplateId,
                        sourceStart: i,
                        sourceEnd: dimEnd,
                        slots: slots,
                        isComplete: false);
                }
            }
            return null;
        }

        public override IReadOnlyList<PatternCompletion> Expand(
            PatternMatch state, PatternScanContext ctx)
        {
            if (state == null || ctx == null) return System.Array.Empty<PatternCompletion>();
            var variant = FindVariantForState(state, _variants);
            if (variant == null) return System.Array.Empty<PatternCompletion>();

            // Détection mode
            int? explicitRows = TryReadIntSlot(state, "explicit_rows");
            int? explicitCols = TryReadIntSlot(state, "explicit_cols");
            string sourceAfterHead = ctx.Source.Length > state.SourceEnd
                ? ctx.Source.Substring(state.SourceEnd)
                : string.Empty;

            // Mode 2 (explicit sep) : présence de , ou ; dans la source post-head
            bool hasExplicitSep = sourceAfterHead.IndexOfAny(new[] { ',', ';' }) >= 0;

            if (explicitRows.HasValue && explicitCols.HasValue)
            {
                // Mode 3 : head paramétré mat<n>x<m>
                return ExpandExplicitDim(state, variant, ctx, explicitRows.Value, explicitCols.Value);
            }
            if (hasExplicitSep)
            {
                // Mode 2 : séparateurs explicites
                return ExpandExplicitSep(state, variant, ctx);
            }
            // Mode 1 : auto-detect via diviseurs
            return ExpandAutoDetect(state, variant, ctx);
        }

        // ─── Mode 1 : auto-detect ─────────────────────────────────────

        private IReadOnlyList<PatternCompletion> ExpandAutoDetect(
            PatternMatch state, QuantifierVariant variant, PatternScanContext ctx)
        {
            var args = ParseArgs(ctx.Source, state.SourceEnd);
            int n = args.Count;

            int sourceEnd = args.Count > 0
                ? args[args.Count - 1].End
                : state.SourceEnd;

            if (n == 0)
            {
                // mat seul → template placeholder 1×1
                return new[] { BuildCompletion(state, variant,
                    cells: new[] { new[] { (string?)null } },
                    rows: 1, cols: 1, filledCount: 0, sourceEnd, ctx) };
            }

            // Enumerate divisors (rows × cols = n)
            var layouts = EnumerateDivisorLayouts(n);
            var completions = new List<PatternCompletion>(layouts.Count);
            foreach (var (rows, cols) in layouts)
            {
                var cells = ArgsToGrid(args, rows, cols, ctx.Source);
                completions.Add(BuildCompletion(state, variant, cells, rows, cols,
                    filledCount: n, sourceEnd, ctx));
            }
            return completions;
        }

        /// <summary>
        /// Énumère tous les couples (rows, cols) tels que rows × cols = n,
        /// triés par "naturalité" math : carrés/proche-carré en premier,
        /// puis vecteurs (1×n, n×1) en queue.
        /// </summary>
        private static IReadOnlyList<(int rows, int cols)> EnumerateDivisorLayouts(int n)
        {
            var pairs = new List<(int rows, int cols)>();
            for (int r = 1; r <= n; r++)
            {
                if (n % r != 0) continue;
                int c = n / r;
                pairs.Add((r, c));
            }
            // Tri : proximité au carré (= |r-c| ascendant), avec carré exact
            // en premier ; départage par rows ascendant.
            pairs.Sort((a, b) =>
            {
                int da = System.Math.Abs(a.rows - a.cols);
                int db = System.Math.Abs(b.rows - b.cols);
                if (da != db) return da.CompareTo(db);
                return a.rows.CompareTo(b.rows);
            });
            return pairs;
        }

        private static string?[][] ArgsToGrid(
            IReadOnlyList<ArgSpan> args, int rows, int cols, string source)
        {
            var grid = new string?[rows][];
            for (int r = 0; r < rows; r++)
            {
                grid[r] = new string?[cols];
                for (int c = 0; c < cols; c++)
                {
                    int idx = r * cols + c;
                    grid[r][c] = idx < args.Count ? args[idx].Text : null;
                }
            }
            return grid;
        }

        // ─── Mode 2 : séparateurs explicites ──────────────────────────

        private IReadOnlyList<PatternCompletion> ExpandExplicitSep(
            PatternMatch state, QuantifierVariant variant, PatternScanContext ctx)
        {
            string source = ctx.Source;
            int pos = SkipWhitespace(source, state.SourceEnd);
            // Parse : lignes séparées par `;`, chaque ligne = cells séparées par `,`
            var rows = new List<List<string>>();
            var current = new List<string>();
            var cell = new StringBuilder();

            int sourceEnd = pos;
            while (pos < source.Length)
            {
                char c = source[pos];
                if (c == ';')
                {
                    FlushCell(current, cell);
                    if (current.Count > 0)
                    {
                        rows.Add(current);
                        current = new List<string>();
                    }
                    pos++;
                    pos = SkipWhitespace(source, pos);
                    sourceEnd = pos;
                    continue;
                }
                if (c == ',')
                {
                    FlushCell(current, cell);
                    pos++;
                    pos = SkipWhitespace(source, pos);
                    sourceEnd = pos;
                    continue;
                }
                cell.Append(c);
                pos++;
                sourceEnd = pos;
            }
            FlushCell(current, cell);
            if (current.Count > 0) rows.Add(current);

            int rowCount = rows.Count;
            int colCount = 0;
            foreach (var r in rows) if (r.Count > colCount) colCount = r.Count;
            if (rowCount == 0 || colCount == 0)
            {
                return new[] { BuildCompletion(state, variant,
                    cells: new[] { new[] { (string?)null } },
                    rows: 1, cols: 1, filledCount: 0, sourceEnd, ctx) };
            }

            // Pad rows incomplets avec null
            var grid = new string?[rowCount][];
            for (int r = 0; r < rowCount; r++)
            {
                grid[r] = new string?[colCount];
                for (int c = 0; c < colCount; c++)
                    grid[r][c] = c < rows[r].Count ? rows[r][c] : null;
            }

            int filledCount = 0;
            foreach (var row in grid)
                foreach (var v in row)
                    if (v != null) filledCount++;

            return new[] { BuildCompletion(state, variant, grid, rowCount, colCount,
                filledCount, sourceEnd, ctx) };
        }

        private static void FlushCell(List<string> current, StringBuilder cell)
        {
            string text = cell.ToString().Trim();
            if (text.Length > 0) current.Add(text);
            cell.Clear();
        }

        // ─── Mode 3 : head paramétré mat<rows>x<cols> ─────────────────

        private IReadOnlyList<PatternCompletion> ExpandExplicitDim(
            PatternMatch state, QuantifierVariant variant, PatternScanContext ctx,
            int rows, int cols)
        {
            var args = ParseArgs(ctx.Source, state.SourceEnd);
            int total = rows * cols;
            int sourceEnd = args.Count > 0
                ? args[args.Count - 1].End
                : state.SourceEnd;

            // Padding : si args.Count < total, remplir avec null pour montrer
            // les carrés hint pour les cells manquantes.
            var grid = new string?[rows][];
            int filledCount = 0;
            for (int r = 0; r < rows; r++)
            {
                grid[r] = new string?[cols];
                for (int c = 0; c < cols; c++)
                {
                    int idx = r * cols + c;
                    if (idx < args.Count)
                    {
                        grid[r][c] = args[idx].Text;
                        filledCount++;
                    }
                }
            }

            return new[] { BuildCompletion(state, variant, grid, rows, cols,
                filledCount, sourceEnd, ctx) };
        }

        // ─── Build rendu LaTeX + completion ───────────────────────────

        private PatternCompletion BuildCompletion(
            PatternMatch state, QuantifierVariant variant,
            string?[][] cells, int rows, int cols, int filledCount,
            int sourceEnd, PatternScanContext ctx)
        {
            string delim = MathCursor.Core.Lattice.LatexRenderer.GlobalOptions.MatrixDelim;
            string preview = BuildMatrixLatex(cells, rows, cols, delim, hideEmpty: true);
            string hint = BuildMatrixLatex(cells, rows, cols, delim, hideEmpty: false);
            string description = BuildMatrixDescription(rows, cols, filledCount);
            SourceMutation? mutation = BuildMutation(state, variant, cells, rows, cols,
                sourceEnd, ctx);

            int total = rows * cols;
            // Score : 25 base + jusqu'à 75 pour les cells remplies
            int score = total == 0 ? 25
                : 25 + (int)System.Math.Min(75, 75.0 * filledCount / total);
            if (filledCount == total && total > 0) score = 100;

            return new PatternCompletion(
                description: description,
                previewLatex: preview,
                hintLatex: hint,
                mutation: mutation,
                completenessScore: score,
                sourceStart: state.SourceStart,
                sourceEnd: sourceEnd);
        }

        private static string BuildMatrixLatex(
            string?[][] cells, int rows, int cols, string delim, bool hideEmpty)
        {
            var sb = new StringBuilder();
            sb.Append("\\begin{").Append(delim).Append("} ");
            for (int r = 0; r < rows; r++)
            {
                if (r > 0) sb.Append(" \\\\ ");
                for (int c = 0; c < cols; c++)
                {
                    if (c > 0) sb.Append(" & ");
                    string? v = cells[r][c];
                    sb.Append(v ?? (hideEmpty ? "" : "\\square"));
                }
            }
            sb.Append(" \\end{").Append(delim).Append("}");
            return sb.ToString();
        }

        private static string BuildMatrixDescription(int rows, int cols, int filledCount)
        {
            int total = rows * cols;
            return total == 0
                ? $"matrice {rows}×{cols}"
                : $"matrice {rows}×{cols} ({filledCount}/{total} remplie)";
        }

        private SourceMutation? BuildMutation(
            PatternMatch state, QuantifierVariant variant,
            string?[][] cells, int rows, int cols,
            int sourceEnd, PatternScanContext ctx)
        {
            int parentStart = state.SourceStart;
            int parentEnd = sourceEnd > state.SourceEnd ? sourceEnd : state.SourceEnd;
            if (parentStart < 0 || parentEnd > ctx.Source.Length || parentEnd <= parentStart)
                return null;

            // Mutation : tête `mat` + dim explicite + cells espace-séparées
            // (= forme canonique normalisée). Permet une lecture cohérente
            // peu importe le mode d'entrée user.
            var sb = new StringBuilder();
            sb.Append(variant.MutationReplacement);
            sb.Append(rows).Append("x").Append(cols);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    string? v = cells[r][c];
                    if (v != null) sb.Append(" ").Append(v);
                }
            }

            return new SourceMutation(parentStart, parentEnd - parentStart, sb.ToString());
        }

        private static int? TryReadIntSlot(PatternMatch state, string slotName)
        {
            if (!state.Slots.TryGetValue(slotName, out var slot)) return null;
            if (slot is not FilledSlotAtom atom) return null;
            if (int.TryParse(atom.Text, out int v)) return v;
            return null;
        }
    }
}
