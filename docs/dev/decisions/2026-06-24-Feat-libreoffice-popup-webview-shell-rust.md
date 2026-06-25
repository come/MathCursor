# Feat — Popup LibreOffice via coquille webview externe Rust (wry+tao) + KaTeX

**Date :** 2026-06-24
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-04-24-Feat-popup-webview-katex.md](2026-04-24-Feat-popup-webview-katex.md) (retracté, contexte Word/WPF in-process — référence non superséante) · spike `libreoffice-ext/_spike_shell/RESULTS.md`

## Citation acté

> « je m'interroge notamment à porter le moteur en rust par exemple pour la legererté et flexibilité sur les OS » — utilisateur, 2026-06-24

Choix de coquille validé sur données du spike :

> « wry + tao (Rust) — recommandé » — utilisateur, 2026-06-24 (sélection après lecture de `RESULTS.md`)

> « yes automode mais je veux pas de fallback gracieuse » — utilisateur, 2026-06-24

## Contexte

L'extension LibreOffice (`libreoffice-ext/mathcursor.py`, Python/UNO) affiche sa
popup au caret via un **dialogue UNO non-modal**, et rend chaque candidat en
image **StarMath** dans un **doc Writer caché** (`_render_doc`/`_previews`,
`loadComponentFromURL`). Ce chemin pose problème sur quatre axes signalés par
l'utilisateur : **gels** (le doc caché pompe la boucle d'événements UNO
single-thread → réentrance/AppHang, d'où la garde `busy`), **positionnement** au
caret bancal, **qualité de rendu** StarMath médiocre, et **cohérence d'archi**
(rien de partagé avec la solution VSCode).

L'extension VSCode a résolu un problème voisin avec une **fenêtre externe** (exe
WPF lancé en subprocess, candidats sur stdin, positionnée au caret,
non-activante). On transpose l'idée à LibreOffice mais de façon **cross-OS** et
**légère** : une coquille webview en process séparé qui rend en KaTeX. Le moteur
tournant **déjà en Python** dans l'extension (`mc_engine/`), la coquille n'a pas
besoin du moteur : elle **rend** seulement les candidats que Python lui envoie
(donc **pas de WASM** — qui n'apporterait qu'un runtime .NET inutile ici).

## Décision

**Cible : LibreOffice uniquement.** On ne touche NI à Word VSTO NI à l'extension
VSCode.

La popup devient une **coquille webview externe écrite en Rust (`wry` + `tao`,
sans framework Tauri ni Node)** :

- **Process séparé persistant** lancé par `mathcursor.py` (`subprocess.Popen`),
  lazy-start « à chaud », tué dans `autodetect_stop` / fermeture doc.
- **Contrat IPC** = une ligne JSON par message. Python → coquille :
  `show {candidates,x,y,lineHeight,selectedIndex}`, `update {selectedIndex}`,
  `close`, `quit`. Coquille → Python : `ready` (handshake), `commit {index}` /
  `dismiss` (clic souris uniquement), `error`.
- **Fenêtre** borderless, topmost, **non-activante** (ne vole pas le focus →
  Writer garde le clavier), positionnée aux **coords écran absolues**.
