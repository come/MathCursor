# MathCursor — Brief ergonomie (Word Desktop)

## Positionnement

MathCursor permet de saisir des mathématiques dans Word **aussi rapidement qu'au stylo sur une feuille**. L'utilisateur écrit naturellement, dans sa langue, et l'outil transforme sa frappe en équations parfaitement formatées sans jamais interrompre son flux de pensée.

Ce n'est ni un éditeur d'équations modal, ni un menu à naviguer, ni un langage à apprendre. C'est un copilote silencieux qui reconnaît l'intention mathématique pendant la frappe et la met en forme.

## Principe directeur

Chaque décision ergonomique est évaluée selon une question unique : **est-ce que ça rapproche ou éloigne l'expérience d'écrire sur une feuille ?**

Les corollaires :
- Zéro friction cognitive : l'outil ne doit jamais obliger l'utilisateur à penser à lui
- Latence perçue inférieure à 100 ms sur le chemin critique
- Ctrl+Z restaure toujours le texte source : aucun piège de conversion irréversible
- Pas d'UI imposée : les utilisateurs qui ne veulent pas de suggestions peuvent les désactiver

## Expérience principale

L'utilisateur tape du texte normalement. Quand MathCursor détecte un pattern mathématique, une popup discrète apparaît juste sous le curseur, en transparence (alpha 0.2), affichant 2 à 4 suggestions d'interprétation.

La première suggestion est pré-sélectionnée. L'utilisateur a trois options :

- **Tab** : valide la première suggestion et continue à taper normalement
- **Flèche bas puis Entrée** : choisit une alternative si la première n'est pas la bonne
- **Échap ou frappe incompatible** : ferme la popup, pas de conversion

La popup suit le curseur, se met à jour à chaque frappe, et disparaît en fondu quand la conversion est validée. L'utilisateur ne quitte jamais sa zone de frappe. Il n'y a pas de popup modale, pas de menu, pas de clic souris obligatoire.

Inspiration directe : le mécanisme IntelliSense de VSCode adapté aux mathématiques.

## Langage d'entrée

L'utilisateur tape comme il parle ou comme il écrirait, dans sa langue. Quelques exemples du français :

- `f(x) = 2x + a` devient une équation formatée
- `racine de x` devient √x
- `somme de i=0 à n de (i+2)` devient Σ avec bornes
- `intégrale de 0 à 1 de x²` devient ∫ avec bornes
- `limite quand x tend vers l'infini de 1/x` devient lim correcte
- `<=>` devient ⟺ (flèche proprement typographiée en OMath, pas la version moche du texte Word)

Le parser est multilingue par construction. Il comprend "somme" en français, "sum" en anglais, "Summe" en allemand, avec la même fluidité. La langue du document est détectée automatiquement, modifiable manuellement si besoin.

## Désambiguïsation

Certaines saisies sont ambiguës. Par exemple `AB` peut être un vecteur, une droite, un segment, une longueur, selon le contexte. MathCursor ne devine pas silencieusement : il propose les interprétations plausibles dans la popup, ordonnées par probabilité contextuelle.

L'utilisateur choisit en une frappe clavier. Ce choix est mémorisé localement pour affiner les suggestions futures.

## Chaînes de raisonnement

Les démonstrations mathématiques sont souvent des suites d'équations reliées par des opérateurs logiques (⟺, ⟹, =). Aujourd'hui dans Word, ces chaînes sont pénibles à formater et rarement bien alignées.

MathCursor introduit un comportement inspiré des listes à puces : quand l'utilisateur tape un opérateur de relation en début de ligne juste après une équation, l'outil détecte l'intention et formate une chaîne alignée professionnellement sur le signe d'égalité. À chaque Entrée, l'opérateur se répète automatiquement, l'alignement est maintenu. Double Entrée sort du mode chaîne.

Le résultat typographique est équivalent à ce que produit LaTeX avec l'environnement align, sans que l'utilisateur n'ait rien à apprendre.

## Édition d'équations existantes

Quand l'utilisateur repositionne son curseur dans une équation déjà créée par MathCursor, l'outil retrouve automatiquement le texte source originel (celui qu'il avait tapé initialement). Il peut le modifier directement au lieu de devoir manipuler l'équation formatée.

La mise à jour est instantanée : il change le source, l'équation se re-rend.

## Qualité typographique

MathCursor ne se contente pas d'insérer du texte avec des caractères Unicode. Il produit des équations OMath natives, rendues en Cambria Math, avec les espacements mathématiques corrects, les flèches proprement dessinées, les intégrales, sommes et produits à la taille appropriée.

Les équations restent éditables nativement par Word et s'exportent correctement dans tous les formats (PDF, DOCX, HTML).

## Ce que MathCursor ne fait pas

Pour rester focalisé sur son angle, MathCursor ne fait volontairement pas :

- Reconnaissance manuscrite : l'angle est clavier, pas stylet
- Reconnaissance vocale : hors scope
- Tutorat mathématique ou résolution : autre produit
- Tableaux de graphiques interactifs : renvoi vers outils spécialisés
- Gestion de quiz ou d'évaluations : hors scope

Cette discipline produit différencie MathCursor des concurrents qui dispersent leur proposition de valeur sur dix fonctionnalités.

## Modes utilisateur

Trois modes au choix dans les préférences :

- **Auto** : suggestions apparaissent dès qu'un pattern est détecté (défaut)
- **Manuel** : suggestions uniquement sur raccourci explicite (Ctrl+Espace)
- **Silent** : aucune suggestion automatique, l'outil n'intervient qu'à la demande

Un power user peut ainsi choisir un comportement très discret, un débutant préférera probablement l'auto.

## Signal produit fondamental

Si un utilisateur écrit une démonstration mathématique dans MathCursor et qu'il ne se souvient pas à la fin d'avoir interagi avec l'outil — parce que tout s'est passé dans le flux naturel de sa pensée — le produit a réussi. Si au contraire il se souvient d'avoir "utilisé MathCursor" comme d'une action explicite, l'UX a échoué quelque part.
