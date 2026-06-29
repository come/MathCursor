# -*- coding: utf-8 -*-
"""Clients stdio des binaires cœur RUST (analyze = moteur, mc-ner = détection),
spawnés par l'extension LibreOffice — LES MÊMES binaires que VSCode (cœur
unifié). Pur Python, sans dépendance UNO -> testable hors LibreOffice.

Requêtes SYNCHRONES : 1 ligne envoyée -> 1 ligne reçue (les services répondent
dans l'ordre). Process persistant, re-spawn paresseux s'il meurt.
"""
import json
import os
import queue
import subprocess
import sys
import threading

_CREATE_NO_WINDOW = 0x08000000  # Windows : pas de console qui flashe


class _StdioProc:
    """Process persistant stdio (handshake READY au démarrage).

    Les lectures sont BORNÉES par timeout (thread lecteur + queue) : un readline
    nu bloque indéfiniment si l'enfant se coince, ce qui gèle l'appelant. CE CLIENT
    PEUT ÊTRE APPELÉ DEPUIS UN THREAD UI (LibreOffice) — un blocage non borné y est
    un gel applicatif. Sur timeout, on TUE l'enfant (respawn paresseux ensuite) :
    plutôt que de garder un process qui répondra en retard et désynchronisera la
    requête suivante. Cf. ADR 2026-06-29-Fix-libreoffice-detection-off-ui-thread.
    """

    def __init__(self, argv, ready_tokens=("READY",), spawn_timeout=20.0,
                 req_timeout=3.0):
        self._argv = argv
        self._ready_tokens = ready_tokens
        self._spawn_timeout = spawn_timeout  # généreux : chargement modèle ONNX
        self._req_timeout = req_timeout
        self._proc = None
        self._q = None
        self._lock = threading.Lock()

    def _alive(self):
        return self._proc is not None and self._proc.poll() is None

    def _reader(self, proc, q):
        """Thread daemon : pompe stdout ligne à ligne vers la queue. Sentinelle
        None à l'EOF (process mort)."""
        try:
            for line in proc.stdout:
                q.put(line)
        except Exception:
            pass
        finally:
            q.put(None)

    def _kill(self):
        p = self._proc
        self._proc = None
        self._q = None
        if p is not None:
            try:
                p.kill()
            except Exception:
                pass

    def _spawn(self):
        kw = {}
        if sys.platform.startswith("win"):
            kw["creationflags"] = _CREATE_NO_WINDOW
        self._proc = subprocess.Popen(
            self._argv, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            text=True, encoding="utf-8", bufsize=1, **kw)
        self._q = queue.Queue()
        threading.Thread(target=self._reader, args=(self._proc, self._q),
                         daemon=True).start()
        try:
            line = self._q.get(timeout=self._spawn_timeout)
        except queue.Empty:
            self._kill()
            raise RuntimeError("service: timeout d'attente READY")
        if line is None or line.strip() not in self._ready_tokens:
            self._kill()
            raise RuntimeError("service non prêt: %r" % line)

    def request(self, payload):
        """Envoie payload (sans \\n), renvoie la ligne de réponse (str) ou None.
        None si le service est indisponible, mort, ou ne répond pas à temps."""
        with self._lock:
            if not self._alive():
                try:
                    self._spawn()
                except Exception:
                    return None
            try:
                self._proc.stdin.write(payload + "\n")
                self._proc.stdin.flush()
            except Exception:
                self._kill()
                return None
            try:
                line = self._q.get(timeout=self._req_timeout)
            except queue.Empty:
                self._kill()  # enfant coincé -> on le recycle (pas de désync)
                return None
            if not line:  # None (EOF) ou ligne vide -> mort
                self._kill()
                return None
            return line.strip()

    def quit(self):
        p = self._proc
        self._proc = None
        self._q = None
        if p is not None:
            try:
                p.stdin.write("QUIT\n")
                p.stdin.flush()
            except Exception:
                pass
            try:
                p.wait(timeout=1.0)
            except Exception:
                try:
                    p.terminate()
                except Exception:
                    pass


def _clean(s):
    return (s or "").replace("\t", " ").replace("\r", " ").replace("\n", " ")


class EngineClient:
    """Moteur Rust (analyze.exe) : src -> dict {decision, ranked:[{latex, starmath,
    cost}], hasNote} (ou None si indisponible/erreur)."""

    def __init__(self, exe_path):
        self._io = _StdioProc([exe_path]) if os.path.isfile(exe_path) else None

    @property
    def available(self):
        return self._io is not None

    def analyze(self, src, culture):
        if self._io is None:
            return None
        line = self._io.request((culture or "fr") + "\t" + _clean(src))
        if not line:
            return None
        try:
            return json.loads(line)
        except Exception:
            return None

    def compose(self, lines, culture):
        """Chaîne multiligne -> bloc aligné. `lines` = liste de (steno, index du
        candidat choisi). Renvoie {"latex":.., "starmath":..} ou None. Le JSON
        échappe tabs/newlines -> pas de collision avec le protocole \\t."""
        if self._io is None:
            return None
        payload = json.dumps([{"steno": s, "index": int(i)} for (s, i) in lines])
        line = self._io.request("COMPOSE\t" + (culture or "fr") + "\t" + payload)
        if not line:
            return None
        try:
            return json.loads(line)
        except Exception:
            return None

    def compose_system(self, rest, culture):
        """Système d'équations { … : `rest` = contenu après le `{` (lignes par `;`).
        Renvoie {"latex":.., "starmath":..} (bloc accolade gauche) ou None."""
        if self._io is None:
            return None
        line = self._io.request("COMPOSE_SYSTEM\t" + (culture or "fr") + "\t" + _clean(rest))
        if not line:
            return None
        try:
            return json.loads(line)
        except Exception:
            return None

    def quit(self):
        if self._io is not None:
            self._io.quit()


class NerClient:
    """Détecteur Rust (mc-ner.exe) : (texte, caret UTF-16) -> (start, end) ou None.
    Le service fait détection + raffinage (zone finale) ; protocole identique au
    ner-helper. ready_tokens inclut FATAL=mort (model absent)."""

    def __init__(self, exe_path, model_dir):
        ok = os.path.isfile(exe_path) and model_dir and os.path.isdir(model_dir)
        self._io = _StdioProc([exe_path, model_dir]) if ok else None

    @property
    def available(self):
        return self._io is not None

    def detect(self, text, caret):
        if self._io is None:
            return None
        line = self._io.request("DETECT\t%d\t%s" % (int(caret), _clean(text)))
        if not line or line == "NONE":
            return None
        parts = line.split("\t")
        if len(parts) >= 3 and parts[0] == "ZONE":
            try:
                return (int(parts[1]), int(parts[2]))
            except Exception:
                return None
        return None

    def quit(self):
        if self._io is not None:
            self._io.quit()
