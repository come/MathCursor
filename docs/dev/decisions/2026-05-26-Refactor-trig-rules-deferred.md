# Refactor — Règles trig (sin/cos/...) différées

**Date :** 2026-05-26
**Kind :** Refactor
**Température :** provisoire
**Statut :** proposé
**Supersedes :** —
**Lié à :**
- ADRs Phase D (RewriteEngine).
- Brief utilisateur 2026-05-26 « pour sin x+1 on est dans le même format que limite ».

## Citation

> « pas besoin d'alias je pense si la regle de dire les 3 premiers caracteres envoie le matching ca fonctionne.. pour sin x+1 on est dans le meme format que limite pour moi .. sin expr ou sin(expr) devrait etre la meme matching » — utilisateur, 2026-05-26

## Contexte

L'utilisateur a deux propositions valides :

1. **Prefix-match dynamique** : `som`, `inte`, `ome` (≥3 chars) résolus automatiquement vers leurs keywords complets sans alias YAML statique. Vise à réduire le YAML.

2. **`sin expr` au format `lim`** : `sin x+1` rendrait `\sin x+1` comme `lim x 0 f(x)` rend `\lim_{x \to 0} f(x)`. Symétrie de design.

## Problème observé sur la proposition 2

L'ajout d'un fichier `data-v2/concepts/trig.yml` avec des règles `\sin {body}`, `\cos {body}`, etc. **casse le `MathEngine.Resolve` actuel** (= legacy). Concrètement :

- Test `CosXBugProbeTests.Cos_paren_x_squared_collé` attend `cos(x)2` → `\cos(x)^{2}` via le `LetterSupSubDetector` legacy.
- Avec la règle YAML `\cos {body}`, MathEngine matche d'abord `\cos (x)` (= body greedy absorbe le groupe parens), puis `2` reste flottant → output `\cos (x)2`.

Cause : `MathEngine.Resolve` et `RewriteEngine.Resolve` partagent le même chargement YAML. Une règle ajoutée affecte les 2.

## Décision

**Différer** l'ajout des règles trig en YAML jusqu'à la **bascule prod** complète (= Phase D-6 finalisée). À ce moment-là, `MathEngine.Resolve` aura été remplacé par `RewriteEngine.Resolve` qui gère correctement la composition `sin x+1` + collisions superscript ailleurs.

Pour la proposition 1 (prefix-match dynamique) : à concevoir séparément. Demande modifier le tokenizer ou ajouter un mécanisme de "prefix Literal" au matcher rewriting. Coût estimé : ~30 LOC.

## Conséquences

- Aucun fichier `trig.yml` ajouté pour V1.
- Comportement `sin x+1` reste inchangé dans MathEngine actuel.
- Le RewriteEngine POC ne couvre PAS `sin x+1` aujourd'hui, mais le mécanisme général supporterait la règle dès qu'on l'ajoute (= après bascule).

## Quand reprendre ce brief

- Après bascule prod RewriteEngine (= Phase D-6 finalisée).
- Pour activer `sin x+1`, ajouter `data-v2/concepts/trig.yml` + ajuster les tests legacy `cos(x)2` qui dépendent du `LetterSupSubDetector`.
- Pour activer prefix-match dynamique, concevoir un mécanisme `Literal.PrefixMatch: int (minLen)` dans le matcher rewriting OU une extension du tokenizer.
