# Feat — Rendu popup via WebView2 + KaTeX (remplace WPF-Math)

**Date :** 2026-04-24
**Kind :** Feat
**Température :** forte
**Statut :** retracté
**Superseded by :** [2026-04-24-Feat-popup-revert-wpfmath.md](2026-04-24-Feat-popup-revert-wpfmath.md)

## Décision

La popup d'aperçu abandonne `WpfMath 2.1` / `XamlMath.Shared` et passe sur
**WebView2 + KaTeX** (JS + CSS + fonts embarqués comme ressources dans
l'assembly). Le contrôle `FormulaControl` est remplacé par un nouveau
`LatexKatexRenderer` (un `WebView2` wrappé avec une API
`Render(string latex)`).

## Pourquoi

- WPF-Math 2.1 ne couvre pas notre vocabulaire produit : ni `\mathbb`, ni
  `\begin{cases}`, ni `\iint`/`\oint`, ni `\mapsto`, ni `\widehat`. Chaque
  nouveau pattern produisait une nouvelle substitution de dégradation dans
  `WpfMathAdapter.Adapt`. La popup affichait une version dégradée que Word
  affiche correctement ensuite → incohérence visible pour l'utilisateur.
- WpfMath v3 améliore mais ne couvre toujours pas `cases`/`matrix`/`iint` —
  on re-accumule les substitutions à moyen terme.
- CSharpMath + SkiaSharp couvre tout mais pèse ~15 Mo supplémentaires (natifs
  x86+x64).
- **WebView2 + KaTeX** : couvre 100% de notre vocabulaire, rendu SVG haute
  qualité, ~1–2 Mo ajoutés (bundle KaTeX ~1 Mo, runtime WebView2 déjà présent
  sur Windows 10/11 Edge Evergreen, bootstrapper fallback ~2 Mo pour les
  cibles rares sans).

## Conséquences

- `adapter-vsto/src/MathCursor/MathCursor.csproj` : ajoute
  `Microsoft.Web.WebView2`, supprime `WpfMath`. Bundle KaTeX
  (`katex.min.css`, `katex.min.js`, `fonts/*.woff2`) embarqué comme
  ressources dans l'assembly (ou écrit sur disque temp au runtime).
- `WpfMathAdapter.cs` supprimé. Le core produit du LaTeX standard, KaTeX le
  rend tel quel. Fini les substitutions dégradantes.
- Nouveau `LatexKatexRenderer.cs` : `WebView2` + HTML template qui charge
  KaTeX offline et rend une formule. Expose `Task RenderAsync(string latex)`.
- `SuggestionPopupWindow.RenderMath` : utilise `LatexKatexRenderer` à la
  place de `FormulaControl`. Fallback texte Cambria Math conservé en cas
  d'erreur d'init WebView2.
- `MathCursor.Adapter.Tests.RenderConformanceWpfTests` devient obsolète. Le
  test OMath (côté Word) reste la vraie cible produit. On supprime le test
  WPF (ou on le remplace par un smoke test `WebView2` sur quelques
  formules).
- Installer MSI : détecter l'absence du runtime WebView2 et lancer le
  bootstrapper Microsoft si besoin.

### Contraintes découvertes

- **`AllowsTransparency=true`** sur `Window` (utilisé pour
  `Opacity < 1` de la popup) est historiquement incompatible avec
  `WebView2` (fond noir, pas de rendu). À tester sur la cible. Si bloquant :
  on accepte de perdre l'effet de transparence (la popup reste discrète via
  taille + position + opacité sur le conteneur).
- **Latence de première init** ~100–200 ms à l'ouverture de la première
  popup (init `CoreWebView2`). Acceptable pour un aperçu, à mesurer. On peut
  pré-instancier au démarrage du Ribbon callback pour amortir.

## Alternatives considérées

| Option | Couverture | Poids | Verdict |
|---|---|---|---|
| WpfMath v3 | partielle (+mathbb, pas cases/iint/mapsto) | +1 Mo | rejeté : dette reviendra |
| CSharpMath + SkiaSharp | 100% | +15 Mo natifs | rejeté : trop lourd |
| WebView2 + KaTeX | 100% | +1–2 Mo | **retenu** |
| WebView2 + MathJax | 100% | +5–8 Mo | rejeté : plus lent, overkill |

## Validé par l'utilisateur

Diagnostic de la dégradation actuelle :
> "2# et 1#"
> "on note que le fix est 'un peu' degueu non ?"

Validation de WebView2 après comparaison :
> "sauf A si pas trop lourd dans l'exe final.. c'est le seul truc bien.
> y'a pas une lib de rendu complete ?"
> "ok go webView !"

## Statut

retracté — voir [revert ADR](2026-04-24-Feat-popup-revert-wpfmath.md).
Les pain points pratiques (latence, transparence, poids) l'ont emporté sur
la couverture LaTeX. L'audit a montré que les 11 manquements WpfMath sont
substituables upstream à coût négligeable, sans dette accumulée.
