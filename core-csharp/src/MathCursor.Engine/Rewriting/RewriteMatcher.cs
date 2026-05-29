using System.Collections.Generic;
using System.Text;

namespace MathCursor.Engine.Rewriting
{
    /// <summary>Résultat d'un match : règle + range [Start, End) + slots
    /// capturés + flag partiel.</summary>
    public sealed class RewriteMatch
    {
        public RewriteRule Rule { get; }
        public int Start { get; }
        public int End { get; }
        public IReadOnlyDictionary<string, Item> Slots { get; }
        public bool IsPartial { get; }

        public RewriteMatch(RewriteRule rule, int start, int end,
            IReadOnlyDictionary<string, Item> slots, bool isPartial)
        {
            Rule = rule;
            Start = start;
            End = end;
            Slots = slots;
            IsPartial = isPartial;
        }

        public int Span => End - Start;

        /// <summary>Nombre de slots effectivement remplis (= non-\square).
        /// Sert au scoring « max slots pleins ».</summary>
        public int FilledSlots => Slots.Count;
    }

    /// <summary>
    /// Tente de matcher une <see cref="RewriteRule"/> contre la séquence
    /// d'<see cref="Item"/> à partir d'une position. Gère literals, classes,
    /// slots typés (subsumption), glued (= absence de Sep), et match partiel
    /// (= slots manquants si <see cref="RewriteRule.AllowPartial"/>).
    ///
    /// <para>Moteur V2 (2026-05-29).</para>
    /// </summary>
    public static class RewriteMatcher
    {
        public static RewriteMatch? TryMatch(RewriteRule rule, IReadOnlyList<Item> items, int start)
        {
            var slots = new Dictionary<string, Item>();
            bool anyLiteralMatched = false;
            bool anySlotMissing = false;
            int i = start;

            foreach (var elem in rule.Pattern.Elements)
            {
                // Glued : exige absence de Sep AVANT (= avant le skip).
                bool glued = elem.Glued;
                if (glued && i < items.Count && IsWsSep(items[i]))
                    return null;
                if (!glued)
                    while (i < items.Count && IsWsSep(items[i])) i++;

                switch (elem)
                {
                    case Literal lit:
                    {
                        if (i < items.Count && items[i].SourceText == lit.Text)
                        {
                            i++;
                            anyLiteralMatched = true;
                        }
                        else if (lit.Optional)
                        {
                            // skip sans consommer
                        }
                        else if (rule.AllowPartial)
                        {
                            anySlotMissing = true;
                        }
                        else return null;
                        break;
                    }

                    case AnyLiteral any:
                    {
                        bool matched = i < items.Count && Contains(any.Alternatives, items[i].SourceText);
                        if (matched)
                        {
                            i++;
                            anyLiteralMatched = true;
                        }
                        else if (any.Optional)
                        {
                            // skip
                        }
                        else if (rule.AllowPartial)
                        {
                            anySlotMissing = true;
                        }
                        else return null;
                        break;
                    }

                    case Slot slot:
                    {
                        if (i < items.Count && Categories.Subsumes(slot.Category, items[i].Category))
                        {
                            slots[slot.Name] = items[i];
                            i++;
                        }
                        else if (rule.AllowPartial)
                        {
                            anySlotMissing = true; // → \square dans l'emit
                        }
                        else return null;
                        break;
                    }

                    case GridSlot:
                    case RepeatGroup:
                        // Phase 5 : non implémenté en Phase 1.
                        return null;
                }
            }

            // Partial autorisé seulement si ≥ 1 literal a matché (= l'anchor
            // identifie la règle). Évite les partials sur règles sans anchor.
            bool isPartial = anySlotMissing;
            if (isPartial && !anyLiteralMatched) return null;

            return new RewriteMatch(rule, start, i, slots, isPartial);
        }

