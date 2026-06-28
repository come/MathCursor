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

# Cœur RUST unifié : moteur (analyze) + détection (mc-ner) spawnés comme binaires
# — LES MÊMES que VSCode. Plus de moteur ni de NER Python. Cf. ADR
# 2026-06-25-Feat-libreoffice-rust-core.
_DEV_EXT = r"D:\Software\MathCursor\libreoffice-ext"
_DEV_RUST = r"D:\Software\MathCursor\rust\target\release"
_DEV_MODEL = r"D:\Software\MathCursor\models\latest"
# Ligne LaTeX optionnelle sous la formule (False = formule seule). VSCode =
# réglage mathcursor.showLatexInPopup.
_SHOW_LATEX = False
# Nb de candidats affichés avant la ligne « voir plus » (flèche bas = déployer).
_MAX_VISIBLE = 3

_STARMATH_CLSID = "078B7ABA-54FC-457F-8551-6147E776A997"
_CULTURE = "fr"   # "fr" / "us"

# rust_clients (+ popup_client) sont bundlés à la racine de l'extension.
for _p in (_root, _DEV_EXT):
    if _p and _p not in sys.path:
        sys.path.insert(0, _p)
from rust_clients import EngineClient, NerClient   # noqa: E402


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


def _bin(name):
    """Binaire cœur Rust : installé (bin/<tag>/) sinon dev (rust/target/release)."""
    exe = name + (".exe" if sys.platform.startswith("win") else "")
    tag = _platform_tag()
    for base in ((os.path.join(_root, "bin", tag) if _root else None), _DEV_RUST):
        if base:
            p = os.path.join(base, exe)
            if os.path.isfile(p):
                return p
    return os.path.join(_DEV_RUST, exe)


# Moteur (analyze) : indispensable (src -> candidats {latex, starmath, cost}).
_ENGINE = EngineClient(_bin("analyze"))

# Détection NER (mc-ner + modèle) : optionnelle — Ctrl+Espace marche sans.
_model = _first_dir(
    os.path.join(_root, "models", "latest") if _root else None,
    os.environ.get("MATHCURSOR_MODEL"),
    _DEV_MODEL,
)
_NER = NerClient(_bin("mc-ner"), _model)


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
    return obj


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
    res = _ENGINE.analyze(src, _CULTURE)
    # rien reconnu (prose, erreur, moteur indispo) : on ne touche PAS au document.
    if not res or res["decision"] == "erreur" or not res["ranked"]:
        return
    ranked = res["ranked"]
    if res["decision"] == "popup" and len(ranked) > 1:
        # plusieurs lectures : popup NON-MODALE (coquille webview), pilotée au
        # clavier comme l'auto-détection. On s'assure que le key handler est
        # attaché (Ctrl+Espace peut être utilisé sans auto-détection active).
        starmaths = [c["starmath"] for c in ranked]
        labels = [c["latex"] for c in ranked]
        _ensure_key_handler()
        _open_autopopup(starmaths, labels, rng, ("convert", id(rng)))
        return  # l'insertion se fait via ↓/↑/Entrée (ou clic)
    chosen_sm = ranked[0]["starmath"]
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
    "sm": [], "idx": 0, "n": 0, "range": None, "navigated": False,
    "meta": {}, "chain": None,  # meta = info ligne courante ; chain = bloc en cours (transitoire)
    "suppress_sig": None, "busy": False, "client": None,
    "timer": None, "asynccb": None, "toolkit": None, "topwin": None,
}

try:
    _K_DOWN = uno.getConstantByName("com.sun.star.awt.Key.DOWN")
    _K_UP = uno.getConstantByName("com.sun.star.awt.Key.UP")
    _K_RETURN = uno.getConstantByName("com.sun.star.awt.Key.RETURN")
    _K_ESCAPE = uno.getConstantByName("com.sun.star.awt.Key.ESCAPE")
    _K_TAB = uno.getConstantByName("com.sun.star.awt.Key.TAB")
    _KMOD_SHIFT = uno.getConstantByName("com.sun.star.awt.KeyModifier.SHIFT")
except Exception:
    _K_DOWN, _K_UP, _K_RETURN, _K_ESCAPE, _K_TAB = 1024, 1025, 1280, 1281, 1282
    _KMOD_SHIFT = 1


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


