# Feat — Collision majuscules `AB` (produit / vecteur / paren) en V2

**Date :** 2026-05-29
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [`2026-05-29-Test-engine-adapter-e2e-headless.md`](2026-05-29-Test-engine-adapter-e2e-headless.md), audit legacy `Lattice/Ambiguity/Scanners/UppercaseSequencesScanner.cs`

## Citation acté

> « oui go » — utilisateur, 2026-05-29

(Phase 1 du portage des collisions legacy choisie après audit : `AB → vecteur/produit/paren`, le cas le plus fréquent au lycée. Tight-chain `1/x+1` et vecteurs colonne/ligne reportés en phases séparées.)

## Contexte

Le moteur V2 ne remonte aujourd'hui à la popup que les collisions
**exposant/indice** (`x2` → `x²` | `x₂`), via le tie-break des primitives
(règles concurrentes de **même span**). L'ancien moteur, lui, signalait via
`UppercaseSequencesScanner` que deux majuscules `AB` sont ambiguës : produit
`A·B`, vecteur `AB⃗`, ou groupe `(AB)`. Très fréquent en géométrie lycée. Non
porté en V2 → la popup ne propose rien sur `AB`.

`AB` est tokenisé en **un seul** token Word `"AB"`, classé `Var`, émis `AB`
tel quel (aucune règle ne tire). `xy` (minuscules) est lui aussi `Var` — il ne
doit PAS recevoir de collision vecteur (on ne met pas de flèche sur un produit
de scalaires). La distinction nécessite donc une notion de « majuscule ».

## Décision

1. **Nouvelle catégorie `UpperSeq`** : `ClassifyWord` tague un token Word
   composé uniquement de **2 ou 3 lettres majuscules** (`AB`, `ABC`) en
   `UpperSeq`. Tout le reste (minuscules `xy`, 4+ lettres, etc.) garde `Var`.
   Subsumption : `Expr ⊃ UpperSeq` (= une valeur composable) et `Var ⊃ UpperSeq`
   (= reste accepté partout où un Var l'était).

2. **3 règles concurrentes même-span** en YAML (`data/concepts/collisions.yml`),
   sur `{x:upperseq}` :
   - `coll-upper-product` (priorité 32, **top**) → `$x` (= `AB`)
   - `coll-upper-vector` (priorité 31) → `\vec{$x}` (= `AB⃗`)
   - `coll-upper-paren` (priorité 30) → `($x)` (= `(AB)`)

   Le tie-break existant (`RunPrimitivePhase` : même `Start`+`Span`, tri par
   priorité décroissante) élit `product` comme top et enregistre `vector` +
   `paren` comme alternatives — qui remontent jusqu'à la popup via le câblage
   déjà en place (`RewriteResult.Alternatives` → `EngineResult.Collisions` →
   `ResolvedZone.PatternCompletions`). **Zéro nouveau mécanisme** : c'est
   exactement le modèle qui fait déjà marcher `x2`.

   La règle `product` (re-émet le token inchangé) est nécessaire : sans elle,
   le matcher élirait `vector` comme top. Elle ne tire que sur `UpperSeq`, donc
   n'affecte rien d'autre (les `AB` deviennent un `Expr "AB"` au lieu d'un token
   brut `"AB"` — même LaTeX, composition inchangée).

## Tradeoff & alternatives écartées

- **Garder `Var` + prédicat majuscule dans une règle** : rejeté — le langage de
  pattern ne sait pas exprimer « majuscule » ; il faudrait un hack hors-modèle.
  Une catégorie typée est l'outil prévu pour ça.
- **Détecteur de collision dédié (à la legacy `IAmbiguityScanner`)** : rejeté —
  réintroduirait un pipeline parallèle alors que le tie-break même-span couvre
  déjà le besoin de façon data-driven.
- **Inclure `[AB]` (intervalle/bracket)** comme 4ᵉ alternative (le legacy
  l'avait) : écarté pour cette phase — l'utilisateur a retenu vecteur/produit/
  paren. Ajoutable trivialement plus tard (1 règle).

## Conséquences

- **Code touché** : `Rewriting/Category.cs` (enum + Subsumes + Parse),
  `Rewriting/Item.cs` (`ClassifyWord`), nouveau `data/concepts/collisions.yml`.
- **Tests** : règle golden (`AB => AB`, `ABC => ABC`) + tests headless
  adapter assertant que la popup reçoit `\vec{AB}` et `(AB)` en collisions pour
  `AB`, et **0 collision** pour `ab` (minuscule) et `X` (1 lettre).
- **API publique** : inchangée.
- **Limite connue** : `EngineResult.Collisions` est sans span — sur `AB+CD`,
  les alternatives des deux spots sont aplaties dans une seule liste popup.
  Acceptable (le cas mono-spot est l'usage courant) ; le span-aware multi-spot
  relève d'une amélioration ultérieure du mapping collision→popup.

## Validation post-fix

Golden + tests headless verts. `AB` → popup `AB` (top) + `AB⃗` + `(AB)` ;
`ab`/`X` → aucune collision. La suite engine + e2e adapter reste verte.
