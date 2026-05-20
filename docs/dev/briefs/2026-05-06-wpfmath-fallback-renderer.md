# 2026-05-06 — Brief : WpfMath Fallback Renderer pour les popups

## Contexte

La librairie **WpfMath 2.1** utilisée pour le rendu des formules LaTeX dans les
popups WPF (`SuggestionPopupWindow`, `EditModePopupWindow`) ne supporte pas
certaines macros LaTeX, notamment :

- Ensembles : `\mathbb{R}`, `\mathbb{N}`, `\mathbb{Z}`, `\mathbb{Q}`, `\mathbb{C}`
- Symboles : `\mapsto`, `\iint`, `\iiint`

**Problème actuel** : le rendu dans les popups utilise des substitutions ad-hoc
dans [`WpfMathAdapter.cs`](../../../adapter-vsto/src/MathCursor/UI/WpfMathAdapter.cs)
(ex: `\mathbb{R}` → `|R`), ce qui donne un résultat visuel dégradé (pas de
double barre pour ℝ).

**Contrainte** :
- **Aucune modification** dans `core-csharp` ou `host-contract-csharp`.
- **Seule la popup WPF** (couche 3 adapter) est concernée.
- Le LaTeX émis vers Word OMath (chemin `SuggestionService.InsertOMathAt`)
  reste **intact** — Word's BuildUp gère nativement `\mathbb`, `\widehat`, etc.

## Objectif

Améliorer le rendu visuel des popups pour les ~5 macros vraiment cassées dans
WpfMath, **avec un coût minimal**.

L'audit complet des macros est dans
[`tools/audit-latex-macros.md`](../../../tools/audit-latex-macros.md)
(46 `\mathbb`, 4 `\mapsto`, 2 `\iint`, etc.).

---

## Étape 1 — Investigation Unicode (FAIT, 2026-05-06)

**Méthodologie** : test xUnit en thread STA qui instancie un `FormulaControl`
WpfMath et render dans un PNG (`adapter-vsto/tests/MathCursor.Tests/UI/`,
`StaRunner.cs` + `WpfMathRenderProbeTests.cs`). PNG sortis dans
`<repo>/.render-probes/` pour inspection visuelle. Confirmation visuelle
manuelle obligatoire — les dimensions seules signalent un échec mais pas un
succès.

### Signal automatisable découvert

WpfMath rend un **glyphe placeholder "."** quand il ne peut pas rendre une
macro/un caractère. Le PNG résultant a une signature reconnaissable :

```
50×50 px, 207 bytes  →  rendu cassé (point unique au centre du Border)
```

Toute autre dimension/taille = WpfMath a rendu **quelque chose**. Reste à
valider visuellement, mais ce n'est plus un placeholder. Cette signature
permet d'automatiser les tests de non-régression (cf. Étape 3).

### Résultats `FormulaControl` (WpfMath, 20 cas)

| Macro | LaTeX | Résultat |
|---|---|---|
| Témoin supportée | `\frac{1}{2}` | ✅ 61×99 |
| Ensemble brut | `\mathbb{R}` | ❌ 50×50 (placeholder) |
| Substitution `\text{}` Unicode | `\text{ℝ}` | ❌ 50×50 |
| Caractère Unicode brut | `ℝ` | ❌ 50×50 |
| **Cas mixte** | `\mathbb{R} \cap \mathbb{N}` | ❌ 50×50 — un `\mathbb` cassé fait planter le rendu **entier** |
| `\setminus` | `A \setminus B` | ✅ 108×74 |
| `\widehat{ABC}` (multi-char) | | ✅ 104×76 |
| `\overline{x+y}` (multi-char) | | ✅ 105×72 |
| `\mapsto` | `f : x \mapsto x^2` | ❌ 50×50 |
| `\iint` | | ❌ 50×50 |
| `\oint` | | ✅ 72×104 |
| `\limsup` / `\liminf` | `\limsup_{n\to\infty} a_n` | ✅ 149×86 / 141×82 |

### Résultats `TextBlock` brut (5 cas, bypass WpfMath)