# Marqueurs de chaîne (port de rust/mc-engine chain.rs / RelationMarkers.cs) :
# (forme tapée, latex d'affichage, connecteur?, marqueur-MOT?). Ordre = longueur
# décroissante (plus-long-match). Sert à détecter une LIGNE de chaîne et à n'envoyer
# que le RESTE au moteur (relation en tête = « erreur » côté moteur).
_CHAIN_MARKERS = [
    ("approx", "\\approx ", False, True),
    ("environ", "\\approx ", False, True),
    ("env", "\\approx ", False, True),
    ("<=>", "\\Leftrightarrow ", True, False),
    ("=>", "\\Rightarrow ", True, False),
    ("<=", "\\leq ", False, False),
    (">=", "\\geq ", False, False),
    ("!=", "\\neq ", False, False),
    ("⟺", "\\Leftrightarrow ", True, False),  # ⟺
    ("⇔", "\\Leftrightarrow ", True, False),  # ⇔
    ("⟹", "\\Rightarrow ", True, False),      # ⟹
    ("⇒", "\\Rightarrow ", True, False),      # ⇒
    ("≤", "\\leq ", False, False),            # ≤
    ("≥", "\\geq ", False, False),            # ≥
    ("≠", "\\neq ", False, False),            # ≠
    ("≈", "\\approx ", False, False),         # ≈
    ("=", "=", False, False),
    ("<", "<", False, False),
    (">", ">", False, False),
]


def _find_open_brace(text):
    """Position d'une accolade `{` NON fermée (la plus externe), ou -1. Reconnaît
    un ouvreur de système n'importe où (`f(x) = {…`). Port de chain.rs."""
    depth = 0
    pos = -1
    for i, c in enumerate(text):
        if c == "{":
            if depth == 0:
                pos = i
            depth += 1
        elif c == "}" and depth > 0:
            depth -= 1
            if depth == 0:
                pos = -1
    return pos if depth > 0 else -1


def _detect_relation_line(line):
    """(typed, marker_latex, is_connector, rest) si la ligne commence par un
    marqueur de chaîne, sinon None. Port fidèle de chain.rs::detect_relation_line."""
    s = line.lstrip()
    if not s:
        return None
    for typed, latex, is_conn, is_word in _CHAIN_MARKERS:
        head = s[:len(typed)]
        if is_word:
            if head.lower() != typed.lower():
                continue
            nxt = s[len(typed):len(typed) + 1]
            if nxt and nxt.isalpha():
                continue  # « approximation » ne matche pas « approx »
        elif head != typed:
            continue
        rest = s[len(typed):].lstrip(" ").rstrip("\r\n")
        return (typed, latex, is_conn, rest)
    return None


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

    # SYSTÈME D'ÉQUATIONS (hors moteur) : une accolade `{` NON fermée n'importe où
    # (ouvreur) — pas seulement en tête (`f(x) = {…`, `A {…`). Le préfixe avant `{`
    # est analysé par le moteur ; le reste est découpé par `;`. Maj+Entrée = saut de
    # ligne STANDARD (dans le ¶) → converti en `;` (ADR 2026-06-26-Feat-systems-matrix-model).
    if _find_open_brace(para_text) >= 0:
        lead = len(para_text) - len(para_text.lstrip())
        end = len(para_text.rstrip())
        payload = para_text[lead:end].replace("\n", ";")  # sauts de ligne → lignes du système
        comp = _ENGINE.compose_system(payload, _CULTURE)
        if not comp or not comp.get("starmath"):
            return None
        sig = (lead, end, ("system", payload))
        return (sig, [comp["starmath"]], [comp["latex"]],
                _zone_range(para_start, lead, end), {"system": True})

    # LIGNE DE CHAÎNE (hors moteur) : le ¶ commence par un marqueur (=, <=>, ≤…).
    # Le moteur renvoie « erreur » sur une relation en tête → on n'analyse que le
    # RESTE et on préfixe le marqueur rendu dans la popup (ADR chaining-port).
    rel = _detect_relation_line(para_text)
    if rel is not None:
        _typed, mlatex, _is_conn, rest = rel
        if not rest:
            return None  # marqueur seul (« = » en cours de frappe) : attendre la suite
        res = _ENGINE.analyze(rest, _CULTURE)
        if not res or res["decision"] == "erreur" or not res["ranked"]:
            return None
        ranked = res["ranked"]
        sms = [c["starmath"] for c in ranked]
        labels = [mlatex + c["latex"] for c in ranked]  # AFFICHAGE : marqueur préfixé
        lead = len(para_text) - len(para_text.lstrip())
        end = len(para_text.rstrip())
        steno = para_text[lead:end]
        sig = (lead, end, tuple(labels), "rel")
        return sig, sms, labels, _zone_range(para_start, lead, end), {"rel": rel, "steno": steno}

    # Détection + raffinage (zone finale) entièrement côté Rust (mc-ner :
    # detect + pick_nearest + merge + extend, comme le ner-helper de Word).
    zone = _NER.detect(para_text, caret)
    if zone is None:
        return None
    s, e = zone
    if caret < s or caret > e:
        return None
    while s < e and para_text[s].isspace():
        s += 1
    while e > s and para_text[e - 1].isspace():
        e -= 1
    if e <= s:
        return None
    res = _ENGINE.analyze(para_text[s:e], _CULTURE)
    if not res or res["decision"] == "erreur" or not res["ranked"]:
        return None
    ranked = res["ranked"]
    sms = [c["starmath"] for c in ranked]
    labels = [c["latex"] for c in ranked]
    sig = (s, e, tuple(labels))
    return sig, sms, labels, _zone_range(para_start, s, e), {"rel": None, "steno": para_text[s:e]}


