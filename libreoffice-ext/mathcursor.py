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


def _insert_formula(text_range, starmath):
    """Remplace text_range (la sténo sélectionnée) par une formule Math StarMath,
    à sa TAILLE NATURELLE (pas étirée)."""
    doc = XSCRIPTCONTEXT.getDocument()  # noqa: F821 (injecté par LibreOffice)
    text = text_range.getText()
    # 1) supprimer la sténo -> point d'insertion. Avec bAbsorb=True, l'objet
    # héritait de la LARGEUR de la sélection (formule étirée). On insère donc
    # à un point (bAbsorb=False) pour que la formule prenne sa taille propre.
    text_range.setString("")
    obj = doc.createInstance("com.sun.star.text.TextEmbeddedObject")
    obj.CLSID = _STARMATH_CLSID
    # ancrage « comme caractère » : la formule vit dans le flux de texte.
    # AS_CHARACTER est une ENUM (pas une constante) -> uno.Enum, pas getConstantByName.
    obj.AnchorType = uno.Enum("com.sun.star.text.TextContentAnchorType", "AS_CHARACTER")
    text.insertTextContent(text_range, obj, False)
    # modèle Math embarqué → markup StarMath.
    model = obj.Component
    model.Formula = starmath
    # 2) filet de sécurité : resynchroniser la taille de l'objet sur la taille
    # NATURELLE de la formule (1 = com.sun.star.embed.Aspects.MSOLE_CONTENT).
    try:
        size = model.getVisualAreaSize(1)
        obj.Width = size.Width
        obj.Height = size.Height
    except Exception:
        pass


def _choose(labels):
    """Popup de choix (UNO) : liste `labels`, renvoie l'index choisi ou None.
    Aperçu = texte (LaTeX) — UNO ne rend pas de math dans une liste."""
    ctx = XSCRIPTCONTEXT.getComponentContext()  # noqa: F821
    smgr = ctx.ServiceManager
    n = min(len(labels), 8)
    dm = smgr.createInstanceWithContext("com.sun.star.awt.UnoControlDialogModel", ctx)
    dm.Title = "MathCursor — choisir la lecture"
    dm.Width = 220
    dm.Height = 22 + 11 * n + 22

    def _ctrl(service, name, **props):
        m = dm.createInstance("com.sun.star.awt." + service)
        for k, v in props.items():
            setattr(m, k, v)
        dm.insertByName(name, m)
        return m

    _ctrl("UnoControlFixedTextModel", "lbl", PositionX=6, PositionY=4, Width=208, Height=10,
          Label="Plusieurs lectures — choisis :")
    _ctrl("UnoControlListBoxModel", "lst", PositionX=6, PositionY=16, Width=208, Height=11 * n,
          Dropdown=False, MultiSelection=False, StringItemList=tuple(labels))
    by = dm.Height - 18
    _ctrl("UnoControlButtonModel", "ok", PositionX=112, PositionY=by, Width=48, Height=14,
          Label="OK", PushButtonType=1, DefaultButton=True)
    _ctrl("UnoControlButtonModel", "cancel", PositionX=164, PositionY=by, Width=48, Height=14,
          Label="Annuler", PushButtonType=2)

    dlg = smgr.createInstanceWithContext("com.sun.star.awt.UnoControlDialog", ctx)
    dlg.setModel(dm)
    toolkit = smgr.createInstanceWithContext("com.sun.star.awt.Toolkit", ctx)
    # parent = fenêtre du document : sans parent (None) le dialogue ne s'affichait pas.
    win = XSCRIPTCONTEXT.getDocument().getCurrentController().getFrame().getContainerWindow()  # noqa: F821
    dlg.createPeer(toolkit, win)
    lb = dlg.getControl("lst")
    lb.selectItemPos(0, True)
    ret = dlg.execute()
    idx = None
    if ret == 1:  # OK
        pos = lb.getSelectedItemsPos()
        idx = pos[0] if pos else 0
    dlg.dispose()
    return idx


def convert_selection(*args):
    """Convertit la sélection en formule. Si plusieurs lectures : popup de choix."""
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
    chosen = res.ranked[0]
    if res.decision == "popup" and len(res.ranked) > 1:
        idx = _choose([c.latex for c in res.ranked])
        if idx is None:
            return  # annulé
        chosen = res.ranked[idx]
    _insert_formula(rng, to_starmath(chosen.node, _CULTURE))


# Fonctions exposées au Script Provider de LibreOffice.
g_exportedScripts = (convert_selection,)