Test pour décider si le problème vient de la police ou de WpfMath :

| Texte | Police | Résultat |
|---|---|---|
| `ℝ` | Cambria Math | ✅ 75×91 |
| `ℝ ∩ ℕ ∖ ℤ` | Cambria Math | ✅ 199×91 |
| `f : x ↦ x²` | Cambria Math | ✅ 181×91 |
| `ℝ` | Segoe UI Symbol | ✅ 72×96 |
| `ℝ` | Segoe UI | ✅ 72×96 (font-fallback automatique de WPF) |

### Conclusions de l'investigation

1. **Le problème est WpfMath, pas la police.** Cambria Math sait afficher
   ℝ ℕ ℤ ↦ ∖ etc. dès qu'on bypasse WpfMath.
2. **WpfMath ignore `FontFamily` pour le mode math** (TFM TexShared interne).
   Même `\text{ℝ}` rend "." → le mode texte de WpfMath ne respecte pas non
   plus la propriété `FontFamily` pour les caractères hors ASCII/TeX.
3. **`\mathbb` cassé fait planter la formule entière**, pas juste le glyphe
   local. Pas de "WpfMath rend ce qu'il peut, fallback pour le reste à
   l'intérieur d'une même formule" — c'est tout-ou-rien.
4. **L'audit `tools/audit-latex-macros.md` est partiellement obsolète** :
   `\setminus`, `\widehat`, `\overline`, `\oint`, `\limsup`, `\liminf` sont
   en fait rendus correctement par WpfMath. À mettre à jour après ce brief.

→ **Branche A** (modif minimale `WpfMathAdapter.cs` + Cambria Math) :
   **invalidée**. WpfMath ignore la `FontFamily`.

→ **Branche B** (mixed rendering) : **retenue**. Voir ci-dessous.

---

## Étape 2 — Branche B simplifiée : Mixed rendering

Plus léger que les 3 classes initiales. Le principe :

1. **Pré-tokeniser** le LaTeX en segments :
   - Segments **WpfMath-safe** → rendus par `FormulaControl`
   - Segments **Unicode** (les ~5 macros cassées) → rendus par `TextBlock`
2. **Assembler** les UIElement résultants dans un `StackPanel
   Orientation=Horizontal` (avec `VerticalAlignment=Center`).

### Macros à substituer Unicode (liste réduite)

D'après l'investigation, seules ces macros nécessitent un fallback `TextBlock` :

| Macro | Substitut Unicode | Code |
|---|---|---|
| `\mathbb{R}` | ℝ | U+211D |
| `\mathbb{N}` | ℕ | U+2115 |
| `\mathbb{Z}` | ℤ | U+2124 |
| `\mathbb{Q}` | ℚ | U+211A |
| `\mathbb{C}` | ℂ | U+2102 |
| `\mathbb{P}` | ℙ | U+2119 |
| `\mapsto` | ↦ | U+21A6 |
| `\iint` | ∬ | U+222C |
| `\iiint` | ∭ | U+2A0C |

`\setminus`, `\widehat{...}`, `\overline{...}`, `\oint`, `\limsup`,
`\liminf` **restent à WpfMath** — ils rendent correctement.

### Architecture

| Fichier | Action | Description |
|---|---|---|
| `adapter-vsto/src/MathCursor/UI/MixedLatexRenderer.cs` | **Créer** | Tokenizer + renderer dans un seul fichier. Point d'entrée `Render(string latex) → UIElement`. ~150 lignes. |
| `adapter-vsto/src/MathCursor/UI/WpfMathAdapter.cs` | **Modifier** | Retirer la substitution `\mathbb{X} → \|X` (devient inutile). Garder cases / matrices / autres substitutions. |
| `adapter-vsto/src/MathCursor/UI/SuggestionPopupWindow.cs` | **Modifier** | Remplacer `new FormulaControl { Formula = WpfMathAdapter.Adapt(...) }` par `MixedLatexRenderer.Render(...)`. |
| `adapter-vsto/src/MathCursor/UI/EditModePopupWindow.cs` | **Modifier** | Idem si applicable. |
| `adapter-vsto/tests/MathCursor.Tests/UI/MixedLatexRendererTests.cs` | **Créer** | Tests dimensionnels (cf. Étape 3) + tests de tokenization. |

