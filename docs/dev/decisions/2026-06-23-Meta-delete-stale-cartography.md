# Meta — Suppression de cartography.md (cartographiait une archi abandonnée)

**Date :** 2026-06-23
**Kind :** Meta
**Température :** molle
**Statut :** acté
**Lié à :** [2026-05-13-Meta-extensibility-axes-abstractions.md](2026-05-13-Meta-extensibility-axes-abstractions.md) (qui l'avait produite)

## Citation acté

> [audit — #7 reste] « 7 est complètement périmé non ? » — utilisateur, 2026-06-23 (choix : suppression pure + nettoyage des refs)

## Contexte

`docs/dev/architecture/cartography.md` cartographiait **intégralement** `core-csharp/src/MathCursor.Core/`
(LatticeEngine, Lattice/, AlternativeGenerator, Resolution/Sidecar, AST, plus le plan
refacto « 5 axes / étapes 2-5 » créant `core-csharp/MathCursor.Core.Abstractions/`).

Or `core-csharp/` **n'existe plus** dans le repo (0 fichier) : c'était l'ancien pipeline
DocMath, abandonné au profit du **portage forest** (`engine/MathCursor.Engine/ForestEngine`,
cf. `PLAN.md` « core-csharp = ancien pipeline de reconnaissance » à dropper). Le doc ne
mentionne **jamais** `ForestEngine`. Il était donc 100 % périmé — et **activement
trompeur** : `CLAUDE.md` le listait en « à lire en premier », induisant un modèle mental
faux (un agent d'audit a remonté une « dette core-csharp/LatticeEngine » inexistante à
cause de lui).

## Décision

Supprimer `cartography.md` et nettoyer les références **vivantes** :
- `CLAUDE.md` : retrait du pointeur « à lire en premier ».
- `ROADMAP.md` : retrait de la liste d'onboarding ; étape 1 marquée supprimée.
- `MC0006_LatexSpliceAntiPattern.cs` : message diagnostic — lien doc retiré.

**Non touché** (convention « ne pas modifier les ADRs ») : les ADRs 2026-05-13
(`Meta-extensibility-axes`, `Meta-harness-phase-0-1`, `Refactor-ast-visitor`) et
2026-06-22 qui lient cartography — archive, leurs liens reflètent l'état d'alors.

## Tradeoff & alternatives écartées

- **Archiver avec en-tête « périmé »** : garde l'historique mais laisse un doc mort que des refs pointent encore. Choix user = suppression nette (git garde l'historique).
- **Réécrire pour le forest engine** : utile mais vrai chantier ; non demandé ici.

## Conséquences

- `cartography.md` supprimé ; `git log`/`git show` gardent l'historique si besoin.
- **Reste suspect, hors périmètre de cet ADR** : `ROADMAP.md` « Chantier 1 — Refacto archi extensibilité (5 axes) » (étapes 2-6) cible le même `core-csharp` disparu → à statuer séparément (lié à l'angle mort #4 « contrat mort » de l'audit).
