---
name: mathcursor-adr
description: Crée un ADR au format MathCursor (Kind + Température + Statut + Supersedes + citation utilisateur) dans docs/dev/decisions/YYYY-MM-DD-Kind-slug.md, et met à jour l'index docs/dev/decisions/README.md. Utilise quand l'utilisateur dit "fais l'ADR", "crée un ADR", "note ça dans une ADR", ou après validation explicite d'un plan via /mathcursor-plan.
user-invocable: true
allowed-tools:
  - Read
  - Write
  - Edit
  - Glob
  - Bash
  - AskUserQuestion
---

# /mathcursor-adr — Création d'ADR MathCursor

Génère un ADR au **format projet** (différent du `docs/adr/NNN-*.md` du brief). Notre format :
- `docs/dev/decisions/YYYY-MM-DD-<Kind>-<slug>.md`
- En-tête avec **Kind**, **Température** (forte/molle/provisoire), **Statut**, **Supersedes/Superseded by**
- Section "Validé par l'utilisateur" avec citation littérale
- Index dans `docs/dev/decisions/README.md` mis à jour

Format spec complet : [`docs/dev/decisions/2026-04-24-Meta-adr-format.md`](../../docs/dev/decisions/2026-04-24-Meta-adr-format.md).

---

## Étape 1 — Recueillir les inputs

Si l'utilisateur n'a pas donné un titre court, demande via AskUserQuestion :
- **Titre** (1 ligne, descriptif)
- **Kind** : `Feat` / `Fix` / `UX` / `Release` / `Meta` / `Test` / `Refactor` / `Limit`
- **Température** : `forte` / `molle` / `provisoire`
- **Supersedes** : ID d'un ADR remplacé (optionnel)

Date = aujourd'hui (extraite de `Bash: date +%Y-%m-%d` si pas fournie).

**Citation utilisateur** : récupère depuis la conversation récente le message validation explicite (« ok », « go », « valide », ou phrase complète). C'est obligatoire pour `Statut: acté`.

---

## Étape 2 — Construire le slug + nom de fichier

Slug = titre kebab-case, max 6-7 mots, descriptif.
- ✅ `extensibility-axes-abstractions`
- ✅ `ghost-doc-invisible`
- ✅ `mc0006-mc0009`
- ❌ `refactor` (trop générique)
- ❌ `the-new-feature-that-we-discussed` (trop long)

Nom complet : `<YYYY-MM-DD>-<Kind>-<slug>.md` (ex: `2026-05-13-Meta-mc0006-mc0009.md`).

---

## Étape 3 — Écrire le fichier

Squelette à adapter (à NE PAS coller mot pour mot — remplis chaque section avec le vrai contenu) :

```markdown
# <Kind> — <Titre court descriptif>

**Date :** YYYY-MM-DD
**Kind :** <Feat | Fix | UX | Release | Meta | Test | Refactor | Limit>
**Température :** <forte | molle | provisoire>
**Statut :** acté
**Supersedes :** <lien vers ADR remplacé, ou "—">
**Lié à :** <ADRs / briefs / commits liés>

## Citation acté

> « <citation user exacte> » — utilisateur, YYYY-MM-DD

## Contexte

[Pourquoi cette décision est nécessaire. Inclure les bugs ou besoins observés.]

## Décision

[Ce qui est choisi. Sections sous-titrées si plusieurs aspects.]

## Tradeoff & alternatives écartées

- **<Alt A>** : raison du rejet (qualité/perf/extensibilité, jamais "temps").
- **<Alt B>** : raison du rejet.

## Conséquences

- **Code touché** : fichiers + lignes principales
- **Tests** : couverture (analyzer, core, adapter) — nombre verts/totaux
- **API publique** : impacté ? rétro-compat ?
- **Règles MC impactées** : promues, démotées, suppressions ADR liées ?

## Validation post-fix

[Comment vérifier que c'est OK. Si test auto impossible → observation user.]

## Plan en cours — état d'avancement

[Si l'ADR fait partie d'un plan plus large (refacto archi étapes 1-8, harnais
phases 0-9), insérer la checklist à jour.]
```

---

## Étape 4 — Mettre à jour l'index

Édite `docs/dev/decisions/README.md` :
- Si la date du jour n'a pas encore de section `### YYYY-MM-DD` → la créer en tête de l'index chronologique.
- Sinon, ajouter une ligne sous la section existante au bon ordre (plus récent en haut).

Format de l'entrée :
```markdown
- `[<température>]` <Kind> — [<Titre court>](<YYYY-MM-DD>-<Kind>-<slug>.md) — <résumé 1 ligne avec le bénéfice tangible>
```

Exemple :
```markdown
- `[molle]` Meta — [Règles MC0006 (splice LaTeX) + MC0009 (SuppressMessage sans ADR)](2026-05-13-Meta-mc0006-mc0009.md) — Phase 2.5 du harnais, MC0006 capture l'anti-pattern du bug double-wrap (4 hits réels), 16 nouveaux tests verts
```

---

## Étape 5 — Si l'ADR retracte / supersedes un ancien

Mettre à jour aussi l'ADR remplacé :
- `Statut: retracté`
- `Superseded by: <lien vers nouveau>`
- Optionnellement : note en haut du contenu expliquant pourquoi.

Et dans l'index : marquer l'ancien avec `` `retracté` ``.

---

## Étape 6 — Confirmer

Affiche à l'utilisateur :
- Chemin du fichier créé
- Entrée d'index ajoutée
- Lien(s) Supersedes/Lié à si pertinent

Pas de commit ici — c'est l'utilisateur qui décide quand committer.

---

## Garde-fous

- **JAMAIS d'ADR sans citation utilisateur** validant la décision. Si l'utilisateur a juste dit « ok » sans contexte, citer le « ok » + référencer le message précédent qui pose la décision.
- **Statut: proposé** si pas de validation explicite encore. L'utilisateur passera à `acté` plus tard.
- **Format de date ISO** : `YYYY-MM-DD`. Pas de format US.
- **Pas de remplissage générique** : chaque section a un contenu concret lié à la décision. Une section "Conséquences" qui dit "0 changement, 0 régression" sans détails ne sert à rien — précise les fichiers / tests / commits.
- **Pas de TODO / commentaires de placeholder** dans l'ADR final. Si une section est vide, l'omettre.
