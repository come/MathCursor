# Feat — Bouton « Unités » : reconnaissance des unités désactivable par document

**Date :** 2026-11-02
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** mode Physique-chimie (`EngineCulture.PhysChem`, `DocumentMode`), backlog moteur « 6h lu comme 6 heures » (02/11)

## Citation acté

> « ptetre que a coté de physiques chimie dans les domaines, on mettrait un bouton
> "unités" pour qu'il ajoute la reconnaissance auto d'unité (coché par defaut) » —
> utilisateur, 2026-11-02.

> « oui je valide le plan » — utilisateur, 2026-11-02 (réglage par document,
> Physique-chimie force les unités).

## Contexte

Le moteur reconnaît toujours les mots de `data/engine/units.json` comme des unités,
même en mode maths. Cas réel (exercice de 1re, taux d'accroissement) :
`(9 + 6h + h2 - 9)/h` → `\frac{9+6\mathrm{h}+h^{2}-9}{h}` : le *h* de `6h` est
rendu droit comme l'heure, alors que *h* est la variable du calcul. Même piège
pour `6s`, `3m`, `2g`… dès qu'une de ces lettres sert de variable.

## Décision

- **Moteur** : `EngineCulture.Units` (défaut `true`) + `WithUnits(bool)`, comme
  `PhysChem`. Units désactivé → un mot-unité n'est plus classé `unit` par le lexer :
  il retombe sur son traitement de nom/variable. Effectif seulement si `PhysChem`
  est faux (**Physique-chimie force les unités**). Même changement dans
  `rust/mc-engine` (parité), fixtures avec unités désactivées.
- **Adapter** : bouton bascule **« Unités »** dans le ruban à côté de
  « Physique-chimie », **coché par défaut**, mémorisé **par document** (variable de
  document, comme Physique-chimie ; absente = coché). Grisé et coché tant que
  Physique-chimie est actif.

## Écartés

- **Deviner** (une lettre-unité qui apparaît aussi comme variable dans la formule
  n'est pas une unité) : fragile (« 6h » seul reste ambigu), moins prévisible pour
  l'élève qu'un bouton explicite.
- **Réglage global** (Paramètres) : un même élève a des documents de maths et de
  physique ; le réglage suit le document, comme Physique-chimie.