def _autodetect_tick():
    """Garde de RÉ-ENTRANCE : certaines opérations (ouverture de doc de rendu,
    déplacement du view-cursor) pompent la boucle d'événements ; sans cette garde,
    un AsyncCallback en file se ré-exécute pendant qu'on est déjà dans le tick →
    travail imbriqué → gel (AppHang)."""
    if not _NER.available or _autodet.get("busy"):
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
    sig, sms, labels, zrange, meta = cand
    if sig == _autodet.get("suppress_sig"):
        return  # fermée par Échap sur CETTE zone -> ne pas rouvrir tant qu'elle ne change pas
    _autodet["suppress_sig"] = None
    if _autodet["popup"] is not None:
        if _autodet.get("sig") == sig:
            return  # inchangé -> garde la popup (et l'index ↓ courant)
        if _refresh_autopopup(sms, labels, zrange, sig, meta):
            return  # contenu mis à jour EN PLACE (pas de fermer/rouvrir)
    _open_autopopup(sms, labels, zrange, sig, meta)


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


def _open_autopopup(starmaths, labels, zrange, sig, meta=None):
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
                    show_latex=_SHOW_LATEX, collapse_at=_MAX_VISIBLE):
        _posdbg.append("coquille: show a échoué (ready timeout / process mort)")
        return
    _autodet.update(popup=True, pos=(x, y), sm=list(starmaths), idx=-1,
                    n=len(starmaths), range=zrange, sig=sig, win=win, navigated=False,
                    meta=meta or {})


def _refresh_autopopup(sms, labels, zrange, sig, meta=None):
    """Met à jour la popup EXISTANTE (nouveaux candidats) SANS bouger : on réutilise
    la position FIGÉE de l'ouverture (_autodet["pos"]), pas de recalcul. Renvoie
    True si géré."""
    cli = _autodet.get("client")
    pos = _autodet.get("pos")
    if cli is None or _autodet["popup"] is None or pos is None:
        return False
    x, y = pos
    if not cli.show(_candidates(labels), x, y, line_height=0, selected_index=-1,
                    show_latex=_SHOW_LATEX, collapse_at=_MAX_VISIBLE):
        return False
    _autodet.update(sm=list(sms), idx=-1, n=len(sms), range=zrange, sig=sig, navigated=False,
                    meta=meta or {})
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
    # `chain` (bloc transitoire) PRÉSERVÉ : l'adjacence est revérifiée au commit.
    _autodet.update(popup=None, pos=None, sm=[], idx=0, n=0, range=None, sig=None, win=None, meta={})


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


def _block_is_just_above(obj, zr):
    """L'objet `obj` (bloc en cours) est-il dans le ¶ juste AU-DESSUS de `zr` ?
    Garde-fou d'adjacence : on ne fusionne que des lignes contiguës."""
    if obj is None or zr is None:
        return False
    try:
        text = zr.getText()
        zc = text.createTextCursorByRange(zr.getStart())
        zc.gotoStartOfParagraph(False)
        above = text.createTextCursorByRange(zc.getStart())
        if not above.goLeft(1, False):
            return False  # zr est au 1ᵉʳ ¶, rien au-dessus
        above.gotoStartOfParagraph(False)
        oc = text.createTextCursorByRange(obj.getAnchor().getStart())
        oc.gotoStartOfParagraph(False)
        return text.compareRegionStarts(oc.getStart(), above.getStart()) == 0
    except Exception:
        return False


def _merge_block_with_line(obj, zr, starmath):
    """Remplace [bloc `obj` … fin de la ligne-relation `zr`] par UN objet aligné
    (supprime l'objet, le saut de ¶ et la sténo, puis insère). Renvoie le nouvel
    objet. Tout dans un contexte d'undo unique (Ctrl+Z = un pas)."""
    text = zr.getText()
    cur = text.createTextCursorByRange(obj.getAnchor().getStart())
    cur.gotoRange(zr.getEnd(), True)  # sélectionne objet + saut(s) de ¶ + sténo
    return _insert_formula(cur, starmath)  # setString("") supprime la sélection puis insère


