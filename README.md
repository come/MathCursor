# MathCursor

Outil de notation mathématique au clavier pour Word, pensé pour les lycéens (notamment avec PAP).

## Phase 1 — VSTO / Windows Desktop

Focus exclusif : Word Desktop Windows via VSTO. L'objectif est la validation produit
avec un PAP concret (le fils d'un utilisateur) et quelques profs en beta.

Office.js (Word Web / Mac / iPad) viendra en phase 2 **une fois l'UX validée**.

## Architecture 3 couches

```
adapter-vsto              (Couche 3 : plateforme VSTO/.NET Framework)
   ↓
host-contract-csharp      (Couche 2 : interfaces abstraites)
   ↓
core-csharp               (Couche 1 : logique métier pure, .NET Standard 2.0)
```

Règle de dépendances : le core ne connaît ni Word, ni VSTO, ni Office.js. Il voit
seulement les interfaces `IDocumentHost`, `IEquationStore`, `IEditorSurface`,
`IUserFeedback`.

## Structure du monorepo

| Dossier | Rôle |
|---------|------|
| `specs/` | Specs formelles, ADRs, test fixtures partagés |
| `data/` | Données multilingues (stopwords, operators, etc.) |
| `core-csharp/` | Core métier C# (tokenization → AST → serialization) |
| `host-contract-csharp/` | Interfaces C# que chaque adapter implémente |
| `adapter-vsto/` | Add-in VSTO Word Desktop (phase 1) |
| `tools/` | Outillage : validation données, conformance runner |
| `archive/officejs-prototype/` | Prototype Office.js (figé, référence seulement) |

## Ce qu'on porte du prototype Office.js

Les algorithmes validés empiriquement (corpus de 47 cas multilingues) :
- Tokenizer avec catégorisation Unicode + normalisation math italic
- Mathiness scorer (heuristique 0..1 par token)
- Zone detector (frontière prose/math via scoring + stopwords)
- Pipeline AST → render OOXML pour `<m:oMath>`

## Ce qu'on NE porte PAS

- Polling fastTick 50ms — en VSTO, events natifs fiables (`ContentControlOnEnter`)
- Décomposition automatique sur clic — trigger explicite (raccourci ou bouton)
- Guard TTL / anti-boucle undo — `Application.Undo` détectable en VSTO

## Critère de validation

Produit utilisable au quotidien par :
- Un élève avec PAP (cours de maths lycée)
- Quelques profs de maths

Pas de prématurité sur les portages multi-plateformes avant ça.
