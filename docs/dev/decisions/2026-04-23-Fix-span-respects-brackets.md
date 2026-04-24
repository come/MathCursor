# Fix — Span Ctrl+Espace respecte brackets/parens

**Date :** 2026-04-23
**Kind :** Fix
**Température :** molle
**Statut :** acté

## Décision

Dans `ComputeManualSpanStart`, les délimiteurs `;` et `,` ne coupent la span
**que** si on est hors de toute paire `[...]` ou `(...)`. Walk backward avec
suivi de profondeur.

## Pourquoi

- Cas utilisateur : `[0;+inf[` avec Ctrl+Espace après le `[` final → span
  tronquée à `+inf[` (car le `;` du milieu était vu comme fin de phrase),
  engine ne reconnaît rien. Attendu : span = `[0;+inf[`.
- Les `;`/`,` internes aux brackets sont des séparateurs math (bornes
  d'intervalle, args de fonction), pas des ruptures de phrase.

## Conséquences

- `SuggestionService.ComputeManualSpanStart` : walk backward avec compteur de
  profondeur `[...]` et `(...)`, délimiteur ignoré tant que `depth > 0`.
- `.`, `!`, `?`, `:`, newline restent toujours délimiteurs (pas de cas math
  connu où ils seraient à préserver).

## Validé par l'utilisateur

Pas de validation explicite — fix dérivé du report visuel du bug :

> "bof bizarre" (screenshot `]-∞, 0]U[0;+inf[` avec popup vide sur le fragment)

Fix appliqué comme conséquence logique du diagnostic.

## Statut

acté