def _autopopup_commit():
    sm = _autodet["sm"]
    idx = _autodet["idx"]
    if idx < 0:
        idx = 0  # Entrée sans avoir navigué -> valide le 1ᵉʳ candidat par défaut
    zr = _autodet["range"]
    meta = _autodet.get("meta") or {}
    rel = meta.get("rel")
    steno = meta.get("steno", "")
    chosen = sm[idx] if 0 <= idx < len(sm) else None
    chain = _autodet.get("chain")
    _close_autopopup()  # préserve "chain"
    if zr is None:
        return
    doc = XSCRIPTCONTEXT.getDocument()  # noqa: F821
    undo = None
    try:
        undo = doc.getUndoManager()
        undo.enterUndoContext("MathCursor : chaîne")
    except Exception:
        undo = None
    try:
        if meta.get("system"):
            # SYSTÈME : un transform autonome (le starmath composé est déjà sm[0]).
            # N'amorce PAS de chaîne (un système ne s'étend pas après commit).
            if chosen is not None:
                _insert_formula(zr, chosen)
            _autodet["chain"] = None
        elif rel is not None:
            # LIGNE DE CHAÎNE : étendre le bloc adjacent au-dessus, sinon repli autonome.
            if chain is not None and _block_is_just_above(chain.get("obj"), zr):
                lines = list(chain["lines"]) + [(steno, idx)]
                comp = _ENGINE.compose(lines, _CULTURE)
                if comp and comp.get("starmath"):
                    obj = _merge_block_with_line(chain["obj"], zr, comp["starmath"])
                    if obj is not None:
                        _autodet["chain"] = {"obj": obj, "lines": lines}
                        return
            # repli autonome : bloc d'UNE ligne (marqueur rendu + reste).
            comp = _ENGINE.compose([(steno, idx)], _CULTURE)
            sm_one = comp.get("starmath") if comp else None
            obj = _insert_formula(zr, sm_one or (chosen or ""))
            _autodet["chain"] = {"obj": obj, "lines": [(steno, idx)]}
        else:
            # ÉQUATION NORMALE : insérer + amorcer une nouvelle chaîne.
            if chosen is None:
                _autodet["chain"] = None
                return
            obj = _insert_formula(zr, chosen)
            _autodet["chain"] = {"obj": obj, "lines": [(steno, idx)]}
    except Exception:
        _autodet["chain"] = None
    finally:
        if undo is not None:
            try:
                undo.leaveUndoContext()
            except Exception:
                pass


def _nav(delta):
    """Transmet ↑/↓ au HTML (qui possède la sélection + le repli « voir plus »).
    Dès la 1ʳᵉ flèche, une ligne est surlignée → on mémorise « a navigué » pour
    décider du sort d'Entrée (avalée vs redescendue à l'éditeur)."""
    _autodet["navigated"] = True
    cli = _autodet.get("client")
    if cli is not None:
        try:
            cli.nav(delta)
        except Exception:
            pass


def _activate(implicit=False):
    """Validation : le HTML valide la ligne surlignée (ou déploie « voir plus »).
    implicit=True (touche commit-1er, ex. Tab) → valide le 1er candidat même sans
    navigation."""
    cli = _autodet.get("client")
    if cli is not None:
        try:
            cli.activate(implicit)
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
        if code == _K_RETURN and (ev.Modifiers & _KMOD_SHIFT):
            # Maj+Entrée = saut de ligne STANDARD (dans le ¶) : on NE consomme PAS
            # et on garde la popup ouverte → le tick recompose (le saut de ligne
            # devient une nouvelle ligne du système, cf. _detect_candidate).
            return False
        if code == _K_DOWN:
            _nav(1)
            return True
        if code == _K_UP:
            _nav(-1)
            return True
        if code == _K_RETURN:
            if _autodet.get("navigated"):
                _activate(False)   # ligne surlignée → valide (consomme Entrée)
                return True
            # aucune sélection → on NE consomme PAS : Writer insère le saut de ligne
            # normal ; on ferme la popup. (cf. ADR 2026-06-25-Feat-popup-enter-passthrough)
            _close_autopopup()
            return False
        if code == _K_TAB:
            # Touche commit-1er (façon Tab dans Word) : valide le 1er candidat même
            # sans navigation. Consommée uniquement popup ouverte (pas d'indentation).
            _activate(True)
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
    if not _NER.available:
        if verbose:
            _msg("NER indisponible : mc-ner.exe ou modèle introuvable.")
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