### Tokenizer

Logique simple, regex-driven :

```csharp
private static readonly (Regex pattern, string unicodeReplacement)[] UnicodeMacros = new[]
{
    (new Regex(@"\\mathbb\{R\}", RegexOptions.Compiled), "ℝ"),
    (new Regex(@"\\mathbb\{N\}", RegexOptions.Compiled), "ℕ"),
    // ... etc
    (new Regex(@"\\mapsto",      RegexOptions.Compiled), "↦"),
    (new Regex(@"\\iint",        RegexOptions.Compiled), "∬"),
    (new Regex(@"\\iiint",       RegexOptions.Compiled), "∭"),
};
```

Algorithme : scanner le LaTeX gauche-à-droite, trouver les matches, émettre
des segments alternés `WpfMath` / `Unicode`. Les segments `WpfMath` adjacents
peuvent rester ensemble pour minimiser le nombre de `FormulaControl`.

**Limite V1 connue** : tokenization au top-level seulement. Si une macro
cassée apparaît dans un sous-contexte (`\frac{\mathbb{R}}{2}`), on garde la
substitution dégradée actuelle (`\frac{|R}{2}` via `WpfMathAdapter`). On
itérera si besoin (rare en pratique d'après le corpus).

### Renderer

```csharp
public static UIElement Render(string latex)
{
    var segments = Tokenize(latex ?? "");
    if (segments.Count == 1 && segments[0].Type == SegmentType.WpfMath)
    {
        // Fast path : pas de \mathbb / \mapsto / \iint, on garde le
        // FormulaControl direct comme avant.
        return MakeFormulaControl(WpfMathAdapter.Adapt(segments[0].Content));
    }
    var panel = new StackPanel
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center,
    };
    foreach (var seg in segments)
    {
        UIElement child = seg.Type == SegmentType.WpfMath
            ? MakeFormulaControl(WpfMathAdapter.Adapt(seg.Content))
            : MakeUnicodeTextBlock(seg.Content);
        if (child is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(child);
    }
    return panel;
}
```

⚠ **Risque baseline alignment** : le `FormulaControl` et le `TextBlock` ont
des baselines différentes. `VerticalAlignment.Center` donne un alignement
"acceptable" mais pas parfait (le ℝ peut paraître plus haut/bas que les
symboles WpfMath autour). À valider visuellement après implémentation. Si
gênant : ajuster via `Padding` ou `Margin.Top` empirique sur le TextBlock.

---

## Étape 3 — Tests automatisés dimensionnels

La signature placeholder **50×50 / 207 bytes** permet de coder un test
**discriminant** sans inspection visuelle. Pour chaque cas attendu OK,
asserter :

```csharp
Assert.True(width > 60 || height > 60, $"Rendu placeholder détecté pour {label}");
Assert.True(bytes > 250, $"PNG trop petit ({bytes}b), suspect placeholder pour {label}");
```

### Tests à ajouter dans `MixedLatexRendererTests.cs`

1. **Tests de tokenization** (rapides, pas de WPF) :
   - `\mathbb{R}` → 1 segment Unicode "ℝ"
   - `\mathbb{R} \cap \mathbb{N}` → 3 segments [Unicode "ℝ", WpfMath " \cap ", Unicode "ℕ"]
   - `\frac{1}{2}` → 1 segment WpfMath (fast path)
   - `f : x \mapsto x^2` → 3 segments [WpfMath "f : x ", Unicode "↦", WpfMath " x^2"]

2. **Tests de rendu dimensionnel** (STA, render PNG) :
   - `\mathbb{R}` → width > 60, bytes > 500 (régression placeholder = fail)
   - `\mathbb{R} \cap \mathbb{N}` → width > 100 (vrai contenu)
   - `\frac{1}{2}` → ne doit pas régresser (largeur ~ baseline frac actuel)

3. **Promouvoir certains probes en vrais tests** : remplacer les
   `_output.WriteLine` par des `Assert` strictes sur les cas connus, et
   sortir le résultat dans `bin/render-tests/` (séparé de `.render-probes/`
   qui reste manuel).

---

## Étape 4 — Mise à jour de l'audit

Mettre à jour `tools/audit-latex-macros.md` :
- Retirer `\setminus`, `\widehat`, `\overline`, `\oint`, `\limsup`,
  `\liminf` de la liste "manquantes" (ils rendent OK).
- Garder `\mathbb`, `\mapsto`, `\iint`, `\iiint`.
- Mentionner la méthodo de validation (probe + dimensions).

---

## Critères de succès

- [ ] `\mathbb{R}`, `\mathbb{N}`, `\mathbb{Z}`, `\mathbb{Q}`, `\mathbb{C}`,
      `\mathbb{P}` s'affichent avec **double barre nette** dans les popups.
- [ ] `\mapsto`, `\iint`, `\iiint` s'affichent avec leur glyphe Unicode propre.
- [ ] **Aucune régression** sur cases / pmatrix / bmatrix / vmatrix
      (tests xUnit verts + validation Word manuelle).
- [ ] Aucune régression sur les macros déjà supportées (`\frac`, `\sum`,
      `\int`, `\sqrt`, `\setminus`, `\widehat`, `\overline`, etc.).
- [ ] Alignement vertical TextBlock / FormulaControl visuellement acceptable
      (validation manuelle screenshots).
- [ ] Tests dimensionnels automatisés en place pour détecter régression
      placeholder.

---

## Décisions ouvertes

1. **Baseline alignment** : si `VerticalAlignment.Center` ne suffit pas,
   ajuster via `Margin.Top` empirique (-2 à -4 px sur le TextBlock) ou via
   `BaselineOffset`. À régler après proto.
2. **Tokenization nested** : V1 limitée au top-level. À reconsidérer si on
   voit des `\frac{\mathbb{R}}{2}` dans le corpus en pratique.
3. **Suppression de `\mathbb{X} → |X` dans `WpfMathAdapter`** : à faire dans
   le même PR pour éviter la double-substitution (le `\mathbb{R}` serait
   d'abord transformé en `|R` par l'adapter, puis n'aurait plus rien à
   tokenizer côté `MixedLatexRenderer`).

---

## Roadmap

| Étape | Tâche | Statut |
|---|---|---|
| 1 | Investigation Cambria Math + `\text{ℝ}` (probe PNG) | ✅ Fait 2026-05-06 |
| 2 | Création `MixedLatexRenderer.cs` (tokenizer + renderer) | À faire |
| 3 | Modif `WpfMathAdapter.cs` (retirer subst `\mathbb`) | À faire |
| 4 | Modif popups (`SuggestionPopupWindow`, `EditModePopupWindow`) | À faire |
| 5 | Tests dimensionnels + tokenization | À faire |
| 6 | MAJ `tools/audit-latex-macros.md` | À faire |
| 7 | Validation visuelle utilisateur (capture popup ℝ ∩ ℕ) | À faire |

---

## Références

- WpfMath GitHub : https://github.com/ForNeVeR/wpfmath
- Unicode Mathematical Double-Struck : https://www.compart.com/en/unicode/block/U+2100
- Probes Étape 1 : `adapter-vsto/tests/MathCursor.Tests/UI/WpfMathRenderProbeTests.cs`
- Sortie probes Étape 1 : `<repo>/.render-probes/` (gitignored)
- Fichier actuel : [`adapter-vsto/src/MathCursor/UI/WpfMathAdapter.cs`](../../../adapter-vsto/src/MathCursor/UI/WpfMathAdapter.cs)
- Audit des macros LaTeX : [`tools/audit-latex-macros.md`](../../../tools/audit-latex-macros.md)
- ADR origine : [`docs/dev/decisions/2026-04-24-Feat-popup-revert-wpfmath.md`](../decisions/2026-04-24-Feat-popup-revert-wpfmath.md)
