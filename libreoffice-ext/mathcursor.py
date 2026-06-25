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


# ── NER (auto-détection à la frappe, optionnel) ──────────────────────────────
# Imports natifs (onnxruntime/numpy) PARESSEUX via mc_ner.load_detector : si
# vendor ou modèle absents -> _DETECTOR = None, l'extension charge quand même et
# Ctrl+Espace marche (dégradation gracieuse, parité AutoDetectController Word).
_DEV_EXT = r"D:\Software\MathCursor\libreoffice-ext"
# Ligne LaTeX (petit, à droite) sous la formule rendue dans la popup. Mettre à
# False pour ne montrer que la formule. (LibreOffice n'a pas d'UI de réglages :
# point de bascule unique ici. VSCode = réglage mathcursor.showLatexInPopup.)
_SHOW_LATEX = True
# Alias stable partagé VSTO/vscode/libreoffice : on remplace le CONTENU de
# models/latest/ au retrain (le nom de dossier ne change plus). Cf. commit
# 8deab5f. Fallback sur les anciens dossiers versionnés v7/v6.
_DEV_MODEL = r"D:\Software\MathCursor\models\latest"


def _platform_tag():
    import platform
    m = platform.machine().lower()
    if sys.platform.startswith("win"):
        return "win_amd64"
    if sys.platform == "darwin":
        return "mac_arm64" if m in ("arm64", "aarch64") else "mac_x86_64"
    return "linux_x86_64"


def _first_dir(*cands):
    for c in cands:
        if c and os.path.isdir(c):
            return c
    return None


_DETECTOR = None
try:
    _tag = _platform_tag()
    # 1) dossier contenant mc_ner (bundlé à la racine de l'ext, ou repo en dev)
    _ext = None
    for _c in (_root, os.environ.get("MATHCURSOR_EXT"), _DEV_EXT):
        if _c and os.path.isdir(os.path.join(_c, "mc_ner")):
            _ext = _c
            break
    if _ext and _ext not in sys.path:
        sys.path.insert(0, _ext)
    # 2) deps natives (onnxruntime/numpy) : bundlé vendor/<tag> -> env -> dev
    _vendor = _first_dir(
        os.path.join(_root, "vendor", _tag) if _root else None,
        os.environ.get("MATHCURSOR_VENDOR"),
        os.path.join(_DEV_EXT, "_spike_vendor"),
    )
    if _vendor and _vendor not in sys.path:
        sys.path.insert(0, _vendor)
    # 3) modèle ONNX : alias 'latest' (bundlé -> env -> dev) puis fallback versionné
    _model = _first_dir(
        os.path.join(_root, "models", "latest") if _root else None,
        os.environ.get("MATHCURSOR_MODEL"),
        _DEV_MODEL,
        os.path.join(_root, "models", "distilmult-v7") if _root else None,
        r"D:\Software\MathCursor\adapter-vsto\installer\payload\models\distilmult-v7",
    )
    if _model:
        import mc_ner  # noqa: E402
        _DETECTOR = mc_ner.load_detector(_model)
except Exception:
    _DETECTOR = None


# ── coquille popup webview (process séparé, KaTeX) ───────────────────────────
# popup_client.py est bundlé à la racine de l'ext (donc importable via _root sur
# sys.path) ; en dev il vit dans _DEV_EXT.
try:
    import popup_client  # noqa: E402
except ImportError:
    if _DEV_EXT and _DEV_EXT not in sys.path:
        sys.path.insert(0, _DEV_EXT)
    try:
        import popup_client  # noqa: E402
    except ImportError:
        popup_client = None


def _shell_exe_html():
    """(exe, html) de la coquille pour cet OS : installé (_root) sinon dev
    (_DEV_EXT). (None, None) si introuvable."""
    tag = _platform_tag()
    exe_name = "mc_popup_shell.exe" if sys.platform.startswith("win") else "mc_popup_shell"
    for b in (_root, _DEV_EXT):
        if not b:
            continue
        exe = os.path.join(b, "shell", tag, exe_name)
        html = os.path.join(b, "assets", "popup", "index.html")
        if os.path.isfile(exe) and os.path.isfile(html):
            return exe, html
    return None, None


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


def _msg(text):
    """Boîte d'information (diagnostic)."""
    doc = XSCRIPTCONTEXT.getDocument()  # noqa: F821
    win = doc.getCurrentController().getFrame().getContainerWindow()
    ctx = XSCRIPTCONTEXT.getComponentContext()  # noqa: F821
    tk = ctx.ServiceManager.createInstanceWithContext("com.sun.star.awt.Toolkit", ctx)
    box = tk.createMessageBox(win, uno.Enum("com.sun.star.awt.MessageBoxType", "INFOBOX"),
                              1, "MathCursor (debug)", text)
    box.execute()


