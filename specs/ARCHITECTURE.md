# MathCursor — Architecture

## Vue d'ensemble

Architecture en 3 couches pour découpler la logique métier de la plateforme hôte,
permettre un portage futur vers Office.js (phase 2), et maximiser la testabilité.

```
┌─────────────────────────────────────────────────────────┐
│  COUCHE 3 : Adaptateur plateforme                       │
│  adapter-vsto   (.NET Framework 4.8 / C#, Word Desktop) │
├─────────────────────────────────────────────────────────┤
│  COUCHE 2 : Contrats d'hôte (interfaces abstraites)     │
│  host-contract-csharp                                   │
├─────────────────────────────────────────────────────────┤
│  COUCHE 1 : Core métier (aucune dépendance plateforme)  │
│  core-csharp  (.NET Standard 2.0)                       │
└─────────────────────────────────────────────────────────┘
```

## Règles de dépendances

- **Couche 1 (core)** : dépend uniquement de Couche 2. Ne référence JAMAIS `Microsoft.Office.*`, ni rien de Word-spécifique.
- **Couche 2 (host-contract)** : pure interface, aucune dépendance.
- **Couche 3 (adapter)** : dépend de Couche 1 + Couche 2, et des SDK plateforme.

Cette règle est **non-négociable** — toute violation casse la portabilité future.

## Phase 1 : VSTO seul

Pour valider le produit rapidement avec un PAP et quelques profs, on se concentre sur :
- `core-csharp` (logique pure)
- `host-contract-csharp` (interfaces)
- `adapter-vsto` (implémentation Word Desktop)

Pas de core TypeScript, pas d'adapter Office.js pour cette phase. Les fixtures
partagées et le schéma d'AST sont néanmoins préparés pour le portage futur.

## Pipeline de conversion

Identique en intention à ce qu'on a validé dans le prototype Office.js :

```
Texte brut
    ↓ Tokenizer (catégorisation Unicode, normalisation)
Tokens
    ↓ Mathiness scorer (score 0..1 par token)
Tokens scorés
    ↓ Zone detector (frontière prose/math)
Zone math
    ↓ Parser (grammaire des opérateurs)
AST
    ↓ Serializer (OMML/LaTeX/Unicode)
Sortie typée (EquationOutput)
```

## Gestion des événements en VSTO

Grâce aux events natifs riches, on abandonne le polling :

| Event | Usage |
|-------|-------|
| `ContentControlOnEnter` | Détection curseur dans équation MathCursor → mode édition |
| `ContentControlOnExit` | Sortie de l'équation |
| `WindowSelectionChange` | Fallback si `ContentControlOnEnter` silencieux |
| Raccourci clavier (Ctrl+Espace) | Déclenchement explicite de conversion |
| `Application.WindowBeforeDoubleClick` | Alternative pour édition |

## Stockage des sources d'équations

Via `Document.CustomXMLParts` — stockage persistant dans le .docx, invisible à
l'utilisateur, retrouvé par ID du ContentControl.

Voir ADR-005 pour la justification du choix ContentControl + tag.

## Multilingue

Les données linguistiques (stopwords, opérateurs, mots de liaison) sont dans
`data/*.json` au niveau racine du repo. En C#, elles sont embarquées via
`EmbeddedResource` dans le `.csproj` du core.

## Tests

- Tests unitaires par module dans `core-csharp/tests/`
- Tests de conformité : `core-csharp/tests/` lit `specs/test-fixtures/*.json`
- Tests d'intégration VSTO dans `adapter-vsto/tests/` (mocks des interfaces hôte)
