# Refactor — Nettoyage du code mort (lot 1 de l'audit d'architecture)

**Date :** 2026-10-14
**Kind :** Refactor
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** audit d'architecture du 2026-10-14 (session Claude Code), [2026-06-23-Refactor-delete-dead-host-contract](2026-06-23-Refactor-delete-dead-host-contract.md)

## Citation acté

> « peut etre qu'on peut commencer à nettoyer le code mort ? » puis « ok je valide le lot 1 » — utilisateur, 2026-10-14

## Contexte

L'audit d'architecture du 2026-10-14 a relevé du code compilé ou déployé sans
aucun appelant :

- **`docs/functions/admin/api/reports/create-issue.js`** : endpoint admin qui
  créait une issue GitHub à partir d'un rapport « Signaler une erreur » en y
  recopiant le **paragraphe Word** de l'utilisateur et 8 000 caractères de
  **log**. Le repo cible configuré (`come/MathCursor`) est **public** depuis
  l'ouverture GPLv3 → contraire à `privacy.html` (« aucun partage avec un
  tiers »). Aucune UI admin ne l'appelle, aucune issue n'a jamais été créée
  (vérifié `gh issue list` vide) : risque latent, pas de fuite constatée.
- **POCs VSTO** compilés dans `MathCursor.dll` mais référencés nulle part
  (ni ruban, ni code) : `Host/Debug/HashKeyPoc.cs` (707 l),
  `Host/UndoPoc.cs` (258 l), `Host/OmathWalkerConformance.cs` (263 l, appelé
  seulement par HashKeyPoc), `Host/Blocks/ChainEqArrPoc.cs` (154 l, idem),
  `Host/Blocks/ChainTablePoc.cs` (129 l). ~1 510 lignes.
- **Chemin de dev codé en dur** `D:\Software\DocMath\models` dans la liste des
  racines du modèle NER (`ThisAddIn.cs`), livré en prod.
- **Restes du port Python** (retiré, cf. CLAUDE.md) : dossier local
  `engine-python/` (non suivi, `__pycache__` seul) et commentaires Rust
  évoquant une parité « C#/Python » ; compte de fixtures périmé (465 au lieu
  de 486) dans les commentaires et CLAUDE.md.

## Décision

Supprimer ce code mort, sans changement de comportement à l'exécution :

1. Supprimer `create-issue.js`. **Action manuelle** : retirer `GITHUB_TOKEN` et
   `GITHUB_REPO` des variables d'environnement Cloudflare Pages.
2. Supprimer les 5 POCs VSTO + leurs `<Compile>` dans `MathCursor.csproj`
   (édition à la main, cf. mémoire csproj UTF-8). Récupérables dans git.
3. Retirer `D:\Software\DocMath\models` de la liste des racines : en dev,
   `MATHCURSOR_MODEL_DIR` couvre déjà le besoin.
4. Supprimer le dossier local `engine-python/`, nettoyer les mentions Python
   des commentaires Rust, aligner le compte de fixtures sur 486.

## Tradeoff & alternatives écartées

- **Garder `create-issue.js` en n'envoyant que source + LaTeX + métadonnées** :
  le commentaire libre peut lui aussi contenir des données personnelles, et on
  maintiendrait un chemin sans usage. Si le besoin revient, le refaire avec un
  aperçu éditable avant envoi et un repo privé.
- **Pointer `GITHUB_REPO` vers un repo privé** : correction par config, mais le
  code mort reste.
- **Mettre les POCs sous `#if DEBUG`** : ils ne sont appelés nulle part, même en
  debug ; git suffit comme archive.

Hors scope (décisions séparées) : démo WASM `docs/demo/_framework` (builds
périmés suivis, build live non suivi → fix dans le flux `deploy-prod`, lot 2),
`host-contract/` (gardé pour la phase 2), les `catch {}` silencieux et les
copies de `LogDiag` (dette qualité, pas du code mort).

## Conséquences

- **Code touché** : `docs/functions/admin/api/reports/create-issue.js`
  (supprimé), 5 fichiers `adapter-vsto/src/MathCursor/Host/**` (supprimés),
  `MathCursor.csproj`, `ThisAddIn.cs`, `OmmlToOMathBuilder.cs` (commentaire),
  commentaires `rust/mc-engine/src/**`, `CLAUDE.md`.
- **API publique** : `/admin/api/reports/create-issue` disparaît (aucun appelant).
- **Données** : `privacy.html` redevient exact sur le partage avec des tiers.

## Validation post-fix

Build de l'add-in VSTO + `scripts/run-tests.ps1` verts.