# NOTE : l'ancien rendu d'aperçu (_render_doc + _previews, doc Writer caché +
# loadComponentFromURL) a été SUPPRIMÉ — il pompait la boucle d'événements UNO et
# provoquait des gels (AppHang). L'aperçu est désormais rendu en KaTeX par la
# coquille webview externe (popup_client). StarMath ne sert plus qu'à l'insertion
# finale (_insert_formula). Cf. ADR 2026-06-24-Feat-libreoffice-popup-webview-shell-rust.


_pos_err_shown = [False]


def _find_focused_text(acc, depth=0):
    """Descend l'arbre d'accessibilité jusqu'au composant texte FOCALISÉ
    (= le paragraphe où est le caret). Renvoie le contexte ou None."""
    if acc is None or depth > 14:
        return None
    try:
        caret = acc.getCaretPosition()  # présent => XAccessibleText
        has_text = True
    except Exception:
        has_text = False
        caret = -1
    try:
        st = acc.getAccessibleStateSet()
        focused = st is not None and st.contains(
            uno.getConstantByName("com.sun.star.accessibility.AccessibleStateType.FOCUSED"))
    except Exception:
        focused = False
    if has_text and focused:
        return acc
    try:
        n = acc.getAccessibleChildCount()
    except Exception:
        n = 0
    for i in range(min(n, 200)):
        try:
            child = acc.getAccessibleChild(i).getAccessibleContext()
        except Exception:
            continue
        r = _find_focused_text(child, depth + 1)
        if r is not None:
            return r
    return None


def _acc_of(win):
    """Contexte d'accessibilité d'une fenêtre (getAccessibleContext direct, sinon
    queryInterface XAccessible). None si la fenêtre n'est pas accessible."""
    if win is None:
        return None
    try:
        return win.getAccessibleContext()
    except Exception:
        pass
    try:
        xa = win.queryInterface(uno.getTypeByName("com.sun.star.accessibility.XAccessible"))
        if xa is not None:
            return xa.getAccessibleContext()
    except Exception:
        pass
    return None


_posdbg = []  # trace de diagnostic du dernier calcul de position


def _caret_pos_accessibility(win):
    """(x, y) du caret RELATIVES à `win`, EXACT, via accessibilité. None si
    indisponible (acc désactivée -> getCharacterBounds/LocationOnScreen absents)."""
    ctx = XSCRIPTCONTEXT.getComponentContext()  # noqa: F821
    ctrl = XSCRIPTCONTEXT.getDocument().getCurrentController()  # noqa: F821
    frame = ctrl.getFrame()
    cand = []
    for getter in (frame.getComponentWindow, frame.getContainerWindow):
        try:
            cand.append(getter())
        except Exception:
            pass
    try:
        tk = ctx.ServiceManager.createInstanceWithContext("com.sun.star.awt.Toolkit", ctx)
        cand.append(tk.getActiveTopWindow())
    except Exception:
        pass
    ft = None
    for w in cand:
        a = _acc_of(w)
        if a is not None:
            ft = _find_focused_text(a)
            if ft is not None:
                break
    wacc = _acc_of(win)
    if ft is None or wacc is None:
        return None
    pos = ft.getCaretPosition()
    cnt = ft.getCharacterCount()
    org = ft.getLocationOnScreen()
    worg = wacc.getLocationOnScreen()
    if cnt <= 0:
        cx, cy = org.X, org.Y + 16
    elif pos >= cnt:
        r = ft.getCharacterBounds(cnt - 1)
        cx, cy = org.X + r.X + r.Width, org.Y + r.Y + r.Height
    else:
        r = ft.getCharacterBounds(max(0, pos))
        cx, cy = org.X + r.X, org.Y + r.Y + r.Height
    return cx - worg.X, cy - worg.Y + 2


def _caret_pos_geometric():
    """(x, y) du caret RELATIVES à la fenêtre conteneur, APPROX (géométrie :
    zoom + centrage page). Repli quand l'accessibilité est désactivée."""
    ctrl = XSCRIPTCONTEXT.getDocument().getCurrentController()  # noqa: F821
    comp = ctrl.getFrame().getComponentWindow()
    off = comp.getPosSize()
    vc = ctrl.getViewCursor()
    pos = vc.getPosition()
    zoom = 100
    try:
        zoom = ctrl.getViewSettings().ZoomValue
    except Exception:
        pass
    # DPI RÉEL (device) pour rester cohérent avec _caret_pos_exact et avec le
    # positionnement PHYSIQUE de la coquille : sur écran HiDPI (ex. 125%) un 96 dpi
    # codé en dur décalerait la popup. convertPointToPixel mesure le vrai DPI.
    dpi = 96.0
    try:
        from com.sun.star.awt import Point as _P
        MM100 = uno.getConstantByName("com.sun.star.util.MeasureUnit.MM_100TH")
        p0 = _P(); p0.X = 0; p0.Y = 0
        p1 = _P(); p1.X = 2540; p1.Y = 0  # 2540/100 mm = 1 pouce
        r0 = comp.convertPointToPixel(p0, MM100)
        r1 = comp.convertPointToPixel(p1, MM100)
        d = abs(r1.X - r0.X)
        if d > 0:
            dpi = float(d)
    except Exception:
        pass
    f = (dpi / 2540.0) * (zoom / 100.0)
    page_w = 21000
    try:
        styles = XSCRIPTCONTEXT.getDocument().getStyleFamilies().getByName("PageStyles")  # noqa: F821
        sname = getattr(vc, "PageStyleName", None) or "Standard"
        page_w = styles.getByName(sname).Width
    except Exception:
        pass
    page_left = max(0, int((off.Width - page_w * f) / 2.0))
    try:
        line_h = int(float(vc.CharHeight) * 35.28)
    except Exception:
        line_h = 500
    x = off.X + page_left + int(pos.X * f)
    y = off.Y + int((pos.Y + line_h) * f)
    return x, y


