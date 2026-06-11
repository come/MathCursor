# UX — Toggle « Tab valide » : commit au Tab opt-in (défaut OFF)

**Date :** 2026-06-10
**Kind :** UX
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Feat-ner-auto-detection-debounce.md](2026-06-10-Feat-ner-auto-detection-debounce.md) (toggle Détection auto voisin), [2026-06-10-Refactor-phase2-adapter-orchestration-rewrite.md](2026-06-10-Refactor-phase2-adapter-orchestration-rewrite.md) (Tab = commit historique)

## Citation acté

> « ok eventuellement un autre toggle à cote pour faire du tab to validate
> (inactif par defaut) le tab valide le premier choix si popup (et ne propage
> pas le tab) » — utilisateur, 2026-06-10

## Contexte

Depuis la Phase 2, Tab committait TOUJOURS le candidat sélectionné quand la
popup était ouverte. Avec l'auto-détection NER, la popup est levée quasi en
permanence pendant la frappe math — un Tab de mise en forme se transforme
alors en commit involontaire. Et inversement, certains utilisateurs veulent
un flux « Tab pour accepter » à la IntelliSense.

## Décision

Réglage **`AppSettings.TabValidate`**, **défaut OFF** :

- **OFF** : Tab n'est jamais intercepté — tabulation Word normale, popup
  ouverte ou pas. (Changement du défaut historique : avant, Tab committait.)
- **ON** : popup ouverte, Tab commit le candidat sélectionné (= le 1er si
  l'utilisateur n'a pas navigué) et la touche est consommée (pas de
  tabulation insérée). Popup fermée : tabulation normale.

Exposé en **toggle ruban « Tab valide »** (groupe Conversion, à côté de
« Détection auto ») + case dans la fenêtre Paramètres, persisté
`tab_validate` dans settings.json. Sync ruban ↔ Paramètres via
`RibbonCallback.Instance.InvalidateSettingsToggles()`. Enter en nav mode et
le clic restent les voies de commit toujours actives.

## Tradeoff & alternatives écartées

- **Garder Tab-commit toujours actif (comportement Phase 2)** : avec la popup
  quasi permanente de l'auto-détection, le coût d'un commit involontaire
  (équation insérée à la place d'une tabulation) dépasse le gain de vitesse ;
  l'opt-in protège le défaut « comportement Word prévisible » (objectif PAP).
- **Lier Tab-commit à l'état du toggle Détection auto** : couplage implicite
  illisible — deux intentions distinctes, deux switches.

## Conséquences

- **Code touché** : `AppSettings`/`SettingsStore` (champ + clé
  `tab_validate`), `ThisAddIn.HandleTabPressed` (gate), `Ribbon.xml` +
  `RibbonCallback` (toggle + sync), `SettingsWindow` (case), `Strings`.
- **Comportement par défaut modifié** : Tab ne commit plus tant que le toggle
  est OFF — à mentionner aux beta-testeurs habitués.
- **API publique** : aucune.

## Validation post-fix

Word : popup ouverte, toggle OFF → Tab insère une tabulation et la popup
reste ; toggle ON → Tab insère la 1ʳᵉ proposition, pas de tabulation. État
persistant après redémarrage, case Paramètres synchrone.
