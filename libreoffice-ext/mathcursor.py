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
_DEV_MODEL = r"D:\Software\MathCursor\adapter-vsto\installer\payload\models\distilmult-v6"


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
    # 3) modèle ONNX : bundlé -> env -> dev
    _model = _first_dir(
        os.path.join(_root, "models", "distilmult-v6") if _root else None,
        os.environ.get("MATHCURSOR_MODEL"),
        _DEV_MODEL,
    )
    if _model:
        import mc_ner  # noqa: E402
        _DETECTOR = mc_ner.load_detector(_model)
except Exception:
    _DETECTOR = None


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


def _render_doc():
    """Document Writer CACHÉ pour rendre les formules, créé UNE fois et réutilisé.

    Auparavant, _previews appelait loadComponentFromURL à CHAQUE popup : cet appel
    pompe la boucle d'événements et provoquait des ré-entrances / gels (AppHang).
    On garde donc un seul doc caché vivant tout le temps."""
    d = _autodet.get("renderdoc")
    if d is not None:
        try:
            d.getText()  # encore vivant ?
            return d
        except Exception:
            _autodet["renderdoc"] = None
    ctx = XSCRIPTCONTEXT.getComponentContext()  # noqa: F821
    smgr = ctx.ServiceManager
    from com.sun.star.beans import PropertyValue
    hidden = PropertyValue()
    hidden.Name = "Hidden"
    hidden.Value = True
    desktop = smgr.createInstanceWithContext("com.sun.star.frame.Desktop", ctx)
    d = desktop.loadComponentFromURL("private:factory/swriter", "_blank", 0, (hidden,))
    _autodet["renderdoc"] = d
    return d


def _previews(starmaths):
    """Rend chaque StarMath en image (XGraphic) via le doc caché RÉUTILISÉ.
    Renvoie une liste d'XGraphic (None par élément si échec)."""
    out = [None] * len(starmaths)
    try:
        hdoc = _render_doc()
        text = hdoc.getText()
        text.setString("")  # vider le rendu précédent
        for i, sm in enumerate(starmaths):
            try:
                cur = text.createTextCursorByRange(text.getEnd())
                obj = hdoc.createInstance("com.sun.star.text.TextEmbeddedObject")
                obj.CLSID = _STARMATH_CLSID
                text.insertTextContent(cur, obj, False)
                obj.Component.Formula = sm
                out[i] = obj.ReplacementGraphic
            except Exception:
                out[i] = None
    except Exception:
        pass
    return out


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
    f = (96.0 / 2540.0) * (zoom / 100.0)
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


