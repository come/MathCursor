# Refactor — Barres `|x|` / `||v||` rendues via l'opérateur vocab `abs`/`norm` (fin du nœud `delim`)

**Date :** 2026-06-15
**Kind :** Refactor
**Température :** molle
**Statut :** acté
**Lié à :** [2026-06-10-Fix-tight-decorations-nesting.md](2026-06-10-Fix-tight-decorations-nesting.md) (le `abs z-conjz` du même chemin), audit moteur 2026-06-15

## Citation acté

> « quand je disais enleve, c'est considère que l'utilisateur va taper NORM ou ABS et jamais | qui est trop dur à aller chercher en premiere intention donc on GARDE la fixtures absolument mais on la sort de l'engine/parser » puis « oui faisons les barres / norm etc c'est le plus trivial » — utilisateur, 2026-06-15.

## Contexte

L'audit du moteur a relevé que le rendu des valeurs absolues / normes existait **en double** :
- forme-mot (`abs x`, `norm v`) → délégué `RenderFn` dans `Vocabulary` (`Vocab["abs"]`/`Vocab["norm"]`), chemin générique propre ;
- forme-barre (`|x|`, `||v||`) → nœud maison `Type="delim"` + champ `Dk` + un `case "delim"` codé en dur dans `LatexRenderer` produisant le **même** LaTeX `\left|…\right|` / `\left\|…\right\|`.

Deux sources de vérité pour la même sortie : changer le rendu d'une valeur absolue obligeait à toucher les deux. C'était le seul vrai défaut de la famille « constructions structurelles » que l'audit voulait déporter.

## Décision

La forme-barre **réutilise l'opérateur vocab** au lieu d'un type de nœud dédié :
- le parser, quand il reconnaît une paire de barres encadrantes, émet désormais `Node{Type="prefix", Sym="abs"|"norm"}` (les barres restent reconnues par le lexer — un délimiteur caractère, inévitable) ;
- l'enfant garde `Grouped=true` (les barres bracketent visuellement, comme avant) ;
- le type de nœud `delim`, le champ `Node.Dk` et le `case "delim"` du renderer **disparaissent**.

Résultat : le rendu de la valeur absolue n'a plus qu'**une** source de vérité (`Vocab["abs"]`/`Vocab["norm"]`), et plus aucun code spécifique aux barres ne vit dans `LatexRenderer`.

`·mid` (barre **au milieu**, `A|B` → `A \mid B`, ex. `{x | x>0}`, `P(A|B)`) est **inchangé** : ce n'est pas une valeur absolue, il passe déjà par un symbole vocab (`·mid`).

## Conséquences

- **Moteur** : `Parser.cs` (bloc « valeur absolue » émet un prefix), `LatexRenderer.cs` (`case "delim"` retiré), `Node.cs` (`Dk` retiré du champ + du `Clone()` + du commentaire de types).
- **Comportement** : aucune régression attendue — le LaTeX produit est identique au caractère près. Le filet de sécurité est le corpus de fixtures (`|x|`, `|x+1|`, `|a|+|b|`, `||v||`, `|z-conjz|`).
- **Hors scope** : la généralisation « délimiteur tapé = intention, dissous seulement sous opérateur regroupant » (qui absorberait `IsPointPair`/`IsRepere`/tuple) est reportée à une discussion dédiée.

## Validation post-refacto

Fixtures moteur intégralement vertes (corpus inchangé), les 5 cas à barres produisent le même LaTeX qu'avant.

## TODO — déport restant (gardé pour plus tard, validé utilisateur)

> « ok non c'est parfait comme ca garde comme c'est fait , garde 1 et 2 pour plus tard dans un TODO » — utilisateur, 2026-06-15.

Le **rendu** d'`abs`/`norm` est entièrement en `Vocabulary`. Ce qui reste hors vocab est la **reconnaissance** du délimiteur `|`, soit 2 littéraux :
- `Lexer.cs:158` — `|` → token bar `Sym="abs"`, `||` → `Sym="norm"` (clés vers Vocab, pas définitions) ;
- `Parser.cs:306` — `_toks[k].Sym == "abs"` dans la règle « barre seule au milieu = `·mid` ».

Deux pistes pour les faire disparaître, **non urgentes** :

1. **Petit pas** — déclarer le mapping `|`→`abs`, `||`→`norm` dans une table de `Vocabulary` (`PairedDelim`) consultée par le lexer + le mid-block. Met les noms dans le fichier vocab ; la reconnaissance du caractère reste structurelle (en partie cosmétique).
2. **Vrai déport** *(préféré)* — fondre les barres dans la future **couche de règles structurelles** (celle qui absorberait aussi `IsPointPair`/`IsRepere`/tuple/parenthèses : « délimiteur tapé = intention, dissous seulement sous opérateur regroupant »). Là, tous les délimiteurs deviennent déclaratifs d'un coup et le parser n'en connaît plus aucun nommément.

Décision : ne pas bricoler (1) en isolé ; traiter les barres avec la passe structurelle (2). Aucun des deux n'est sur le chemin critique.
