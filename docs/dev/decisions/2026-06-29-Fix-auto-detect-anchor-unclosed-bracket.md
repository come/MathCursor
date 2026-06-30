# Fix — Auto-détection : ancrage sur parenthèse non fermée (anti-clignotement matrice)

**Date :** 2026-06-29
**Kind :** Fix
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :**
- [2026-06-19-Fix-spancomputer-unclosed-bracket-matrix](2026-06-19-Fix-spancomputer-unclosed-bracket-matrix.md) — la logique d'ancrage réutilisée (chemin manuel)
- [2026-06-10-Feat-ner-auto-detection-debounce](2026-06-10-Feat-ner-auto-detection-debounce.md) — le pipeline auto modifié

## Citation acté

> « ok chantier 1 » — utilisateur, 2026-06-29

(Validation du plan cadré en plan mode : « repli SpanComputer dans le chemin auto ».)

## Contexte

En **auto-détection** (popup pendant la frappe), taper/éditer une matrice
`(a b; c d)` fait **clignoter** la popup : elle tombe sur certains états
partiels puis revient au caractère suivant.

Diagnostic confirmé cette session par le **log runtime**
(`%AppData%\MathCursor\logs\mathcursor.log`) + **exécution réelle** des moteurs :
le **NER fragmente** parfois la matrice sur un état transitoire (surtout pendant
les Backspace) et ne renvoie **que la queue** au caret — ex. zone `[6,9]="c d"`
sur le texte `(a n ;c d`. Ce fragment seul → moteur « aucune lecture » →
`HideAuto` → la popup tombe.

Écarté **avec preuve** (tests jetables rejouant le pipeline, binaires Rust
`mc-engine`/`mc-ner`, console moteur) :
- le **moteur** forest C# **== Rust** produit le candidat matrice-à-carrés
  (`\square`) à **chaque** état partiel (`(a b;` → `pmatrix{a & b \\ □ & □}`) ;
- le **NER** (C# `MathNerDetector` ET Rust `mc-ner`) renvoie la zone **complète**
  sur frappe propre ;
- l'**espace insécable** FR que Word insère avant `;` est encaissée par NER + moteur ;
- le **rendu popup** (`SuggestionPopupWindow.ShowCandidates`) rafraîchit **en place**.

Le seul trou = la fragmentation occasionnelle de la zone NER **dans le chemin auto**.
Le chemin **manuel** (Ctrl+Espace) n'a pas ce bug : il calcule la span via
`SpanComputer`, qui ancre sur la `(`/`[` non fermée englobant le caret et traite
`;`/`,` internes comme **structurels** (ADR 2026-06-19). Le chemin **auto**
(`AutoDetectController.RunDetection`) n'utilise pas `SpanComputer`.

## Décision

Donner au chemin auto le **même filet**, en repli **chirurgical** : dans
`AutoDetectController.RunDetection`, ajouter un **attempt prioritaire** (prepend)
qui réutilise `SpanComputer.ComputeSpanStart/End` **quand `aStart < zone.Start`**
— signe que le NER a largué la tête de la matrice. L'ancré étant tenté **en
premier** et parsable, `TryProposeAuto` affiche directement et `return` : pas de
`HidePopup` intermédiaire (pas de flash).

Garde-fous intrinsèques (no-op hors cas fragmentation) :
- zone NER démarrant déjà à l'ouvrante → `aStart == zone.Start` → pas d'ancré ;
- pas de `(`/`[` non fermé → `SpanComputer` renvoie `aStart ≥ zone.Start` → no-op.

Réutilisation directe de code validé (ADR 2026-06-19). **Zéro** touche moteur /
NER / insertion / OMath / ContentControl.

## Tradeoff & alternatives écartées

- **Remonter la logique dans `ZoneRefiner`** (pour parité LibreOffice/VS Code) :
  écartée — LibreOffice ne montre pas le symptôme (son `mc-ner` renvoie la zone
  complète sur les mêmes textes), et le chemin manuel C# a déjà la logique. Fix
  gardé local et minimal ; remontée possible plus tard si parité demandée.
- **Remplacer le NER par une heuristique légère** : écartée par un **spike
  mesuré** sur les corpus — le NER fait **0 fausse alarme** sur ~420 phrases de
  prose, l'heuristique 18 à 100 % selon le corpus (parenthèses de prose
  « (voir page 12) », polysémie « in »/« limite »/« somme »). Le NER fait le
  travail dur.
- **Corriger la robustesse du NER au contenu** (`b` vs `n` ne devrait rien
  changer) : vrai problème mais **séparé** (corpus v12, chantier 2). Le fix
  adapter masque le symptôme sans dépendre d'un réentraînement.

## Conséquences

- **Code touché** : `adapter-vsto/src/MathCursor/Host/AutoDetectController.cs`
  (`RunDetection`, construction de `attempts`).
- **Tests** : `adapter-vsto/tests/MathCursor.Tests/` — tests purs déterministes
  (récupération : `ComputeSpanStart("(a n ;c d", 9) == 0` + moteur parse ; no-op :
  zone complète → pas d'ancré). Gate `scripts/run-tests.ps1` vert.
- **API publique** : inchangée.
- **Règles MC impactées** : aucune. Reste dans le périmètre détection/popup
  (pas insertion/positions) → la liste de lecture « avant de toucher l'ergo VSTO »
  ne s'applique pas, mais le process plan→ADR→code a été suivi.

## Validation post-fix

1. `scripts/run-tests.ps1` vert.
2. Dans Word : taper `(a b;c d)` puis éditer/backspacer — la popup ne tombe plus
   sur les états partiels.
3. Non-régression : Ctrl+Espace inchangé ; détection non-matricielle inchangée ;
   pas de popup parasite sur prose entre parenthèses (l'ancré ne se déclenche que
   si le NER a **déjà** détecté une zone math dans le groupe).
