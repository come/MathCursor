# UX — Site : polices math expliquées + « pourquoi ce n'est pas de la triche »

**Date :** 2026-11-02
**Kind :** UX
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-11-02-Feat-sans-serif-math-font](2026-11-02-Feat-sans-serif-math-font.md), [2026-06-22-Feat-math-font-selector](2026-06-22-Feat-math-font-selector.md)

## Citation acté

> « est ce que tu peux rajouter les polices utilisées et pourquoi sur le site web ?
> par ailleurs est ce qu'on ferait pas une section pour expliquer pourquoi
> MathCursor est parfaitement valide pour les controle ce n'est pas de la
> triche » — utilisateur, 2026-11-02.

> « oui c'est bien » — utilisateur, 2026-11-02 (validation du plan, y compris la
> formulation prudente plutôt que « parfaitement valide »).

## Décision

1. **Polices** : une question de FAQ sur l'accueil (« Quelle police pour les
   formules ? ») : les 4 polices du menu et la raison de chacune (Cambria = défaut
   Word ; Latin Modern = rendu LaTeX ; STIX Two = publications scientifiques ;
   Lete Sans Math = sans sérif, plus lisible pour beaucoup d'élèves dyslexiques),
   toutes libres et installées avec MathCursor. Un paragraphe Lete Sans Math sur
   `amenagement.html` et `pap-dys/`.
2. **Contrôles et examens** : section « En contrôle et en examen : pourquoi ce
   n'est pas de la triche » sur `amenagement.html` (remplace le bloc « En
   examen »), une question de FAQ sur l'accueil qui y renvoie, une phrase sur
   `prof/`.

**Règle de formulation (forte dans l'esprit)** : le site **n'affirme pas** que
MathCursor est « autorisé » ou « valide » en contrôle. L'autorisation appartient
au prof en classe, et à l'autorité académique pour un examen (aménagement sur
dossier, dont le PAP ; un PAP ne vaut pas automatiquement aménagement d'examen).
Le site explique pourquoi l'outil est **neutre** par des faits vérifiables : il
ne calcule, ne résout, ne corrige rien, pas d'IA, ne propose jamais de résultat ;
il produit une équation Word standard comme l'éditeur d'équations de Word ;
hors ligne, sans compte ; et conseille d'en parler en amont et de l'utiliser
toute l'année (cohérence avec les aménagements de la scolarité).

Sources consultées (2026-11-02) : code de l'éducation art. D. 351-27 à
D. 351-31 ; circulaire PAP n° 2015-016 ; BO n° 31 du 21-08-2025
(MENE2506522C, annexes applicables à la session 2026).

## Écartés

- « Parfaitement valide pour les contrôles » : promesse que MathCursor ne peut
  pas tenir ; risque de crédibilité si un prof refuse.
- Citer précisément la circulaire d'examen sur le site : textes qui changent
  chaque année ; on renvoie à l'établissement.

## Caveat

Lete Sans Math n'est pas dans la version publiée au moment de l'écriture : le
contenu « polices » est à mettre en ligne avec la release qui l'embarque.
