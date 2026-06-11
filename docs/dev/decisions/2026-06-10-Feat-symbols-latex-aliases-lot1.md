# Feat — Symboles lycée manquants + entrée LaTeX en alias (lot 1 table Wikipédia)

**Date :** 2026-06-10
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Feat-culture-scoped-aliases.md](2026-06-10-Feat-culture-scoped-aliases.md) (mécanisme alias), [2026-06-10-Feat-multiline-chain-eqarr-architecture.md](2026-06-10-Feat-multiline-chain-eqarr-architecture.md) (⇒/⇔ exclus : multiligne hors moteur, volontaire)

## Citation acté

> « tu peux checker ça [table Wikipédia] et faire un plan détaillé pour rajouter ce qu'il manque avec des alias (les champs latex peuvent être un alias aussi) sur les trucs cruciaux » ; validation du plan item par item, dont « je parlais d'utiliser le mot derrière l'antislash du latex en alias générique.. genre \infty => alias:infty pour que les latexiens soient pas perdus » et « surtout le % ne pas le jeter » — utilisateur, 2026-06-10

## Contexte

Audit du vocabulaire contre la table des symboles (filtre : programme lycée).
Couverture déjà large ; manques retenus après arbitrage utilisateur :
- ⇒/⇔ EXCLUS volontairement (chaînes multilignes eqArr, hors moteur) ;
- alias divise/vide/limite refusés (« pas besoin »).

## Décision

1. **`+-` / `-+`** → entrées symboles vers `pm`/`mp` + alias `plusmoins` (FR),
   `plusminus` (gén.) — `x = -b +- racine delta` → `x=-b\pm\sqrt{\delta}`.
2. **`angle`** → alias générique vers `hat` : `angle ABC` → `\widehat{ABC}`.
3. **`pgcd` / `ppcm`** → `\operatorname{…}` + alias `gcd`/`lcm`. Rendu
   « pgcd » aussi en US (assumé, à culturaliser si besoin réel).
4. **`%`** → postfixe `\%` (« surtout ne pas le jeter »).
5. **Entrée LaTeX** : le lexer AVALE l'antislash devant un mot (`\sum` ≡
   `sum`) — les clés canoniques étant les noms LaTeX, tout marche ; les noms
   divergents deviennent des alias génériques NUS : `infty`→inf, `neq`→`!=`,
   `leq`→`<=`, `geq`→`>=`, `wedge`→and, `vee`→or, `cdot`→`.`, `times`→`*`,
   `varnothing`→emptyset.
6. **Unicode collé-copié** : `≤ ≥ ≠ × ÷ ∘ ± ·` → clés symboles vers les
   entrées existantes.
7. **`parmi`** (validé après explication « oui ok ») : alias FR vers l'entrée
   `·parmi`, infixe `bracketed` qui INVERSE les arguments — « k parmi n » →
   `\binom{n}{k}` (l'oral français inverse l'écrit). `2 parmi 4` →
   `\binom{4}{2}`, `k parmi n+1` sans parenthèses superflues.

## Tradeoff & alternatives écartées

- **Alias latex actifs seulement après `\`** (proposé) : l'utilisateur veut
  les mots NUS (« pour que les latexiens soient pas perdus ») ; collisions
  sondées (`times` etc. : aucun mot français usuel).
- **Tout le contenu de la table** : post-bac écarté (∁, ⊕⊗, ≪≫, ↪↠, ∮, ℵ).

## Conséquences

- **Code touché** : `Vocabulary.cs` (entrées + alias), `Lexer.cs` (1 ligne :
  antislash avalé devant lettre).
- **Fixtures** : +27 (corpus à 359) — second degré complet, angle, pgcd,
  50%, mots latex nus et avec `\`, Unicode, parmi (FR + isolation US),
  non-régressions `rectangle`/`angles`. Zéro fixture existante modifiée.
- **Aval** : `\leq/\geq/\neq/\times/\%`… présents dans la table Symbols de
  LatexToOmml (vérifié) ; `\%` couvert par l'audit OMML via la fixture 50%.

## Validation post-fix

- Suites moteur/serialization/adapter complètes vertes, mutations comprises.
- Word : `x = -b +- racine delta`, `angle ABC`, `50%`, `\forall x \in R`.
