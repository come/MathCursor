# POC RewriteEngine — archivé 2026-05-29

Premier POC du moteur rewriting (Phase A → D-6, mai 2026). Démontré
fonctionnel (100% YAML inline, bascule prod franche). Archivé pour
repartir d'une base propre selon l'ADR
`docs/dev/decisions/2026-05-28-Refactor-rewriting-engine-v2-clean.md`.

## Ce qui était bon (= à reprendre dans la V2)

- `Category` enum + subsumption (Expr ⊃ valeurs, Set ⊃ Interval).
- `Item` (TokenItem / RewriteItem) typés.
- `Pattern` / `PatternElement` (Literal, Slot, RepeatGroup, AnyLiteral).
- `RewriteMatcher.ApplyTemplate` (= $slot + $list | join).
- Loop fixed-point + multi-phase scheduling.

## Ce qui était limité (= corrigé par l'ADR V2)

- Scheduling local glouton (= cassait `1/Somme k 0 n f(k)`).
- Pas de scan-keywords + scoping top-down.
- Pas de partial match en typing flow.
- Pas de multi-chains / beam search.
- Pas de slot `grid` 2D.
- Format YAML legacy converti (vs natif).

## Restauration

`git log` + `git checkout <sha> -- archive/poc-rewriting-2026-05-29/`
pour récupérer une implémentation de référence.
