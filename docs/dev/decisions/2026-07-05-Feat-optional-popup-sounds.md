# Feat — Sons synthétisés optionnels à l'ouverture / validation de la popup

**Date :** 2026-07-05
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** `ConversionController` (hooks popup show / commit), `SettingsWindow`, ADR [2026-06-18-Feat-usage-counter-telemetry](2026-06-18-Feat-usage-counter-telemetry.md) (modèle de réglage opt-out `SendUsageStats`)

## Citation acté

> « tu peux me faire un truc optionnel (sur parametre false par defaut ! activable via le ruban word) pour jouer des petits sons quand la popup s'ouvre, et quand elle est validée ? » — utilisateur, 2026-07-05

Toggle **dans la fenêtre Paramètres uniquement** (pas de bouton ruban, contrairement à la formulation initiale).

Sur la source des sons, deux temps dans la même séance : d'abord une **synthèse en code** (sons trop agressifs au test), puis l'auteur fournit deux fichiers concrets — « prends ces deux là [`creatorshome-low-pop`, `universfield-bubble-pop-02`] et réduis-les au minimum pour qu'il n'y ait aucun temps mort ? low pop pour l'ouverture de popup ». Décision finale : **WAV embarqués**, rognés au silence près depuis ces mp3.

## Contexte

Un petit
retour sonore doux à l'ouverture de la popup de suggestions et à la
validation d'une formule rend la saisie plus agréable. C'est purement un
réglage d'agrément : il ne doit avoir **aucun impact sur l'usage réel** (élève
PAP, profs beta) ni sur l'ergo Word.

## Décision

Ajouter un réglage booléen `PlaySounds`, **OFF par défaut**, exposé comme case à
cocher dans la fenêtre Paramètres (pas dans le ruban). Quand il est activé :

- **Ouverture** de la popup (transition non-visible → visible uniquement) : joue
  le « low pop ».
- **Validation** (commit réussi d'une formule) : joue le « bubble pop ».

Les deux sons sont des **WAV embarqués** (`EmbeddedResource`, LogicalName
`MathCursor.Sounds.open.wav` / `…validate.wav`), obtenus en convertissant les
mp3 fournis en **mono 44,1 kHz 16-bit**, en **rognant le silence de début** (attaque
immédiate) et le silence numérique de **fin** (`ffmpeg silenceremove` -50 dB en
tête / -75 dB en queue) tout en **conservant toute la décroissance audible** +
fade-out 40 ms (fin en douceur, pas de coupe nette). Durées : ouverture ≈ 1,36 s
(traîne complète du low pop), validation ≈ 0,26 s. Le helper statique `SoundEffects` les
charge une seule fois via `GetManifestResourceStream` et les joue avec
`System.Media.SoundPlayer` (asm déjà référencé, lecture non bloquante). Chaque
appel est gardé en tête par `if (!SettingsStore.Current.PlaySounds) return;`
(coût nul quand OFF) et enveloppé dans un try/catch qui avale tout (un son ne
doit jamais casser l'UX).

Hooks (sur des points de cycle de vie existants, aucune API Word critique) :
`AnalyzeAndShow` et `ShowSystem` gardés par `!wasVisible` (pas de son au refresh
frappe, ni au `WarmUpPopup` off-screen) ; `CommitSelected` juste avant le
`return true`, à côté du compteur d'usage. `BoxAtCaret` n'est pas sonorisé.

## Tradeoff & alternatives écartées

- **Synthèse en code** (première tentative) : zéro asset binaire, mais rendu
  trop agressif au test ; l'auteur a préféré fournir des sons choisis. Deux WAV
  embarqués = qualité maîtrisée pour un coût binaire modeste (~140 Ko au total).
  Premier trim trop serré (traîne coupée net, « trop rogné ») → corrigé en
  gardant toute la décroissance audible + fade-out.
- **Override fichier `%APPDATA%\MathCursor\sounds\`** : envisagé pour itérer sans
  rebuild, mais l'auteur a fourni des fichiers précis → embarqués directement.
  Réactivable si le besoin de changer souvent revient.
- **Bouton toggle dans le ruban** (formulation initiale) : écarté pour ne pas
  encombrer le ruban avec un réglage d'agrément ; la fenêtre Paramètres est le
  bon foyer (cohérent avec les autres réglages secondaires).
- **Sonoriser chaque frappe / `BoxAtCaret` / `WarmUpPopup`** : bruyant et hors
  demande. On limite aux deux moments demandés (ouverture réelle, validation).

## Conséquences

- **Code touché** :
  - `adapter-vsto/src/MathCursor/Host/Settings/AppSettings.cs` — `PlaySounds` (défaut `false`).
  - `adapter-vsto/src/MathCursor/Host/Settings/SettingsStore.cs` — clé `play_sounds` (Serialize + TryParse ; absence → défaut).
  - `adapter-vsto/src/MathCursor/Host/Sound/SoundEffects.cs` — **nouveau**, charge + play les WAV embarqués.
  - `adapter-vsto/src/MathCursor/Host/Sound/open.wav` + `validate.wav` — **nouveaux** assets (rognés), embarqués via `MathCursor.csproj` (`EmbeddedResource` + LogicalName).
  - `adapter-vsto/src/MathCursor/Host/ConversionController.cs` — hooks ouverture (2 sites) + validation.
  - `adapter-vsto/src/MathCursor/UI/SettingsWindow.cs` + `Strings.cs` — case à cocher + libellés.
  - `MathCursor.csproj` (Compile Include du nouveau `.cs`), `MathCursor.Tests.csproj` (test).
- **Tests** : nouveau test de réglage miroir de `SendUsageStatsSettingTests` (défaut `false`, clone, save/load, clé absente → `false`). `SoundEffects` non testé unitairement (I/O audio, vérif manuelle).
- **API publique** : aucune. Additif, rétro-compatible (settings.json sans la clé reste silencieux).
- **Règles MC impactées** : aucune. N'entre pas dans le périmètre word-api critique (aucun `SetRange/Delete`, `OMaths.Add/BuildUp`, ContentControls, positions).

## Validation post-fix

- Gate `scripts/run-tests.ps1` vert, dont le nouveau test de réglage.
- Manuel Word : Paramètres → cocher « Sons » → Ctrl+Espace = « pop » à
  l'ouverture (une seule fois, pas par frappe, pas au warm-up) ; commit
  (Entrée/Tab) = « ding ». Décocher → silence total. `settings.json` gagne
  `"play_sounds":false` par défaut, `true` après activation.

## Ajustement du 2026-10-26 : plus de son d'ouverture, validation plus tôt

> « on retirera le son d'arrivée de popup (pas de sortie de popup) et si possible
> je veux bien que tu check si on peut pas mettre le son de sortir de popup plus
> tot, on a l'impression qu'il y'a un leger decalage » — utilisateur, 2026-10-26.

- **Son d'ouverture retiré** : appels `PlayOpen` supprimés de `AnalyzeAndShow` /
  `ShowSystem`, ressource `open.wav` retirée du projet.
- **Son de validation avancé** : joué au début de `CommitSelected` (dès que la
  sélection est valide), **avant** l'insertion Word (ZoneCleaner, walker, OMath :
  quelques centaines de ms) au lieu d'après. Contrepartie acceptée : si l'insertion
  échoue, le son a déjà été joué (réglage d'agrément).
- **Latence de déclenchement réduite** : un `SoundPlayer` unique, préchargé
  (`Load()`) à la première validation puis réutilisé, au lieu d'un player recréé
  et relu à chaque fois.