- **Rendu KaTeX bundlé local** (offline, jamais de CDN) — couvre 100 % du
  vocabulaire (leçon de l'ADR retracté 2026-04-24).
- **Le clavier reste 100 % côté UNO** : `_KeyHandler` ↑↓/Entrée/Échap pilote la
  coquille via `update`/`close` ; la coquille ne renvoie que les clics souris.
- **StarMath conservé pour l'insertion finale** (`_insert_formula`), pas pour
  l'aperçu.

**Suppressions** (coupe nette, **aucun fallback gracieux** — décision
utilisateur) : `_render_doc`/`_previews` (cause #1 des gels), le dialogue UNO
(`_open_autopopup`/`_refresh_autopopup`/`_close_autopopup` réécrits pour piloter
la coquille), `_choose`/`_choose_rendered` (repli texte). Si la coquille ne
démarre pas / meurt → erreur loguée dans `_posdbg`, **aucune popup** (pas de
repli sur l'ancien chemin).

## Tradeoff & alternatives écartées

Spike comparatif sur Windows 11 (WebView2 149 préinstallé), proof identique pour
chaque candidat (`libreoffice-ext/_spike_shell/`) :

- **pywebview (Python)** : même langage que l'extension, pas de compilation côté
  nous. Rejeté : **~6–7 Mo** (webview + pythonnet/.NET + clr_loader/bottle),
  démarrage **~1,96 s**, et surtout **3 jeux de dépendances compilées par OS** à
  vendoriser (pythonnet/.NET sur Windows, pyobjc sur macOS, PyGObject+WebKitGTK
  sur Linux) pour l'ABI exacte du Python de LibreOffice → fragile cross-OS.
- **Moteur WASM dans la popup (popup autonome)** : rejeté pour la cible
  LibreOffice — le moteur tourne déjà en Python ; le WASM ne traînerait qu'un
  runtime .NET Blazor (~1,2 Mo gzip min) pour recalculer ce que Python a déjà
  calculé. Réservé à une éventuelle unification VSCode/Word ultérieure.
- **wry + tao (Rust)** — **retenu** : binaire **autonome 490 Ko** par OS,
  démarrage **~0,45–0,76 s**, aucune dépendance Python/.NET, packaging `.oxt`
  propre (1 binaire par tag), webview système (WebView2/WKWebView/WebKitGTK).
  Cohérent avec l'intuition « Rust pour la légèreté » **sans toucher au moteur**.
  Coût : une toolchain Rust en CI par OS.

Les risques cross-OS (positionnement absolu refusé sur **Wayland**, **WebKitGTK**
à installer sur Linux, **HiDPI**/coords logiques vs physiques) sont **communs aux
deux** candidats — ils tiennent aux webviews/windowing système, pas à la
coquille. À valider sur Mac/Linux (le spike n'a couvert que Windows).

## Conséquences

- **Code touché** :
  - Nouveau `libreoffice-ext/shell/` (source Rust `wry`+`tao` : `Cargo.toml` +
    `src/main.rs`), binaire compilé déposé en `shell/<platform_tag>/` dans le
    `.oxt` (`win_amd64`, `mac_arm64`, `mac_x86_64`, `linux_x86_64`).
  - Nouveaux assets `libreoffice-ext/assets/popup/index.html` +
    `assets/katex/**` (offline, ~1,5 Mo dont fonts ; réductibles au woff2 seul).
  - Nouveau `libreoffice-ext/popup_client.py` (pur Python testable hors LO :
    `Popen` + handshake + `show/update/close/quit` + relance lazy + découverte
    binaire via `_ext_root()` + tag plateforme).
  - `libreoffice-ext/mathcursor.py` : réécriture `_open_autopopup` /
    `_refresh_autopopup` / `_close_autopopup` ; suppression
    `_render_doc`/`_previews`/`_choose`/`_choose_rendered` ; `_caret_screen_xy`
    rend des coords **écran absolues** ; `_autodet` perd `renderdoc`/`model`,
    gagne `client`.
  - `libreoffice-ext/build_oxt.py` : embarque `assets/` + `shell/<tag>/`.
- **Tests** : `popup_client.py` testable en isolation (mock subprocess) ; proof
  coquille validé sur Windows (rendu KaTeX, positionnement, IPC, clic→commit —
  cf. screenshots du spike). Parité Mac/Linux à établir.
- **API publique** : aucune (extension hors produit phase 1 ; moteur/serialization
  purs **non touchés**).
- **Règles MC impactées** : aucune. Le moteur (`engine/`) et la sérialisation
  restent purs ; cette décision ne concerne que l'adapter LibreOffice.

## Validation post-fix

Spike Windows déjà concluant (`_spike_shell/RESULTS.md`). Intégration à vérifier
end-to-end : build `.oxt` → `unopkg add` → LibreOffice Writer → taper `1/2`,
`x^2+1/2`, `lim x 0 g(x)` → popup au caret non-activante, ↑/↓ surligne, Entrée
insère le bon StarMath, Échap ne rouvre pas ; **frappe soutenue sans gel** ;
multi-écran / zoom ≠ 100 % / HiDPI ; binaire absent → erreur loguée + aucune
popup (pas de fallback) ; `stop`/`start` sans process orphelin. Puis Mac/Linux.

## Plan en cours — état d'avancement

- [x] Étape 0 — Spike comparatif coquille (Windows) → `RESULTS.md`
- [x] Étape 1 — Décision (cet ADR)
- [x] Étape 2 — `shell/src` Rust de prod (custom protocol `mc://` + handshake `loaded` + profil WebView2 en temp) → binaire `shell/win_amd64/` 502 Ko ; `popup_client.py` (testé) ; `assets/popup/` KaTeX woff2 600 Ko
- [x] Étape 3 — Intégration `mathcursor.py` (suppression `_render_doc`/`_previews`/`_choose*` ; `_open/_refresh/_close_autopopup` + `_autopopup_move` pilotent `PopupClient` ; `_caret_screen_abs` + `_win_screen_origin` ; callbacks souris marshalés via AsyncCallback ; Ctrl+Espace via `_ensure_key_handler`)
- [x] Étape 4 — `build_oxt.py` (embarque `assets/` + `shell/<tag>/`, exclut cache `.WebView2`) → `.oxt` 51 fichiers / 644 Ko
- [x] Étape 5 — Vérif end-to-end **Windows** : rendu + lancement + branche popup + **positionnement au caret** + **anti-gel** tous validés dans Writer. Positionnement : coords UNO = pixels PHYSIQUES → coquille en `PhysicalPosition` (`LogicalPosition` re-scalait ×1.25 en HiDPI 125%) ; `_caret_pos_geometric` aligné sur le vrai DPI ; fenêtre dimensionnée au contenu (IPC `size:`). Anti-gel : **pré-chauffage** de la coquille en thread daemon dès l'activation (boot WebView2 ~0,7 s hors thread UI) ; stress-test churn open/refresh/close sur le thread UI = **max 7,5 ms / moy 4,1 ms** (vs l'AppHang du doc StarMath caché, supprimé).
- [ ] **RESTE** — binaires **Mac/Linux** (`cargo build` par OS, cf. `shell/README.md` ; non cross-compilable depuis Windows) + risques Wayland (positionnement absolu) / WebKitGTK / Retina. Auto-détection live avec le **modèle NER** (absent en local, retrain = job Colab) à éprouver une fois le modèle dispo.
