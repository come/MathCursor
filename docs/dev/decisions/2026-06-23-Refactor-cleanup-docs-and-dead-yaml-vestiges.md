# Refactor — Nettoyage post-audit : docs autoritaires réalignées + vestiges YAML morts supprimés

**Date :** 2026-06-23
**Kind :** Refactor
**Température :** molle
**Statut :** acté
**Lié à :** [2026-06-23-Refactor-delete-dead-host-contract.md](2026-06-23-Refactor-delete-dead-host-contract.md), [2026-06-23-Meta-delete-stale-cartography.md](2026-06-23-Meta-delete-stale-cartography.md), [2026-06-22-Refactor-delete-dead-latextounicodemath.md](2026-06-22-Refactor-delete-dead-latextounicodemath.md)

## Citation acté

> [2ᵉ passe d'audit — « cleanup restant »] « go les deux » (P0 docs autoritaires + P2 suppressions) — utilisateur, 2026-06-23

## Contexte

Après suppression du `core-csharp`/lattice, de `LatexToUnicodeMath`, de `cartography.md`
et du contrat host-contract, une 2ᵉ passe d'audit a trouvé : (a) des **docs autoritaires**
décrivant encore le monde disparu (dont une **skill lue avant chaque dev**), et (b) des
**vestiges YAML morts** (données + outils du moteur YAML pré-lattice).

## Décision

**Réalignement docs (P0/P1)** — réécrites sur l'archi réelle (moteur pur `engine/` +
`serialization/` ← adapter ; plus de « 3 couches / 4 interfaces ») :
- `.claude/skills/mathcursor-plan/SKILL.md` (table couches L0-L3, étape « contrats » → frontière de pureté) — **prioritaire** (invoquée avant chaque code).
- `CLAUDE.md` : « Algorithmes à porter » (table TS→C# cibles inexistantes → note « portage forest fait »), pointeur d'onboarding (ROADMAP gelé → `decisions/README` + `git log` + PLAN), `archive/officejs-prototype/` retiré (inexistant), Stack.
- `README.md` racine, `adapter-vsto/README.md` (réécrit : `ConversionController`, moteur direct, Inno Setup), `adapter-vsto/INSTALL.md` (liste projets + bandeau péremption), `adapter-vsto/installer/README.md` (payload), `docs/briefs/architecture-flow.md` (bloc couches §7 + bandeau).

**Suppressions (P2)** — vestiges du moteur YAML mort (dossier `data/yaml_domains/` déjà disparu) :
- `data/concepts/*.yml` (15) + `data/locale/*.yml` (2) — zéro référence code (vérifié : non embarqués, mentions uniquement en commentaires).
- `tools/extract_yaml_gold.py`, `tools/audit_latex_macros.py`, `tools/generate_coverage_pdf.py` — pointent tous vers `data/yaml_domains/` (absent) / `core-csharp/tests` ; non invoqués. (`tools/audit-latex-macros.md` **gardé** : cité par `WpfMathAdapter.cs:10`.)

## Tradeoff & alternatives écartées

- **Réécrire intégralement INSTALL.md / les briefs** : risque d'introduire du faux sur le flux actuel non vérifié → bandeau de péremption + correction des pièges concrets (liste de projets, bloc couches) préféré.
- **ADRs/briefs datés liant l'ancien monde** : non touchés (convention d'immuabilité).

## Conséquences

- **Différé (hors batch, décision user requise)** : `docs/release/` (ClickOnce v0.1.0.0 trackée, dont `MathCursor.Core.dll.deploy`) — possible endpoint d'auto-update ClickOnce des bêtas installées ; ne pas supprimer sans confirmer. `docs/briefs/detection-ner.md` (NER non branché en beta) — bandeau à ajouter plus tard.
- **À surveiller (déjà signalé)** : `.sln` partiel (3 projets vivants hors sln), `host-contract` réduit à 1 type, `docs/demo/` (~17 Mo build commité), `PLAN.md` (plan exécuté).
- Gate inchangé (les YAML/outils supprimés ne sont ni embarqués ni testés).
