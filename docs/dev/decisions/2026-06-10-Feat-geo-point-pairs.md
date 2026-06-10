# Feat — Objets géométriques (AB) et [AB] : lecture « droite/segment » dans la forêt

**Date :** 2026-06-10
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Fix-nbsp-keyword-case-tolerance.md](2026-06-10-Fix-nbsp-keyword-case-tolerance.md) (notation `//` parallèles, même usage cible)

## Citation acté

> « important dans le cas de (AB) idéalement il faudrait se dire qu'on n'est dans une droite et garder les () […] en gros c'est (souvent ou tout le temps ?) une paire de points entourés de parenthese ou de [] » puis « oui (AB) et [AB] » — utilisateur, 2026-06-10 (validation du plan : parenthèses et crochets d'abord ; demi-droites `[AB)`/`(AB]` et droites nommées `(d)` reportées à une itération suivante).

## Contexte

Notation géométrie lycée : `(AB)` = droite passant par A et B, `[AB]` = segment.
État avant : `(AB) // (CD)` **perdait les parenthèses** (lecture « groupement »
pur, rendu `AB ⫽ CD`), et `[AB] // [CD]` était en **erreur** (le crochet ne
savait être qu'intervalle — exige un séparateur — ou matrice).

## Décision

Pas de cas spécial codé en dur : une **lecture supplémentaire dans la forêt**,
qui concourt au coût comme les autres. Dans `Parser.ParseSpan` :

- **Groupe `( atome )`** dont l'atome est une paire de majuscules (`^[A-Z]{2}$`,
  sans lectures multiples) → ajout d'une lecture atome littérale `(AB)`
  (délimiteurs conservés au rendu). La lecture « groupement » reste.
- **Crochets `[ atome ]`** même pattern → lecture atome `[AB]` (transforme
  l'erreur actuelle en résultat ; la voie intervalle reste inchangée).
- **Cohérence de mode** (mécanisme existant `Coh`/`Ai`, celui des ensembles
  R vs ℝ) : la lecture géométrique porte `Coh="geo", Ai=1`, la lecture
  groupement de la même paire porte `Coh="geo", Ai=0`. Mélanger les modes dans
  une même expression coûte `MODE_MIX` → `(AB) ⫽ (CD)` et `AB ⫽ CD` montent
  ensemble, les hybrides `(AB) ⫽ CD` sortent de la fenêtre popup.
- **Ordre à coût égal** : la lecture géométrique est insérée AVANT la lecture
  groupement → à égalité de coût (tri stable), `(AB)` prime — l'utilisateur a
  tapé des parenthèses, on les garde en tête.

## Tradeoff & alternatives écartées

- **Détection contextuelle (parens gardées seulement près de `//`/`perp`)** :
  coût contextuel = nouvelle machinerie de scoring ; la forêt + cohérence de
  mode produit le même effet avec les mécanismes existants.
- **Toujours garder les parenthèses typées** : casserait le groupement
  algébrique ordinaire (`(x+1)*2`) ; la lecture double + classement est
  exactement le modèle du moteur (« les slurps sont des membres de la forêt »).
- **Demi-droites `[AB)`/`(AB]` et droites nommées `(d)`/`(Δ)` dans ce lot** :
  reportées (validation utilisateur) — délimiteurs mixtes à ouvrir au parser,
  et `(d)` est plus ambigu (variable groupée) ; selon retour d'usage beta.

## Conséquences

- **Code touché** : `engine/src/MathCursor.Engine/Parser.cs` uniquement
  (helper pattern + 2 insertions dans `ParseSpan`).
- **Tests** : fixtures ajoutées au corpus ((AB)//(CD) avec priorité de la
  lecture géo dans l'ordre des candidats, [AB]//[CD], [AB] seul,
  non-régression `(x+1)*2` et `(R*)`) — politique « tout snapshot va aux
  fixtures », compte exact dans FixtureTests, gardes `>=` ailleurs.
- **Aval** : rendu popup et OMML déjà compatibles (caractères littéraux).
- **Vérifié avant code** : AUCUNE fixture existante ne contient `(XX)`/`[XX]`
  majuscules — pas de régression de corpus possible par construction.

## Validation post-fix

- `dotnet test` engine + serialization + adapter verts, corpus complet.
- Test manuel Word : `(AB) // (CD)` + Ctrl+Espace → top `(AB) ⫽ (CD)` (popup
  avec `AB ⫽ CD` en alternative) ; `[AB] // [CD]` ne donne plus « rien ».
