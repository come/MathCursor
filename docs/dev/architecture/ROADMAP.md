# ROADMAP — État des chantiers MathCursor

**Dernière mise à jour** : 2026-05-13
**Audience** : moi-future / Claude Code re-démarré / contributeur tiers

Document unique d'orientation. Lecture en 2 minutes → état complet des
plans en cours. **Mis à jour à chaque fin de sous-livraison.**

---

## Comment se mettre dans le bain (nouvel agent)

1. **CLAUDE.md** racine — contexte produit + stack + règles dev + process décision
2. **Ce fichier ROADMAP.md** — état des chantiers en cours
3. **`docs/dev/architecture/cartography.md`** — image archi détaillée par fichier, dette identifiée
4. **`docs/dev/decisions/README.md`** — index chrono de tous les ADRs (≥ 50 décisions)
5. **`git log --oneline -30`** — contexte des derniers commits
6. **Skills disponibles** :
   - `/mathcursor-plan <sujet>` — force le bon process de planification (couche + tradeoff qualité + règles MC)
   - `/mathcursor-adr` — génère ADR au format projet
   - `/deploy-prod` — pipeline release
   - `/build-iss` — build installer

---

## Branches et état git

| Branche | État | Note |
|---|---|---|
| `main` | référence stable | derniers releases |
| `refactor/big-bang-clean-arch` | **active** | refacto DDD + extensibilité en cours (toutes les sessions 2026-05-13 ici) |
| `multiline-systems` | obsolète | mergée |

---

## Chantier 1 — Refacto archi extensibilité (5 axes)

**Brief source** : `MATHCURSOR_ARCHITECTURE_EXTENSIBILITE.md` (téléchargé 2026-05-13, archivé hors repo).
**ADR-clé** : [`2026-05-13-Meta-extensibility-axes-abstractions.md`](../decisions/2026-05-13-Meta-extensibility-axes-abstractions.md)

### Étapes du brief

- [x] **Étape 1** — Cartographie (`docs/dev/architecture/cartography.md`)
- [x] **Étape 2** — Projet `MathCursor.Core.Abstractions/` avec 6 contrats (`IConstructStrategy`, `IDomainParser`, `ILocaleLexer`, `ILocaleNER`, `IOutputSerializer<TFormat>`, `ParseContext`)
- [ ] **Étape 3** — Implémentation des contrats par types existants (`LatticeEngine` → `IDomainParser`, etc.) — **OPTIONNEL**, déclencher si extension de domaine ou format effective
- [x] **Étape 4** — Visitor sur AST (`IAstVisitor<TResult>` + 18 `Accept` overrides + `LatexRenderingVisitor`). ADR [`Refactor-ast-visitor`](../decisions/2026-05-13-Refactor-ast-visitor.md)
- [ ] **Étape 5** — Sortir chaînes FR du Core → `locales/fr/keywords.yaml` + `FrenchLocaleLexer`. **Active MC0002** à ce stade
- [ ] **Étape 6** — `DomainRouter` (placeholder math-only)
- [ ] **Étape 7** — `ShortcutResolver` (overlay YAML user `~/.mathcursor/shortcuts.yaml`)
- [ ] **Étape 8** — Test d'intégration extensibilité (EmptySetStrategy + sérialiseur Unicode factice + raccourci user)

### Refacto source-mutation des pins sidecar (sous-chantier)

**ADR-clé** : [`2026-05-13-Refactor-source-mutation-pins-sidecar.md`](../decisions/2026-05-13-Refactor-source-mutation-pins-sidecar.md)
**Objectif** : éliminer le hit MC0006 sur `ZoneResolver:205` (splice latex anti-pattern racine).

- [x] **S0** — Extraction Strategy `IAmbiguityScanner` + Pipeline. ADR [`Refactor-ambiguity-scanners-strategy`](../decisions/2026-05-13-Refactor-ambiguity-scanners-strategy.md). Commit `e5b8ee7`
- [x] **S1** — Mutations vec/paren sur `ScanUppercaseSequences` source-based. ADR [`Refactor-s1-twoupper-source-mutations`](../decisions/2026-05-13-Refactor-s1-twoupper-source-mutations.md). Fixe bug 06-05 single-line
- [ ] **S2** — `ApplyAllMutations` étendu (cross-merge multi-ligne, pins sidecar, offset tracking Pin v2)
- [ ] **S3** — Élagage splice loop + retirer `MC0006` du `WarningsNotAsErrors` Core.csproj + bench perf

---

## Chantier 2 — Harnais d'analyse statique

**Brief source** : `MATHCURSOR_HARNESS_BRIEF.md` (téléchargé 2026-05-13).

### Phases