        /// <summary>
        /// Match d'une règle ANCHOR (= scoping). Diffère de <see cref="TryMatch"/> :
        /// les slots de catégorie composite (Expr/Set/Interval/Matrix) capturent
        /// un <b>chunk délimité par les espaces</b> (= modèle de frappe
        /// <c>sum k 0 n body</c>), récursivement résolu via
        /// <paramref name="resolveChunk"/>. Les slots atomiques (Letter/Number/…)
        /// prennent un seul token. Les literals/classes consomment un token.
        ///
        /// <para>Permet <c>1/sum k 0 n f(k)</c> (= sum capture ses chunks à
        /// droite avant que <c>/</c> n'agisse) et <c>sum k=1 n k</c> (= <c>=?</c>
        /// consomme le <c>=</c> avant rel-eq). Cf. ADR Phase 2.</para>
        /// </summary>
        public static RewriteMatch? TryMatchAnchor(RewriteRule rule, IReadOnlyList<Item> items,
            int start, System.Func<List<Item>, Item> resolveChunk)
        {
            var slots = new Dictionary<string, Item>();
            bool anyLiteralMatched = false;
            bool anySlotMissing = false;
            int i = start;

            // Forme « appel » : KEYWORD(args) ≡ KEYWORD(args,...) ≡ KEYWORD args.
            // Activée si, juste après l'anchor literal, vient un '('. Dans ce
            // mode, les args sont aussi séparables par ',' et la parenthèse
            // finale est consommée. Cf. ADR anchor unifié.
            bool parenMode = false;
            // Mode appel réservé aux anchors mot-clé (= 1er élément literal
            // alphabétique : sum, frac, lim…). Exclut function-call/paren-group
            // dont le pattern contient déjà ses propres parenthèses.
            bool keywordAnchor = rule.Pattern.Elements.Count > 0
                && rule.Pattern.Elements[0] is Literal fl && !fl.Optional
                && fl.Text.Length > 0 && char.IsLetter(fl.Text[0]);

            var elements = rule.Pattern.Elements;
            for (int ei = 0; ei < elements.Count; ei++)
            {
                var elem = elements[ei];
                bool glued = elem.Glued;
                if (glued && i < items.Count && IsWsSep(items[i])) return null;
                if (!glued) while (i < items.Count && IsWsSep(items[i])) i++;

                // Après l'anchor literal (ei==0), détecte le '(' d'appel.
                if (ei == 1 && keywordAnchor && !parenMode && i < items.Count
                    && items[i] is TokenItem pt && pt.Token.Kind == Tokenization.TokenKind.OpenDelim
                    && pt.Token.Text == "(")
                {
                    parenMode = true;
                    i++; // consomme '('
                    while (i < items.Count && IsWsSep(items[i])) i++;
                }

                switch (elem)
                {
                    case Literal lit:
                        if (i < items.Count && items[i].SourceText == lit.Text)
                        { i++; anyLiteralMatched = true; }
                        else if (lit.Optional) { }
                        else if (rule.AllowPartial) anySlotMissing = true;
                        else return null;
                        break;

                    case AnyLiteral any:
                        if (i < items.Count && Contains(any.Alternatives, items[i].SourceText))
                        { i++; anyLiteralMatched = true; }
                        else if (any.Optional) { }
                        else if (rule.AllowPartial) anySlotMissing = true;
                        else return null;
                        break;

                    case Slot slot when IsComposite(slot.Category):
                    {
                        // Capture GREEDY jusqu'au literal SEULEMENT si ce
                        // literal est un délimiteur fermant ()]}) : ex.
                        // paren-group `( {inner} )` → inner prend tout jusqu'à
                        // `)`. Pour un literal séparateur (`;`) ou un slot
                        // suivant → 1 chunk délimité par espace : ainsi
                        // `[ {a} ; {b} ]` capture a=1 chunk (= `[1 2 ; 3 4]`
                        // ne matche PAS un intervalle, laisse la matrice).
                        var nextLit = NextLiteralText(elements, ei);
                        int slotEnd = IsClosingDelim(nextLit)
                            ? CaptureUntilLiteral(items, i, nextLit!)
                            : CaptureChunkEnd(items, i, parenMode);
                        if (slotEnd > i)
                        {
                            var chunk = TrimSeps(items, i, slotEnd);
                            var resolved = resolveChunk(chunk);
                            if (Categories.Subsumes(slot.Category, resolved.Category))
                            { slots[slot.Name] = resolved; i = slotEnd; }
                            else if (rule.AllowPartial) anySlotMissing = true;
                            else return null;
                        }
                        else if (rule.AllowPartial) anySlotMissing = true;
                        else return null;
                        SkipArgSeparator(items, ref i, parenMode);
                        break;
                    }

                    case Slot slot: // atomique (letter/number/function/…)
                        if (i < items.Count && Categories.Subsumes(slot.Category, items[i].Category))
                        { slots[slot.Name] = items[i]; i++; }
                        else if (rule.AllowPartial) anySlotMissing = true;
                        else return null;
                        SkipArgSeparator(items, ref i, parenMode);
                        break;

                    case GridSlot grid:
                    {
                        // Capture jusqu'au délim fermant de niveau 0 (= le `)`
                        // que le prochain Literal de la règle consommera).
                        int gs = i;
                        int depth = 0;
                        while (i < items.Count)
                        {
                            if (items[i] is TokenItem ot && ot.Token.Kind == Tokenization.TokenKind.OpenDelim)
                                depth++;
                            else if (items[i] is TokenItem ct && ct.Token.Kind == Tokenization.TokenKind.CloseDelim)
                            {
                                if (depth == 0) break;
                                depth--;
                            }
                            i++;
                        }
                        var gridItems = new List<Item>(i - gs);
                        for (int k = gs; k < i; k++) gridItems.Add(items[k]);
                        // Une matrice exige ≥1 séparateur de ligne (= ≥2 lignes).
                        // Sinon `(sum k 0 n k)`, `(x+1)` = groupement, pas matrice.
                        if (SplitTopLevel(gridItems, grid.RowSeparator).Count < 2) return null;
                        var gridLatex = RenderGrid(gridItems, grid, resolveChunk);
                        slots[grid.Name] = new RewriteItem(
                            "grid", Category.Matrix, "", gridLatex, false);
                        break;
                    }

                    case ListSlot list:
                    {
                        // Capture jusqu'au prochain literal du pattern (= le
                        // crochet/accolade fermant de la règle, qui peut être
                        // `]`, `[`, `}` selon l'intervalle), puis découpe sur
                        // les séparateurs (,/;), résout chaque élément (espaces
                        // de bord jetés), joint en sortie.
                        // La liste s'arrête au 1er délimiteur de niveau 0
                        // (= `]`, `[`, `}` ou `)` qui borne l'intervalle/
                        // ensemble), PAS au literal précis de la règle : ainsi
                        // `[0;1] U [2;3]` → la 1ère liste stoppe à `]`, et
                        // seule interval-closed (`]`) matche — half-open (`[`)
                        // échoue au lieu d'attraper le `[` du 2e intervalle.
                        int ls = i;
                        int le = CaptureUntilDelim(items, i);
                        // Un élément de liste (= borne d'intervalle / élément
                        // d'ensemble) est une VALEUR UNIQUE : pas d'espace
                        // interne de niveau 0. Si un élément en a (= `1 2`),
                        // c'est une structure 2D (matrice) → on rejette pour
                        // laisser la règle grid gagner.
                        if (HasMultiCellElement(items, ls, le, list)) return null;
                        var listLatex = RenderList(items, ls, le, list, resolveChunk);
                        slots[list.Name] = new RewriteItem(
                            "list", Category.Set, "", listLatex, false);
                        i = le;
                        break;
                    }

                    case RepeatGroup:
                        return null; // non implémenté
                }
            }

            // Forme appel : consomme la parenthèse fermante finale.
            if (parenMode)
            {
                while (i < items.Count && IsWsSep(items[i])) i++;
                if (i < items.Count && items[i] is TokenItem ct
                    && ct.Token.Kind == Tokenization.TokenKind.CloseDelim
                    && ct.Token.Text == ")")
                    i++;
                else if (!rule.AllowPartial) return null;
            }

            bool isPartial = anySlotMissing;
            if (isPartial && !anyLiteralMatched) return null;
            return new RewriteMatch(rule, start, i, slots, isPartial);
        }

