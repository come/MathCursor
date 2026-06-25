---
name: update-ner-model
description: Met à jour le modèle NER partagé `models/latest/` à partir d'un nouveau modèle (zip téléchargé du Drive ou dossier). Archive l'ancien dans `models/archive/<horodatage>/`, valide les 7 fichiers requis, installe le nouveau, puis lance les tests de corpus NER (F1) pour valider la non-régression. Rollback automatique proposé si les seuils ne passent pas. À utiliser quand l'utilisateur dit "maj du modèle NER", "update latest", "nouveau modèle NER", "remplacer le modèle", "j'ai réentraîné le NER".
user-invocable: true
allowed-tools:
  - Read
  - Bash
  - Glob
  - AskUserQuestion
---

# /update-ner-model — Swap fiable du modèle NER `models/latest/`

`models/latest/` est l'**alias stable** lu par les 3 adapters (VSTO runtime `ThisAddIn.TryFindModelDir`, installeur `build.ps1`, fixture de tests `NerCorpusFixture`) et les futurs hosts vscode/libreoffice. On ne renomme JAMAIS le dossier au retrain : on **remplace son contenu**. Ce skill fait ce swap proprement, sans perdre l'ancien modèle et sans installer un modèle cassé ou en régression.

Working dir : `D:/Software/MathCursor`. `models/` est **gitignoré** → aucune opération git ici, tout est local disque + tests.

## Les 7 fichiers requis d'un modèle valide

```
model_quantized.onnx   config.json          ort_config.json
vocab.txt              tokenizer.json       tokenizer_config.json
special_tokens_map.json
```

Si un seul manque → **STOP**, ne pas swapper (un modèle incomplet = crash au chargement ONNX ou tokenizer KO chez les users).

---

## Étape 0 — Localiser le nouveau modèle (source)

La source vient en argument (`$ARGUMENTS`) : chemin d'un **.zip** (export Drive, souvent dans `~/Downloads`) OU d'un **dossier**.

- Si `$ARGUMENTS` est vide → demander à l'utilisateur le chemin (AskUserQuestion ou question simple). Cas le plus courant : un zip `onnx-int8-*-<date>.zip` dans `C:\Users\wanadev\Downloads`.
- Si c'est un **.zip** → l'extraire dans un dossier temporaire (`$env:TEMP\mc-ner-<stamp>`), puis **trouver le dossier qui contient `model_quantized.onnx`** (le zip nidifie souvent sous un sous-dossier type `onnx-int8-distilmult-pruned/`). Ce dossier = `$srcDir`.
- Si c'est un **dossier** → `$srcDir` = ce dossier (ou le sous-dossier contenant `model_quantized.onnx`).

```powershell
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
# … extraction zip si besoin via Expand-Archive …
# Repérer la racine réelle du modèle (où vit model_quantized.onnx)
$srcDir = (Get-ChildItem -Path $extractRoot -Recurse -Filter 'model_quantized.onnx' | Select-Object -First 1).Directory.FullName
```

---

## Étape 1 — Valider la source (AVANT de toucher à latest)

Vérifier les 7 fichiers dans `$srcDir`. Si incomplet → lister ce qui manque et **STOP** (ne rien archiver, ne rien supprimer).

```powershell
$required = @('model_quantized.onnx','config.json','ort_config.json',
              'special_tokens_map.json','tokenizer.json','vocab.txt','tokenizer_config.json')
$missing = $required | Where-Object { -not (Test-Path (Join-Path $srcDir $_)) }
if ($missing) { Write-Host "INCOMPLET, manque : $($missing -join ', ')" -ForegroundColor Red; exit 1 }
$onnxMB = [math]::Round((Get-Item (Join-Path $srcDir 'model_quantized.onnx')).Length/1MB,1)
Write-Host "Source OK ($onnxMB Mo)" -ForegroundColor Green
```

---

## Étape 2 — Archiver l'ancien `models/latest/`

Si `models/latest/` existe et est non vide → le déplacer vers `models/archive/<stamp>/` AVANT d'installer le nouveau. **Jamais d'écrasement sans archive.** Écrire un petit `MANIFEST.txt` (date, taille, source) pour retrouver d'où vient chaque archive.

```powershell
$latest  = 'D:/Software/MathCursor/models/latest'
$archive = "D:/Software/MathCursor/models/archive/$stamp"
if ((Test-Path $latest) -and (Get-ChildItem $latest -ErrorAction SilentlyContinue)) {
    New-Item -ItemType Directory -Force -Path (Split-Path $archive) | Out-Null
    Move-Item $latest $archive
    "archivé le $stamp depuis $latest" | Out-File (Join-Path $archive 'MANIFEST.txt') -Encoding utf8
    Write-Host "Ancien modèle archivé -> models/archive/$stamp" -ForegroundColor Green
}
```

> Note sandbox : `Remove-Item -Recurse -Force` peut être bloqué en interactif. Préférer `Move-Item` (archive) et `Copy-Item` (install). On ne supprime jamais en dur : l'ancien part en archive, pas à la corbeille.

---

## Étape 3 — Installer le nouveau modèle dans `models/latest/`

Copier **uniquement les 7 fichiers** (à plat, pas de sous-dossier parasite issu du zip) dans `models/latest/`.

