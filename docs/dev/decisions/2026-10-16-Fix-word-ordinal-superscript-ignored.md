# Fix — L'exposant d'ordinal posé par Word (2ᵉ) n'est pas un exposant mathématique

**Date :** 2026-10-16
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-10-14-Feat-word-subscript-superscript-format](2026-10-14-Feat-word-subscript-superscript-format.md) (lecture de la mise en forme Indice/Exposant) ; [2026-10-16-Feat-physics-chemistry-mode](2026-10-16-Feat-physics-chemistry-mode.md) (électron `e-`) ; [2026-06-18-Fix-input-autocorrect-fraction-factorial](2026-06-18-Fix-input-autocorrect-fraction-factorial.md) (précédent : option ½ coupée)

## Citation acté

> « un truc bizarre j'ai l'impression que quand je tape e- ou e+ word passe en mode exposant » — utilisateur, 2026-10-16
>
> Sur l'alternative A (couper l'option de Word) / B (ignorer l'exposant d'ordinal côté MathCursor, recommandée) : « ah oui ! »

## Contexte

La correction automatique de Word « Ordinaux (1er) en exposant » met en exposant le suffixe d'un ordinal dès le caractère suivant : `2e` devient 2ᵉ. Depuis le 2026-10-14, MathCursor lit la mise en forme Exposant de Word et l'envoie au moteur comme exposant exact. Résultat : `2e-` (2 électrons) arrivait au moteur comme 2 puissance e suivi de `-`, et l'électron ne sortait pas (logs du 2026-10-16 : masque `^` sur le `e`).

## Décision

Dans la traduction de la mise en forme (`ScriptMarkup.Apply`, adapter), un run en exposant qui est **un suffixe d'ordinal** collé derrière un chiffre et suivi d'une fin de mot est lu comme du **texte normal**.

- Suffixes : `e`, `er`, `re`, `ère`, `ème`, `eme`, `es`, `ers`, `res`, `èmes`, `nd`, `nde`, `nds`, `ndes`, `d`, `de`, `st`, `rd`, `th` (insensible à la casse).
- `2e-` arrive tel quel au moteur : 2e⁻ en physique-chimie ; en maths `2e` reste 2·e, comme sans la correction de Word.
- Les options de Word ne sont pas touchées : « XIXᵉ siècle » reste automatique dans la prose.

## Tradeoff & alternatives écartées

- **A. Couper l'option de Word au démarrage** (comme ½) : simple, mais retire les ordinaux automatiques dans tout Word (histoire, français), et l'audit pointe déjà les options globales modifiées sans restauration.
- **Perte assumée** : un vrai exposant posé à la main avec le bouton Exposant sur un chiffre, de la forme d'un suffixe d'ordinal (2 puissance e via le bouton) est lu `2e`. Très rare ; `2^e` tapé reste possible.

## Conséquences

- **Code** : `adapter-vsto/src/MathCursor/Host/Detection/ScriptMarkup.cs` (fonction pure), tests `ScriptMarkupTests`.
- **Moteur** : inchangé.

## Validation post-fix

- Tests `ScriptMarkupTests` : `2e-` avec `e` en exposant → texte `2e-` ; `x2` avec `2` en exposant inchangé ; `2e` en exposant suivi d'une lettre inchangé.
- Word : option ordinaux active, mode physique-chimie, taper `2Fe + 2e-` + Ctrl+Espace → 2Fe + 2e⁻.