        /// <summary>En parenMode, skip un ',' séparateur d'argument (+ seps).</summary>
        private static void SkipArgSeparator(IReadOnlyList<Item> items, ref int i, bool parenMode)
        {
            if (!parenMode) return;
            int j = i;
            while (j < items.Count && IsWsSep(items[j])) j++;
            if (j < items.Count && items[j].SourceText == ",")
            {
                i = j + 1;
                while (i < items.Count && IsWsSep(items[i])) i++;
            }
        }

        /// <summary>True si un élément de la liste (= entre 2 séparateurs)
        /// contient un espace de niveau 0 (= `1 2` = 2 cellules → matrice,
        /// pas un intervalle/ensemble).</summary>
        private static bool HasMultiCellElement(IReadOnlyList<Item> items, int start, int end, ListSlot list)
        {
            int depth = 0;
            bool sawContent = false, sawSpaceAfterContent = false;
            for (int k = start; k < end; k++)
            {
                var it = items[k];
                if (it is TokenItem o && o.Token.Kind == Tokenization.TokenKind.OpenDelim) { depth++; sawContent = true; continue; }
                if (it is TokenItem c && c.Token.Kind == Tokenization.TokenKind.CloseDelim) { depth--; continue; }
                if (depth != 0) continue;
                if (Contains(list.Separators, it.SourceText))
                { sawContent = false; sawSpaceAfterContent = false; continue; } // nouvel élément
                if (IsWsSep(it)) { if (sawContent) sawSpaceAfterContent = true; continue; }
                // token de contenu
                if (sawSpaceAfterContent) return true; // contenu, espace, contenu = multi-cellule
                sawContent = true;
            }
            return false;
        }

