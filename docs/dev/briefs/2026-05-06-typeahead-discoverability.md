# 2026-05-06 — Brief : Typeahead / découvrabilité dans la suggestion popup

> Statut : **réflexion / sous le coude**. Aucun plan d'implémentation, aucun
> ADR. À reprendre quand on décide d'attaquer l'axe découvrabilité.

## Problème

L'utilisateur ne sait pas ce qu'il peut taper. Le dictionnaire de raccourcis
(YAML domaines : `racine`, `lim`, `som`, `int`, `mapsto`, `mathbb`...) est
**invisible** sauf si on ouvre la cheatsheet. Conséquence : en pratique, les
gens découvrent une fraction du vocabulaire et restent bloqués sur les
mêmes 10 raccourcis.

Inspiration externe : Corca pousse le framing "Input works like search". On
**n'emprunte pas** ce framing (cf. positionnement MathCursor : la promesse
est la **rapidité au clavier**, pas la "naturalité"). Mais l'idée
sous-jacente — pendant qu'on tape, montrer ce qui pourrait être tapé en
plus court / plus précis — est légitime.

## Contraintes de premier plan

1. **Ne pas perturber le flow de saisie.** Zéro pop-up modal, zéro vol de
   focus, zéro nécessité d'arrêter de taper pour lire. Si l'helper
   ralentit la frappe, il ne respecte pas l'identité du produit.
2. **Pas de framing "langage naturel"** (cf. positionnement). L'helper
   apprend des raccourcis efficaces, pas des phrases.
3. **Pas de nouveau composant UI majeur** si la `SuggestionPopupWindow`
   peut absorber le rôle.

## Piste retenue (provisoire) : intégrer dans `SuggestionPopupWindow`

La popup a déjà deux modes natifs (cf.
[`SuggestionPopupWindow.cs`](../../../adapter-vsto/src/MathCursor/UI/SuggestionPopupWindow.cs)) :

- **Display** (opacity 0.5, passif, montre la formule reconnue à valider)
- **Nav** (opacity 0.9, interactif, l'utilisateur navigue avec ↓/Enter)

Le helper de découverte s'inscrit naturellement dans le mode **Display**
(passif). Concrètement, quand le NER **n'a pas** de candidat fort à
proposer, mais que le caret reste dans une zone de texte qui matche
**partiellement** un raccourci connu, la popup peut s'afficher en mode
discovery :

- En mode discovery, **Enter ne convertit rien** (différence majeure vs le
  flow normal). C'est purement éducatif.
- Liste les 3-5 raccourcis qui matchent les derniers caractères tapés,
  avec leur glyphe rendu (Word OMath ou WpfMath fallback).
- Une bordure / un fond / une icône distinctifs signalent au lecteur :
  "ceci n'est pas une suggestion à valider, c'est un mémo".
- Disparaît dès que :
  - Un vrai candidat est reconnu (passage automatique en mode normal)
  - L'utilisateur tape une touche sans match
  - Timeout court (1-2 s)

### Pourquoi popup-first vs cheatsheet pane qui filtre live

| | Popup-first | Pane filtre live |
|---|---|---|
| Spatial locality | ✅ Au caret | ❌ À 20 cm dans la sidebar |
| Charge cognitive | ✅ Une UI à apprendre | ❌ Deux UIs (popup + pane) |
| Découvrabilité passive | ✅ Apparaît tout seul | ❌ L'user doit ouvrir la pane |
| Risque de surcharger | ⚠ La popup peut devenir bruyante | ✅ Pane prévue pour ça |

Le risque "popup bruyante" est mitigeable via le mode discovery passif
(opacity 0.5, pas de focus, dismiss automatique).

### Pourquoi popup-first vs overlay autonome

Un overlay autonome multiplierait les composants UI. La popup existante a
déjà le bon comportement de positionnement au caret, le bon throttling, la
bonne gestion d'opacité. Ajouter un mode au lieu d'une nouvelle fenêtre.

## Décisions ouvertes (à trancher avant impl)

1. **Cible de la recherche** : raccourcis FR/EN (`lim`, `som`, `racine`)
   ou aussi les glyphes (`Σ`, `∫`, `↦`) ? Probablement les deux mais avec
   des poids différents — un user qui tape `lim` s'intéresse au raccourci,
   un user qui colle `Σ` s'intéresse à comment l'avoir au clavier.
2. **Déclenchement** : seuil de caractères tapés ? Délai d'inactivité ?
   Trigger explicite (`Ctrl+?` qui passe la popup en mode discovery) ?
   L'auto-trigger est plus découvrable mais plus risqué pour le flow.
3. **Scoring / ranking** : prefix-match strict, fuzzy (FuzzySharp est déjà
   dans le projet), poids par fréquence d'usage du raccourci ?
4. **Opt-out** : un user expérimenté qui connaît tous les raccourcis va
   trouver l'helper bruyant. Toggle dans ribbon ? Réglage qui désactive
   le mode discovery automatique en gardant le manuel `Ctrl+?` ?
5. **Limite de signaux** : combien de raccourcis affichés en mode
   discovery ? 3 (concis, focus) ? 5 ? Liste défilable ?
6. **Comportement post-conversion** : si l'user tape `lim`, voit `\lim`,
   `\liminf`, `\limsup` listés, et ensuite tape un caractère qui résout
   l'ambiguïté → on bascule en mode normal de la popup avec le bon
   candidat. Cohérent avec le NavMode existant.
7. **Source des données** : on parse les YAML domaines à chaud à chaque
   frappe (pas idéal) ou on construit un index une fois au load
   (cohérent avec ce que fait probablement déjà la cheatsheet) ?

## Pistes écartées (pour mémoire)

- **Pane cheatsheet qui filtre live et s'ouvre tout seul** : trop
  intrusif spatialement, force l'user à regarder à 20 cm du caret.
- **Overlay flottant autonome** : duplique l'effort de positionnement /
  throttling / styling déjà fait dans la popup.
- **Indication inline en gris dans le texte** (style auto-complete IDE) :
  techniquement très lourd dans Word VSTO, et risque de polluer le
  document si l'user accepte par mégarde.

## Références internes

- [`SuggestionPopupWindow.cs`](../../../adapter-vsto/src/MathCursor/UI/SuggestionPopupWindow.cs) — popup qui absorberait le rôle
- [`adapter-vsto/src/MathCursor/Cheatsheet/`](../../../adapter-vsto/src/MathCursor/Cheatsheet/) — cheatsheet pane existante (déjà avec un filtre / search) → source d'inspiration UI + de l'index des raccourcis
- Brief connexe : [`2026-05-05-ribbon-refactor-cheatsheet.md`](2026-05-05-ribbon-refactor-cheatsheet.md)
- Mémoire `project_positioning_speed.md` (rapidité, pas naturel)

## Inspiration externe

- Corca (corca.app) — concept "Input works like search". On **emprunte la
  mécanique** (helper de découverte pendant la saisie), **pas le framing
  marketing** (rapidité, pas naturalité).
