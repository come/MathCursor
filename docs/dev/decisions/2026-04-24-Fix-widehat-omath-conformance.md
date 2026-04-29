# Fix — `\widehat` non converti + tests de conformance de rendu (OMath & WPF)

**Date :** 2026-04-24
**Kind :** Fix
**Température :** molle
**Statut :** acté

## Décision

1. Fix du bug immédiat : `\widehat{ABC}` et `\widetilde{...}` étaient laissés
   tels quels par `LatexToUnicodeMath.Convert` → Word insérait la macro LaTeX
   brute en texte. On les ajoute au dispatcher d'accents pour produire
   `\widehat(ABC)` que Word UnicodeMath rend correctement.

2. Ajout de **deux nouveaux tests de conformance** parcourant tous les gold
   examples des patterns YAML :

   - **`RenderConformanceOmathTests`** (dans `core-csharp/tests`) — vérifie
     qu'après `LatexToUnicodeMath.Convert`, **aucune macro LaTeX** (`\xxx`) ne
     subsiste hors d'une whitelist explicite de commandes que Word UnicodeMath
     consomme nativement.

   - **`RenderConformanceWpfTests`** (dans un nouveau projet
     `adapter-vsto/tests/MathCursor.Adapter.Tests`, cible **net48**, utilisant
     `Xunit.StaFact`) — instancie un `FormulaControl` WPF-Math avec le LaTeX
     passé par `AdaptForWpfMath` et asserte `!HasError`. Si erreur, la popup
     afficherait le fallback texte brut.

## Pourquoi

Un pattern qui produit `\widehat{ABC}` passait les tests `PatternEngineGoldTests`
(rendu LaTeX correct) mais fuyait en texte brut côté Word. Il n'existait
aucun test automatique couvrant le trajet complet `pattern → rendu final`. Toute
macro nouvelle (ou existante mais mal convertie) peut échapper silencieusement
à la détection jusqu'à ce que l'utilisateur la signale.

Les deux pipelines de rendu (popup WPF et Word OMath) sont **indépendants**
et **partiellement lossy** :

| Pipeline | Convertisseur | Mode d'échec |
|---|---|---|
| Popup WPF | `AdaptForWpfMath` → `FormulaControl` | `HasError=true` → fallback Cambria Math lisible mais moche |
| Word OMath | `LatexToUnicodeMath.Convert` → `OMath.BuildUp` | macro non traduite → texte LaTeX brut dans Word |

Sans test systématique sur chaque gold example, ajouter un nouveau pattern
contenant une macro non supportée par l'un des deux pipelines produit un bug
silencieux.

## Conséquences

- `LatexToUnicodeMath.cs` : `widehat` et `widetilde` rejoignent la liste des
  commandes d'accent dispatches. Pas d'entrée dans `AccentMap` — le fallback
  `"\\" + cmd` convient (Word UnicodeMath reconnaît `\widehat(…)` et
  `\widetilde(…)` nativement).

- Toute macro non-whitelist qui apparaît dans une sortie `Convert(...)` fait
  désormais rougir `RenderConformanceOmathTests`. Pour ajouter une macro :
  - soit compléter `ConvertStructural` (cas à arg entre accolades) ;
  - soit ajouter une ligne dans `LiteralReplacements` (cas symbole simple) ;
  - soit ajouter la macro à la whitelist si Word la consomme telle quelle.
  Le test oblige à prendre la décision explicitement.

- Toute macro qui casse WPF-Math fait rougir `RenderConformanceWpfTests` avec
  la liste des erreurs du parser. Pour la résoudre : étendre les substitutions
  de `AdaptForWpfMath` dans `SuggestionPopupWindow.cs`.

- Nouveau projet ajouté à la solution : `MathCursor.Adapter.Tests`
  (SDK-style, `net48`). Dépend de `WpfMath 2.1.0`, `Xunit.StaFact`,
  `MathCursor.Core` (pour charger les gold via `PatternRepository`).

## Validé par l'utilisateur

Diagnostic :
> "c'est juste qu'il n'y a pas de OMath"
> "ca me met le latex en dur"

Demande de test :
> "il faut qu'on verifie bien (tests?) que chaque produit de conversion renvoie
> bien un latex (visible en WPF) et un omath ! si pas de latex en WPF comment
> on etend le truc pour les ajouter ?"

Validation du plan (option A = nouveau projet adapter-tests + StaFact) :
> "A et ok"

## Statut

acté