        /// <summary>Rend une liste 1D : découpe items[start..end) sur les
        /// séparateurs (niveau 0), trim chaque élément, résout, joint.
        /// Même mécanique de split propre que le grid (= espaces jetés).</summary>
        private static string RenderList(IReadOnlyList<Item> items, int start, int end,
            ListSlot list, System.Func<List<Item>, Item> resolveChunk)
        {
            var pieces = new List<List<Item>>();
            var current = new List<Item>();
            int depth = 0;
            for (int k = start; k < end; k++)
            {
                var it = items[k];
                if (it is TokenItem o && o.Token.Kind == Tokenization.TokenKind.OpenDelim) depth++;
                else if (it is TokenItem c && c.Token.Kind == Tokenization.TokenKind.CloseDelim) depth--;
                if (depth == 0 && Contains(list.Separators, it.SourceText))
                { pieces.Add(current); current = new List<Item>(); continue; }
                current.Add(it);
            }
            pieces.Add(current);

            var rendered = new List<string>(pieces.Count);
            foreach (var p in pieces)
            {
                var trimmed = TrimSeps(p, 0, p.Count);
                if (trimmed.Count == 0) continue;
                rendered.Add(resolveChunk(trimmed).Latex);
            }
            return string.Join(list.OutputSeparator, rendered);
        }

        /// <summary>Rend une grille : découpe par séparateur de ligne
        /// (niveau 0), puis chaque ligne par séparateur de cellule, résout
        /// chaque cellule, joint <c>cell &amp; cell \\ row \\ row</c>.</summary>
        private static string RenderGrid(IReadOnlyList<Item> items, GridSlot grid,
            System.Func<List<Item>, Item> resolveChunk)
        {
            var rows = SplitTopLevel(items, grid.RowSeparator);
            var renderedRows = new List<string>(rows.Count);
            foreach (var row in rows)
            {
                var cells = SplitCells(row, grid.CellSeparator);
                var renderedCells = new List<string>(cells.Count);
                foreach (var cell in cells)
                {
                    if (cell.Count == 0) continue;
                    renderedCells.Add(resolveChunk(cell).Latex);
                }
                renderedRows.Add(string.Join(" & ", renderedCells));
            }
            return string.Join(@" \\ ", renderedRows);
        }

