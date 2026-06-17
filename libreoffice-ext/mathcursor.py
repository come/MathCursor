# -*- coding: utf-8 -*-
"""MathCursor — extension/macro LibreOffice Writer (P4, MVP).

USAGE (test rapide, sans packaging .oxt) :
  1. Copier ce fichier dans le dossier utilisateur des scripts Python LibreOffice :
       - Linux   : ~/.config/libreoffice/4/user/Scripts/python/
       - Windows : %APPDATA%\\LibreOffice\\4\\user\\Scripts\\python\\
       - macOS   : ~/Library/Application Support/LibreOffice/4/user/Scripts/python/
  2. Pointer le moteur : variable d'env MATHCURSOR_ENGINE = chemin de engine-python/
     du repo (sinon adapter ENGINE_PATH ci-dessous).
  3. Dans Writer : sélectionner la sténo (ex. « 1/2 », « x^2+1/2 », « lim x 0 g(x) »),
     puis Outils ▸ Macros ▸ … ▸ convert_selection. Lier à Ctrl+Espace via
     Outils ▸ Personnaliser ▸ Clavier.

MVP : convertit la SÉLECTION en formule LibreOffice Math (objet StarMath). Pas
encore d'auto-détection ni de popup de candidats (v2). La justesse StarMath
(module starmath.py) est best-effort — remonter les écarts pour itérer.
"""
import os
import sys
import uno

_EXT_ID = "fr.mathcursor.libreoffice"


def _ext_root():
    """Racine de l'extension installée. ATTENTION : dans le contexte script de
    LibreOffice, la variable de chemin du module n'est pas définie -> on passe par
    le PackageInformationProvider (robuste même quand le cache uno_packages change)."""
    ctx = uno.getComponentContext()
    pip = ctx.getValueByName("/singletons/com.sun.star.deployment.PackageInformationProvider")
    return uno.fileUrlToSystemPath(pip.getPackageLocation(_EXT_ID))


# ── localisation du moteur Python (port P2) ──────────────────────────────────
# Installé en .oxt (moteur + data bundlés à la racine de l'extension) OU dev
# (variable MATHCURSOR_ENGINE pointant engine-python/).
_root = None
try:
    _root = _ext_root()
except Exception:
    _root = None

if _root and os.path.isdir(os.path.join(_root, "mc_engine")):
    if _root not in sys.path:
        sys.path.insert(0, _root)
    from mc_engine import data as _data       # noqa: E402
    _data.set_data_dir(os.path.join(_root, "data", "engine"))
else:
    _eng = os.environ.get("MATHCURSOR_ENGINE", r"D:\Software\MathCursor\engine-python")
    if _eng and _eng not in sys.path:
        sys.path.insert(0, _eng)

from mc_engine.engine import analyze        # noqa: E402
from mc_engine import culture                # noqa: E402
from mc_engine.starmath import to_starmath   # noqa: E402

_STARMATH_CLSID = "078B7ABA-54FC-457F-8551-6147E776A997"
_CULTURE = culture.FR


def _insert_formula(text, text_range, starmath):
    """Insère un objet formule Math (StarMath) en remplaçant text_range."""
    doc = XSCRIPTCONTEXT.getDocument()  # noqa: F821 (injecté par LibreOffice)
    obj = doc.createInstance("com.sun.star.text.TextEmbeddedObject")
    obj.CLSID = _STARMATH_CLSID
    # ancrage « comme caractère » : la formule vit dans le flux de texte.
    # AS_CHARACTER est une ENUM (pas une constante) -> uno.Enum, pas getConstantByName.
    obj.AnchorType = uno.Enum("com.sun.star.text.TextContentAnchorType", "AS_CHARACTER")
    # True = remplace la sélection par l'objet.
    text.insertTextContent(text_range, obj, True)
    # modèle Math embarqué → markup StarMath.
    model = obj.Component
    model.Formula = starmath


def convert_selection(*args):
    """Convertit la sélection courante en formule. À lier à un raccourci."""
    doc = XSCRIPTCONTEXT.getDocument()  # noqa: F821
    controller = doc.getCurrentController()
    sel = controller.getSelection()
    if sel is None or not hasattr(sel, "getCount") or sel.getCount() == 0:
        return
    rng = sel.getByIndex(0)
    src = rng.getString().strip()
    if not src:
        return
    res = analyze(src, _CULTURE)
    # rien reconnu (prose, erreur) : on ne touche PAS au document.
    if res.decision == "erreur" or not res.ranked:
        return
    starmath = to_starmath(res.ranked[0].node, _CULTURE)
    _insert_formula(rng.getText(), rng, starmath)


# Fonctions exposées au Script Provider de LibreOffice.
g_exportedScripts = (convert_selection,)
