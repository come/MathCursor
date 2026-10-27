# Fix — Ctrl+Espace : repli sur la fin de zone quand des mots de texte la précèdent (« entre x » → x)

**Date :** 2026-10-27
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Lié à :** [2026-04-23-Feat-trigger-ctrl-space](2026-04-23-Feat-trigger-ctrl-space.md) ; [2026-04-29-Feat-iterative-zone-expansion-ctrl-space](2026-04-29-Feat-iterative-zone-expansion-ctrl-space.md) (extension vers la gauche, inchangée) ; parité SpanComputer C#↔JS (ADR 2026-06-23, non touchée)

## Citation acté

> « y'a vraiment un soucis dans le moteur, le ctrl+espace qui renvoie rien alors qu'il y'a
> une lettre, il doit au moins renvoyer la lettre » puis « oui on tente le changement mais
> attention attention, rejoue bien les tests » — utilisateur, 2026-10-27

## Contexte

Cas réel : « … est la distance entre x » puis Ctrl+Espace → rien. Journal de
l'add-in :

```
convert: span=[14,30] → "distance entre x"
convert: aucune lecture pour "distance entre x"
```

La zone du Ctrl+Espace est purement lexicale (`SpanComputer`) : elle remonte jusqu'au
dernier mot-outil (le, la, de, on…). « entre », « remplace », « abscisse » n'en sont pas :
la zone embarque des mots de texte, le moteur n'a aucune lecture, et le chemin manuel n'a
**aucun repli** (simple message de barre d'état). Le cas « On remplace a » ne marchait que
grâce à l'auto-détection, qui essaie plusieurs zones NER et tombe sur « a » seul.

## Décision

Dans `ConversionController.Trigger` (chemin manuel, zone calculée — pas l'extension
`ExtendOneStop`) : si la zone ne donne **aucune lecture**, on retire un à un les **mots de
texte en tête** de zone (mot de 2 lettres ou plus, élision comprise — « d'abscisse »,
« l'image » —, suivi d'un blanc ; jamais « f' » ni « x ») et on retente : « distance entre x » → « entre x » → « x ». La première zone qui
donne une lecture est proposée ; « rien de reconnu » seulement si toutes échouent.

- Calcul des débuts candidats : fonction pure `SpanComputer.LeadingWordTrimStarts`, testée.
- Une zone qui a déjà une lecture n'est **jamais** raccourcie : rien ne change là où
  Ctrl+Espace marchait.
- On ne retire que des **mots** en tête : « 2 x + 1 » (tête = nombre) ou « (x - 2) … » ne
  sont jamais coupés ; on ne découpe pas une formule en morceaux.

## Tradeoff & alternatives écartées

- **Ajouter « entre », « est », « remplace »… aux mots-outils** : il en manquera toujours
  un (tout verbe, tout nom), et la liste est partagée avec le port JS (fixture de parité).
- **Repli sur tous les suffixes** (y compris après un nombre ou un opérateur) : risque de
  convertir un fragment d'une formule que le moteur ne sait pas lire en entier.
- **Utiliser le NER sur Ctrl+Espace** : change le comportement validé du déclencheur
  manuel ; hors du périmètre d'un correctif.

Contrepartie : une saisie prose + formule illisible en entier (« selon la formule xyz@ »)
peut maintenant proposer la fin lisible (« xyz ») au lieu de rien ; l'utilisateur garde
Échap, et Ctrl+Espace à nouveau élargit vers la gauche comme avant.

## Validation

- Gate complète `scripts/run-tests.ps1` verte après : Engine 32, Serialization 65,
  Analyzers 27, Adapter 455 (dont 17 cas `LeadingWordTrimStarts`), parité SpanComputer
  C#↔JS 17/17, conformance Rust 618/618. Le run « avant » a été pollué par l'édition en
  cours (mêmes totaux) ; son seul échec, `WpfMathSmokeTests` (`\mapsto`), est instable
  (vert 3 fois de suite ensuite) et sans lien.
- Essai dans Word (add-in recompilé) : « … la distance entre x », « On remplace a »,
  « au point d'abscisse a » + Ctrl+Espace → x, a, a convertis (journal : `repli span`).
  Le premier essai a montré le cas « d'abscisse » (élision) : ajouté puis revalidé.
