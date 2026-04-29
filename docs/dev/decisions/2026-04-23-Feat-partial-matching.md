# Feat — Matching préfixe (propositions partielles)

**Date :** 2026-04-23
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

Quand aucun pattern complet ne matche le span courant, on tente un match
"préfixe" sur **tous** les patterns : on accepte qu'ils se terminent avant la
fin de leurs éléments si l'input utilisateur s'est épuisé. Les slots non
atteints sortent en `\ldots`, coloriés en **rouge** dans la popup pour signaler
à l'utilisateur qu'il reste à taper ces bouts-là.

## Pourquoi

- Cas d'usage observé : l'utilisateur tape `]-inf` et déclenche Ctrl+Espace →
  aucun pattern d'intervalle ne matche (tous exigent les deux bornes). Popup
  vide (`∅`). Frustrant et pédagogiquement mauvais ("ça marche pas").
- Alternative écartée : écrire des patterns "partiels" dédiés (`]-inf` →
  `]-\infty`). Rejetée car duplication des dizaines de patterns existants.
- L'approche préfixe **réutilise** les patterns existants — zéro maintenance
  nouvelle, chaque pattern ajouté bénéficie automatiquement du mode partiel.

## Conséquences

- `PatternMatcher.TryMatchPrefix` : variante de `TryMatch` qui accepte les
  fins prématurées. `MatchResult.IsPartial` indique si on a dû court-circuiter.
- `TemplateRenderer` : si `IsPartial`, les slots non capturés sont rendus
  `\ldots` au lieu du littéral `{{name}}`.
- `PatternEngine.ConvertPartials` : appelé uniquement si aucun candidat complet
  ne sort ; top 3 par tokens consommés.
- `SymbolChoice.IsPartial` + rendu popup : wrap `\ldots` en
  `\color{red}{\ldots}` via WpfMath, label `…` au lieu du score numérique.
- **Option (a) retenue** : partiels affichés **seulement** si aucun complet.
  Évite le bruit (patterns complets ne sont pas noyés par les partiels).
- Limite connue : `generic_expression` (match `EXPR:full`) est très greedy et
  absorbe des entrées comme `lim x` comme littéral, donc les partials ne se
  déclenchent pas dessus.

## Validé par l'utilisateur

Conception discutée :
> "peux t'on faire, si pas de complet trouvé, on fait une boucle sur tout pour
> voir si on a pas un bout de pattern.. et on rajoute "..." le entrée, fait
> clignoter en rouge "dou" le pattern dans le wpf pour signaler qu'il faut
> finir le pattern ? tu vois le genre"

Option (a) explicitement retenue sur demande de confirmation :
> "a"

## Statut

acté (MVP sans animation clignotante, rouge fixe). Tests : 4 tests dédiés
`PartialMatchingTests.cs` + auto-tests gold examples.
