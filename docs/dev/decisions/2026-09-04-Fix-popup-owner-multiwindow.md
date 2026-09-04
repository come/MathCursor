# Fix — Popup de suggestion : owner suivi de la fenêtre Word active (multi-fenêtres)

**Date :** 2026-09-04
**Kind :** Fix
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Refactor-phase2-adapter-orchestration-rewrite](2026-06-10-Refactor-phase2-adapter-orchestration-rewrite.md) (naissance de la popup unique + `WS_EX_NOACTIVATE`)

## Citation acté

> « B on fait je pense » — utilisateur, 2026-09-04

(en réponse au périmètre proposé : re-parentage de la popup sur la fenêtre active
à chaque affichage **+ option B** = masquer la popup sur `WindowDeactivate`.)

## Contexte

Bug reproductible signalé par l'utilisateur : deux documents Word ouverts (donc
deux fenêtres top-level, Word étant SDI depuis 2013), on tape une formule dans
l'une puis dans l'autre → **le focus bascule sur la première fenêtre** qui a reçu
la popup.

Cause racine unique, `UI/SuggestionPopupWindow.cs` (constructeur, `SourceInitialized`) :

```csharp
try { helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; }
catch { }
```

Trois faits qui se combinent :

1. **Une seule popup pour toute l'application.** `ThisAddIn` crée un seul
   `ConversionController`, qui garde une seule instance `_popup` réutilisée
   (`EnsurePopup`). Pas de popup par fenêtre.
2. **L'owner est figé une fois pour toutes, sur la mauvaise fenêtre.**
   L'affectation tourne dans `SourceInitialized` (donc une seule fois, souvent au
   `WarmUpPopup` idle du démarrage), et `Process.MainWindowHandle` renvoie **une**
   fenêtre arbitraire (en pratique la première fenêtre Word créée).
3. **Une fenêtre *owned* est z-ordonnée juste au-dessus de son owner.** Afficher
   la popup au-dessus de la fenêtre #2 alors qu'elle est *possédée* par la #1
   réactive la relation de z-order avec la #1 → remontée de la #1 → bascule.

Le **positionnement** de la popup était correct (`ComputeAnchor` lit
`_app.ActiveWindow.GetPoint` / le caret GDI) : seul l'ownership était faux.

## Décision

**Rafraîchir l'owner de la popup vers la fenêtre Word active à chaque affichage**,
au lieu de le figer une fois.

1. **Sortir l'affectation `Owner` de `SourceInitialized`.** Les styles étendus
   `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` y restent posés une fois (la popup ne
   vole jamais le focus, n'apparaît pas dans l'Alt-Tab).
2. **Re-parenter au show.** Dans `ShowCandidates`, avant `Show()` et à chaque
   appel (refresh compris), poser `GWLP_HWNDPARENT` de la popup sur le HWND de la
   fenêtre Word active, via `SetWindowLongPtr` direct (fonctionne popup visible ou
   non ; re-poser le même HWND est un no-op sans flicker).
3. **HWND actif via `GetGUIThreadInfo(0).hwndActive`.** C'est la fenêtre active du
   thread d'entrée = exactement la fenêtre qui possède le caret. Déjà la mécanique
   de `CaretScreenPositionReader`, donc cohérent avec l'existant.
4. **Option B — masquer la popup sur perte de focus Word.** Brancher
   `_conversion?.HidePopup()` sur `Application.WindowDeactivate` (le handler
   existe déjà pour le flush du compteur d'usage). Élimine toute popup fantôme qui
   réapparaîtrait périmée après un aller-retour Alt-Tab, et couvre la bascule
   entre les deux fenêtres Word. Gardé par `IsCommitting` (un commit ne switche
   pas de fenêtre, mais ceinture + bretelles).

## Tradeoff & alternatives écartées

- **Une popup par fenêtre Word** : correct aussi, mais bien plus lourd (cycle de
  vie multi-fenêtres, warm-up ×N, surface de bug accrue) pour un gain nul face au
  simple re-parentage. Écarté.
- **`GetForegroundWindow()`** au lieu de `hwndActive` : marche dans la quasi-
  totalité des cas mais moins robuste sur les cas focus limite, et n'est pas déjà
  dans la codebase. Écarté au profit de `hwndActive`.
- **Ne pas ajouter l'option B (garder la popup au retour de focus)** : plus
  simple, mais laisse une popup potentiellement mal placée réapparaître après
  Alt-Tab (si le doc a scrollé). Contraire à l'esprit « comportement prévisible et
  sans friction ». Écarté.

## Conséquences

- **Code touché** :
  - `adapter-vsto/src/MathCursor/UI/SuggestionPopupWindow.cs` — owner sorti de
    `SourceInitialized`, re-parentage au show (`GetGUIThreadInfo` + `SetWindowLongPtr`
    sur `GWLP_HWNDPARENT`).
  - `adapter-vsto/src/MathCursor/ThisAddIn.cs` — `OnWindowDeactivate` masque aussi
    la popup de suggestion (en plus du flush usage).
- **API publique** : inchangée. Pur comportement fenêtre Win32.
- **Tests** : logique Win32 d'ownership non testable en unitaire (dépend du
  gestionnaire de fenêtres). Validation par observation utilisateur (voir plus bas).
  Le reste des gates (moteur/serialization/adapter) inchangé.
- **Règles MC impactées** : aucune. Ne touche ni OMath, ni ContentControls, ni
  positions Word internes, ni `SetRange`/`Delete`/`BuildUp`.

## Validation post-fix

Manuelle, deux instances Word ouvertes (SDI) :

1. Taper une formule dans Word #1, déclencher la popup, la laisser ouverte.
2. Passer sur Word #2, taper une formule, déclencher la popup.
   **Attendu :** aucune bascule vers Word #1 ; la popup s'affiche sous le caret de
   Word #2.
3. Alt-Tab vers une autre appli puis retour sur Word : la popup ne réapparaît pas
   toute seule périmée (option B).
4. Alt-Tab entre Word #1 et Word #2 : pas de flip, pas de popup fantôme.

Point de vigilance à confirmer au test : `WindowDeactivate` ne doit pas être
déclenché par l'apparition de la popup WPF elle-même (elle est `NOACTIVATE` →
en principe non), sinon on se cacherait soi-même.