def _caret_pos_exact(target=None):
    """(x, y) RELATIVES à la fenêtre conteneur, EXACT, sans accessibilité. Par
    défaut = position du caret ; si `target` (un XTextRange) est fourni, position
    de SON début (ex. le 1ᵉʳ caractère de la formule détectée, à gauche du caret).

    Principe : la relation doc->pixel est affine, pixel = origine + scale(doc).
    `convertPointToPixel` donne le scale exact (DPI réel). On trouve l'`origine`
    en sondant un pixel connu via `createTextRangeByPixelPosition` (pixel->texte)
    puis en lisant la position doc de ce point (on déplace le view-cursor le temps
    de la lecture, puis on le restaure — invisible car synchrone). Renvoie None
    si rien de sondable."""
    from com.sun.star.awt import Point
    ctrl = XSCRIPTCONTEXT.getDocument().getCurrentController()  # noqa: F821
    comp = ctrl.getFrame().getComponentWindow()
    off = comp.getPosSize()
    vc = ctrl.getViewCursor()
    MM100 = uno.getConstantByName("com.sun.star.util.MeasureUnit.MM_100TH")

    def cpp(xmm, ymm):
        p = Point()
        p.X, p.Y = int(xmm), int(ymm)
        r = comp.convertPointToPixel(p, MM100)
        return r.X, r.Y

    caret = vc.getStart()   # XTextRange du caret (collapsed)
    text = vc.getText()
    try:
        line_mm = float(vc.CharHeight) * 35.28 * 1.25
    except Exception:
        line_mm = 600.0
    ew, eh = off.Width, off.Height

    def cmp_pos(px, py, rng):
        """compareRegionStarts(pixel->début de range, rng) : 1 si AVANT rng, 0 si
        égal, -1 si APRÈS. None si non sondable. Ne déplace PAS le view-cursor."""
        pp = Point()
        pp.X, pp.Y = int(px), int(py)
        try:
            r = ctrl.createTextRangeByPixelPosition(pp)
            if r is None:
                return None
            return text.compareRegionStarts(r.getStart(), rng)
        except Exception:
            return None

    # Caret en fin de frappe : la région des pixels mappant au caret s'étend à
    # DROITE (même ligne) et EN DESSOUS. Son coin haut-gauche = le caret. On exige
    # que le coin bas-droit de la zone d'édition mappe au caret (sinon du texte
    # existe APRÈS le caret -> méthode inapplicable, repli géométrie). Robuste même
    # quand le doc est quasi vide (≠ la calibration par sondes, foireuse au 1er popup).
    if cmp_pos(ew - 4, eh - 4, caret) != 0:
        _posdbg.append("exact: coin bas-droit ne mappe pas au caret -> repli geometrie")
        return None
    # caret_y : plus petit Y (X très à droite) mappant encore au caret = haut de ligne.
    lo, hi = 0, eh - 4
    for _ in range(20):
        if hi - lo <= 1:
            break
        mid = (lo + hi) // 2
        lo, hi = (lo, mid) if cmp_pos(ew - 4, mid, caret) == 0 else (mid, hi)
    caret_y = hi
    # X (bord gauche) d'un range sur la ligne du caret : plus petit X dont le pixel
    # mappe AU/APRÈS le range. Renvoie None si non sondable (NE PAS s'effondrer à 0).
    probe_y = min(eh - 2, caret_y + 3)

    def find_x(rng):
        lo, hi = 0, ew - 4
        ok = False
        for _ in range(20):
            if hi - lo <= 1:
                break
            mid = (lo + hi) // 2
            c = cmp_pos(mid, probe_y, rng)
            if c is None:
                return None  # sondage impossible -> abandon (pas de collapse à gauche)
            ok = True
            # c == 1 : pixel AVANT le range -> aller à DROITE ; sinon à gauche.
            lo, hi = (mid, hi) if c == 1 else (lo, mid)
        return hi if ok else None

    caret_x = find_x(caret)
    if caret_x is None:
        _posdbg.append("exact: caret_x introuvable -> repli geometrie")
        return None
    # cible = début de la formule détectée (à gauche du caret, même ligne) si fourni ;
    # si son sondage échoue, on RETOMBE sur le caret (jamais collé au bord gauche).
    x = caret_x
    if target is not None:
        tx = find_x(target)
        if tx is not None:
            x = tx
        else:
            _posdbg.append("exact: target_x KO -> repli sur caret_x")
    _posdbg.append("exact: x=%d caret_x=%d caret_y=%d target=%s"
                   % (x, caret_x, caret_y, target is not None))
    _, lh = cpp(0, line_mm)
    return off.X + x, off.Y + caret_y + 2 * lh  # ~une ligne sous la ligne


