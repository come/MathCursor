#!/usr/bin/env bash
# MathCursor — script de déploiement Cloudflare Pages.
#
# Usage :
#   tools/cloudflare/deploy.sh [site|installer VERSION|vsix VERSION [DIR]|model]
#
#   vsix  = upload des VSIX VS Code multiplateforme (alphas) construits par la CI
#           vers le bucket mathcursor-releases (cf. README §Publier un VSIX).
#
#   model = (ré)upload du modèle NER (models/latest/) vers le bucket public
#           mathcursor-models, consommé par la CI VSIX VSCode (workflow
#           vscode-vsix). À lancer après un réentraînement NER.
#
# Prérequis :
#   - ~/.mathcursor/cloudflare.env configuré (voir tools/cloudflare/README.md)
#   - npx disponible (nodejs installé)

set -euo pipefail

ENV_FILE="$HOME/.mathcursor/cloudflare.env"
if [ ! -f "$ENV_FILE" ]; then
  echo "ERREUR : $ENV_FILE introuvable." >&2
  echo "Crée ce fichier avec ton token Cloudflare (voir tools/cloudflare/README.md)." >&2
  exit 1
fi
# shellcheck source=/dev/null
source "$ENV_FILE"

if [ -z "${CLOUDFLARE_API_TOKEN:-}" ] || [ "${CLOUDFLARE_API_TOKEN}" = "REMPLACE_PAR_TON_TOKEN" ]; then
  echo "ERREUR : CLOUDFLARE_API_TOKEN non rempli dans $ENV_FILE." >&2
  exit 1
fi

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
DOCS="$ROOT/docs"
INSTALLER_DIR="$ROOT/adapter-vsto/installer/output"

cmd="${1:-site}"

case "$cmd" in
  site)
    echo "Déploiement du site → mathcursor.pages.dev"
    cd "$DOCS"
    npx --yes wrangler@latest pages deploy . \
      --project-name=mathcursor \
      --branch=main \
      --commit-dirty=true
    ;;

  installer)
    version="${2:-}"
    if [ -z "$version" ]; then
      echo "Usage : $0 installer <version>   (ex. 0.3.0)" >&2
      exit 1
    fi
    file="$INSTALLER_DIR/MathCursor-Setup-$version.exe"
    if [ ! -f "$file" ]; then
      echo "ERREUR : $file introuvable. Build-le d'abord." >&2
      exit 1
    fi
    echo "Upload $file → R2://mathcursor-releases/MathCursor-Setup-$version.exe"
    npx --yes wrangler@latest r2 object put \
      "mathcursor-releases/MathCursor-Setup-$version.exe" \
      --file="$file" \
      --content-type="application/octet-stream" \
      --remote
    echo ""
    echo "N'oublie pas :"
    echo "  - Mettre à jour LATEST_VERSION dans docs/functions/download/[[filename]].js"
    echo "  - Ajouter l'entrée dans docs/releases.html"
    echo "  - Re-déployer le site : $0 site"
    ;;

  vsix)
    # Upload des VSIX VS Code *platform-specific* (alphas) vers R2. Les .vsix sont
    # construits par la CI (workflow vscode-vsix, un runner par OS) → télécharge les
    # artifacts puis lance cette commande sur le dossier qui les contient. Cf. ADR
    # 2026-06-29-Feat-vscode-vsix-multiplatform-site-distribution.
    version="${2:-}"
    dir="${3:-$HOME/Downloads}"
    if [ -z "$version" ]; then
      echo "Usage : $0 vsix <version> [dossier-artifacts]   (ex. 0.1.0 ~/Downloads/vsix)" >&2
      exit 1
    fi
    found=0
    for t in win32-x64 linux-x64 darwin-arm64; do
      # Artifact CI = mathcursor-<target>.vsix (éventuellement dans un sous-dossier
      # vsix-<target>/). On prend le premier match. Cible absente = ignorée (ex. mac
      # non buildé si la CI a tourné sur push, sans workflow_dispatch).
      src="$(find "$dir" -type f -name "mathcursor-$t*.vsix" 2>/dev/null | head -n1)"
      if [ -z "$src" ]; then
        echo "  (cible $t : aucun .vsix dans $dir, ignorée)"
        continue
      fi
      dst="mathcursor-$t-$version.vsix"
      echo "Upload $src → R2://mathcursor-releases/$dst"
      npx --yes wrangler@latest r2 object put \
        "mathcursor-releases/$dst" \
        --file="$src" \
        --content-type="application/octet-stream" \
        --remote
      found=$((found + 1))
    done
    if [ "$found" -eq 0 ]; then
      echo "ERREUR : aucun .vsix trouvé dans $dir (attendu mathcursor-<target>.vsix)." >&2
      exit 1
    fi
    echo ""
    echo "N'oublie pas :"
    echo "  - Mettre à jour la map LATEST_VSCODE_VSIX dans docs/functions/_latest.js (version $version)"
    echo "  - Vérifier les 3 boutons dans docs/releases.html"
    echo "  - Re-déployer le site : $0 site"
    ;;

  model)
    # Modèle NER pour la CI VSIX VSCode (bucket PUBLIC mathcursor-models). À
    # lancer après un réentraînement (cf. /update-ner-model) pour que le workflow
    # vscode-vsix récupère le nouveau modèle. Cf. ADR
    # 2026-06-25-Feat-vscode-marketplace-publishing-model.
    model_dir="$ROOT/models/latest"
    onnx="$model_dir/model_quantized.onnx"
    tok="$model_dir/tokenizer.json"
    for f in "$onnx" "$tok"; do
      if [ ! -f "$f" ]; then
        echo "ERREUR : $f introuvable (modèle NER absent dans models/latest)." >&2
        exit 1
      fi
    done
    echo "Upload modèle NER → R2://mathcursor-models/latest/"
    npx --yes wrangler@latest r2 object put \
      "mathcursor-models/latest/model_quantized.onnx" \
      --file="$onnx" \
      --content-type="application/octet-stream" \
      --remote
    npx --yes wrangler@latest r2 object put \
      "mathcursor-models/latest/tokenizer.json" \
      --file="$tok" \
      --content-type="application/json" \
      --remote
    echo ""
    echo "Modèle NER uploadé. Le prochain run du workflow vscode-vsix le prendra."
    ;;

  *)
    echo "Usage : $0 [site | installer <version> | vsix <version> [dossier] | model]" >&2
    exit 1
    ;;
esac
