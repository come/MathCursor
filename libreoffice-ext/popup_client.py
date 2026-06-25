# -*- coding: utf-8 -*-
"""Client de la coquille popup webview (process séparé, IPC stdin/stdout JSON).

Pur Python, sans dépendance UNO -> testable hors LibreOffice (cf. _selftest).
La coquille (binaire Rust `mc_popup_shell`, cf. shell/) rend les candidats en
KaTeX dans une fenêtre borderless/topmost/non-activante positionnée aux coords
ÉCRAN ABSOLUES. Le clavier reste géré côté hôte ; la coquille ne renvoie que les
clics souris (`commit`/`dismiss`), via les callbacks `on_commit`/`on_dismiss`.

ATTENTION (intégration UNO) : les callbacks `on_commit`/`on_dismiss` sont appelés
sur le THREAD LECTEUR (fond). Tout travail UNO doit être re-posté sur le thread
principal (AsyncCallback), comme le tick de détection.
"""
import json
import os
import subprocess
import sys
import threading
import time

_CREATE_NO_WINDOW = 0x08000000  # Windows : pas de console qui flashe


class PopupClient:
    def __init__(self, exe_path, html_path, on_commit=None, on_dismiss=None,
                 on_error=None, ready_timeout=8.0):
        self.exe_path = exe_path
        self.html_path = html_path
        self.on_commit = on_commit
        self.on_dismiss = on_dismiss
        self.on_error = on_error
        self.ready_timeout = ready_timeout
        self._proc = None
        self._ready = threading.Event()
        self._lock = threading.Lock()

    # ── cycle de vie ─────────────────────────────────────────────────────────
    def _alive(self):
        return self._proc is not None and self._proc.poll() is None

    def _spawn(self):
        if not os.path.isfile(self.exe_path):
            raise FileNotFoundError("coquille introuvable: %s" % self.exe_path)
        kw = {}
        if sys.platform.startswith("win"):
            kw["creationflags"] = _CREATE_NO_WINDOW
        self._ready.clear()
        self._proc = subprocess.Popen(
            [self.exe_path, self.html_path],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            text=True, bufsize=1, **kw)
        threading.Thread(target=self._pump, args=(self._proc,), daemon=True).start()

    def _pump(self, proc):
        for line in proc.stdout:
            line = line.strip()
            if not line:
                continue
            try:
                ev = json.loads(line)
            except Exception:
                continue
            kind = ev.get("evt")
            if kind == "ready":
                self._ready.set()
            elif kind == "commit" and self.on_commit:
                try:
                    self.on_commit(int(ev.get("index", 0)))
                except Exception:
                    pass
            elif kind == "dismiss" and self.on_dismiss:
                try:
                    self.on_dismiss()
                except Exception:
                    pass
            elif kind == "error" and self.on_error:
                try:
                    self.on_error(ev.get("msg", ""))
                except Exception:
                    pass

    def ensure(self):
        """Lance la coquille si besoin (lazy/relance si morte) et attend `ready`.
        Retourne True si prête. Lève si le binaire est introuvable."""
        with self._lock:
            if not self._alive():
                self._spawn()
        return self._ready.wait(self.ready_timeout)

    # ── commandes ───────────────────────────────────────────────────────────
    def _send(self, obj):
        if not self._alive():
            return False
        try:
            self._proc.stdin.write(json.dumps(obj) + "\n")
            self._proc.stdin.flush()
            return True
        except (BrokenPipeError, OSError, ValueError):
            self._proc = None
            return False

    def show(self, candidates, x, y, line_height=0, selected_index=0, theme="light",
             show_latex=True, collapse_at=0):
        """candidates = liste de dicts {"latex": "..."}. (x, y) = coords ÉCRAN
        absolues du caret ; la popup s'affiche `line_height` px plus bas.
        theme = "light"/"dark" (LibreOffice/Writer = clair) ; la coquille suit
        prefers-color-scheme si on n'envoie rien. show_latex = ligne LaTeX sous
        la formule (optionnelle)."""
        if not self.ensure():
            return False
        return self._send({"cmd": "show", "candidates": candidates,
                           "x": int(x), "y": int(y),
                           "lineHeight": int(line_height),
                           "selectedIndex": int(selected_index),
                           "theme": theme, "showLatex": bool(show_latex),
                           "collapseAt": int(collapse_at)})

    def update(self, selected_index):
        return self._send({"cmd": "update", "selectedIndex": int(selected_index)})

    # Le HTML possède la sélection + le repli « voir plus » : on transmet juste
    # l'intention clavier (la coquille relaie à window.mcNav / window.mcActivate).
    def nav(self, delta):
        return self._send({"cmd": "nav", "delta": int(delta)})

    def activate(self):
        return self._send({"cmd": "activate"})

    def close(self):
        return self._send({"cmd": "close"})

    def quit(self):
        self._send({"cmd": "quit"})
        p = self._proc
        self._proc = None
        if p is not None:
            try:
                p.wait(timeout=1.5)
            except Exception:
                try:
                    p.terminate()
                except Exception:
                    pass


# ── self-test hors LibreOffice : python popup_client.py ──────────────────────
def _selftest():
    here = os.path.dirname(os.path.abspath(__file__))
    tag = "win_amd64" if sys.platform.startswith("win") else (
        "mac_arm64" if sys.platform == "darwin" else "linux_x86_64")
    exe = os.path.join(here, "shell", tag,
                       "mc_popup_shell.exe" if sys.platform.startswith("win") else "mc_popup_shell")
    html = os.path.join(here, "assets", "popup", "index.html")
    got = {}
    cli = PopupClient(exe, html,
                      on_commit=lambda i: got.setdefault("commit", i),
                      on_dismiss=lambda: got.setdefault("dismiss", True),
                      on_error=lambda m: print("  [shell error]", m))
    cands = [{"latex": r"\frac{1}{2}"}, {"latex": r"x^{2}+\frac{1}{2}"},
             {"latex": r"\lim_{x\to 0} g(x)"}]
    print("ensure/show ->", cli.show(cands, 500, 400, line_height=20, selected_index=0))
    time.sleep(1.5)
    print("update ->", cli.update(2))
    time.sleep(0.8)
    print("close ->", cli.close())
    time.sleep(0.3)
    cli.quit()
    print("ready reçu:", cli._ready.is_set(), "| events souris:", got)


if __name__ == "__main__":
    _selftest()
