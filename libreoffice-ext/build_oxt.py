#!/usr/bin/env python3
"""Construit MathCursor.oxt — l'extension/installeur LibreOffice AUTONOME.

Bundle : la macro + le moteur Python (P2) + la data universelle, à la racine de
l'extension (mathcursor.py auto-détecte ce layout). Python pur → marche sur
Win/Mac/Linux, sans zip/bash externe. Cf. README (section B).

Layout produit :
  description.xml
  META-INF/manifest.xml
  Addons.xcu
  Scripts/python/mathcursor.py
  mc_engine/**            (moteur)
  data/engine/**          (vocab universel)
"""
import os
import zipfile

HERE = os.path.dirname(os.path.realpath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
OUT = os.path.join(HERE, "MathCursor.oxt")

entries = [
    (os.path.join(HERE, "oxt", "description.xml"), "description.xml"),
    (os.path.join(HERE, "oxt", "META-INF", "manifest.xml"), "META-INF/manifest.xml"),
    (os.path.join(HERE, "oxt", "Addons.xcu"), "Addons.xcu"),
    (os.path.join(HERE, "mathcursor.py"), "Scripts/python/mathcursor.py"),
]


def add_tree(src_dir, arc_prefix):
    for dp, _dns, fns in os.walk(src_dir):
        if "__pycache__" in dp.replace("\\", "/").split("/"):
            continue
        for fn in fns:
            if fn.endswith(".pyc"):
                continue
            ap = os.path.join(dp, fn)
            rel = os.path.relpath(ap, src_dir).replace(os.sep, "/")
            entries.append((ap, arc_prefix + "/" + rel))


add_tree(os.path.join(ROOT, "engine-python", "mc_engine"), "mc_engine")
add_tree(os.path.join(ROOT, "data", "engine"), "data/engine")

if os.path.exists(OUT):
    os.remove(OUT)
with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED) as z:
    for src, arc in entries:
        z.write(src, arc)

print(f"OK -> {OUT}")
print(f"   {len(entries)} fichiers, {os.path.getsize(OUT)} octets")
