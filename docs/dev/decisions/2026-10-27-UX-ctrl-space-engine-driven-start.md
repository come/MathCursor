# UX — Ctrl+Espace : le moteur choisit le début de la zone (plus de liste de mots-outils)

**Date :** 2026-10-27
**Kind :** UX
**Température :** molle
**Statut :** acté
**Supersedes :** dans [2026-10-27-Fix-ctrl-space-prose-fallback](2026-10-27-Fix-ctrl-space-prose-fallback.md), le repli « mots de texte en tête retirés » (`LeadingWordTrimStarts`), désormais inutile ; pour le **Ctrl+Espace de Word uniquement**, la borne « dernier mot-outil » de [2026-05-25-Refactor-chantier1-data-driven-fr-keywords](2026-05-25-Refactor-chantier1-data-driven-fr-keywords.md)
**Lié à :** [2026-04-29-Feat-iterative-zone-expansion-ctrl-space](2026-04-29-Feat-iterative-zone-expansion-ctrl-space.md) (2e appui = élargir, inchangé) ; parité SpanComputer C#↔JS (ADR 2026-06-23, `ComputeSpanStart` inchangé)

## Citation acté

> « on peut pas faire un truc vraiment dur plutot qu'une liste de mot "francais" ?? » puis,
> entre A (un espace de plus à chaque appui) et B (le moteur choisit la plus longue zone
> lisible) : « ok B » — utilisateur, 2026-10-27

## Contexte

Le début de la zone du Ctrl+Espace était « après le dernier mot-outil » (liste française de
29 mots : soit, et, donc, le, la, de…). Il en manque toujours un (« entre », « remplace »,
« abscisse ») : la zone embarque alors de la prose, le moteur n'a aucune lecture et rien ne
s'affiche. Le repli du matin (retirer les mots en tête) corrigeait le symptôme mais gardait
la liste.

## Décision

Ctrl+Espace (premier appui, zone calculée) :

1. **Bornes structurelles** inchangées : délimiteurs (`. ; ? = < >`, saut de ligne) hors
   parenthèses, formule déjà convertie, parenthèse ou crochet ouvert, début du paragraphe
   (`SpanComputer.ComputeStructuralSpanStart`). Fin de zone inchangée.
2. **Débuts candidats** : la borne, puis chaque début de mot après un blanc, hors parenthèses
   et crochets (`SpanComputer.WordBoundaryStarts`).
3. On propose la **plus longue zone que le moteur lit** (premier candidat, du plus long au
   plus court, qui donne au moins une lecture) ; « rien de reconnu » si aucune.

Le moteur rejette déjà la prose (« distance entre x », « de f », « 2 x » : erreur) : c'est lui
qui fait le tri, sans liste de mots ni langue. Le 2e appui (`ExtendOneStop`) et
l'auto-détection (NER) ne changent pas. `ComputeSpanStart` (avec mots-outils) reste pour
l'extension, l'ancrage de l'auto-détection et la démo web (parité JS).

## Tradeoff & alternatives écartées

- **A. Un espace de plus à chaque appui** (proposition utilisateur) : le plus prévisible,
  mais une formule tapée avec des espaces demande plusieurs appuis, et le premier propose un
  fragment (« 1 » pour « (n+1) + 1 ») ; 98 zones sur 227 du journal local contiennent un
  espace.
- **Allonger la liste des mots-outils** : il en manquera toujours un ; dépend de la langue.
- **Utiliser le NER pour le Ctrl+Espace** : change la nature du déclencheur manuel.

Risque : une prose que le moteur lit par hasard (mots-clés du vocabulaire : « limite »,
« part », « pour ») serait englobée. La mesure l'a montré (« limite de la suite » →
lim_{de→la} suite) ; d'où un **garde prose**, sans liste de mots : une zone de plusieurs mots
est écartée si un mot de la saisie (3 lettres ou plus, ne touchant pas un chiffre) ressort
tel quel dans la lecture, hors commandes et unités (`SpanComputer.LatexLooksLikeProse`).
Le dernier mot seul n'est jamais écarté (comportement d'avant).

Limites connues : un mot-clé consommé par le moteur n'est pas vu (« On part de » → ∂de en
popup ; « pour q ≠ 1 » → ∀q ≠ 1 au lieu de q ≠ 1) ; un produit de 3 lettres collées dans une
zone de plusieurs mots (« abc + d ») est écarté au profit de la zone plus courte.

## Validation

- Mesure (script `measure_ctrlspace.py`, port Python fidèle de `SpanComputer` + moteur Rust
  `analyze.exe`, phrases réelles de 40 exercices types) :
  - **162 formules** tapées après de la prose : ancienne règle 9 zones = formule tapée,
    nouvelle 12 ; **aucune régression** ; gagnées : « … de la forme ax2 + bx + c »,
    « On remplace a », « … d'abscisse a » ; 8 cas sans lecture des deux côtés
    (« … = 0,999... », délimiteur `.` final, inchangé).
  - **109 fins de phrase en prose** + Ctrl+Espace : 88 → dernier mot seul (comme avant),
    15 → rien, 6 → plusieurs mots, dont 4 vraies formules (« a ≠ 0 », « n + 1 »,
    « 1, 2, 1 », « (a - b) ») et les 2 limites ci-dessus.
- Gate `scripts/run-tests.ps1` verte : Adapter 461 (dont `WordBoundaryStarts`,
  `ComputeStructuralSpanStart`, `LatexLooksLikeProse`), parité SpanComputer C#↔JS 17/17
  (`ComputeSpanStart` inchangé), conformance Rust 618/618.
- Essai dans Word (add-in recompilé) : « … distance entre x », « On remplace a », « … courbe
  de f au point d'abscisse a » (Ctrl+Espace aussitôt après la frappe) → x, a, f, a convertis ;
  « Déterminer la limite de la suite » → lim_{de→la} écarté (journal « lecture écartée
  (prose) »), retombe sur « suite » comme avant.
