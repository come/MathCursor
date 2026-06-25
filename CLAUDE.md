# MathCursor — Notation math au clavier pour Word Desktop

## Contexte

Outil destiné à des lycéens, notamment avec PAP, pour prendre des cours de maths
de façon fluide au clavier. Objectif : comportement prévisible et sans friction.

**Phase 1** : Word Desktop Windows via **VSTO** uniquement.
**Phase 2** (après validation produit) : portage Office.js pour Word Web / Mac / iPad.

## Où trouver l'état d'avancement (lire en premier)

- **[`docs/dev/decisions/README.md`](docs/dev/decisions/README.md)** — index chrono de tous les ADRs : le journal vivant des décisions.
- **`git log --oneline -30`** — état réel le plus fiable.
- **[`PLAN.md`](PLAN.md)** — plan de consolidation beta (daté, partiellement exécuté).
- **[`docs/dev/architecture/ROADMAP.md`](docs/dev/architecture/ROADMAP.md)** — ⚠️ **gelé** (chantiers DocMath, 2026-05-21) : référence historique, ne pas suivre ses chemins.

Quand tu reprends une session : `decisions/README.md` + `git log` pour l'état réel. Si plusieurs chantiers ouverts, demander la priorité utilisateur.

## ⚠️ Avant de toucher à l'ergo VSTO Word (OBLIGATOIRE)

Si la modif touche : insertion/suppression d'OMath, ContentControls, positions
Word internes, sticky-zone, auto-grow, revert, edit mode, ou tout ce qui
interagit avec `sel.SetRange/Delete/TypeText`, `om.Range`, `cc.Range`,
`ContentControls.Add`, `OMaths.Add/BuildUp`, etc. →

