# ADR-007 — Trigger de conversion explicite

**Statut** : Accepté (2026-04-17)

## Contexte

Le prototype Office.js détectait la frappe de Tab via polling (fastTick à 50ms)
pour déclencher la conversion. Ce mécanisme a montré trois défauts majeurs :

1. **Polling CPU coûteux** — 20 tick/s, chacun avec un `Word.run` sync.
2. **Collisions avec undo** — après Ctrl+Z, le tab restauré re-déclenche une
   conversion, produisant une boucle infinie.
3. **Impossible de distinguer user vs programmatique** — Office.js ne donne
   aucun event "user typed X".

## Décision

Abandon du polling. La conversion est déclenchée **explicitement** par :
- Un raccourci clavier dédié (par défaut : `Ctrl+Espace`)
- Un bouton sur la ribbon
- Un bouton dans la popup de suggestions

En VSTO, ces triggers sont des events Windows (WPF keybinding, ribbon click)
qui ne peuvent pas être causés par undo — la distinction user vs programmatique
est native.

## Conséquences

**Positif :**
- Pas de boucle undo possible : l'utilisateur décide seul quand convertir.
- CPU minimal : aucun polling en tâche de fond.
- UX prévisible : "je presse Ctrl+Espace → ça convertit, point".

**Négatif :**
- Perte de la magie "je tape Tab et ça convertit sans effort". L'utilisateur
  doit apprendre le raccourci.
- Mitigation : `Ctrl+Espace` est un standard IntelliSense très courant.
- Mitigation : affichage d'une bulle "Ctrl+Espace pour convertir" à la
  première utilisation.

## Note sur la "zéro friction"

L'objectif de CLAUDE.md reste valide — mais "zéro friction" signifie
"comportement prévisible et sans surprise", pas "magie invisible qui se
trompe une fois sur deux". Un raccourci explicite est plus fluide
qu'un polling qui boucle.
