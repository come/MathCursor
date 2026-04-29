# Meta — Format des ADR + température de décision

**Date :** 2026-04-24
**Kind :** Meta
**Température :** molle
**Statut :** acté

## Décision

Chaque décision produit ou technique du projet vit dans un fichier dédié sous
`docs/dev/decisions/`, nommé `YYYY-MM-DD-<Kind>-<slug>.md`, avec un header
standardisé incluant une **température** qui indique à quel point la décision
est structurante vs révocable.

### Kinds

- `Feat-` : nouvelle feature
- `Fix-` : correction de bug ou fix comportement
- `UX-` : choix ergonomie / produit
- `Release-` : bump de version et contenu d'une release (pas de température)
- `Meta-` : décision sur le processus lui-même (format ADR, conventions…)

### Température

- **forte** — structurelle, coûte cher à changer. Interface publique, règle
  produit fondamentale, contrat avec l'extérieur. On ne revient dessus qu'avec
  un très bon argument.
- **molle** — décision de travail, on y tient mais ouverte à révision si bon
  argument. Choix d'implémentation, ergonomie, patterns. Default pour la
  plupart des décisions courantes.
- **provisoire** — "on fait comme ça aujourd'hui mais on reverra". MVP,
  scaffold en attendant une brique manquante, hypothèse à confirmer. À
  re-examiner activement (pas juste "au cas où").

### Statut

- **proposé** — ADR rédigée, en attente de validation utilisateur.
- **acté** — validée explicitement (citation dans "Validé par l'utilisateur").
- **retracté** — décision abandonnée ou remplacée (voir "Superseded by").

### Workflow

1. Je (l'agent) propose un plan → je rédige un brouillon d'ADR avec
   **Statut : proposé** et **Température : à valider**. Tu valides ou amendes.
2. Tu valides → **Statut : acté**, **Température : <choisie>**. La citation
   de validation va dans la section "Validé par l'utilisateur".
3. Plus tard, si on revient dessus → **nouveau** fichier ADR qui `Supersedes`
   l'ancien. L'ancien passe en `Statut: retracté` + `Superseded by: <lien>`.
   Rien n'est supprimé, l'historique reste lisible.

### Template de header

```markdown
# <Kind> — <titre court>

**Date :** YYYY-MM-DD
**Kind :** Feat | Fix | UX | Release | Meta
**Température :** forte | molle | provisoire
**Statut :** proposé | acté | retracté
**Supersedes :** (optionnel, lien vers l'ADR remplacée)
**Superseded by :** (rempli a posteriori si remplacée)
```

### Sections standard

1. **Décision** (1-2 phrases)
2. **Pourquoi** (contexte, contraintes, alternatives écartées)
3. **Conséquences** (ce qui change dans le code/produit)
4. **Validé par l'utilisateur** (citation ou paraphrase explicite)
5. Éventuellement : **Alternatives considérées**, **Notes de révision**

## Pourquoi

- Le journal monolithique `decisions.md` était peu lisible et sans convention
  claire de "à quel point c'est acté".
- Sans notion de température, impossible de distinguer une brique de protocole
  bétonnée ("on utilise CustomXMLParts pour stocker les sources") d'un choix
  quotidien qu'on peut renverser demain ("le lien popup dit 'Signaler une
  erreur'").
- Un ADR par fichier permet le *diff* git clair, les liens croisés
  `Supersedes`, et une navigation par date/kind via le nom de fichier.
- Format léger exprès : pas de template à 15 sections, juste ce qui se lit en
  30 secondes et sert à toi (valider) et à un autre (comprendre).

## Conséquences

- `docs/dev/decisions/README.md` sert d'index chronologique annoté des
  températures.
- Labellage rétroactif des 11 ADR déjà écrites (dans la même passe que cet ADR).
- Memory agent : pointeur interne mis à jour pour rappeler d'inclure
  Température + Statut dans toute nouvelle entrée.

## Validé par l'utilisateur

Brief initial :
> "en fait je veux pouvoir mettre en place une architecture de projet, ou je
> valide les plans et pour m'aider à valider, ou un autre il puisse voir les
> ADR (un peu) qui sont dans les decisions j'aimerai meme voir une temperature
> de decisions. decision forte / molle / on fait comme ca aujourd'hui mais
> potentiellement plus tard on change"

Approbation de l'approche proposée :
> "oui laisse supprimé et je valide l'approche"

## Statut

acté
