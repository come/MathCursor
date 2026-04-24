# Meta — Process de décision rappelé dans CLAUDE.md

**Date :** 2026-04-24
**Kind :** Meta
**Température :** forte
**Statut :** acté

## Décision

Ajout d'une section **"Process de décision"** à la fin de `CLAUDE.md` qui
rappelle les 4 étapes avant toute modification non-triviale :

1. Proposer le plan (tradeoff + alternatives écartées)
2. Attendre validation explicite
3. Créer l'ADR (Kind, Température, citation)
4. Ensuite coder

Plus : la règle "ne jamais supprimer un ADR, on supersede".

Combiné à la mémoire agent `reference_decisions_log.md` qui porte le même
message côté persistance inter-sessions. **Pas de hook shell** pour ne pas
polluer les interactions rapides (questions, lectures, commandes de check).

## Pourquoi

- Sans rappel structurel, l'agent dérive : il voit un problème, il le corrige,
  pas d'ADR. Le journal devient inégal, certaines décisions n'ont jamais
  transité par une validation explicite.
- `CLAUDE.md` est chargé à chaque session → le rappel est mécanique, visible
  dans le repo, auditable via git.
- La mémoire agent complète côté continuité inter-conversations (même agent,
  plusieurs sessions).
- Hook shell écarté volontairement : "quelle est la valeur de WdOMathJc ?"
  ne doit pas déclencher un process ADR.

Température **forte** : c'est une règle de travail structurelle qui régit
comment toutes les autres décisions sont prises. Changer ce process demande
une réflexion explicite, pas un pivot silencieux.

## Conséquences

- `CLAUDE.md` a une nouvelle section finale "Process de décision".
- Dérogations explicites : diagnostic, lectures, fixes d'une ligne, commandes
  de check — pas d'ADR requis.
- La mémoire `reference_decisions_log.md` contient déjà le workflow et la
  règle "ne supprime jamais un ADR".

## Alternatives considérées

- **Skill `/adr`** user-invocable pour drafter un stub ADR. Écartée pour
  l'instant (l'utilisateur préfère qu'on ajoute à la demande, pas via slash
  command). Peut être ajoutée plus tard comme bonus.
- **Hook `UserPromptSubmit`** qui injecterait le rappel à chaque prompt.
  Écartée : trop bruyant, pénalise les demandes légitimes qui n'ont pas
  besoin d'ADR.

## Validé par l'utilisateur

Question d'architecture :
> "comment on met en place un skill ou autre pour respecter le process sur
> chaque demande ? c'est quoi l'approche ?"

Réponse après proposition des 3 leviers :
> "1+2 bien on fera l'adr plus tard"

(1 = CLAUDE.md, 2 = mémoire — les deux déjà en place après cet ADR. Skill
`/adr` repoussé à plus tard.)

## Statut

acté