def _caret_pos_exact():
    """(x, y) du caret RELATIVES à la fenêtre conteneur, EXACT, sans accessibilité.

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

    def maps_to_caret(px, py):
        """True si le pixel (px,py) se recale EXACTEMENT sur le caret. Ne déplace
        PAS le view-cursor : compare juste les débuts de range."""
        pp = Point()
        pp.X, pp.Y = int(px), int(py)
        try:
            rng = ctrl.createTextRangeByPixelPosition(pp)
            if rng is None:
                return None
            return text.compareRegionStarts(rng.getStart(), caret) == 0
        except Exception:
            return None

    # Caret en fin de frappe : la région des pixels mappant au caret s'étend à
    # DROITE (même ligne) et EN DESSOUS. Son coin haut-gauche = le caret. On exige
    # que le coin bas-droit de la zone d'édition mappe au caret (sinon du texte
    # existe APRÈS le caret -> méthode inapplicable, repli géométrie). Robuste même
    # quand le doc est quasi vide (≠ la calibration par sondes, foireuse au 1er popup).
    if maps_to_caret(ew - 4, eh - 4) is not True:
        _posdbg.append("exact: coin bas-droit ne mappe pas au caret -> repli geometrie")
        return None
    # caret_y : plus petit Y (X très à droite) mappant encore au caret = haut de ligne.
    lo, hi = 0, eh - 4
    for _ in range(20):
        if hi - lo <= 1:
            break
        mid = (lo + hi) // 2
        lo, hi = (lo, mid) if maps_to_caret(ew - 4, mid) is True else (mid, hi)
    caret_y = hi
    # caret_x : à un Y dans la ligne du caret, plus petit X mappant au caret.
    probe_y = min(eh - 2, caret_y + 3)
    lo, hi = 0, ew - 4
    for _ in range(20):
        if hi - lo <= 1:
            break
        mid = (lo + hi) // 2
        lo, hi = (lo, mid) if maps_to_caret(mid, probe_y) is True else (mid, hi)
    caret_x = hi
    _posdbg.append("exact: caret_x=%d caret_y=%d ew=%d eh=%d" % (caret_x, caret_y, ew, eh))
    _, lh = cpp(0, line_mm)
    return off.X + caret_x, off.Y + caret_y + 2 * lh  # ~une ligne sous le caret


def _caret_screen_xy(win):
    """(x, y) du caret RELATIVES à `win`. Calibration exacte
    (createTextRangeByPixelPosition + convertPointToPixel) si possible, sinon
    géométrie (APPROX). Trace dans _posdbg."""
    del _posdbg[:]
    try:
        xy = _caret_pos_exact()
        if xy is not None:
            _posdbg.append("methode=exacte (calibration pixel) -> %r" % (xy,))
            return xy
        _posdbg.append("calibration impossible -> repli geometrie")
    except Exception as e:
        _posdbg.append("calibration KO %r -> repli geometrie" % e)
    xy = _caret_pos_geometric()
    _posdbg.append("methode=geometrie (approx) -> %r" % (xy,))
    return xy


def _apply_caret_pos(dlg, win, xy):
    """Positionne `dlg` aux coords `xy` RELATIVES à `win` (la fenêtre parente)."""
    if xy is None:
        return
    try:
        POS = uno.getConstantByName("com.sun.star.awt.PosSize.POS")
        x, y = xy
        try:
            ds = dlg.getPosSize()
            cont = win.getPosSize()
            if ds.Height:
                y += ds.Height // 2   # descendre la popup d'une demi-hauteur
            if ds.Width:
                x = max(0, min(x, max(0, cont.Width - ds.Width)))
            if ds.Height:
                y = max(0, min(y, max(0, cont.Height - ds.Height)))
        except Exception:
            pass
        dlg.setPosSize(x, y, 0, 0, POS)
    except Exception:
        if not _pos_err_shown[0]:
            _pos_err_shown[0] = True
            try:
                import traceback
                _msg("MathCursor — position (diag, 1×) :\n" + traceback.format_exc())
            except Exception:
                pass


def _place_at_caret(dlg, win):
    """Calcule la position du caret puis l'applique (chemin modal)."""
    _apply_caret_pos(dlg, win, _caret_screen_xy(win))