        /// <summary>Découpe par séparateur literal au niveau 0 (= délim
        /// équilibrés). Sépare aussi sur les Sep si <paramref name="sep"/>
        /// est " ".</summary>
        private static List<List<Item>> SplitTopLevel(IReadOnlyList<Item> items, string sep)
        {
            var result = new List<List<Item>>();
            var current = new List<Item>();
            int depth = 0;
            foreach (var it in items)
            {
                if (it is TokenItem ot && ot.Token.Kind == Tokenization.TokenKind.OpenDelim) depth++;
                else if (it is TokenItem ct && ct.Token.Kind == Tokenization.TokenKind.CloseDelim) depth--;
                if (depth == 0 && it.SourceText == sep)
                {
                    result.Add(current);
                    current = new List<Item>();
                    continue;
                }
                current.Add(it);
            }
            result.Add(current);
            return result;
        }

        /// <summary>Découpe une ligne en cellules sur le séparateur de cellule
        /// (= espace par défaut → split sur les Sep ; ou virgule).</summary>
        private static List<List<Item>> SplitCells(IReadOnlyList<Item> row, string cellSep)
        {
            var result = new List<List<Item>>();
            var current = new List<Item>();
            int depth = 0;
            bool spaceSep = cellSep == " ";
            foreach (var it in row)
            {
                if (it is TokenItem ot && ot.Token.Kind == Tokenization.TokenKind.OpenDelim) depth++;
                else if (it is TokenItem ct && ct.Token.Kind == Tokenization.TokenKind.CloseDelim) depth--;
                bool isBoundary = depth == 0 && (
                    (spaceSep && IsWsSep(it)) || (!spaceSep && it.SourceText == cellSep));
                if (isBoundary)
                {
                    if (current.Count > 0) { result.Add(current); current = new List<Item>(); }
                    continue;
                }
                current.Add(it);
            }
            if (current.Count > 0) result.Add(current);
            return result;
        }

        private static bool IsClosingDelim(string? text)
            => text == ")" || text == "]" || text == "}";

        /// <summary>Fin (exclusive) de la liste : 1er crochet/accolade
        /// (<c>[ ] { }</c>) de niveau 0 — la borne de l'intervalle/ensemble,
        /// quel qu'il soit (= French half-open <c>[a;b[</c> n'est pas
        /// bracket-balancé). Les parenthèses <c>( )</c> sont des groupes
        /// internes (= <c>f(x)</c>) et incrémentent la profondeur : elles
        /// n'arrêtent pas la liste tant qu'elles sont équilibrées.</summary>
        private static int CaptureUntilDelim(IReadOnlyList<Item> items, int start)
        {
            int paren = 0;
            int i = start;
            while (i < items.Count)
            {
                if (items[i] is TokenItem t)
                {
                    var txt = t.Token.Text;
                    if (txt == "(") paren++;
                    else if (txt == ")") { if (paren == 0) break; paren--; }
                    else if (paren == 0 &&
                             (txt == "[" || txt == "]" || txt == "{" || txt == "}"))
                        break;
                }
                i++;
            }
            return i;
        }

        /// <summary>Extrait items[start..end) en retirant les séparateurs
        /// blancs de bord (= évite que `0 ` capture le space avant `;`).</summary>
        private static List<Item> TrimSeps(IReadOnlyList<Item> items, int start, int end)
        {
            int a = start, b = end;
            while (a < b && IsWsSep(items[a])) a++;
            while (b > a && IsWsSep(items[b - 1])) b--;
            var list = new List<Item>(b - a);
            for (int k = a; k < b; k++) list.Add(items[k]);
            return list;
        }

