# Feat — Caractère `²` (AZERTY) traité comme `^2`

**Date :** 2026-04-24
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

Le tokenizer pré-remplace le caractère `²` (U+00B2) par la séquence `^2`
avant tokenization. Résultat : `x²` matche naturellement le pattern `power`
existant et produit `x^{2}`.

Limité à `²` uniquement — seul superscript natif sur clavier AZERTY FR (touche
au-dessus du Tab, à côté du `1`). `³ ⁴ ⁵ ...` non traités (pas au clavier).

## Pourquoi

- `²` est la notation la plus courante pour "carré" chez tous les lycéens FR,
  surtout ceux qui tapent sans Alt+code et sans pavé numérique (PAP).
- Aujourd'hui, taper `x²` laissait le `²` littéral passer — rendu Unicode en
  exposant visible mais sans structure OMath : impossible à éditer comme un
  vrai exposant dans Word.
- Le tokenizer a déjà la responsabilité de normaliser les chars math unicode
  (math italic → ASCII, `×` → `*`, etc.). Ajouter `²` → `^2` est cohérent.

## Conséquences

- `Tokenizer.Tokenize(text)` : pré-remplacement `²` → `^2` tout en haut.
- Gold examples ajoutés pour `x²`, `2x²+1`, etc.
- `³`, `⁴`, etc. non supportés pour l'instant. Si besoin remonté par un
  utilisateur, ADR séparée.
- Les tests de tokenization existants ne sont pas impactés (aucun n'utilise
  `²`).

## Alternatives écartées

- **Normalisation via `NormalizeCodepoint`** : insuffisant car renvoie un
  token unique ; le pattern `power` a besoin de 2 tokens distincts (POWER
  puis NUMBER). Le pré-remplacement textuel résout ça naturellement.
- **Pattern YAML dédié** (`IDENT SUPERTWO → {{x}}^{2}`) : duplication logique
  du pattern `power` existant ; pré-normalisation est plus DRY.

## Validé par l'utilisateur

> "y'a aussi un truc c'est ce charactere clavier \"²\" il est tres utilisé
> par les gens normaux mais pas bien géré du tout chez nous .."

> "limiter à ² c'est le seul qui est au clavier non ?"

## Statut

acté