def _caret_screen_xy(win, target=None):
    """(x, y) RELATIVES à `win`. Calibration exacte (createTextRangeByPixelPosition
    + convertPointToPixel) si possible, sinon géométrie (APPROX). `target` = début
    de zone à pointer (sinon le caret). Trace dans _posdbg."""
    del _posdbg[:]
    try:
        xy = _caret_pos_exact(target)
        if xy is not None:
            _posdbg.append("methode=exacte (calibration pixel) -> %r" % (xy,))
            return xy
        _posdbg.append("calibration impossible -> repli geometrie")
    except Exception as e:
        _posdbg.append("calibration KO %r -> repli geometrie" % e)
    xy = _caret_pos_geometric()
    _posdbg.append("methode=geometrie (approx) -> %r" % (xy,))
    return xy


def _win_screen_origin(win):
    """(X, Y) ÉCRAN absolus du coin haut-gauche de `win`. Accessibilité si dispo
    (getLocationOnScreen), sinon getPosSize (top-level = coords écran)."""
    a = _acc_of(win)
    if a is not None:
        try:
            p = a.getLocationOnScreen()
            return p.X, p.Y
        except Exception:
            pass
    try:
        ps = win.getPosSize()
        return ps.X, ps.Y
    except Exception:
        return 0, 0


def _caret_screen_abs(win, target=None):
    """(x, y) ÉCRAN absolus = origine écran de `win` + position RELATIVE
    (_caret_screen_xy). `target` = XTextRange dont on veut le début (ex. 1ᵉʳ
    caractère de la formule) ; sinon le caret. C'est ce qu'attend la coquille
    externe (fenêtre OS positionnée en coords écran)."""
    rx, ry = _caret_screen_xy(win, target)
    ox, oy = _win_screen_origin(win)
    return rx + ox, ry + oy


def convert_selection(*args):
    """Point d'entrée : enrobe _convert pour afficher toute erreur (sinon échec
    silencieux côté LibreOffice = « pas de popup » sans explication)."""
    try:
        _convert()
    except Exception as e:  # noqa: BLE001
        import traceback
        _msg("MathCursor erreur :\n%r\n\n%s" % (e, traceback.format_exc()))


def _convert():
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
    if res.decision == "popup" and len(res.ranked) > 1:
        # plusieurs lectures : popup NON-MODALE (coquille webview), pilotée au
        # clavier comme l'auto-détection. On s'assure que le key handler est
        # attaché (Ctrl+Espace peut être utilisé sans auto-détection active).
        starmaths = [to_starmath(c.node, _CULTURE) for c in res.ranked]
        labels = [c.latex for c in res.ranked]
        _ensure_key_handler()
        _open_autopopup(starmaths, labels, rng, ("convert", id(rng)))
        return  # l'insertion se fait via ↓/↑/Entrée (ou clic)
    chosen_sm = to_starmath(res.ranked[0].node, _CULTURE)
    _insert_formula(rng, chosen_sm)


# ════════════════════════════════════════════════════════════════════════════
#  Auto-détection NER : popup FLOTTANTE non-modale, pilotée au clavier (↓/↑/⏎).
#  Port single-thread du AutoDetectController Word. keyReleased tourne sur le
#  thread UI (sûr pour créer un dialogue) ; l'inférence est postée via
#  AsyncCallback (découple la latence de frappe + coalesce les rafales).
# ════════════════════════════════════════════════════════════════════════════
import threading  # noqa: E402
import unohelper  # noqa: E402
from com.sun.star.awt import XKeyHandler, XCallback, XTopWindowListener  # noqa: E402

# Délai d'inactivité avant détection (debounce) : la détection ne tourne qu'à la
# PAUSE de frappe, jamais à chaque touche (sinon spam/flicker).
_DEBOUNCE_S = 0.25

# état partagé (un seul doc/handler à la fois en v1).
#   popup  : True quand la popup webview est affichée, None sinon (plus un dialogue UNO).
#   client : PopupClient persistant (coquille webview), lazy, vit jusqu'à autodetect_stop.
_autodet = {
    "handler": None, "controller": None,
    "popup": None, "sig": None, "win": None, "pos": None,
    "sm": [], "idx": 0, "n": 0, "range": None,
    "suppress_sig": None, "busy": False, "client": None,
    "timer": None, "asynccb": None, "toolkit": None, "topwin": None,
}

try:
    _K_DOWN = uno.getConstantByName("com.sun.star.awt.Key.DOWN")
    _K_UP = uno.getConstantByName("com.sun.star.awt.Key.UP")
    _K_RETURN = uno.getConstantByName("com.sun.star.awt.Key.RETURN")
    _K_ESCAPE = uno.getConstantByName("com.sun.star.awt.Key.ESCAPE")