        /// <summary>Texte du prochain Literal (non-optionnel) dans le pattern
        /// après l'index <paramref name="ei"/>, ou null si le prochain élément
        /// significatif n'est pas un Literal.</summary>
        private static string? NextLiteralText(IReadOnlyList<PatternElement> elements, int ei)
        {
            if (ei + 1 < elements.Count && elements[ei + 1] is Literal lit && !lit.Optional)
                return lit.Text;
            return null;
        }

        /// <summary>Capture jusqu'au literal <paramref name="stop"/> (exclu),
        /// délimiteurs équilibrés (= un `)` interne n'arrête pas si déséquilibré).</summary>
        private static int CaptureUntilLiteral(IReadOnlyList<Item> items, int start, string stop)
        {
            int depth = 0;
            int i = start;
            while (i < items.Count)
            {
                var it = items[i];
                // Stop d'abord au niveau 0 (= avant la logique de profondeur,
                // pour pouvoir s'arrêter sur un OpenDelim comme `[` qui ferme
                // un intervalle ouvert `]0;1[`).
                if (depth == 0 && it.SourceText == stop) break;
                if (it is TokenItem t && t.Token.Kind == Tokenization.TokenKind.OpenDelim) depth++;
                else if (it is TokenItem t2 && t2.Token.Kind == Tokenization.TokenKind.CloseDelim
                         && depth > 0) depth--;
                i++;
            }
            return i;
        }

        /// <summary>Fin (exclusive) du chunk démarrant à <paramref name="start"/> :
        /// run de tokens non-séparateurs, en respectant l'équilibre des
        /// délimiteurs (= <c>f(k)</c>, <c>(x+1)</c> = un seul chunk). S'arrête
        /// au 1er séparateur de niveau 0.</summary>
        private static int CaptureChunkEnd(IReadOnlyList<Item> items, int start, bool parenMode = false)
        {
            int depth = 0;
            int i = start;
            while (i < items.Count)
            {
                var it = items[i];
                if (it is TokenItem t && t.Token.Kind == Tokenization.TokenKind.OpenDelim) depth++;
                else if (it is TokenItem t2 && t2.Token.Kind == Tokenization.TokenKind.CloseDelim)
                {
                    if (depth == 0) break; // ) fermant un scope parent
                    depth--;
                }
                else if (depth == 0 && IsWsSep(it)) break; // sep top-level = fin de chunk
                else if (parenMode && depth == 0 && it.SourceText == ",") break; // ',' = fin d'arg
                i++;
            }
            return i;
        }

        /// <summary>Catégories qui capturent un chunk (= valeurs composites)
        /// vs atomiques (= 1 token).</summary>
        private static bool IsComposite(Category c)
            => c == Category.Expr || c == Category.Set
            || c == Category.Interval || c == Category.Matrix;

        /// <summary>Applique le template emit : <c>$name</c> → Latex du slot,
        /// slot manquant → <c>\square</c>.</summary>
        public static string ApplyTemplate(string template, IReadOnlyDictionary<string, Item> slots)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            var sb = new StringBuilder(template.Length * 2);
            int i = 0;
            while (i < template.Length)
            {
                char c = template[i];
                if (c == '$' && i + 1 < template.Length && IsNameStart(template[i + 1]))
                {
                    int j = i + 1;
                    while (j < template.Length && IsNameCont(template[j])) j++;
                    var name = template.Substring(i + 1, j - (i + 1));
                    sb.Append(slots.TryGetValue(name, out var item) ? item.Latex : @"\square");
                    i = j;
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static bool IsWsSep(Item item)
            => item is TokenItem t && t.Category == Category.Sep && t.Token.Text == " ";

        private static bool Contains(IReadOnlyList<string> list, string value)
        {
            for (int k = 0; k < list.Count; k++)
                if (list[k] == value) return true;
            return false;
        }

        // Noms alphanumériques only (= PAS '_', sinon "$a_$b" lirait le nom
        // "a_" au lieu de "$a" + literal "_" + "$b").
        private static bool IsNameStart(char c) => char.IsLetter(c);
        private static bool IsNameCont(char c) => char.IsLetterOrDigit(c);
    }
}