**LIRE D'ABORD** :
1. **[`docs/dev/architecture/word-api-helpers.md`](docs/dev/architecture/word-api-helpers.md)** — inventaire des helpers à utiliser (ParagraphPositionTranslator, CcMetaResolver, ZoneCleaner, etc.) + l'ordre d'opérations validé (= ZWSP plain → math → BuildUp → CC last).
2. **[`docs/dev/decisions/2026-05-19-Feat-anchor-cc-pattern.md`](docs/dev/decisions/2026-05-19-Feat-anchor-cc-pattern.md)** — pattern anchor CC (= la CC vit À CÔTÉ de l'OMath, pas autour).

Et appliquer les règles dures de la mémoire `feedback_word_api_workflow` :
- Si 2-3 patches s'empilent sans converger → STOP, remonter.
- POC ribbon button minimal AVANT la prod (= sans CC, sans Tag, sans pipeline).
- Normalize positions via `sel.SetRange(p,p) + readback sel.Start` systématiquement.
- L'ordre `TypeText / BuildUp / CC.Add` change ce que Word absorbe → tester chaque permutation via POC.
- Si l'utilisateur dit « ça marchait avant » → `git log` + diff IMMÉDIATEMENT, ne pas re-inventer.
- Ne JAMAIS ajouter `cc.LockContentControl = true` à l'insert sans tester `cc.Delete` au revert.

## Validation produit (critère de succès phase 1)

Produit utilisable au quotidien par :
- Un élève avec PAP (cours de maths lycée, fils de l'auteur)
- Quelques profs de maths beta-testeurs

Si ce critère est atteint → on attaque la phase 2.

## Stack

- **VSTO** Word Add-in, .NET Framework 4.8
- **C#** pour tout le code (moteur pur, sérialisation, adapter)
- **WPF** pour les popups au caret
- **xUnit** pour les tests
- Pas de dépendances lourdes

## Architecture

Le **moteur** est une fonction PURE (texte → candidats LaTeX classés), portable,
sans aucune dépendance plateforme. L'**adapter** VSTO l'orchestre et l'appelle
directement.

```
adapter-vsto/MathCursor                  (plateforme Word/VSTO : orchestration, popup WPF, interop)
   ↓ appelle directement
engine/MathCursor.Engine                 (moteur « forest » PUR : texte → candidats, netstandard2.0)
serialization/MathCursor.Serialization   (LaTeX → OMML pour l'insertion Word)
host-contract                            (types partagés légers — EquationHandle)
```

**Règle dure :** le moteur (`engine/`) et la sérialisation (`serialization/`) ne
connaissent ni Word, ni VSTO, ni Office.js — `netstandard2.0`, zéro
`Microsoft.Office.*`, zéro WPF. C'est ce qui rend la **phase 2** (Office.js / Mac /
Web) possible : un autre hôte réutilise le **même moteur** (la démo WASM le fait
déjà). L'adapter appelle le moteur **en direct** — pas d'interface d'inversion.
L'ancien « contrat à 4 interfaces » (core-pilote-hôte) a été supprimé comme code
mort (ADR 2026-06-23-Refactor-delete-dead-host-contract) ; il datait de l'archi
`core-csharp`/lattice abandonnée au profit du portage forest.

## Structure

```
D:\Software\MathCursor\
├── MathCursor.sln
├── data/                        # JSON embarqués (symbols.json, cultures.json…) via EmbeddedResource
├── engine/                      # moteur forest PUR — src + tests + fixtures.json (source de vérité)
├── serialization/               # LaTeX → OMML — src + tests
├── host-contract/               # types partagés légers (EquationHandle)
├── adapter-vsto/                # add-in VSTO Word Desktop — src + tests + installer
├── analyzers/                   # analyzers Roslyn (MC0001/0006/0009) + tests
├── web-demo/                    # démo Blazor WASM (réutilise le moteur compilé)
├── rust/                        # cœur RUST des hosts non-Word : mc-engine (moteur,
│                                #   gate fixtures.json 456/456) + mc-ner + mc-popup
├── adapter-vscode/              # extension VSCode (spawne les binaires Rust)
├── libreoffice-ext/             # extension LibreOffice (spawne les mêmes binaires Rust)
└── scripts/                     # outillage (run-tests.ps1 = gate de test local)
```

## Règles de dev

- **Règle de dépendances** : `engine` + `serialization` = PUR (netstandard2.0), zéro `Microsoft.Office.*` / WPF. L'adapter dépend du moteur, **jamais l'inverse**.
- **Triggers explicites** : conversion via raccourci (`Ctrl+Espace`) ou bouton, pas de polling.
- **Events natifs VSTO** : `ContentControlOnEnter`, `WindowSelectionChange`, `Application.Undo` → pas d'heuristiques fragiles.
- **Stockage sources** : `Document.CustomXMLParts`, pas de storage global.
- **Tests xUnit** : `engine/tests/` + `serialization/tests/` + `analyzers/` (purs), intégration dans `adapter-vsto/tests/`. Gate local complet : `scripts/run-tests.ps1`.
- **Fixtures partagées** : `engine/tests/MathCursor.Engine.Tests/fixtures.json` — source de vérité, rejouée par plusieurs pipelines (moteur, tolérance, OMML, walker, popup).
- **Données multilingues** : `data/*.json` à la racine, embarquées via `EmbeddedResource`.

## Ce qu'on ne fait PAS (phase 1)

- Pas de core TypeScript (cf. ADR-001) — ajouté en phase 2.
- Pas d'adapter Office.js — phase 2.
- Pas d'éditeur web standalone — phase 3+ si besoin.
- Pas de backend, pas de télémétrie réseau, pas de cloud.
- Pas de VBA.

## Moteur : portage « forest » (fait)

Le moteur de reconnaissance/conversion est porté en C# pur :
**`engine/MathCursor.Engine`** (« forest engine » — `Lexer`, `Parser`/`Forest`,
`Score`, `LatexRenderer`, `Vocabulary`) + **`serialization/MathCursor.Serialization`**
(`LatexToOmml`). Verrouillé par le corpus `engine/tests/.../fixtures.json` (rejoué
par plusieurs pipelines) + le **port Rust** `rust/mc-engine` (gate `fixtures.json`
456/456), qui exécute le moteur pour VSCode et LibreOffice. (Le port Python
`engine-python/` a été retiré une fois le Rust vert — récupérable dans git.)

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

## Process de décision

Journal des décisions dans `docs/dev/decisions/` — un fichier ADR par décision
au format `YYYY-MM-DD-<Kind>-<slug>.md`. Spec complète dans
[`docs/dev/decisions/2026-04-24-Meta-adr-format.md`](docs/dev/decisions/2026-04-24-Meta-adr-format.md).
Index dans [`docs/dev/decisions/README.md`](docs/dev/decisions/README.md).

**Pour toute modification non-triviale (feature, refactor, choix ergo, règle
produit) :**

1. **Proposer le plan** (2-3 phrases, tradeoff, alternatives écartées). Ne pas
   commencer à coder avant validation.
2. **Attendre la validation explicite** de l'utilisateur. Citation gardée dans
   l'ADR.
3. **Créer l'ADR** avec `Kind`, `Température` (**forte** / **molle** /
   **provisoire**) et `Statut: acté` + citation, puis mettre à jour
   `docs/dev/decisions/README.md`.
4. **Seulement ensuite, coder.**

**Dérogations** (pas d'ADR nécessaire) : questions de diagnostic, lectures de
code, fixes évidents d'une ligne, commandes shell de check, appels de build/test.

**Si on revient sur une décision** : **ne pas supprimer** l'ADR. Créer une
nouvelle qui `Supersedes` l'ancienne ; l'ancienne passe en `Statut: retracté`
avec `Superseded by`. L'historique reste lisible.