except Exception:
    _K_DOWN, _K_UP, _K_RETURN, _K_ESCAPE = 1024, 1025, 1280, 1281


class _ShellCommitCallback(unohelper.Base, XCallback):
    """Clic souris sur un candidat (reçu sur le thread lecteur de la coquille) ->
    re-posté ici sur le THREAD PRINCIPAL pour valider/insérer en sécurité UNO."""

    def notify(self, idx):
        try:
            _autodet["idx"] = int(idx)
            _autopopup_commit()
        except Exception:
            pass


class _ShellDismissCallback(unohelper.Base, XCallback):
    def notify(self, data):
        try:
            sig = _autodet.get("sig")
            _close_autopopup()
            _autodet["suppress_sig"] = sig  # ne pas rouvrir cette zone jusqu'à modif
        except Exception:
            pass


_shellcommitcb = _ShellCommitCallback()
_shelldismisscb = _ShellDismissCallback()


def _on_shell_commit(idx):
    acb = _autodet.get("asynccb")
    if acb is not None:
        try:
            acb.addCallback(_shellcommitcb, idx)
        except Exception:
            pass


def _on_shell_dismiss():
    acb = _autodet.get("asynccb")
    if acb is not None:
        try:
            acb.addCallback(_shelldismisscb, None)
        except Exception:
            pass


def _popup_client():
    """PopupClient persistant (lazy). None si popup_client ou la coquille manque."""
    cli = _autodet.get("client")
    if cli is not None:
        return cli
    if popup_client is None:
        _posdbg.append("popup_client non importé")
        return None
    exe, html = _shell_exe_html()
    if exe is None:
        _posdbg.append("coquille popup introuvable (shell/<tag>/ + assets/popup)")
        return None
    cli = popup_client.PopupClient(
        exe, html, on_commit=_on_shell_commit, on_dismiss=_on_shell_dismiss,
        on_error=lambda m: _posdbg.append("shell: " + str(m)))
    _autodet["client"] = cli
    return cli


def _para_context():
    """(texte_¶, offset_caret, range_début_¶) ou None si non sûr.

    Sécurité : on vérifie que `goRight(caret)` depuis le début du ¶ retombe
    EXACTEMENT sur le caret. Si un objet OLE en amont décale positions vs
    offsets-chaîne, on renvoie None (on n'insère jamais au mauvais endroit ;
    Ctrl+Espace reste dispo). Sélection active -> None."""
    doc = XSCRIPTCONTEXT.getDocument()  # noqa: F821
    vc = doc.getCurrentController().getViewCursor()
    if not vc.isCollapsed():
        return None
    text = vc.getText()
    p = text.createTextCursorByRange(vc.getStart())
    p.gotoStartOfParagraph(False)
    start = text.createTextCursorByRange(p.getStart())  # collapsed au début du ¶
    head = text.createTextCursorByRange(p.getStart())
    head.gotoRange(vc.getStart(), True)
    caret = len(head.getString())
    full = text.createTextCursorByRange(p.getStart())
    full.gotoEndOfParagraph(True)
    para_text = full.getString()
    probe = text.createTextCursorByRange(start.getStart())
    probe.goRight(caret, False)
    try:
        if text.compareRegionStarts(probe.getStart(), vc.getStart()) != 0:
            return None
    except Exception:
        return None
    return para_text, caret, start


def _zone_range(para_start, zstart, zend):
    """Range texte couvrant [zstart, zend) du ¶, à partir de son début."""
    text = para_start.getText()
    cur = text.createTextCursorByRange(para_start.getStart())
    cur.goRight(zstart, False)
    cur.goRight(zend - zstart, True)
    return cur


def _detect_candidate():
    """¶ courant -> NER -> zone au caret -> candidats. Renvoie
    (sig, starmaths, labels, zone_range) ou None. `sig` = signature (span +
    labels) pour décider si la popup doit être rafraîchie."""
    info = _para_context()
    if info is None:
        return None
    para_text, caret, para_start = info
    if not para_text or caret <= 0:
        return None
    # signal de sortie : tab ou double-espace juste avant le caret = « pas maintenant ».
    if para_text[caret - 1] == "\t" or (caret >= 2 and para_text[caret - 1] == " " and para_text[caret - 2] == " "):
        return None

    from mc_ner import refiner
    left, window = refiner.compute_window(para_text, [], caret)
    if not window.strip():
        return None
    zones = _DETECTOR.detect(window)
    if not zones:
        return None
    for z in zones:  # coords fenêtre -> ¶
        z.start += left
        z.end += left
        z.text = para_text[z.start:z.end]
    zone, _d = refiner.pick_nearest(zones, caret)
    if zone is None:
        return None
    zone = refiner.try_extend_forward_whitespace(para_text, zone, caret)
    if caret < zone.start or caret > zone.end:
        return None
    merged = refiner.merge_whitespace_adjacent(zones, para_text, zone)
    attempts = [merged, zone] if (merged.start != zone.start or merged.end != zone.end) else [zone]
    for att in attempts:
        z2 = refiner.extend_backward_keyword(para_text, att)
        s, e = z2.start, z2.end
        while s < e and para_text[s].isspace():
            s += 1
        while e > s and para_text[e - 1].isspace():
            e -= 1
        if e <= s:
            continue
        res = analyze(para_text[s:e], _CULTURE)
        if res.decision == "erreur" or not res.ranked:
            continue
        sms = [to_starmath(c.node, _CULTURE) for c in res.ranked]
        labels = [c.latex for c in res.ranked]
        sig = (s, e, tuple(labels))
        return sig, sms, labels, _zone_range(para_start, s, e)
    return None