def _choose_rendered(starmaths, labels):
    """Fenêtre de choix avec FORMULES RENDUES (image par candidat + radio).
    Repli sur la liste texte (_choose) si aucune image n'a pu être rendue."""
    graphics = _previews(starmaths)
    if not any(g is not None for g in graphics):
        return _choose(labels)  # repli texte

    ctx = XSCRIPTCONTEXT.getComponentContext()  # noqa: F821
    smgr = ctx.ServiceManager
    n = len(starmaths)
    img_h = 26          # hauteur d'image fixe ; la largeur suit le RATIO réel
    row = img_h + 12
    max_w = 210
    dm = smgr.createInstanceWithContext("com.sun.star.awt.UnoControlDialogModel", ctx)
    dm.Title = "MathCursor — choisir la lecture"
    dm.Width = 250
    dm.Height = 8 + row * n + 26

    def ctrl(service, name, **props):
        m = dm.createInstance("com.sun.star.awt." + service)
        for k, v in props.items():
            setattr(m, k, v)
        dm.insertByName(name, m)
        return m

    def aspect(g):
        # ratio largeur/hauteur de la formule (préférer la taille vectorielle).
        for attr in ("Size100thMM", "SizePixel"):
            try:
                s = getattr(g, attr)
                if s.Width and s.Height:
                    return s.Width / float(s.Height)
            except Exception:
                pass
        return 3.0

    # 1) TOUS les radios d'abord, consécutivement = un seul groupe. Sinon (un
    #    contrôle image inséré entre deux radios) le groupe se brise : cliquer le
    #    2e ne décoche pas le 1er -> la lecture renvoie toujours l'index 0.
    y = 6
    for i in range(n):
        rb = ctrl("UnoControlRadioButtonModel", "r%d" % i,
                  PositionX=6, PositionY=y + row // 2 - 6, Width=12, Height=12, Label="")
        if i == 0:
            rb.State = 1
        y += row
    # 2) images / textes ensuite (la position visuelle est indépendante de l'ordre).
    y = 6
    for i in range(n):
        if graphics[i] is not None:
            w = max(12, min(max_w, int(round(img_h * aspect(graphics[i])))))
            img = ctrl("UnoControlImageControlModel", "img%d" % i,
                       PositionX=24, PositionY=y + (row - img_h) // 2,
                       Width=w, Height=img_h, ScaleImage=True, Border=0)
            try:
                img.Graphic = graphics[i]
            except Exception:
                pass
            try:
                img.ScaleMode = 1   # ISOTROPIC (préserve le ratio si supporté)
            except Exception:
                pass
        else:
            ctrl("UnoControlFixedTextModel", "txt%d" % i,
                 PositionX=24, PositionY=y + row // 2 - 5, Width=max_w, Height=12, Label=labels[i])
        y += row

    by = dm.Height - 18
    ctrl("UnoControlButtonModel", "ok", PositionX=142, PositionY=by, Width=48, Height=14,
         Label="OK", PushButtonType=1, DefaultButton=True)
    ctrl("UnoControlButtonModel", "cancel", PositionX=194, PositionY=by, Width=48, Height=14,
         Label="Annuler", PushButtonType=2)

    dlg = smgr.createInstanceWithContext("com.sun.star.awt.UnoControlDialog", ctx)
    dlg.setModel(dm)
    toolkit = smgr.createInstanceWithContext("com.sun.star.awt.Toolkit", ctx)
    win = XSCRIPTCONTEXT.getDocument().getCurrentController().getFrame().getContainerWindow()  # noqa: F821
    dlg.createPeer(toolkit, win)
    _place_at_caret(dlg, win)
    ret = dlg.execute()
    idx = None
    if ret == 1:
        idx = 0
        for i in range(n):
            if dm.getByName("r%d" % i).State == 1:
                idx = i
                break
    dlg.dispose()
    return idx


def _choose(labels):
    """Popup de choix (UNO) : liste `labels`, renvoie l'index choisi ou None.
    Aperçu = texte (LaTeX) — repli quand le rendu image échoue."""
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
    _place_at_caret(dlg, win)
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
        starmaths = [to_starmath(c.node, _CULTURE) for c in res.ranked]
        idx = _choose_rendered(starmaths, [c.latex for c in res.ranked])
        if idx is None:
            return  # annulé
        chosen_sm = starmaths[idx]
    else:
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
from com.sun.star.awt import XKeyHandler, XCallback  # noqa: E402

# Délai d'inactivité avant détection (debounce) : la détection ne tourne qu'à la
# PAUSE de frappe, jamais à chaque touche (sinon spam/flicker).
_DEBOUNCE_S = 0.25

# état partagé (un seul doc/handler à la fois en v1).
_autodet = {
    "handler": None, "controller": None,
    "popup": None, "model": None, "sig": None, "win": None,
    "sm": [], "idx": 0, "n": 0, "range": None,
    "suppress_sig": None, "busy": False, "renderdoc": None,
    "timer": None, "asynccb": None,
}

try:
    _K_DOWN = uno.getConstantByName("com.sun.star.awt.Key.DOWN")
    _K_UP = uno.getConstantByName("com.sun.star.awt.Key.UP")
    _K_RETURN = uno.getConstantByName("com.sun.star.awt.Key.RETURN")
    _K_ESCAPE = uno.getConstantByName("com.sun.star.awt.Key.ESCAPE")
except Exception:
    _K_DOWN, _K_UP, _K_RETURN, _K_ESCAPE = 1024, 1025, 1280, 1281


def _aspect(g):
    """Ratio largeur/hauteur d'un XGraphic (taille vectorielle de préférence)."""
    for attr in ("Size100thMM", "SizePixel"):
        try:
            s = getattr(g, attr)
            if s.Width and s.Height:
                return s.Width / float(s.Height)
        except Exception:
            pass
    return 3.0


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


def _open_autopopup(starmaths, labels, zrange, sig):
    """Popup FLOTTANTE non-modale (createPeer+setVisible, sans execute). Ferme
    d'abord toute popup existante (refresh propre). Formules rendues + radios ;
    l'état vit dans _autodet, piloté au clavier."""
    if _autodet["popup"] is not None:
        _close_autopopup()
    ctx = XSCRIPTCONTEXT.getComponentContext()  # noqa: F821
    smgr = ctx.ServiceManager
    win = XSCRIPTCONTEXT.getDocument().getCurrentController().getFrame().getContainerWindow()  # noqa: F821
    # Position du caret capturée AVANT toute fenêtre (popup/_previews) : sinon le
    # focus quitte le document et le caret n'est plus localisable.
    caret_xy = _caret_screen_xy(win)
    graphics = _previews(starmaths)
    n = len(starmaths)
    img_h = 26
    row = img_h + 12
    max_w = 210
    dm = smgr.createInstanceWithContext("com.sun.star.awt.UnoControlDialogModel", ctx)
    dm.Title = "MathCursor"
    dm.Width = 250
    dm.Height = 8 + row * n + 6

    def ctrl(service, name, **props):
        m = dm.createInstance("com.sun.star.awt." + service)
        for k, v in props.items():
            setattr(m, k, v)
        dm.insertByName(name, m)
        return m

    y = 6
    for i in range(n):  # radios consécutifs = un seul groupe
        rb = ctrl("UnoControlRadioButtonModel", "r%d" % i,
                  PositionX=6, PositionY=y + row // 2 - 6, Width=12, Height=12, Label="")
        if i == 0:
            rb.State = 1
        y += row
    y = 6
    for i in range(n):
        if graphics[i] is not None:
            w = max(12, min(max_w, int(round(img_h * _aspect(graphics[i])))))
            img = ctrl("UnoControlImageControlModel", "img%d" % i,
                       PositionX=24, PositionY=y + (row - img_h) // 2,
                       Width=w, Height=img_h, ScaleImage=True, Border=0)
            try:
                img.Graphic = graphics[i]
            except Exception:
                pass
            try:
                img.ScaleMode = 1
            except Exception:
                pass
        else:
            ctrl("UnoControlFixedTextModel", "t%d" % i,
                 PositionX=24, PositionY=y + row // 2 - 5, Width=max_w, Height=12, Label=labels[i])
        y += row

    dlg = smgr.createInstanceWithContext("com.sun.star.awt.UnoControlDialog", ctx)
    dlg.setModel(dm)
    toolkit = smgr.createInstanceWithContext("com.sun.star.awt.Toolkit", ctx)
    dlg.createPeer(toolkit, win)  # win calculé en haut de la fonction
    _apply_caret_pos(dlg, win, caret_xy)  # position capturée avant le vol de focus
    dlg.setVisible(True)  # NON-modal : pas d'execute().
    # setVisible active la fenêtre popup -> on REREND le focus à la zone d'édition
    # pour que l'utilisateur continue de taper (les touches restent routées vers
    # notre XKeyHandler, qui pilote la popup en ↓/↑/Entrée).
    try:
        XSCRIPTCONTEXT.getDocument().getCurrentController().getFrame().getComponentWindow().setFocus()  # noqa: F821
    except Exception:
        pass
    _autodet.update(popup=dlg, model=dm, sm=list(starmaths), idx=0, n=n, range=zrange, sig=sig, win=win)


def _refresh_autopopup(sms, labels, zrange, sig):
    """Met à jour le contenu de la popup EXISTANTE sans la recréer (anti-flicker).
    Possible seulement si le nombre de candidats est inchangé et que la nature des
    lignes (image/texte) coïncide ; sinon renvoie False (l'appelant rouvre)."""
    dm = _autodet["model"]
    n = _autodet["n"]
    if dm is None or n == 0 or len(sms) != n:
        return False
    graphics = _previews(sms)
    img_h = 26
    max_w = 210
    try:
        for i in range(n):
            try:
                dm.getByName("r%d" % i).State = 1 if i == 0 else 0
            except Exception:
                pass
            iname = "img%d" % i
            tname = "t%d" % i
            if dm.hasByName(iname):
                if graphics[i] is None:
                    return False  # image -> texte : structure différente, rouvrir
                m = dm.getByName(iname)
                m.Graphic = graphics[i]
                m.Width = max(12, min(max_w, int(round(img_h * _aspect(graphics[i])))))
            elif dm.hasByName(tname):
                if graphics[i] is not None:
                    return False  # texte -> image : rouvrir
                dm.getByName(tname).Label = labels[i]
            else:
                return False
    except Exception:
        return False
    _autodet.update(sm=list(sms), idx=0, range=zrange, sig=sig)
    win = _autodet.get("win")
    if win is not None and _autodet["popup"] is not None:
        _place_at_caret(_autodet["popup"], win)
    return True


def _close_autopopup():
    dlg = _autodet["popup"]
    if dlg is not None:
        # setVisible(False) AVANT dispose : sinon la fenêtre non-modale peut
        # rester affichée à l'écran (« elle ne se ferme pas »).
        try:
            dlg.setVisible(False)
        except Exception:
            pass
        try:
            dlg.dispose()
        except Exception:
            pass
    _autodet.update(popup=None, model=None, sm=[], idx=0, n=0, range=None, sig=None, win=None)


def _autopopup_move(delta):
    dm = _autodet["model"]
    n = _autodet["n"]
    if dm is None or n == 0:
        return
    idx = (_autodet["idx"] + delta) % n
    for i in range(n):
        dm.getByName("r%d" % i).State = 1 if i == idx else 0
    _autodet["idx"] = idx


def _autopopup_commit():
    sm = _autodet["sm"]
    idx = _autodet["idx"]
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


def _start_autodetect(verbose):
    """Enregistre le key handler sur le contrôleur courant. `verbose` = boîtes
    d'info (chemin menu) ; silencieux pour l'auto-start (Job à l'ouverture)."""
    if _DETECTOR is None:
        if verbose:
            _msg("NER indisponible : modèle ou deps natives non chargés.")
        return
    ctrl = XSCRIPTCONTEXT.getDocument().getCurrentController()  # noqa: F821
    # déjà actif sur CE contrôleur -> rien à faire.
    if _autodet["handler"] is not None and _autodet["controller"] is ctrl:
        if verbose:
            _msg("Auto-détection déjà active.")
        return
    # actif sur un AUTRE doc -> retire l'ancien handler avant de réattacher.
    if _autodet["handler"] is not None and _autodet["controller"] is not None:
        try:
            _autodet["controller"].removeKeyHandler(_autodet["handler"])
        except Exception:
            pass
    ctx = XSCRIPTCONTEXT.getComponentContext()  # noqa: F821
    # AsyncCallback créé sur le THREAD PRINCIPAL (réutilisé ensuite par le timer
    # de fond, qui n'appelle QUE addCallback — seul appel cross-thread).
    _autodet["asynccb"] = ctx.ServiceManager.createInstanceWithContext(
        "com.sun.star.awt.AsyncCallback", ctx)
    h = _KeyHandler()
    ctrl.addKeyHandler(h)
    _autodet["handler"] = h
    _autodet["controller"] = ctrl
    if verbose:
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
        rd = _autodet.get("renderdoc")
        if rd is not None:
            try:
                rd.close(False)
            except Exception:
                pass
        _autodet["handler"] = None
        _autodet["controller"] = None
        _autodet["timer"] = None
        _autodet["renderdoc"] = None
        _autodet["busy"] = False
        _msg("Auto-détection désactivée.")
    except Exception:
        pass


# Fonctions exposées au Script Provider de LibreOffice.
g_exportedScripts = (convert_selection, autodetect_start, autodetect_autostart,
                     autodetect_stop)
