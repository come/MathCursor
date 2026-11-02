# Feat — Police math sans sérif (Lete Sans Math) pour les élèves dyslexiques

**Date :** 2026-11-02
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-22-Feat-math-font-selector](2026-06-22-Feat-math-font-selector.md) (sélecteur de police math + polices embarquées dans l'installeur)

## Citation acté

> « disposer d'une écriture "sans sérif" pour les écritures mathématiques (une
> écriture "Sérif" comme les 3 actuellement proposée peut poser problème aux
> élèves fortement dyslexiques) » — retour d'un utilisateur, relayé le 2026-11-02.

> « c'est open source ? je peux le packer dans mon exe ? » puis « ok vas y » —
> utilisateur, 2026-11-02 (validation du plan, 1er candidat Fira Math).

> « vas y teste » — utilisateur, 2026-11-02 (test de Lete Sans Math après l'échec
> partiel de Fira Math ; plan annoncé : si la couverture est complète, on
> l'intègre telle quelle).

## Contexte

Le menu « Police math » propose Cambria Math (défaut), Latin Modern Math et STIX
Two Math : trois polices à sérif. Pour des élèves fortement dyslexiques, une
police sans sérif est plus lisible.

Contrainte héritée de l'ADR du 22/06 : Word ne met en page les maths qu'avec une
police dotée d'une **table OpenType « MATH »**. Arial, OpenDyslexic, etc. sont
exclues (fractions, racines, matrices cassées). Il faut une **police math sans
sérif**.

## Décision

Ajouter **Lete Sans Math** (v0.63, dérivée de Lato, Chenjing Bu) au catalogue
curaté, 4e entrée du menu, avec le même traitement que STIX :

- `MathFontCatalog.Fonts` : `"Lete Sans Math"` ; `DownloadUrl` →
  `https://github.com/abccsss/LeteSansMath/releases` (repli si non installée).
- Installeur : `fonts/LeteSansMath.otf` installé par utilisateur (`{autofonts}`,
  `FontInstall: "Lete Sans Math"`, `onlyifdoesntexist uninsneveruninstall`) ;
  licence `fonts/LeteSansMath-OFL.txt` déposée dans `{app}\fonts-licenses`.
- Infobulle du menu (FR/EN : « sans sérif, souvent plus lisible pour les élèves
  dyslexiques »), « À propos », `THIRD-PARTY-NOTICES.md`, `README.md`.

**Licence** : SIL OFL 1.1 ; copyright Łukasz Dziedzic (Lato, Reserved Font Name
« Lato »), Xiangdong Zeng, Chenjing Bu. Police embarquée **non modifiée et non
renommée** : le RFN est sans objet. Agrégat vis-à-vis de la GPL v3 (même
raisonnement que STIX, ADR du 22/06).

## Tests (POC Word, 2026-11-02)

Rendu à l'écran de Word (capture de fenêtre), police installée par utilisateur :

- **Lete Sans Math** : fraction, racine, limite, somme, intégrale avec espace
  fine `\,`, `\quad`, matrice avec ⋯ ⋮ ⋱, valeur absolue, indices, `P_A(B)`, ⊥
  (bon espacement de relation), ℝ∖{0}, ⟹ : **tout correct**. Couverture
  (fontTools, 4 518 glyphes) : tous les caractères émis par `LatexToOmml`
  présents ; alphabets math italique et gras complets.
- **Fira Math** (1er candidat) : rendu propre, mais 5 caractères émis par
  MathCursor absents → carré vide : espace fine U+2009 (`\,dx`), ∖ (`ℝ∖{0}`),
  ⊥ (`perp`), ⋮, ⋱. Repli par caractère impossible : passer un caractère en
  Cambria bascule **toute l'équation** (Word = une police math par équation).
  Il aurait fallu réécrire ces caractères dans les équations : écarté.

**Bug séparé constaté** : l'export PDF de Word perd les parenthèses étirables
« petites » (f(x), n(n+1)) avec les polices OTF CFF (Latin Modern déjà embarquée
**et** Fira Math) ; l'affichage écran est correct. Préexistant, hors périmètre.

## Écartés

- **Polices de texte** (Arial, OpenDyslexic…) : pas de table MATH, rendu cassé.
- **Fira Math** : glyphes manquants ci-dessus (+ 0.3.4 « prerelease » figée
  depuis 2020).
- **Noto Sans Math** : non testée, Lete couvre tout.
- **Variables droites** (sans italique, `m:sty p`) : piste distincte pour la
  dyslexie, hors périmètre ; à proposer si le retour revient.

## Caveat

Lete Sans Math est en 0.63 « prerelease » (projet actif, versions régulières en
2026). À surveiller lors des mises à jour de l'installeur ; la version embarquée
est figée par le fichier dans `adapter-vsto/installer/fonts/`.
