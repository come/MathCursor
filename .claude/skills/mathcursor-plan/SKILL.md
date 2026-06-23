---
name: mathcursor-plan
description: Cadre le plan d'implémentation d'une feature/refacto MathCursor avant tout code. Force l'identification de la couche cible (L0/L1/L2/L3), liste les trade-offs UNIQUEMENT en termes de qualité+perf (jamais "vitesse d'implémentation"), identifie les règles MC pertinentes, et propose un découpage en étapes. Utilise systématiquement avant d'écrire du code non-trivial.
user-invocable: true
allowed-tools:
  - Read
  - Grep
  - Glob
  - AskUserQuestion
---

# /mathcursor-plan — Plan d'implémentation MathCursor

**Principe non-négociable** : ce plan vise **qualité + performance**. Le temps d'implémentation IA n'est **pas** une dimension considérée. Si une option moins ambitieuse est proposée, elle doit être justifiée par une raison non-temporelle (spike jetable, baseline perf, mock test, contrainte externe documentée).

Refuse explicitement toute formulation "rapide vs propre" — si l'utilisateur ou un agent précédent l'a introduite, redresse la conversation : « voici l'option qualité ; voici les contraintes qui pourraient justifier de la dégrader ».

---

## Étape 1 — Identifier la couche

Demande explicitement quelle couche est concernée :

| Couche | Description | Exemples |
|---|---|---|
| **L0** — Données | Lexiques, cultures, corpus NER, fixtures de test | `data/engine/*.json`, `engine/tests/.../fixtures.json` |
| **L1** — Moteur PUR | Lexer, parser « forest », scorer, renderer LaTeX, sérialisation OMML | `engine/MathCursor.Engine/`, `serialization/MathCursor.Serialization/` (netstandard2.0, zéro Word/WPF) |
| **L2** — Adapter VSTO | Orchestration, interop Word/OMath, pipeline commit, NER, SourceMap, edit mode | `adapter-vsto/src/MathCursor/Host/` |
| **L3** — UI WPF | Popup suggestion, ribbon, fenêtres, mode édition | `adapter-vsto/src/MathCursor/UI/` |

Si la feature couvre plusieurs couches : **décompose en sous-features par couche**. Une sous-feature = un plan séparé.

---

## Étape 2 — Respecter la frontière de pureté

Le **moteur** (`engine/`, `serialization/`) est PUR : `netstandard2.0`, zéro
`Microsoft.Office.*`, zéro WPF, sans état global mutable. C'est ce qui rend la
phase 2 (Office.js / Mac / Web) possible — la démo WASM réutilise ce moteur tel
quel. L'adapter l'appelle **en direct** : il n'y a pas d'interface d'inversion
(l'ancien contrat `host-contract` à 4 interfaces a été supprimé, ADR
2026-06-23 ; il ne reste que le DTO `EquationHandle`).

→ Si la feature touche L1 : vérifier qu'aucune dépendance plateforme ne fuit dans
le moteur. Si elle a besoin de Word, c'est qu'elle est en réalité L2.

---

## Étape 3 — Trade-offs qualité-orientés uniquement

Liste 2-3 alternatives d'implémentation. Pour chacune, évalue sur :

- **Performance runtime** (complexité algo, latence perçue dans le pipeline)
- **Performance mémoire** (allocations, cycle de vie)
- **Testabilité** (mockable, déterministe, edge cases couvrables)
- **Robustesse** (comportement sur entrée mal formée, ambiguïté, vide)
- **Extensibilité** (ajouter une notation/construction sans réécrire le pipeline)
- **Impact sur le scoring/classement des candidats** (si le parser ou le coût est concerné)
- **Conformité MC0001-MC0009** (règles MC du harnais)
- **Tests à ajouter au corpus** (`engine/tests/.../fixtures.json` pour le moteur, `adapter-vsto/tests/` pour l'adapter)

**Interdit dans cette section** : "temps d'implémentation", "effort", "rapidité de mise en œuvre", "simplicité du code" comme synonyme de raccourci. Si tu te surprends à formuler "X est plus simple/rapide à coder", reformule : "X dégrade Y dimension qualité ; on accepte ce trade-off si Z".

Recommandation finale : choix + justification ancrée dans les critères ci-dessus + trade-off explicitement accepté.

---

## Étape 4 — Règles MC pertinentes (anti-patterns à éviter)

Liste les règles MC actives qui pourraient déclencher sur cette feature :

| ID | Smell | Quand vigilance |
|---|---|---|
| MC0001 | Regex sur XML/OMath | Si tu touches du WordOpenXML, OMath, MathML |
| MC0006 | Splice LaTeX sur texte rendu | Si tu concatènes des `Substring` de LaTeX déjà rendu (anti-pattern double-wrap) |
| MC0009 | SuppressMessage sans ADR | Si tu prévois de supprimer un diagnostic |

Pour chaque règle pertinente, anticipe : conformité par construction OU `SuppressMessage` avec ADR (lequel ?).

---

## Étape 5 — Plan d'étapes numérotées

Format :

```
1. <Action> dans <fichier>:<ligne>
   - Couche : L<n>
   - Risque MC : <id> ou "none"
   - Test à ajouter : <fichier de test ou "couvert par X">
   - Bench/mesure : <si perf touchée>

2. ...
```

Une étape = une modification atomique testable. Si tu mets "implémente la feature complète" en 1 étape, c'est trop gros — décompose.

---

## Étape 6 — ADR si nécessaire

Critères qui imposent un ADR :
- Nouveau contrat / interface
- Choix d'archi avec alternatives non triviales
- Suppression / dépréciation de règle MC
- Nouvelle dépendance externe
- Dégradation volontaire de qualité (spike, baseline) — l'ADR doit alors documenter la fenêtre de réversibilité

→ Propose : « ADR `<Kind>-<slug>` à créer via `/mathcursor-adr` après validation ».

Sinon, pas d'ADR nécessaire — c'est une modif locale traçable par le commit.

---

## Sortie

Markdown structuré, **à valider explicitement par l'utilisateur** avant tout code. Format type :

```markdown
## Plan : <titre court>

**Couche** : L<n>
**Contrats touchés** : <liste ou "aucun nouveau">
**Règles MC pertinentes** : <liste ou "aucune">

### Trade-offs
[tableau qualité-orienté]

### Recommandation
[choix + justification]

### Étapes
1. ...
2. ...

### ADR
[à créer / pas nécessaire]
```

Attends validation utilisateur (« ok », « go », ou correction) avant de toucher au code.