```powershell
New-Item -ItemType Directory -Force -Path $latest | Out-Null
foreach ($f in $required) { Copy-Item (Join-Path $srcDir $f) (Join-Path $latest $f) -Force }
Get-ChildItem $latest | Select-Object Name, @{n='Mo';e={[math]::Round($_.Length/1MB,2)}}
```

---

## Étape 4 — Tests de corpus NER (validation non-régression)

Le cœur du skill. La fixture `NerCorpusFixture` lit désormais `models/latest/` → avec le modèle en place, les `MathNerInferenceTests` **tournent** (au lieu de skip) et calculent les F1 sur le corpus + le gold hold-out, avec seuils :

| Test | Métrique | Seuil |
|------|----------|-------|
| corpus échantillon | entity-F1 | ≥ 0.90 |
| corpus précision | precision | ≥ 0.95 |
| 80 premiers | F1 | ≥ 0.90 |
| gold hold-out | entity-F1 | ≥ 0.93 |

```powershell
dotnet test adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj `
  --filter "FullyQualifiedName~MathNerInferenceTests" --nologo --verbosity normal
```

- Lire la sortie : si des tests sont **skip** → le modèle n'a pas été trouvé (vérifier que les 7 fichiers sont bien dans `models/latest/`) → diagnostiquer, ne pas prétendre que c'est validé.
- Si des seuils **échouent** → le nouveau modèle régresse sous la baseline. **Ne pas laisser le modèle en place silencieusement.** Montrer les F1 obtenus vs seuils et demander via AskUserQuestion : *"Le nouveau modèle régresse (détails). Rollback vers l'archive, ou garder quand même ?"*

> `regression_v1_gold.jsonl` est le hold-out (jamais dans le train). Une chute du F1 gold est le signal le plus fiable d'un sur-apprentissage / mauvais export.

---

## Étape 5 — Rollback (si tests rouges ou refus utilisateur)

Restaurer l'archive fraîchement créée :

```powershell
Remove-Item $latest -Recurse -Force -ErrorAction SilentlyContinue  # ou Move-Item vers un .bad
Move-Item $archive $latest
Write-Host "Rollback : models/latest/ restauré depuis l'archive $stamp" -ForegroundColor Yellow
```

---

## Étape 6 — Publier le modèle sur R2 pour la CI VSIX (SI validé)

⚠️ **Uniquement si l'étape 4 est PASS** (ne jamais publier un modèle en régression).

La CI VSIX VSCode (workflow `vscode-vsix`) ne lit **pas** le disque : elle tire le
modèle du bucket **public** `mathcursor-models`. Tant qu'on ne le ré-uploade pas,
la CI continue d'empaqueter **l'ancien** modèle. Après un retrain validé, publier :

```bash
tools/cloudflare/deploy.sh model
```

(Pousse `models/latest/model_quantized.onnx` + `tokenizer.json` vers
`mathcursor-models/latest/`. Nécessite `~/.mathcursor/cloudflare.env`. Cf. ADR
`2026-06-25-Feat-vscode-marketplace-publishing-model` + skill `/deploy-prod`
étape 4b.)

Demander via AskUserQuestion avant de pousser (action sortante, bucket public) :
*« Publier le nouveau modèle sur R2 pour la CI VSIX maintenant ? »* — si non, le
rappeler dans le rapport (à faire plus tard). N'affecte **ni** l'installer Word
**ni** le commit (le modèle reste gitignoré localement).

---

## Rapport final

Format court :

```
✓ Source     : <zip/dossier> (<N> Mo, 7/7 fichiers)
✓ Archivé    : models/archive/<stamp>/  (ancien modèle préservé)
✓ Installé   : models/latest/  (<N> Mo)
✓ Corpus NER : F1 échantillon X.XXX (≥0.90) · précision X.XXX (≥0.95) · 80p X.XXX (≥0.90) · gold X.XXX (≥0.93) → PASS
✓ R2 (CI VSIX) : modèle publié sur mathcursor-models / à publier (`deploy.sh model`)
→ Pour livrer Word : /build-iss puis /deploy-prod (le modèle est gitignoré, rien à committer).
→ Pour livrer VSCode : publier le modèle sur R2 (étape 6) → la CI vscode-vsix le prendra.
```

Si rollback : ✗ + F1 obtenus vs seuils + "modèle précédent restauré, rien n'a changé en prod".

---

## Garde-fous

- **Toujours archiver avant d'écraser.** L'ancien modèle ne doit jamais être perdu — il part dans `models/archive/<stamp>/`, pas supprimé.
- **Valider les 7 fichiers AVANT** de toucher à `latest`. Pas de swap partiel.
- **Ne jamais** committer/pusher (`models/` est gitignoré de toute façon). Pas de `git add -A` (sweep le WIP `adapter-vscode/`).
- **Ne pas toucher** à `adapter-vscode/` (WIP parallèle de l'utilisateur).
- Si les tests de corpus **skippent** (modèle introuvable) ce n'est PAS un succès — diagnostiquer.
- Un seul nom de dossier neutre (`latest`) pour les 3 adapters : ne pas réintroduire de chemin versionné côté code.

Arguments : `$ARGUMENTS` = chemin du zip ou dossier source (optionnel ; demandé si absent).