def _autodetect_tick():
    """Garde de RÉ-ENTRANCE : certaines opérations (ouverture de doc de rendu,
    déplacement du view-cursor) pompent la boucle d'événements ; sans cette garde,
    un AsyncCallback en file se ré-exécute pendant qu'on est déjà dans le tick →
    travail imbriqué → gel (AppHang)."""
    if _DETECTOR is None or _autodet.get("busy"):
        return
    _autodet["busy"] = True
    try:
        _autodetect_tick_inner()
    finally:
        _autodet["busy"] = False


def _autodetect_tick_inner():
    cand = _detect_candidate()
    if cand is None:
        _autodet["suppress_sig"] = None  # le texte a changé -> on lèvera la suppression
        if _autodet["popup"] is not None:
            _close_autopopup()
        return
    sig, sms, labels, zrange = cand
    if sig == _autodet.get("suppress_sig"):
        return  # fermée par Échap sur CETTE zone -> ne pas rouvrir tant qu'elle ne change pas
    _autodet["suppress_sig"] = None
    if _autodet["popup"] is not None:
        if _autodet.get("sig") == sig:
            return  # inchangé -> garde la popup (et l'index ↓ courant)
        if _refresh_autopopup(sms, labels, zrange, sig):
            return  # contenu mis à jour EN PLACE (pas de fermer/rouvrir)
    _open_autopopup(sms, labels, zrange, sig)


def _candidates(labels):
    """Candidats au format attendu par la coquille (rendu KaTeX du LaTeX)."""
    return [{"latex": s} for s in labels]


def _line_height_px():
    """Hauteur d'une ligne en pixels écran (DPI réel) au caret, pour décaler la
    popup d'UNE ligne sous la frappe (sinon elle se place au-dessus de la ligne)."""
    try:
        from com.sun.star.awt import Point
        ctrl = XSCRIPTCONTEXT.getDocument().getCurrentController()  # noqa: F821
        comp = ctrl.getFrame().getComponentWindow()
        vc = ctrl.getViewCursor()
        MM100 = uno.getConstantByName("com.sun.star.util.MeasureUnit.MM_100TH")
        try:
            line_mm = float(vc.CharHeight) * 35.28 * 1.3  # pt -> 1/100mm + interligne
        except Exception:
            line_mm = 600.0
        p1 = Point(); p1.X = 0; p1.Y = int(line_mm)
        p0 = Point(); p0.X = 0; p0.Y = 0
        return abs(comp.convertPointToPixel(p1, MM100).Y - comp.convertPointToPixel(p0, MM100).Y)
    except Exception:
        return 24


def _open_autopopup(starmaths, labels, zrange, sig):
    """Affiche la popup webview au caret (coquille externe, non-activante). L'état
    (starmaths/idx/range) vit dans _autodet ; la navigation se fait au clavier.
    La position est calculée ICI (1ʳᵉ ouverture) et FIGÉE dans _autodet["pos"]
    jusqu'à fermeture — les refresh ne la recalculent pas (la popup ne saute pas)."""
    cli = _popup_client()
    if cli is None:
        return  # pas de fallback (décision ADR) : silencieux, juste tracé dans _posdbg
    win = XSCRIPTCONTEXT.getDocument().getCurrentController().getFrame().getContainerWindow()  # noqa: F821
    # Position = AU CARET (la version qui marchait ; l'ancrage début-de-formule a
    # été abandonné). Figée ensuite jusqu'à fermeture.
    x, y = _caret_screen_abs(win)
    y += _line_height_px()  # une ligne plus bas : sous la ligne de frappe, pas au-dessus
    # idx=-1 : rien de surligné à l'ouverture (simple suggestion). ↓ entre dans la liste.
    if not cli.show(_candidates(labels), x, y, line_height=0, selected_index=-1,
                    show_latex=_SHOW_LATEX):
        _posdbg.append("coquille: show a échoué (ready timeout / process mort)")
        return
    _autodet.update(popup=True, pos=(x, y), sm=list(starmaths), idx=-1,
                    n=len(starmaths), range=zrange, sig=sig, win=win)


