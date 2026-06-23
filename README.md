# MathCursor

Outil de notation mathématique au clavier pour Word, pensé pour les lycéens (notamment avec PAP).

## Phase 1 — VSTO / Windows Desktop

Focus exclusif : Word Desktop Windows via VSTO. L'objectif est la validation produit
avec un PAP concret (le fils d'un utilisateur) et quelques profs en beta.

Office.js (Word Web / Mac / iPad) viendra en phase 2 **une fois l'UX validée**.

## Architecture

Le **moteur** est une fonction pure (texte → candidats LaTeX classés), portable,
sans dépendance plateforme. L'**adapter** VSTO l'orchestre et l'appelle en direct.

```
adapter-vsto/MathCursor                  (plateforme Word/VSTO : orchestration, popup WPF, interop)
   ↓ appelle
engine/MathCursor.Engine                 (moteur « forest » PUR : texte → candidats, netstandard2.0)
serialization/MathCursor.Serialization   (LaTeX → OMML pour l'insertion Word)
```

Règle dure : le moteur et la sérialisation ne connaissent ni Word, ni VSTO, ni
Office.js (netstandard2.0, zéro `Microsoft.Office.*` / WPF). C'est ce qui rendra la
phase 2 (Office.js / Mac / Web) possible — la démo WASM réutilise déjà le même moteur.

## Structure du monorepo

| Dossier | Rôle |
|---------|------|
| `data/` | Données embarquées (`engine/symbols.json`, `cultures.json`, corpus NER) |
| `engine/` | Moteur « forest » C# PUR (texte → candidats) + tests + `fixtures.json` |
| `serialization/` | LaTeX → OMML (insertion Word) + tests |
| `host-contract/` | Types partagés légers (`EquationHandle`) |
| `adapter-vsto/` | Add-in VSTO Word Desktop (orchestration, UI WPF, interop) + tests + installer |
| `analyzers/` | Analyzers Roslyn (règles MC) + tests |
| `web-demo/` | Démo Blazor WASM (réutilise le moteur compilé) |
| `engine-python/` | Port Python de parité (conformance) |
| `scripts/` | Outillage (`run-tests.ps1` = gate de test local) |

## Moteur (porté)

Le pipeline de reconnaissance/conversion est porté en C# pur dans `engine/` :
tokenizer, scoring, parser « forest », rendu LaTeX, puis sérialisation OMML dans
`serialization/`. Verrouillé par le corpus `fixtures.json` (rejoué par plusieurs
pipelines) + un port Python de parité.

## Ce qu'on NE porte PAS

- Polling fastTick 50ms — en VSTO, events natifs fiables (`ContentControlOnEnter`)
- Décomposition automatique sur clic — trigger explicite (raccourci ou bouton)
- Guard TTL / anti-boucle undo — `Application.Undo` détectable en VSTO

## Critère de validation

Produit utilisable au quotidien par :
- Un élève avec PAP (cours de maths lycée)
- Quelques profs de maths

Pas de prématurité sur les portages multi-plateformes avant ça.
