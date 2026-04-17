# MathCursor — Notation math au clavier pour Word Desktop

## Contexte

Outil destiné à des lycéens, notamment avec PAP, pour prendre des cours de maths
de façon fluide au clavier. Objectif : comportement prévisible et sans friction.

**Phase 1** : Word Desktop Windows via **VSTO** uniquement.
**Phase 2** (après validation produit) : portage Office.js pour Word Web / Mac / iPad.

## Validation produit (critère de succès phase 1)

Produit utilisable au quotidien par :
- Un élève avec PAP (cours de maths lycée, fils de l'auteur)
- Quelques profs de maths beta-testeurs

Si ce critère est atteint → on attaque la phase 2.

## Stack

- **VSTO** Word Add-in, .NET Framework 4.8
- **C#** pour tout le code (core, contrats, adapter)
- **WPF** pour les popups au caret
- **xUnit** pour les tests
- Pas de dépendances lourdes

## Architecture en 3 couches

```
adapter-vsto              (Couche 3 : plateforme Word Desktop / VSTO)
   ↓
host-contract-csharp      (Couche 2 : 4 interfaces abstraites)
   ↓
core-csharp               (Couche 1 : logique métier pure, .NET Standard 2.0)
```

**Règle dure :** le core ne connaît ni Word, ni VSTO, ni Office.js. Il voit
seulement les interfaces :
- `IDocumentHost` — lire contexte, insérer/éditer équations, events curseur
- `IEquationStore` — persister les sources (CustomXMLParts en VSTO)
- `IEditorSurface` — UI des suggestions et mode édition
- `IUserFeedback` — logging local opt-in

## Structure

```
D:\Software\DocMath\
├── MathCursor.sln
├── specs/                      # ADRs, schéma AST, fixtures de tests
├── data/                       # JSON multilingues (stopwords, operators...)
├── core-csharp/                # Couche 1 — logique pure
├── host-contract-csharp/       # Couche 2 — interfaces
├── adapter-vsto/               # Couche 3 — VSTO Word Desktop
├── tools/                      # Validation données, futur conformance runner
└── archive/officejs-prototype/ # Prototype Office.js figé (référence seule)
```

## Règles de dev

- **Règle de dépendances** : Couche 1 → Couche 2 uniquement. Pas de `Microsoft.Office.*` dans le core.
- **Triggers explicites** : conversion via raccourci (`Ctrl+Espace`) ou bouton, pas de polling.
- **Events natifs VSTO** : `ContentControlOnEnter`, `WindowSelectionChange`, `Application.Undo` → pas d'heuristiques fragiles.
- **Stockage sources** : `Document.CustomXMLParts`, pas de storage global.
- **Tests unitaires xUnit** dans `core-csharp/tests/`, tests d'intégration dans `adapter-vsto/tests/`.
- **Fixtures partagées** : `specs/test-fixtures/*.json` — lu par les tests. Source de vérité cross-implémentations.
- **Données multilingues** : `data/*.json` à la racine, embarquées via `EmbeddedResource`.

## Ce qu'on ne fait PAS (phase 1)

- Pas de core TypeScript (cf. ADR-001) — ajouté en phase 2.
- Pas d'adapter Office.js — phase 2.
- Pas d'éditeur web standalone — phase 3+ si besoin.
- Pas de backend, pas de télémétrie réseau, pas de cloud.
- Pas de VBA.

## Algorithmes à porter du prototype Office.js

Validés empiriquement (47/47 tests sur corpus FR/EN/DE/ES), à réimplémenter en C# :

| Module prototype TS | Cible C# |
|---------------------|----------|
| `conversion/tokenizer.ts` | `Tokenization/Tokenizer.cs` |
| `conversion/scorer.ts` | `ZoneDetection/Scorer.cs` |
| `conversion/zone-detector.ts` | `ZoneDetection/ZoneDetector.cs` |
| `conversion/parser.ts` | `Ast/Parser.cs` (à créer) |
| `conversion/render.ts` | `Serialization/OmmlSerializer.cs` |

Le prototype reste accessible dans `archive/officejs-prototype/` comme référence
uniquement. **Ne pas modifier le prototype** — c'est une photo figée.

## Roadmap phase 1

| Phase | Durée estimée | Livrable |
|-------|----------------|----------|
| A | 2-3 sem | Scaffold (fait) + ADRs + fixtures |
| B | 6-8 sem | Core C# complet (tokenization → serialization) |
| C | 4-6 sem | Adapter VSTO, popup WPF, MSI signé |
| Validation | — | Usage quotidien par le PAP + quelques profs |

## Git

- Branche principale : `main` (à créer quand on quitte `v2-mathiness`)
- Tag de référence prototype Office.js : `prototype-officejs-final`
- Pas de push automatique, demander confirmation avant `git push`
