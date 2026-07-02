#!/usr/bin/env python3
# MathCursor: capturing mathematical intent from linear keyboard input.
# Copyright (C) 2026  Côme de Percin
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
# GNU General Public License for more details.
#
# You should have received a copy of the GNU General Public License
# along with this program.  If not, see <https://www.gnu.org/licenses/>.

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
import shutil
import subprocess
import sys
import zipfile

HERE = os.path.dirname(os.path.realpath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
OUT = os.path.join(HERE, "MathCursor.oxt")

entries = [
    (os.path.join(HERE, "oxt", "description.xml"), "description.xml"),
    (os.path.join(HERE, "oxt", "META-INF", "manifest.xml"), "META-INF/manifest.xml"),
    (os.path.join(HERE, "oxt", "Addons.xcu"), "Addons.xcu"),
    (os.path.join(HERE, "oxt", "Jobs.xcu"), "Jobs.xcu"),
    (os.path.join(HERE, "oxt", "jobs.py"), "jobs.py"),
    (os.path.join(HERE, "mathcursor.py"), "Scripts/python/mathcursor.py"),
    # Clients stdio à la racine de l'ext (importés par mathcursor.py via _root).
    (os.path.join(HERE, "popup_client.py"), "popup_client.py"),
    (os.path.join(HERE, "rust_clients.py"), "rust_clients.py"),
]


def add_tree(src_dir, arc_prefix):
    for dp, _dns, fns in os.walk(src_dir):
        parts = dp.replace("\\", "/").split("/")
        if "__pycache__" in parts:
            continue
        # cache de profil WebView2 créé au runtime à côté de l'exe (mc_*.exe.WebView2/)
        if any(seg.endswith(".WebView2") for seg in parts):
            continue
        for fn in fns:
            if fn.endswith(".pyc"):
                continue
            ap = os.path.join(dp, fn)
            rel = os.path.relpath(ap, src_dir).replace(os.sep, "/")
            entries.append((ap, arc_prefix + "/" + rel))


# Cœur RUST (analyze=moteur, mc-ner=détection) : build + stage dans bin/<tag>/.
# Remplace le moteur Python (mc_engine) + data/engine (le Rust embarque la data)
# + le NER Python (mc_ner). Le modèle NER (~46 Mo) n'est PAS bundlé (dev = repo) ;
# le bundle release du modèle = étape packaging ultérieure.
def _stage_core_bins():
    win = sys.platform.startswith("win")
    ext = ".exe" if win else ""
    if win:
        tag = "win_amd64"
    elif sys.platform == "darwin":
        import platform
        tag = "mac_arm64" if platform.machine().lower() in ("arm64", "aarch64") else "mac_x86_64"
    else:
        tag = "linux_x86_64"
    rustdir = os.path.join(ROOT, "rust")
    for crate, binname in (("mc-engine", "analyze"), ("mc-ner", "mc-ner")):
        if subprocess.run(["cargo", "build", "--release", "-p", crate, "--bin", binname],
                          cwd=rustdir).returncode != 0:
            raise SystemExit("cargo build %s a échoué" % binname)
    dest_dir = os.path.join(HERE, "bin", tag)
    os.makedirs(dest_dir, exist_ok=True)
    for binname in ("analyze", "mc-ner"):
        shutil.copy2(os.path.join(rustdir, "target", "release", binname + ext),
                     os.path.join(dest_dir, binname + ext))
    add_tree(os.path.join(HERE, "bin"), "bin")
    print("   cœur Rust -> bin/%s/ (analyze%s, mc-ner%s)" % (tag, ext, ext))


_stage_core_bins()

# Popup webview : HTML + KaTeX PARTAGÉS (rust/mc-popup/web, source unique avec
# VSCode) → stagés dans assets/popup/ (dev + bundle), puis bundlés.
def _stage_popup_web():
    web = os.path.join(ROOT, "rust", "mc-popup", "web")
    dest = os.path.join(HERE, "assets", "popup")
    if os.path.isdir(dest):
        shutil.rmtree(dest)
    shutil.copytree(web, dest)
    print("   assets popup (HTML+KaTeX) <- rust/mc-popup/web")


_stage_popup_web()
add_tree(os.path.join(HERE, "assets"), "assets")


# Coquille popup = crate workspace rust/mc-popup (SOURCE UNIQUE, partagée avec
# VSCode ; l'ancien shell/src dupliqué a été retiré). On la build pour l'OS
# courant (mode PASSIF, sans --active = exactement le contrat LibreOffice) et on
# la stage dans shell/<tag>/ sous le nom attendu par popup_client/mathcursor.
def _stage_popup_shell():
    win = sys.platform.startswith("win")
    if win:
        tag, src_name = "win_amd64", "mc-popup.exe"
    elif sys.platform == "darwin":
        import platform
        arm = platform.machine().lower() in ("arm64", "aarch64")
        tag, src_name = ("mac_arm64" if arm else "mac_x86_64"), "mc-popup"
    else:
        tag, src_name = "linux_x86_64", "mc-popup"
    rustdir = os.path.join(ROOT, "rust")
    if subprocess.run(["cargo", "build", "--release", "-p", "mc-popup"], cwd=rustdir).returncode != 0:
        raise SystemExit("cargo build -p mc-popup a échoué")
    built = os.path.join(rustdir, "target", "release", src_name)
    dest_dir = os.path.join(HERE, "shell", tag)
    os.makedirs(dest_dir, exist_ok=True)
    dest = os.path.join(dest_dir, "mc_popup_shell.exe" if win else "mc_popup_shell")
    shutil.copy2(built, dest)
    print(f"   coquille mc-popup -> shell/{tag}/{os.path.basename(dest)}")


_stage_popup_shell()
_shell_dir = os.path.join(HERE, "shell")
if os.path.isdir(_shell_dir):
    for _tag in sorted(os.listdir(_shell_dir)):
        if _tag == "src":
            continue
        _td = os.path.join(_shell_dir, _tag)
        if os.path.isdir(_td):
            add_tree(_td, "shell/" + _tag)

if os.path.exists(OUT):
    os.remove(OUT)
with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED) as z:
    for src, arc in entries:
        z.write(src, arc)

print(f"OK -> {OUT}")
print(f"   {len(entries)} fichiers, {os.path.getsize(OUT)} octets")
