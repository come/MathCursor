# Fix — `environ` / `env` reconnus comme marqueur ≈ en début de ligne

**Date :** 2026-06-19
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Feat-multiline-chain-eqarr-architecture.md](2026-06-10-Feat-multiline-chain-eqarr-architecture.md) (table `RelationMarkers`), [2026-06-18-Feat-approx-and-second-derivative.md](2026-06-18-Feat-approx-and-second-derivative.md) (alias `environ`→approx), [2026-06-18-Feat-prefix-keyword-expansion.md](2026-06-18-Feat-prefix-keyword-expansion.md)

## Citation acté

> « bah du coup Env f(x) doit passer et être reconnu en ~ f(x) » puis « oui fais
> le fix environ » — utilisateur, 2026-06-19

## Contexte

L'utilisateur tape `Env f(x)` (ou `environ f(x)`) en **début de ligne** et attend
`≈ f(x)`. Rien ne sort — ni en auto-détection (NER), ni au Ctrl+Espace.

Diagnostic mesuré (modèle NER réel + moteur) :

- Le **NER n'est pas en cause** : il détecte bien la zone `[0,12)` complète.
- `environ` est l'alias de `approx`, une **relation** (`cut:true`). En tête
  d'entrée, comme `=2x` / `<3`, le membre gauche est vide → le moteur renvoie
  `erreur` (cf. `LeadingRelationTests`). `f(x) environ 3` (relation au milieu) →
  `f(x)\approx 3` marche déjà.
- Le mot **`approx`** est, lui, déjà reconnu comme **marqueur de tête de ligne**
  (`RelationMarkers`, ajouté le 2026-06-12 : « approx 3,14 ») : `approx f(x)` en
  début de ligne donne `≈ f(x)` (le ≈ est mis en préfixe, le reste `f(x)` est
  analysé par le moteur). **Incohérence** : `environ`/`env` ne sont pas dans la
  table → ils retombent dans le moteur et font `erreur`.

Deuxième constat : **Word met une majuscule automatique en début de ligne**
(`env`→`Env`, `approx`→`Approx`). La comparaison de `RelationMarkers.TryMatch`
était `CompareOrdinal` (sensible à la casse) → même `approx` aurait cassé à un
vrai début de ligne capitalisé.

## Décision

1. **Ajouter `environ` et `env` à `RelationMarkers.Table`** (LaTeX `\approx `,
   `IsConnector = false`), à côté de `approx`. La frontière de mot déjà exigée
   par `TryMatch` empêche `environnement` / `enveloppe` de matcher.
2. **Rendre `TryMatch` insensible à la casse** (`OrdinalIgnoreCase`) pour
   absorber la majuscule auto de Word. Sans effet sur les marqueurs symboles
   (`=`, `<=`, `≈`…). La forme canonique (minuscule) reste retournée dans
   `MarkerTyped` ; les offsets restent exacts (même longueur que la forme tapée).

Effet (en **début de ligne** uniquement, comme `=` / `approx`) :

| Saisie (début de ligne) | Résultat |
|---|---|
| `Env f(x)` | `≈ f(x)` |
| `environ f(x)` | `≈ f(x)` |
| `Approx f(x)` | `≈ f(x)` (réparé : la majuscule auto cassait avant) |
| `f(x) environ 3` | `f(x) ≈ 3` (inchangé, voie alias infixe) |

## Tradeoff & alternatives écartées

- **Rendre le moteur tolérant au « mot inconnu + formule »** (convertir la queue
  math d'office) : écarté — ouvre la porte à la conversion de prose (« le
  résultat f(x) »). Le refus du moteur est volontaire.
- **`env` à 3 lettres comme marqueur** : un poil agressif (une variable `env` en
  début de ligne donnerait `≈`), mais explicitement demandé, et borné au début
  de ligne + frontière de mot. Cohérent avec la valeur « sans friction ».
- **Refonte du grab Ctrl+Espace** (« partir petit, étendre mot par mot ») :
  proposée par l'utilisateur, mais c'est un changement de comportement de
  sélection plus large → traité séparément (plan + ADR dédiés).

## Conséquences

- **Code touché (L3 adapter)** : `adapter-vsto/src/MathCursor/Host/Blocks/
  RelationMarkers.cs` — 2 entrées + comparaison `OrdinalIgnoreCase`. Aucune autre
  couche ; le moteur (L1) est inchangé (l'alias `environ`→approx y reste pour la
  voie infixe `a environ b`).
- **Tests** : `RelationLineDetectorTests` — cas `environ`/`env`/casse (`Env`,
  `Approx`). Suite adapter verte.
- **Auto + manuel** : les deux passent par `ConversionController.AnalyzeAndShow`
  → `RelationLineDetector.TryDetect` en amont du moteur → le fix couvre Ctrl+Espace
  ET l'auto-détection.

## Validation post-fix

En Word, début de ligne : `Env f(x)` + Ctrl+Espace → popup `≈ f(x)`. `environ x`
idem. `environnement` n'est pas capturé (frontière de mot). `f(x) environ 3`
toujours `f(x) ≈ 3`.
