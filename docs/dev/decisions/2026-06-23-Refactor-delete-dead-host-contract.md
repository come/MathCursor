# Refactor — Suppression du contrat host-contract mort + réalignement de l'archi (CLAUDE.md)

**Date :** 2026-06-23
**Kind :** Refactor
**Température :** forte
**Statut :** acté
**Lié à :** [2026-06-03-Refactor-retrait-lattice-legacy-engine.md](2026-06-03-Refactor-retrait-lattice-legacy-engine.md), [2026-06-10-Refactor-phase2-adapter-orchestration-rewrite.md](2026-06-10-Refactor-phase2-adapter-orchestration-rewrite.md), [2026-06-23-Meta-delete-stale-cartography.md](2026-06-23-Meta-delete-stale-cartography.md)
**Amende :** le principe « Architecture en 3 couches / 4 interfaces abstraites » de `CLAUDE.md` (scaffold phase A).

## Citation acté

> [audit — #4] « ok on avance le 4 proprement » puis choix : « Supprimer (garder EquationHandle) » — utilisateur, 2026-06-23

## Contexte

`host-contract` définissait 4 interfaces (`IDocumentHost`, `IEquationStore`,
`IEditorSurface`, `IUserFeedback`) + des DTOs (`ContextText`, `TextZone`,
`EquationOutput`, `EquationMetadata`, `StoredEquation`, `RankedCandidate`,
`CaretPosition`, delegates). Elles modélisent un paradigme **« le core pilote
l'hôte »** (core stateful qui lit le caret, insère/édite/revert, s'abonne à des
événements) — l'archi de l'ancien `core-csharp`/lattice. Preuve : `EquationMetadata.SidecarJson`
référençait `MathCursor.Core.Resolution.SidecarSerializer`, le `core-csharp` **supprimé**.

État réel mesuré (audit 2026-06-22) :
- **Aucune** des 4 interfaces n'est implémentée ni consommée nulle part (adapter, engine, web-demo, tests).
- **Seul `EquationHandle`** (wrapper d'id string trivial) est utilisé — uniquement par `EditModeController` (adapter).
- L'archi vivante est l'**inverse** du contrat : l'adapter **orchestre** et appelle un moteur **pur et sans état** (`ForestEngine` : texte → candidats). Le moteur n'a aucune notion de caret/insert/événements.
- La portabilité phase 2 (Office.js) est **déjà** assurée par le moteur pur (`netstandard2.0`, zéro Word) — la démo **WASM** le prouve en appelant directement `ForestEngine`. Les 4 interfaces n'y contribuent pas.

Le contrat est donc un vestige de l'archi pré-portage forest, même couche géologique
que `cartography.md` (supprimée) et le « 5 axes » (caduc).

## Décision

1. **Supprimer** les 4 interfaces + les DTOs morts ; **garder `EquationHandle`**
   (seul type utilisé) dans `host-contract` (projet conservé, désormais = types
   partagés adapter↔(futur hôte), pas un « contrat d'inversion »). `IsExternalInit`
   supprimé (plus aucun `init` dans le projet réduit).
2. **Réaligner `CLAUDE.md`** sur l'archi réelle :
   - Couches = **moteur pur** (`engine/MathCursor.Engine`, netstandard2.0, texte→candidats) + **sérialisation** (`serialization`, LaTeX→OMML) ← **adapter** (`adapter-vsto`, orchestrateur VSTO). `host-contract` = types partagés.
   - Règle dure conservée et reformulée : **le moteur ne connaît ni Word ni VSTO** (fonction pure portable) — mais l'adapter l'appelle **en direct**, pas via une interface d'inversion.
   - Corriger les autres péremptions du bloc (`core-csharp`, chemin `D:\…\DocMath`, dépendance « Couche 1→2 » inversée, tests dans `core-csharp/tests`, fixtures `specs/`).

## Tradeoff & alternatives écartées

- **Brancher l'adapter sur les interfaces** : impossible — paradigme inverse (core-pilote-hôte vs adapter-orchestre-moteur-pur). Retrofit d'une abstraction obsolète.
- **Geler + documenter dormant** : laisse du code mort que CLAUDE.md présente comme le pivot d'archi → continue d'induire en erreur (un agent d'audit a halluciné dessus). Choix user = suppression.
- **Garder un vrai contrat pour le moteur pur** : si un jour un 2ᵉ hôte le justifie, on écrira une abstraction **adaptée au moteur pur** (entrée texte/culture → candidats), pas ces interfaces d'inversion. Pas de spéculation maintenant (un seul hôte VSTO).

## Conséquences

- **Code** : `host-contract` → suppression `IDocumentHost.cs`, `IEquationStore.cs`, `IEditorSurface.cs`, `IUserFeedback.cs`, `IsExternalInit.cs` ; `Types.cs` réduit à `EquationHandle` (→ `EquationHandle.cs`). Projet + `ProjectReference` adapter + `.sln` **inchangés** (projet conservé, slim). Engine/serialization : zéro.
- **Doc** : `CLAUDE.md` bloc Architecture/Structure/Règles réécrit. Reste hors périmètre (péremptions adjacentes à traiter à part) : `CLAUDE.md` « Algorithmes à porter » (déjà portés) ; `ROADMAP.md` « Chantier 1 — 5 axes » (même `core-csharp` fantôme) ; `PLAN.md`. ADRs historiques liant le contrat : non touchés (convention).
- **Tests** : aucun ne référençait les types supprimés → suite inchangée, gate vert attendu.
