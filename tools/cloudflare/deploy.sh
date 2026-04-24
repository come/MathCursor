#!/usr/bin/env bash
# MathCursor — script de déploiement Cloudflare Pages.
#
# Usage :
#   tools/cloudflare/deploy.sh [site|installer VERSION]
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

  *)
    echo "Usage : $0 [site | installer <version>]" >&2
    exit 1
    ;;
esac
