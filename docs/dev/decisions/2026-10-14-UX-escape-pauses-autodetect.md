# UX — Échap met la détection auto en pause pour le document

**Date :** 2026-10-14
**Kind :** UX
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Feat-ner-auto-detection-debounce](2026-06-10-Feat-ner-auto-detection-debounce.md) (détection auto NER + toggle ruban)

## Citation acté

> « y'a un truc sur lequel je m'interrogeais.. c'est de faire un truc avec la popup dans word, si on fait echap, ca force l'etat off par defaut jusqu'au prochain ctrl+espace .. ca permettrait d'evuter d'etre polué par la popup quand on est en train de faire un doc sur de l'histoire geo » — utilisateur, 2026-10-14
>
> Choix : portée « Par document », geste « Un seul Échap ». Ajout demandé : « peut etre rajouter a coté du "echap pour fermer, ctrl+espace pour retrouver" ou un truc du style ? » puis « essayons ca en effet ».

## Contexte

Dans un document sans maths (histoire-géo…), le NER propose parfois une
popup sur de la prose (dates, sigles, nombres). Échap ne faisait que masquer
la popup : la frappe ou le déplacement de caret suivant relançait la
détection et la popup revenait. Le seul moyen de couper était le toggle
« Détection auto » du ruban, persisté, donc global et durable.

## Décision

- **Échap sur la popup de suggestion** : masque la popup (inchangé) **et** met
  la détection auto **en pause pour le document actif**. La barre d'état
  confirme : « Détection auto en pause, Ctrl+Espace pour reprendre ».
- **Ctrl+Espace** : lève la pause du document actif, puis convertit comme avant.
- **Pause en mémoire de session**, clé = `Document.FullName`. Non persistée,
  n'écrit pas `AppSettings.AutoDetect` : le toggle ruban reste le choix
  durable, la pause est un état temporaire par-dessus.
- **Rappel dans la popup** : une ligne grise dans le pied, « Échap : pause ·
  Ctrl+Espace : reprendre » (FR/EN via `Strings`), affichée seulement quand
  la détection auto est activée (sinon Échap ne met rien en pause).
- Échap sur la popup d'édition d'équation, ou sans popup : inchangé.
- Pause applicable même si le modèle NER n'est pas encore chargé : elle
  s'appliquera dès qu'il l'est.

Code : classe pure `Host/AutoDetectPause.cs` (ensemble de clés document),
possédée par `AutoDetectController` (`PauseActiveDocument` /
`ResumeActiveDocument`, garde dans `RunDetection`) ; câblage dans
`ThisAddIn.HandleEscapePressed` / `HandleCtrlSpacePressed` ; rappel dans
`UI/SuggestionPopupWindow`.

## Tradeoff & alternatives écartées

- **Écrire le réglage persisté `AutoDetect`** : un Échap réflexe couperait la
  détection durablement, y compris après redémarrage, et dans tous les
  documents.
- **Pause pour tout Word** : un Échap dans le doc d'histoire couperait aussi
  le doc de maths ouvert à côté.
- **Pause par paragraphe** : en histoire-géo il faudrait refaire Échap à
  chaque paragraphe, la pollution reste.
- **Double Échap** : évite la pause accidentelle mais geste peu découvrable ;
  l'utilisateur préfère le simple Échap, le rappel dans la popup + le message
  de barre d'état compensent le risque de pause involontaire.

Limites assumées : un « Enregistrer sous » change `FullName` et lève la
pause ; Échap sur une popup ouverte par Ctrl+Espace met aussi en pause (règle
unique, plus simple à retenir).

## Conséquences

- **Code touché** : `Host/AutoDetectPause.cs` (nouveau),
  `Host/AutoDetectController.cs`, `ThisAddIn.cs`,
  `UI/SuggestionPopupWindow.cs`, `Strings.cs`, les deux csproj (add-in +
  tests).
- **Aucune touche** à l'insertion OMath, aux ContentControls ni aux positions
  Word.
- **Tests** : `AutoDetectPauseTests` (pause/reprise, isolation par document,
  clé nulle).

## Validation post-fix

Tests xUnit verts ; en Word : taper de la prose qui déclenche une popup →
Échap → la popup ne revient plus dans ce document, message en barre d'état ;
un autre document continue de proposer ; Ctrl+Espace relance.