- [x] **Phase 0+1** — Projet `analyzers/MathCursor.Analyzers/` + règle MC0001 (Regex sur XML structuré). ADR [`Meta-harness-phase-0-1-mc0001`](../decisions/2026-05-13-Meta-harness-phase-0-1-mc0001.md). Commit `fc3e1da`
- [x] **Phase 2** — `Directory.Build.props` racine généralise l'analyzer à tous les projets. Commit `2845fdd`
- [x] **Phase 2.5** — Règles MC0006 (splice LaTeX anti-pattern racine) + MC0009 (SuppressMessage sans ADR). ADR [`Meta-mc0006-mc0009`](../decisions/2026-05-13-Meta-mc0006-mc0009.md). Commit `b3d3c79`
- [x] **Phase 3** — Skills `/mathcursor-plan` + `/mathcursor-adr` versionnées via `.claude/skills/`. Commit `70a45e6`
- [ ] **Phase 4** — Règles MC additionnelles : MC0002 (VSTO leak in Core, post-étape 5 axes) + MC0003 (chaîne `if/else` sur discriminant de type) + MC0005 (god method) — voir brief pour la liste complète
- [ ] **Phase 5** — Diff summarizer Python (SARIF + filtres anti-bruit + rapport markdown classifié + pre-commit hook)
- [ ] **Phase 6** — Sources Tier 2 (T2-NEWTYPE, T2-NEWDEP, T2-COMPLEXITY-DELTA, T2-PUBLIC-SURFACE, T2-MAGIC-LITERAL, T2-NOVEL-PATTERN)
- [ ] **Phase 7** — Sources Tier 3 (T3-COVERAGE-GAP, T3-DOC-DENSITY, T3-NAMING-DEVIANCE)
- [ ] **Phase 8** — **Agents formels** : `architecte`, `developpeur`, `auditeur`, `harness-maintainer` avec configs Claude Code dans `.claude/agents/` (versionné via `.gitignore` exception déjà en place — cf. brief §"Couche 3")
- [ ] **Phase 9** — Boucle feedback (verdicts user + recalibration auto des poids des signaux)

### Règles MC actives

| ID | Smell | Severity | Hits actuels |
|---|---|---|---|
| MC0001 | Regex sur XML/OMath/MathML | warning | 6 Core (`LatexToUnicodeMath`) + 1 test + 10 Adapter (`WpfMathAdapter`, `MixedLatexRenderer`, `OMathParaJcPatcher`) |
| MC0006 | Splice LaTeX sur texte rendu | warning | 2 Core (`ZoneResolver:205` — éliminés par S2+S3) + 2 test (légitimes, ADR SuppressMessage à venir) |
| MC0009 | SuppressMessage sans ADR | warning | 0 |

`WarningsNotAsErrors` côté `MathCursor.Core.csproj` : `MC0001;MC0006` (à retirer au fil du nettoyage).

---

## Chantier 3 — Dette de cleanup post-refacto S0

- [ ] **Cleanup S0.7+** : déplacer le vrai code des `Scan*` `internal static` de `AlternativeGenerator` vers les classes scanners (delegation actuelle remplacée par implémentation autonome). Trigger : quand on doit modifier le comportement d'un scanner (typiquement S1 sur `UppercaseSequencesScanner`)
- [ ] **Refacto `LatexToUnicodeMath`** : Regex → XDocument ou parser dédié, élimine 6 MC0001
- [ ] **Refacto `OMathParaJcPatcher`** : Regex → XDocument, élimine 5 MC0001
- [ ] **Refacto `WpfMathAdapter` / `MixedLatexRenderer`** : élimine 5 MC0001 résiduels
- [ ] **`SuggestionService.cs`** (~1775 lignes) : poursuite extraction DDD si volonté de descendre sous 500 lignes
- [ ] **`AlternativeGenerator.cs`** : passera ~1100 → ~30-50 lignes une fois S0 cleanup fait (déplacement vrai code)

---

## Chantier 4 — Conventions et persistance

- [x] **Format ADR** : `docs/dev/decisions/YYYY-MM-DD-Kind-slug.md` avec Kind + Température + Statut + Supersedes + citation user (ADR [`Meta-adr-format`](../decisions/2026-04-24-Meta-adr-format.md))
- [x] **CLAUDE.md** racine : process décision + règles dev (cf. `## Process de décision`)
- [x] **ROADMAP.md** (ce fichier) : index unique des chantiers, mis à jour à chaque sous-livraison
- [x] **Skills user-invocable** versionnées : `/mathcursor-plan`, `/mathcursor-adr`, `/deploy-prod`, `/build-iss` dans `.claude/skills/`
- [ ] **Mémoire claude** : pointer vers ROADMAP.md depuis `~/.claude/projects/.../memory/MEMORY.md`

---

## Pour un Claude qui reprend la session

**Question 1 : « Sur quoi je travaillais ? »**
→ Lire les 10 derniers commits (`git log --oneline -10`) + cette ROADMAP. La dernière sous-livraison fermée a son commit hash listé.

**Question 2 : « Quelle est la prochaine étape ? »**
→ Cocher dans ROADMAP la première case `[ ]` non cochée du chantier en cours. Si plusieurs chantiers ouverts, demander à l'utilisateur de prioriser.

**Question 3 : « Quel ADR sert de référence pour cette étape ? »**
→ Liens dans chaque section de la ROADMAP. Tous les ADRs récents contiennent une section "Plan d'exécution" ou "Plan refacto — état d'avancement".

**Question 4 : « Comment je commence proprement ? »**
→ Invoquer `/mathcursor-plan <sujet>`. La skill force : couche cible + trade-offs qualité-orientés (jamais "temps") + règles MC pertinentes + plan en étapes numérotées + ADR si nécessaire.

**Question 5 : « Comment je documente ma décision ? »**
→ Invoquer `/mathcursor-adr` après validation utilisateur. Cite la citation user explicite.

---

## Mises à jour de cette ROADMAP

- **Quand** : à la fin de chaque sous-livraison (= chaque commit qui change l'état d'avancement).
- **Quoi** : cocher les cases `[ ]` → `[x]`, ajouter le hash du commit, ajuster la "Dernière mise à jour" en haut.
- **Comment** : éditer ce fichier dans le même commit que la sous-livraison.
