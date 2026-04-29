# Feat — Revert popup vers WpfMath + substitutions ciblées

**Date :** 2026-04-24
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** [2026-04-24-Feat-popup-webview-katex.md](2026-04-24-Feat-popup-webview-katex.md)

## Décision

On revient à `WpfMath 2.1` pour le rendu de la popup. WebView2 + KaTeX est
retiré complètement (DLL, bundle JS/CSS/fonts, `KatexWebViewRenderer`,
`ILatexPopupRenderer`). Les manquements de WpfMath sont compensés par des
**substitutions LaTeX → LaTeX** dans `WpfMathAdapter` côté adapter VSTO.

## Pourquoi

Le pivot WebView2 partait d'un bon constat (couverture LaTeX incomplète de
WpfMath). Mais à l'usage le résultat est plus pénalisant que la dette qu'il
voulait résoudre :
- Latence d'init perceptible à la première popup (init `CoreWebView2`).
- Visuel dégradé : `AllowsTransparency=true` rend le fond noir / transparence
  cassée, pas d'effet d'opacité animée propre.
- Poids supplémentaire dans l'installer (bundle KaTeX + bootstrapper WebView2).

L'audit (`tools/audit_latex_macros.py` → `tools/audit-latex-macros.md`) montre
que **seules 11 macros distinctes** sortent du périmètre WpfMath sur les 59
émises par le core. Sur ces 11, **8 ont une substitution LaTeX/Unicode
équivalente trivialement gérable côté adapter** (`\mathbb{R}` → `ℝ`,
`\setminus` → `\backslash`, `\mapsto` → `↦`, `\widehat` → `\hat`,
`\iint`/`\oint` → `∬`/`∮`, `\bmod` ok, etc.).

Reste `\begin{cases}` (1 pattern, `system_fr`) — dégradation visuelle
acceptable en pile via `\stackrel`/`\frac`. Si gênant en pratique, on
re-ouvrira un ADR dédié pour patcher WpfMath sur ce point précis (fork
ciblé sur l'environnement `cases`).

## Conséquences

### Suppressions

- `adapter-vsto/src/MathCursor/UI/KatexWebViewRenderer.cs` — supprimé
- `adapter-vsto/src/MathCursor/UI/ILatexPopupRenderer.cs` — supprimé
- `adapter-vsto/src/MathCursor/UI/katex/` — bundle JS/CSS/fonts supprimé
- `Microsoft.Web.WebView2` package retiré du `MathCursor.csproj`
- Section `[Files]` de `MathCursor.iss` : retire les DLL WebView2 + bundle
- `build.ps1` : retire les références WebView2 de la liste de copie

### Restaurations

- `WpfMath` 2.1 + `XamlMath.Shared` réintroduits dans le csproj
- `SuggestionPopupWindow.RenderMath` : `FormulaControl` (WPF-Math) en place
  du `KatexWebViewRenderer`
- Nouveau `WpfMathAdapter.cs` (côté UI) qui applique les substitutions
  upstream sur le LaTeX avant de le passer à `FormulaControl`

### Substitutions appliquées (`WpfMathAdapter`)

| LaTeX entrée | Substitution sortie | Justification |
|---|---|---|
| `\mathbb{R}` | `ℝ` | Idem N/Z/Q/C ; Word OMath re-fait `\mathbb{R}` au BuildUp |
| `\setminus` | `\backslash` | Visuellement équivalent, supporté natif |
| `\mapsto` | `↦` | Caractère Unicode rendu par font math |
| `\widehat{X}` | `\hat{X}` | Chapeau simple, accent étendu perdu (toléré) |
| `\iint` | `∬` | Caractère Unicode |
| `\oint` | `∮` | Caractère Unicode |
| `\limsup` | `\lim\sup` | Composition de deux macros supportées |
| `\liminf` | `\lim\inf` | Idem |
| `\bmod` | `\,\mathrm{mod}\,` | Texte droit avec espacement |
| `\overline{X}` | `\bar{X}` | Acceptable pour 1 char ; multi-char dégrade |
| `\begin{cases}…\end{cases}` | rendu en pile via `\stackrel` ou `\frac` | Dégradation acceptée pour ce cas isolé |

`\mid` est supporté par WpfMath : pas de substitution.

### Tests

- Aucun test n'est cassé par le revert (le core ne change pas, seul
  l'adapter affichage popup est touché).
- À ajouter (suivi) : un test de smoke sur `WpfMathAdapter.Adapt(...)` qui
  vérifie chaque règle de substitution.

## Validé par l'utilisateur

Constat à l'usage :
> "objectivement c'est foireux.. c'etait mieux avec wpf math.. tu peux
> revenir en arriere ?"

Choix de la stratégie de substitution upstream après audit :
> "C plutot.. mais on pourrait pas ajouter les manquement à la lib wpfmath ?
> ce serait plus propre, etant donné qu'on maitrise toutes les entrées q'on
> va avoir dedans.. les ajouts seront aux fils de l'aeu"

Décision finale après présentation de l'audit (11 macros, dont 8
substituables trivialement, 1 cas isolé) :
> "ok mais tu peux virer webview, on refera si besoin"

## Statut

acté
