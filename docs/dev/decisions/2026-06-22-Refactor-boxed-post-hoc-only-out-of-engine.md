# Refactor — `boxed` post-hoc uniquement (sortie du moteur)

**Date :** 2026-06-22
**Kind :** Refactor
**Température :** molle
**Statut :** acté
**Supersedes :** [2026-06-20-Feat-encadres-callouts-boxed](2026-06-20-Feat-encadres-callouts-boxed.md) — **partie A1 seulement** (l'entrée data sténo `boxed`/`encadre`/`cadre`). A2, A3, A4 et le volet B (callouts) de cet ADR restent actés.

## Citation acté

> « retirer la fonction boxed du moteur […] ne garder que le bouton dans le ruban
> pour injecter le boxed après coup, plutôt qu'en mode inline » — utilisateur,
> 2026-06-22

> « il faut reserializer avec boxed dans le latex mais que ça sorte du A1 c'est
> sûr.. et idéalement, rajouter une entrée au dessus de "revenir à la saisie",
> qui dirait "encadrer cette formule" » — utilisateur, 2026-06-22

## Contexte

L'ADR [2026-06-20](2026-06-20-Feat-encadres-callouts-boxed.md) avait fait de
`boxed` une **notation math first-class** : tapable en sténo (`boxed(x)`,
`encadre(x)`, `cadre(x)` → `\boxed{…}`) grâce à une entrée data moteur (volet
A1), puis sérialisée en `m:borderBox` (A2), construite par le walker (A3), et
aussi posable après coup par un bouton ruban (A4).

À l'usage, l'utilisateur revient sur le **mode inline** : taper `boxed(...)` au
clavier n'est pas le bon geste. Encadrer un résultat est une **décoration
appliquée après coup** sur une équation déjà posée, pas une notation que l'on
saisit. C'est exactement l'alternative « boxed après coup » qui figurait en
*tradeoff* de l'ADR 2026-06-20 et qui avait alors été écartée au profit du
first-class — on l'adopte désormais.

## Décision

`boxed` n'est plus une notation saisissable ; c'est une action **post-hoc** sur
une équation existante. Concrètement :

- **A1 — rétracté.** Suppression de l'entrée `boxed` dans
  `data/engine/symbols.json`, des alias `encadre`/`cadre`/`encadrer` dans
  `data/engine/cultures.json`, et des fixtures `boxed …` du moteur. Le moteur ne
  reconnaît plus `boxed(...)` (redevient du texte ordinaire).
- **A2 conservé.** La re-sérialisation `\boxed{…}` → `m:borderBox`
  (`serialization/.../LatexToOmml.cs`) reste : c'est elle qui permet d'injecter
  le cadre après coup (demande explicite « reserializer avec boxed dans le
  latex »).
- **A3 conservé.** Walker `borderBox` + aperçu popup inchangés. Le verrou
  `WalkerCoverageTests` garde un test **explicite** `BorderBox_EstConstructible`
  indépendant du corpus moteur → la sortie de `boxed` du corpus ne le casse pas.
- **A4 conservé, ajusté.** Le bouton ruban « Encadrer » reste. `BoxAtCaret`
  stocke désormais la **sténo intérieure** (`entry.Steno`) au lieu de
  `boxed(<sténo>)` : comme `boxed` a quitté le moteur, `boxed(...)` ne serait
  plus reconvertible, donc « Revenir à la saisie » sur une formule encadrée
  rend la **formule simple** (désencadrement), pas un littéral `boxed(...)`. Le
  garde-fou anti double-encadrement reste basé sur `entry.Latex.StartsWith("\\boxed{")`.
- **Nouveau — entrée popup d'édition.** La popup contextuelle
  (`EditModePopupWindow`), qui s'ouvre quand le caret atterrit sur une de nos
  OMaths, reçoit une ligne **« Encadrer cette formule »** au-dessus de
  « Revenir à la saisie initiale ». Clic → `ConversionController.BoxAtCaret`
  (callback câblé via `ThisAddIn` → `EditModeController`). Libellé i18n FR/EN
  dans `Strings.cs`.

## Tradeoff & alternatives écartées

- **Garder `boxed` en sténo (statu quo ADR 2026-06-20)** : écarté à la demande
  utilisateur — le geste de frappe n'est pas naturel pour une décoration de
  résultat.
- **Stocker `boxed(<sténo>)` comme source** : écarté — sténo non reconvertible
  une fois `boxed` hors moteur, casse le round-trip d'édition. La sténo
  intérieure rend le revert prévisible (désencadre).
- **Retirer aussi A2/A3 et wrapper l'OMath directement côté Word** : écarté —
  réutiliser le pipeline `\boxed` → `LatexToOmml` → walker existant est plus sûr
  (un seul chemin d'insertion, testé) et répond au « reserializer avec boxed ».

## Conséquences

- **Code touché** :
  - Moteur/data : `data/engine/symbols.json`, `data/engine/cultures.json`,
    `engine/tests/.../fixtures.json` (sortie A1).
  - Adapter : `Host/ConversionController.cs` (`BoxAtCaret` source intérieure),
    `UI/EditModePopupWindow.cs` (2ᵉ ligne + event `BoxRequested`),
    `Host/EditMode/EditModeController.cs` (callback `boxAtCaret` + handler),
    `ThisAddIn.cs` (câblage), `Strings.cs` (libellé i18n).
- **Inchangés** : `LatexToOmml.cs` (A2), `OmmlWalkerWhitelist.cs` /
  `OmmlToOMathBuilder.cs` (A3), `MixedLatexRenderer.cs` / `WpfMathAdapter.cs`
  (aperçu), bouton ruban `BoxResultButton`, volet B callouts.
- **Tests** : fixtures moteur `boxed …` supprimées ; tests A2 `Boxed_*`
  (serialization) et A3 `BorderBox_EstConstructible` (adapter) restent verts.
- **API publique** : inchangée (`ForestEngine.Analyze`, `OMathInserter.Insert`,
  `ConversionController.BoxAtCaret`).
- **Règles MC impactées** : aucune. Port JS de la démo web non concerné.

## Validation post-fix

- Moteur : `dotnet test` engine → aucun test ne référence `boxed` ; `boxed(x)`
  redevient du texte.
- Serialization : `dotnet test` → `Boxed_*` verts (A2 intact).
- Adapter : `dotnet test` → `WalkerCoverageTests.BorderBox_EstConstructible`
  vert ; build VSTO OK.
- Manuel Word :
  1. `boxed(x=2)` + `Ctrl+Espace` → ne s'encadre plus ;
  2. caret dans une équation → bouton ruban « Encadrer » → encadrée, 1 Ctrl+Z
     annule ;
  3. caret sur une de nos équations → popup montre « Encadrer cette formule »
     au-dessus de « Revenir à la saisie initiale » → clic → encadrée ;
  4. « Revenir à la saisie » sur une formule encadrée → formule simple ;
  5. FR/EN libellés.
