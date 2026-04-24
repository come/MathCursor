# UX — Alignement OMath respecte le paragraphe parent

**Date :** 2026-04-23
**Kind :** UX
**Température :** molle
**Statut :** acté

## Décision

`OMath.Justification` et `OMathPara.Justification` suivent l'alignement du
paragraphe texte contenant l'insertion, au lieu d'être forcés à
`wdOMathJcLeft` systématiquement.

Mapping `WdParagraphAlignment` → `WdOMathJc` :
- Left / Justify → Left (3)
- Center → Center (2)
- Right → Right (4)

## Pourquoi

- Word centre par défaut les équations via `wdOMathJcCenterGroup` — c'était
  ressenti comme un comportement non-prévisible par l'utilisateur.
- La première version forçait tout à gauche, mais on casse l'usage des profs
  qui centrent volontairement leurs équations pour la présentation de leur
  cours.
- La règle produit initiale ("toujours à gauche", cf. `CLAUDE.md`) était trop
  rigide : le bon principe est "ne surprend pas, respecte le choix utilisateur".

## Conséquences

- `SuggestionService.SyncOMathJustificationToParagraph` lit
  `paragraphe.Format.Alignment`, le map vers `WdOMathJc`, applique sur
  `OMath.Justification` et `OMathPara.Justification` uniquement.
- **Ne touche plus** au paragraphe texte ni aux Content Controls englobants.
- Suppression de 4 méthodes (`TrySetContainingParagraphAlignLeft`,
  `TryFixContentControlAlignment`, `TryForceOMathInline`,
  `DumpInsertionState`).
- `OMathParagraphs` accessible uniquement via `Range`, pas `Document`
  (`DISP_E_UNKNOWNNAME` signalé par le log diag avant fix).
- Valeurs d'enum `WdOMathJc` confirmées par reflection live :
  `CenterGroup=1, Center=2, Left=3, Right=4, Inline=7` (cargo-cult `0` ne
  fonctionne pas, Word remappe à Center).

## Validé par l'utilisateur

> "ok la ca marche ! du coup tu peux simplifier le code pour ne mettre ca que
> autour de l'omath ? et eventuellement respecter l'alignement courant (si
> gauche=> gauche etc"

## Statut

acté