def _refresh_autopopup(sms, labels, zrange, sig):
    """Met à jour la popup EXISTANTE (nouveaux candidats) SANS bouger : on réutilise
    la position FIGÉE de l'ouverture (_autodet["pos"]), pas de recalcul. Renvoie
    True si géré."""
    cli = _autodet.get("client")
    pos = _autodet.get("pos")
    if cli is None or _autodet["popup"] is None or pos is None:
        return False
    x, y = pos
    if not cli.show(_candidates(labels), x, y, line_height=0, selected_index=-1,
                    show_latex=_SHOW_LATEX):
        return False
    _autodet.update(sm=list(sms), idx=-1, n=len(sms), range=zrange, sig=sig)
    return True


def _close_autopopup():
    """Masque la popup webview (le process coquille reste vivant pour la prochaine
    fois — il n'est tué qu'à autodetect_stop). Libère la position figée."""
    cli = _autodet.get("client")
    if cli is not None:
        try:
            cli.close()
        except Exception:
            pass
    _autodet.update(popup=None, pos=None, sm=[], idx=0, n=0, range=None, sig=None, win=None)


def _autopopup_move(delta):
    n = _autodet["n"]
    if n == 0:
        return
    cur = _autodet["idx"]
    # depuis « rien sélectionné » (-1) : ↓ entre sur le 1ᵉʳ, ↑ sur le dernier.
    idx = (0 if delta > 0 else n - 1) if cur < 0 else (cur + delta) % n
    _autodet["idx"] = idx
    cli = _autodet.get("client")
    if cli is not None:
        try:
            cli.update(idx)
        except Exception:
            pass


def _autopopup_commit():
    sm = _autodet["sm"]
    idx = _autodet["idx"]
    if idx < 0:
        idx = 0  # Entrée sans avoir navigué -> valide le 1ᵉʳ candidat par défaut
    zr = _autodet["range"]
    chosen = sm[idx] if 0 <= idx < len(sm) else None
    _close_autopopup()
    if zr is not None and chosen is not None:
        try:
            _insert_formula(zr, chosen)
        except Exception:
            pass


class _TickCallback(unohelper.Base, XCallback):
    """notify() est exécuté sur le THREAD PRINCIPAL (posté via AsyncCallback) :
    seul endroit sûr pour toucher l'UNO + créer la popup."""

    def notify(self, data):
        try:
            _autodetect_tick()
        except Exception:
            pass


_tickcb = _TickCallback()


def _debounce_fire():
    """Exécuté sur le thread du timer (FOND) APRÈS la pause de frappe. Ne fait
    QU'UNE chose cross-thread : poster le tick sur le thread principal."""
    acb = _autodet.get("asynccb")
    if acb is not None:
        try:
            acb.addCallback(_tickcb, None)
        except Exception:
            pass


def _arm_debounce():
    """(Re)lance le timer d'inactivité : annule le précédent, repart à zéro.
    Tant que l'utilisateur frappe, le timer est repoussé -> rien ne tourne."""
    t = _autodet.get("timer")
    if t is not None:
        try:
            t.cancel()
        except Exception:
            pass
    nt = threading.Timer(_DEBOUNCE_S, _debounce_fire)
    nt.daemon = True
    _autodet["timer"] = nt
    nt.start()


class _KeyHandler(unohelper.Base, XKeyHandler):
    def keyPressed(self, ev):
        if _autodet["popup"] is None:
            return False
        code = ev.KeyCode
        if code == _K_DOWN:
            _autopopup_move(1)
            return True
        if code == _K_UP:
            _autopopup_move(-1)
            return True
        if code == _K_RETURN:
            _autopopup_commit()
            return True
        if code == _K_ESCAPE:
            sig = _autodet.get("sig")
            _close_autopopup()
            _autodet["suppress_sig"] = sig  # ne pas rouvrir cette zone jusqu'à modif
            return True
        # toute autre touche : laisse passer (l'utilisateur continue de taper). La
        # popup PERSISTE et sera rafraîchie/fermée par le tick après la pause —
        # plus de fermeture/réouverture à chaque frappe (anti-flicker).
        return False

    def keyReleased(self, ev):
        # Toute frappe repousse le debounce : la détection ne tournera qu'après
        # la pause. (Si une popup est ouverte, le tick no-op — voir _autodetect_tick.)
        _arm_debounce()
        return False

    def disposing(self, ev):
        pass


class _DeactivateListener(unohelper.Base, XTopWindowListener):
    """Ferme la popup quand une fenêtre top de l'office est DÉSACTIVÉE (l'utilisateur
    bascule ailleurs via Alt+Tab) — sinon la popup topmost resterait devant l'autre
    application. La coquille étant non-activante, l'ouvrir ne désactive pas Writer,
    donc pas de fermeture parasite."""

    def windowDeactivated(self, ev):
        if _autodet["popup"] is not None:
            try:
                _close_autopopup()
            except Exception:
                pass

    def windowActivated(self, ev):
        pass

    def windowOpened(self, ev):
        pass

    def windowClosing(self, ev):
        pass

    def windowClosed(self, ev):
        pass

    def windowMinimized(self, ev):
        pass

    def windowNormalized(self, ev):
        pass

    def disposing(self, ev):
        pass


