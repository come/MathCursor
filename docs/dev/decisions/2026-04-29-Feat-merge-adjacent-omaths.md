# Feat — Fusionner OMath adjacents lors d'une conversion

**Date :** 2026-04-29
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

Quand une conversion se déclenche (Enter sur popup, auto-commit), si la zone
à insérer touche un OMath adjacent (à gauche ou à droite, séparé uniquement
par espaces/tabs), **fusionner** les sources et insérer un seul OMath qui
englobe l'ensemble.

| Cas | Comportement |
|-----|--------------|
| OMath gauche + zone collée (0 char) | Fusion |
| OMath gauche + 1 espace + zone | Fusion |
| OMath gauche + 2+ espaces + zone | **Pas de fusion** |
| OMath gauche + tab + zone | **Pas de fusion** |
| Saut de ligne entre | **Pas de fusion** (séparation intentionnelle) |
| Texte non-blanc entre (`donc`, ...) | **Pas de fusion** |
| OMath sans handle MathCursor | **Pas de fusion** (source non récupérable) |

**Spec stricte (révision utilisateur 29-04)** : 0 ou 1 espace UNIQUEMENT.
Pas de tab, pas d'espaces multiples. L'utilisateur veut un comportement
prévisible : si la séparation visuelle est subtile (1 char d'espace), c'est
qu'il a probablement voulu coller. Au-delà, il a probablement voulu séparer.

Les anciens handles des OMaths fusionnés sont supprimés du store
(`IEquationStore.RemoveAsync`).

## Pourquoi

Avant ce ADR, taper une suite de formule à côté d'un OMath existant créait
un second OMath séparé. Visuellement collés mais traités séparément par Word
(copier-coller, édition mode revert, etc.). Le merge restitue une seule unité
math cohérente.

### Pourquoi seulement les espaces/tabs comme jointure

- Saut de ligne = séparation explicite voulue par l'utilisateur (paragraphe
  différent). Ne pas franchir.
- Texte (mot, ponctuation forte) = contexte intentionnel entre les deux
  formules (« donc », « mais », etc.). Pas un cas de fusion.
- Espaces/tabs = formattage neutre, pas de sémantique de séparation. Fusion
  appropriée.

### Pourquoi pas les OMaths sans handle

Sans handle dans le store, on n'a pas accès au source brut d'origine. On ne
peut donc pas reconstruire un OMath cohérent post-fusion. Le LaTeX de
l'OMath natif Word existe mais le re-tokenisation via le pipeline produirait
potentiellement un résultat différent. Préférable de laisser ces OMaths
intacts et créer le nouveau OMath séparément (comportement actuel).

## Conséquences

### Code (couche 3 — adapter VSTO)

- **SuggestionService.cs** : nouvelle méthode `TryMergeWithAdjacentOMaths`
  qui scan à gauche/droite, retourne `MergeResult` avec positions étendues +
  source mergé + handles à supprimer.
- **`CommitLatexAndOMath`** appelle `TryMergeWithAdjacentOMaths` AVANT
  `InsertOMathAt`. Si merge possible (et pas en mode édition), recalcule
  le LaTeX via le `ZoneResolver` sur le source mergé, supprime les anciens
  handles, et insère sur la zone étendue.
- **Helper `IsSingleSpaceAt`** : strict char `' '` uniquement (pas tab, pas
  newline). Spec révisée 29-04.

### Pas de modif côté core

Le merge est purement orchestration côté adapter. Le pipeline lattice n'a
rien à savoir : il reçoit un source mergé et le rend normalement.

### Mode édition

`CommitLatexAndOMath` skip le merge si `_editHandle != null` (mode revert
d'un OMath existant). Le revert remplace l'OMath en cours, pas de logique
d'absorption.

### Tests

Tests automatisés non ajoutés (logique VSTO Word, mock complexe). Tests
manuels dans Word obligatoires (cf. brief §5, 8 cas).

## Validé par l'utilisateur

Brief complet :
[`docs/dev/briefs/2026-04-29-merge-adjacent-omaths.md`](../briefs/2026-04-29-merge-adjacent-omaths.md)

Direction (sélection des briefs à attaquer) :
> "iterative et implication et merge"

Révision spec merge (29-04, après test) :
> "dans le merge omaths adjacent, le tabs ne doit pas etre pris en compte!
> juste direct adjacence, et un espace sinon pas de merge"

## Statut

acté
