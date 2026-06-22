# Fix — Rendre visibles les échecs silencieux côté élève (commit / revert / source)

**Date :** 2026-06-22
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Lié à :** —

## Citation acté

> [audit 2026-06-22, angle mort n°2] échecs silencieux côté élève. « oui pour 1 et 2 » — utilisateur, 2026-06-22

## Contexte

L'audit a relevé plusieurs `catch` qui avalent l'erreur sans aucun signal visible,
sur des actions **déclenchées par l'utilisateur** :
- `ConversionController.cs:416` (`commit_error`) → l'équation n'est **pas insérée**, popup masquée, **zéro message**. Pire cas produit.
- `EditModeController.cs:229` (`revert_error`) → l'élève clique « Revenir à la saisie initiale », **rien ne se passe**.
- `OMathInserter.cs:101/104` (`record_flush…`) → équation insérée mais **source non enregistrée** → revert/ré-édition cassés plus tard, sans trace.

Pour une cible PAP « sans friction », un échec **muet** est pire qu'une erreur visible :
l'élève ne sait pas que son geste a échoué, ni qu'il faut réessayer.

## Décision

Ajouter un message **StatusBar** dans chacun de ces `catch` (et le re-probe raté de
flush). Canal choisi = StatusBar, pas MessageBox :
- cohérent avec l'existant (`TryStatusBar(Strings.ConvertNothingRecognized)`) ;
- non-intrusif, fidèle à la philosophie « sans friction » (un MessageBox couperait le flow et l'undo) ;
- ces échecs sont des chemins d'exception rares, pas des validations courantes.

Changement **purement additif** : aucune logique d'insertion / CC / revert / BuildUp
n'est touchée — uniquement un message dans des `catch` déjà existants. Messages
localisés FR/EN via `Strings` (`ConvertCommitFailed`, `RevertFailed`, `SourceNotRecorded`).

## Tradeoff & alternatives écartées

- **MessageBox sur `commit_error`** : plus visible, mais intrusif et casse le flow — réservé aux actions ruban explicites dans ce code. Si l'usage montre que la StatusBar passe inaperçue sur la perte d'équation, on pourra l'upgrader (révision facile).
- **Implémenter `IUserFeedback`** (contrat) pour router ces messages : bonne cible long terme, mais le contrat est aujourd'hui inerte (cf. audit) — hors périmètre de ce fix minimal.
- **Ne rien faire** : laisse l'élève devant un geste sans effet ni explication.

## Conséquences

- **Code** : `Strings.cs` (3 clés FR/EN), `ConversionController.cs` (`commit_error` → `TryStatusBar`), `EditModeController.cs` (`revert_error` → StatusBar), `OMathInserter.cs` (`record_flush` re-probe raté + `record_flush_error` → StatusBar). Aucun test cassé (chemins d'exception non couverts ; comportement nominal inchangé).
- Le contournement complet (revert qui re-marche, source ré-enregistrée) reste un sujet à part — ici on rend l'échec **visible**, on ne le corrige pas.
