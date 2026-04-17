# ADR-001 — Core C# uniquement pour phase 1

**Statut** : Accepté (2026-04-17)

## Contexte

Un prototype Office.js a été développé pour explorer la faisabilité d'un outil
de notation math au clavier dans Word. Ce prototype a révélé des limitations
structurelles d'Office.js (paragraph split sur `insertOoxml`, absence d'event
undo, latence des syncs). Ces limites rendent l'UX "zéro friction" impossible
à atteindre de façon fiable.

Décision stratégique : pivoter vers VSTO (Word Desktop Windows) pour valider
le produit auprès d'un PAP concret et de quelques profs. Le portage vers
Office.js (Web, Mac, iPad) est reporté en phase 2.

## Décision

Le core métier est implémenté **uniquement en C#** pour la phase 1. Pas de
double implémentation C# + TypeScript à ce stade.

## Conséquences

**Positif :**
- Un seul codebase à maintenir pour le core — zéro risque de drift.
- Itération plus rapide sur les algos (tokenizer, zone detector, parser).
- Toutes les optimisations ciblent .NET/VSTO directement.
- L'équipe se concentre sur la validation produit, pas sur le portage.

**Négatif :**
- Un portage TS sera nécessaire en phase 2 pour Office.js — coût différé.
- Mitigation : les **fixtures de tests** et le **schéma d'AST** (`specs/`) sont
  préparés dès maintenant pour rendre le portage futur déterministe et
  vérifiable par tests de conformité.

**Neutre :**
- Si en phase 2 Office.js n'est pas jugé assez stable, un core TS peut aussi
  servir à un adapter web standalone.

## Révision

À ré-évaluer quand la phase VSTO atteint son critère de validation (produit
utilisable au quotidien par le PAP et quelques profs).