def _ensure_key_handler():
    """Attache le _KeyHandler sur le contrôleur courant (idempotent) et crée
    l'AsyncCallback (thread principal). Partagé par l'auto-détection et par
    Ctrl+Espace (popup non-modale pilotée au clavier même sans auto-détection)."""
    ctrl = XSCRIPTCONTEXT.getDocument().getCurrentController()  # noqa: F821
    if _autodet["handler"] is not None and _autodet["controller"] is ctrl:
        return  # déjà actif sur CE contrôleur
    if _autodet["handler"] is not None and _autodet["controller"] is not None:
        try:
            _autodet["controller"].removeKeyHandler(_autodet["handler"])
        except Exception:
            pass
    if _autodet.get("asynccb") is None:
        ctx = XSCRIPTCONTEXT.getComponentContext()  # noqa: F821
        # AsyncCallback créé sur le THREAD PRINCIPAL (réutilisé par le timer de
        # fond ET les callbacks souris de la coquille — seul appel cross-thread).
        _autodet["asynccb"] = ctx.ServiceManager.createInstanceWithContext(
            "com.sun.star.awt.AsyncCallback", ctx)
    h = _KeyHandler()
    ctrl.addKeyHandler(h)
    _autodet["handler"] = h
    _autodet["controller"] = ctrl
    # Écouteur de désactivation (Alt+Tab) -> ferme la popup. Enregistré une fois
    # sur le Toolkit (signale l'activation/désactivation des fenêtres top).
    if _autodet.get("topwin") is None:
        try:
            ctx = XSCRIPTCONTEXT.getComponentContext()  # noqa: F821
            tk = ctx.ServiceManager.createInstanceWithContext("com.sun.star.awt.Toolkit", ctx)
            lis = _DeactivateListener()
            tk.addTopWindowListener(lis)
            _autodet["toolkit"] = tk
            _autodet["topwin"] = lis
        except Exception:
            pass
    # PRÉ-CHAUFFAGE : booter la coquille (init WebView2 ~0,5 s) EN FOND dès
    # l'activation, pour que le 1ᵉʳ `show` ne bloque pas le thread UI. La
    # construction est synchrone (sans spawn) ; ensure() (spawn + attente ready)
    # tourne dans un thread daemon (PopupClient.ensure a son propre verrou).
    cli = _popup_client()
    if cli is not None:
        threading.Thread(target=cli.ensure, daemon=True).start()


def _start_autodetect(verbose):
    """Active l'auto-détection (key handler) sur le contrôleur courant. `verbose`
    = boîtes d'info (chemin menu) ; silencieux pour l'auto-start (Job)."""
    if _DETECTOR is None:
        if verbose:
            _msg("NER indisponible : modèle ou deps natives non chargés.")
        return
    already = (_autodet["handler"] is not None and
               _autodet["controller"] is XSCRIPTCONTEXT.getDocument().getCurrentController())  # noqa: F821
    _ensure_key_handler()
    if verbose:
        if already:
            _msg("Auto-détection déjà active.")
        else:
            _msg("Auto-détection MathCursor ACTIVÉE.\nTape des maths : la popup apparaît\n(↓/↑ choisir, Entrée valider, Échap fermer).")


def autodetect_start(*args):
    """Active l'auto-détection (chemin menu, verbeux)."""
    try:
        _start_autodetect(verbose=True)
    except Exception:
        import traceback
        _msg("autodetect_start erreur :\n" + traceback.format_exc())


def autodetect_autostart(*args):
    """Active l'auto-détection SANS boîte d'info (appelé par le Job à l'ouverture
    d'un document Writer)."""
    try:
        _start_autodetect(verbose=False)
    except Exception:
        pass


def autodetect_stop(*args):
    """Désactive l'auto-détection (retire le key handler)."""
    try:
        ctrl = _autodet["controller"]
        h = _autodet["handler"]
        if ctrl is not None and h is not None:
            ctrl.removeKeyHandler(h)
        t = _autodet.get("timer")
        if t is not None:
            try:
                t.cancel()
            except Exception:
                pass
        _close_autopopup()
        tk = _autodet.get("toolkit")
        lis = _autodet.get("topwin")
        if tk is not None and lis is not None:
            try:
                tk.removeTopWindowListener(lis)
            except Exception:
                pass
        cli = _autodet.get("client")
        if cli is not None:
            try:
                cli.quit()  # tue le process coquille (plus de doc de rendu à fermer)
            except Exception:
                pass
        _autodet["handler"] = None
        _autodet["controller"] = None
        _autodet["timer"] = None
        _autodet["client"] = None
        _autodet["toolkit"] = None
        _autodet["topwin"] = None
        _autodet["busy"] = False
        _msg("Auto-détection désactivée.")
    except Exception:
        pass


# Fonctions exposées au Script Provider de LibreOffice.
g_exportedScripts = (convert_selection, autodetect_start, autodetect_autostart,
                     autodetect_stop)
