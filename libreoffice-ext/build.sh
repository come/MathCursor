#!/usr/bin/env bash
# Construit MathCursor.oxt : bundle la macro + le moteur Python (P2) + la data
# universelle. Cf. libreoffice-ext/README.md (section B). À VALIDER en conditions
# réelles (le câblage Script Provider d'un .oxt n'a pas été testé).
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
STAGE="$HERE/build"

rm -rf "$STAGE"
mkdir -p "$STAGE/META-INF" "$STAGE/Scripts/python" "$STAGE/data"

cp "$HERE/oxt/description.xml" "$STAGE/"
cp "$HERE/oxt/Addons.xcu" "$STAGE/"
cp "$HERE/oxt/META-INF/manifest.xml" "$STAGE/META-INF/"
cp "$HERE/mathcursor.py" "$STAGE/Scripts/python/"
cp -R "$ROOT/engine-python/mc_engine" "$STAGE/Scripts/python/mc_engine"
cp -R "$ROOT/data/engine" "$STAGE/data/engine"

( cd "$STAGE" && zip -r -X -q "$HERE/MathCursor.oxt" . )
rm -rf "$STAGE"
echo "OK -> $HERE/MathCursor.oxt"
