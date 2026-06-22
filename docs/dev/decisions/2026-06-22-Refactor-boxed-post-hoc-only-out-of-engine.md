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
- **A2 / A3 dormants.** Après itération (banc POC ruban), l'encadrement se fait
  **EN PLACE** sur l'OMath, **sans** repasser par le LaTeX `\boxed` ni le walker.
  Le serializer `\boxed{…}`→`m:borderBox` (A2) et le walker `borderBox` (A3) —
  avec leurs tests (`Boxed_*`, `BorderBox_EstConstructible`) — restent en place
  mais **ne sont plus sur le chemin** de l'encadrement (conservés : inoffensifs,
  testés, et le `\boxed` stocké en map sert encore de marqueur).
- **A4 — `BoxAtCaret` en place** (bouton ruban + entrée popup). Plus de
  delete+redraw (qui butait sur `Range.Delete` « Cannot edit Range » pour les
  OMaths structurées) :
  - `om.Functions.Add(om.Range, wdOMathFunctionBorderBox)` enveloppe le contenu
    de l'OMath directement (objet model Word).
  - **padding horizontal** : un U+2004 (three-per-em) inséré dans l'argument `E`
    de chaque côté (`borderBox` n'a **aucun** attribut de marge — confirmé par
    réflexion sur l'interop ; vertical laissé au ras, choix user).
  - **dernière ligne d'un bloc** (chaîne/système) uniquement : on encadre la
    dernière row de l'eqArr ; on garde **K** `&` d'alignement de tête (**2** sur
    chaîne à connecteur `Row3 = & ⟺ & lhs & relRhs`, **1** sinon), on supprime
    les autres `&` (localisés via `Range.Characters` — fiable, pas de `MoveRight`
    qui dérive sur les marqueurs — supprimés via `Range.Delete`). Résultat :
    connecteur **hors** cadre et aligné avec les autres, contenu boxé **sans `&`
    visible**, alignement des lignes du dessus préservé (la ligne boxée ne
    réaligne pas son signe interne — contenu = un bloc, assumé).
  - source-map re-`Record` après mutation (le contenu change → K1/K2 changent ;
    `Type` du bloc préservé) ; sténo stockée = **sténo intérieure** → « Revenir à
    la saisie » désencadre proprement. Garde anti double-encadrement :
    `entry.IsAlreadyBoxed` (`\boxed` présent dans le latex stocké).
- **Nouveau — entrée popup d'édition.** `EditModePopupWindow` reçoit une ligne
  au-dessus de « Revenir à la saisie initiale » : **« Encadrer cette formule »**
  (simple) ou **« Encadrer la dernière ligne »** (bloc — `EquationSource.IsBlock`).
  Clic → `ConversionController.BoxAtCaret` (callback câblé `ThisAddIn` →
  `EditModeController`). Libellés i18n FR/EN dans `Strings.cs`.

## Tradeoff & alternatives écartées

- **Garder `boxed` en sténo (statu quo ADR 2026-06-20)** : écarté à la demande
  utilisateur — le geste de frappe n'est pas naturel pour une décoration de
  résultat.
- **Stocker `boxed(<sténo>)` comme source** : écarté — sténo non reconvertible
  une fois `boxed` hors moteur, casse le round-trip d'édition. La sténo
  intérieure rend le revert prévisible (désencadre).
- **Wrapper l'OMath directement côté Word (in-place)** : d'abord écarté au profit
  du pipeline `\boxed`→`LatexToOmml`→walker, puis **adopté** après que ce pipeline
  (delete+redraw) a buté sur `Range.Delete` « Cannot edit Range » pour les OMaths
  structurées (frac/int/eqArr). `Functions.Add` en place est plus robuste.
- **Grattage des `&` par position (`MoveRight`)** : écarté — les marqueurs
  d'alignement `&` ne sont pas des positions navigables fiables → un `&` survit.
  `Range.Characters` (énumération native) les localise correctement.
- **Re-composer le bloc via `ChainComposer` + ré-insérer** : écarté — exigeait de
  supprimer l'OMath structuré (retour du `Selection.Delete`, jugé trop risqué par
  l'utilisateur). Le grattage in-place des `&` évite toute ré-insertion.
- **`borderBox` pleine ligne sur un bloc** : écarté — engloberait les `&` (visibles
  en clair) et casserait l'alignement. D'où le « garder K `&`, boxer le reste ».

## Conséquences

- **Code touché** :
  - Moteur/data : `data/engine/symbols.json`, `data/engine/cultures.json`,
    `engine/tests/.../fixtures.json` (sortie A1).
  - Adapter : `Host/ConversionController.cs` (`BoxAtCaret` en place +
    `FindEqArray` + `BoxLastLatexLine`), `Host/SourceMap/EquationSource.cs`
    (`IsBlock`/`IsMatrixLike`/`IsAlreadyBoxed`/`CanBox`),
    `UI/EditModePopupWindow.cs` (2ᵉ ligne + event `BoxRequested` + libellé),
    `Host/EditMode/EditModeController.cs` (callback `boxAtCaret` + handler +
    choix libellé), `ThisAddIn.cs` (câblage), `Strings.cs` (libellés i18n).
- **Inchangés** : `LatexToOmml.cs` (A2), `OmmlWalkerWhitelist.cs` /
  `OmmlToOMathBuilder.cs` (A3), `MixedLatexRenderer.cs` / `WpfMathAdapter.cs`
  (aperçu), bouton ruban `BoxResultButton`, volet B callouts. (A2/A3 hors chemin
  d'encadrement désormais, mais conservés.)
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
- Manuel Word (validé en session 2026-06-22) :
  1. `boxed(x=2)` + `Ctrl+Espace` → ne s'encadre plus ;
  2. équation simple → popup « Encadrer cette formule » / bouton ruban → encadrée
     en place + padding latéral ;
  3. **chaîne `⟺`** → popup « Encadrer la dernière ligne » → seule la dernière
     ligne encadrée, connecteur dehors et aligné, **pas de `&` visible** (validé
     « c'est parfait ») ;
  4. « Revenir à la saisie » sur une formule encadrée → formule simple ;
  5. FR/EN libellés.
