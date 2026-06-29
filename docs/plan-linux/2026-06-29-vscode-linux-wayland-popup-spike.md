# Palier 3 Linux (Wayland) — Spike AT-SPI pour mc-popup

> Plan rédigé sous Windows (où le code Linux ne compile pas), destiné à être
> **exécuté sous Linux**. Cadré en plan mode + agent Plan (sources vérifiées,
> 2026-06-29). Lis ce doc en entier avant de coder : le verdict de faisabilité
> change la nature du livrable.

## Context

Sur le VSIX Linux, la popup Rust au caret ne sort pas : `popup.ts` fait
`isHelperAvailable() === 'win32'`, donc hors Windows l'extension bascule
**volontairement** sur l'autocomplete natif VSCode (`editor.action.triggerSuggest`,
`adapter-vscode/extension/src/extension.ts:151`). Le binaire `mc-popup` est
pourtant déjà compilé et copié dans le VSIX Linux par `build.mjs` — shippé mais
**dormant**.

Objectif palier 3 : faire sortir la vraie popup wry au caret sous Linux. Le mode
actif Windows (`mod active_mode`, `rust/mc-popup/src/main.rs`) repose sur 4
primitives Win32 :

| # | Primitive Windows | Équivalent Linux visé |
|---|-------------------|-----------------------|
| 1 | Caret écran (MSAA `OBJID_CARET` + `accLocation`) | AT-SPI `Text.GetCharacterExtents(offset, Screen)` |
| 2 | Hook clavier consommant (`WH_KEYBOARD_LL`) | AT-SPI `DeviceEventController` (⚠ voir R1) |
| 3 | Fenêtre `WS_EX_NOACTIVATE` + `SetWindowPos` | hints GTK/tao + positionnement (⚠ voir R2) |
| 4 | Watch `GetForegroundWindow` (200 ms) | events focus AT-SPI |

Cible : **Wayland** (GNOME/KDE récent). Machine de test dispo.

## Verdict de faisabilité — DEUX murs Wayland

**Deux des quatre primitives sont architecturalement impossibles sous Wayland
natif.** D'où le choix « spike d'abord » : on mesure avant de bâtir.

- **R1 (bloquant clavier)** — `DeviceEventController.RegisterKeystrokeListener`
  est **déprécié** (`atspi-proxies` 0.14) avec mention explicite *« does not work
  on Wayland »* ; Mutter n'expose pas de device controller ; Wayland interdit par
  design tout hook clavier global. → pas de consommation ↑↓/Entrée/Tab/Échap façon
  Windows.
- **R2 (bloquant ancrage)** — Wayland n'a **pas** de coordonnées écran globales ;
  `window.set_outer_position` est **sans effet** (tao issue #566). Le fallback
  passif actuel `cfg(not(windows))` ne marche donc que sous X11/**XWayland**. →
  ancrer la popup au caret nécessite XWayland.
- **R3 (majeur)** — VSCode/Monaco n'expose peut-être pas d'extents de caret
  exploitables via AT-SPI (Monaco = buffer logique, pas forcément la géométrie
  glyphe écran).
- **R4 (moyen)** — pré-requis a11y non garantis (bus `org.a11y.Bus`, arbre a11y de
  VSCode activé).

Conséquence : **ne pas coder l'intégration complète à l'aveugle.** Livrer d'abord
un **spike diagnostique** qui mesure R1/R2/R3/R4 sur la machine cible. Son verdict
décide l'étape B.

## Périmètre du spike

Spike **uniquement**. Pas de modif `popup.ts`/`build.mjs`, pas d'intégration.

Fichiers touchés :

- **`rust/mc-popup/src/main.rs`** — ajouter `#[cfg(target_os="linux")] mod
  active_mode_linux` avec `pub fn run_spike()`. Dans `main()`, **tout en haut**
  (avant la construction de l'event loop tao) :

  ```rust
  #[cfg(target_os = "linux")]
  if std::env::args().any(|a| a == "--spike-linux") {
      active_mode_linux::run_spike();
      std::process::exit(0);
  }
  ```

  Le spike n'ouvre **aucune** fenêtre wry/tao — purement diagnostique, sortie =
  lignes texte sur stdout. Donc pas de conflit d'event loop : il peut posséder son
  propre runtime tokio.

- **`rust/mc-popup/Cargo.toml`** — ajouter (section windows inchangée) :

  ```toml
  [target.'cfg(target_os = "linux")'.dependencies]
  atspi = { version = "0.30", features = ["connection", "proxies", "tokio"] }
  tokio = { version = "1", features = ["rt", "sync", "time", "macros"] }
  # zbus : NE PAS épingler — laisser atspi tirer sa version. Si besoin d'un proxy
  # custom (listener clavier), ajouter zbus avec la MÊME version que celle
  # résolue par atspi (vérifier `cargo tree -p mc-popup -i zbus`).
  ```

  ⚠ Ne **pas** activer la feature `x11-legacy` en croyant débloquer le keystroke
  listener : elle réactive juste l'interface dépréciée, sans la rendre
  fonctionnelle sous Wayland.

## API confirmées (atspi 0.30 / atspi-proxies 0.14)

- `atspi::connection::AccessibilityConnection::new().await` — connexion au bus a11y.
  Re-exports : `atspi::proxy` (= `atspi-proxies`), `atspi::connection`.
- `proxy::text::TextProxy::caret_offset()` — propriété `#[zbus(property)]`.
- `proxy::text::TextProxy::get_character_extents(offset: i32, coord_type: CoordType)
  -> zbus::Result<(i32,i32,i32,i32)>` — `(x,y,w,h)`. Équivalent direct d'`accLocation`.
- `proxy::component::ComponentProxy::get_extents(CoordType)` — fallback si pas d'iface Text.
- `proxy::accessible::AccessibleProxy` — `role()`, `name()`, chaîne parent (remonter
  jusqu'à l'app pour confirmer `Code`).
- `atspi_common::CoordType` (`#[repr(u32)]`) = `Screen | Window | Parent` —
  **`Screen` = coords écran**.
- `proxy::device_event_controller::DeviceEventControllerProxy::register_keystroke_listener(...)`
  + `EventListenerMode { synchronous, preemptive, global }`, `EventType::KeyPressed`
  — **déprécié, no-op Wayland** ; le spike l'appelle uniquement pour MESURER R1.
- Flux d'events : `AccessibilityConnection::event_stream()` + abonnement
  `object:state-changed:focused` et `object:text-caret-moved`.

Cohabitation tokio ↔ tao : le spike tourne dans un runtime tokio current-thread
(`tokio::runtime::Builder::new_current_thread().enable_all().build()`,
`rt.block_on(async { ... })`). Pas de fenêtre → aucun conflit. (Pour l'intégration
finale, le module mettra ce runtime sur un **thread dédié** qui poste des
`UserEvent` via `EventLoopProxy`, en miroir des callbacks Win32.)

## Contrat de sortie du spike (stdout)

~30 s, poll 200 ms. Répond oui/non aux 4 questions :

- **(0) bus** → `SPIKE bus: connected name=<…>` (+ `a11y_enabled=<bool>` via
  `org.a11y.Status` si exposé) ou `FAIL bus: <err>`.  *(R4)*
- **(1+2) caret** → sur event focus : identifier l'app (remontée parent = `Code` ?),
  lire `caret_offset` + `get_character_extents(off, Screen)`. Affiche
  `SPIKE focus: app=<…> role=<…> caret_off=<n> extents(screen)= x= y= w= h=`
  ou `NO Text iface (role=<…>)`. La boucle 200 ms doit montrer que les extents
  **bougent** quand on tape (suivi réel ≠ valeur figée).  *(R3)*
- **(3) clavier** → `register_keystroke_listener([Échap], synchronous+preemptive)` ;
  appuyer Échap dans VSCode → `SPIKE key: CONSUMED` (improbable) ou
  `SPIKE key: NOT CONSUMED on Wayland` (attendu).  *(R1)*
- **(4) session** → lire `XDG_SESSION_TYPE` / `WAYLAND_DISPLAY` / `GDK_BACKEND` →
  `SPIKE session: type=<wayland|x11>`.  *(R2)*

**Critère de succès** : le spike tranche clairement les 4 questions. Un spike qui
affiche « tout KO » est un **succès** — il invalide l'approche active avant qu'on
écrive l'intégration.

## Squelette du spike (à compléter sous Linux)

```rust
#[cfg(target_os = "linux")]
mod active_mode_linux {
    use atspi::connection::AccessibilityConnection;
    use atspi::proxy::{accessible::AccessibleProxy, text::TextProxy};
    use atspi_common::CoordType;

    pub fn run_spike() {
        let rt = tokio::runtime::Builder::new_current_thread()
            .enable_all().build().expect("tokio rt");
        rt.block_on(async {
            // (4) session
            let st = std::env::var("XDG_SESSION_TYPE").unwrap_or_default();
            println!("SPIKE session: type={} wayland_display={} gdk_backend={}",
                st,
                std::env::var("WAYLAND_DISPLAY").unwrap_or_default(),
                std::env::var("GDK_BACKEND").unwrap_or_default());

            // (0) bus
            let conn = match AccessibilityConnection::new().await {
                Ok(c) => { println!("SPIKE bus: connected"); c }
                Err(e) => { println!("FAIL bus: {e}"); return; }
            };

            // (1+2) s'abonner à focus + text-caret-moved, lire extents au caret.
            //   - registry / event_stream() -> filtrer les events focus
            //   - sur l'émetteur : AccessibleProxy (role/name + remontée parent
            //     pour confirmer l'app "Code")
            //   - TextProxy::caret_offset() + get_character_extents(off, CoordType::Screen)
            //   - boucle 200 ms ~30 s : vérifier que (x,y,w,h) BOUGE quand on tape
            //   TODO: implémenter le flux d'events (cf. exemples odilia-app/atspi)

            // (3) clavier : DeviceEventControllerProxy::register_keystroke_listener
            //   ([Échap], EventListenerMode{ synchronous:true, preemptive:true, global:false })
            //   -> exposer un objet listener (zbus), attendre le callback.
            //   Imprimer CONSUMED / NOT CONSUMED selon que Échap agit encore dans VSCode.
            //   TODO: implémenter (API dépréciée -> probablement NOT CONSUMED)
        });
    }
}
```

Références d'implémentation : exemples du repo `odilia-app/atspi` (screen reader
Rust qui fait exactement ces abonnements focus/caret), docs.rs `atspi` 0.30 /
`atspi-proxies` 0.14.

## Vérification (sous Linux)

1. `cd rust && cargo build --release -p mc-popup` (vérifie que atspi/zbus/tokio
   compilent — **impossible sur Windows**).
2. Lancer VSCode avec l'a11y active : `ACCESSIBILITY_ENABLED=1 code` dans l'env de
   **VSCode** (pas seulement la popup), ou réglage `editor.accessibilitySupport: "on"`.
   Placer le curseur dans un éditeur de texte.
3. `./target/release/mc-popup --spike-linux`, taper du texte dans VSCode, appuyer Échap.
4. Lire les lignes `SPIKE …`. **Coller la sortie dans la session Claude** → on
   décide l'étape B.

## Étape B (conditionnelle au verdict du spike)

Selon les lignes `SPIKE …` :

1. **Mode actif partiel + XWayland** — si caret OK (R3) et session XWayland (R2) :
   caret via cache AT-SPI (thread async pousse les extents, event loop lit, jamais
   d'appel D-Bus bloquant dans le thread UI), positionnement via `GDK_BACKEND=x11`
   sur le process popup, clavier abandonné (R1) → piloté par l'extension.
2. **Repli passif** (contrat LibreOffice déjà existant) — l'extension calcule la
   position et relaie `nav`/`activate`/`close` en JSON. Bute quand même sur le
   positionnement (XWayland requis).
3. **Statu quo autocomplete** — si les murs sont infranchissables sur la config.

Câblage TS associé (`adapter-vscode/extension/src/popup.ts`), à activer **seulement
après** spike concluant : `isHelperAvailable()` → `'win32' || 'linux'` ; spawn avec
selon le verdict `env GDK_BACKEND=x11` (positionnement XWayland), `env
ACCESSIBILITY_ENABLED=1`, ou spawn **sans** `--active` (repli passif). Le binaire
`mc-popup` Linux est déjà copié dans le VSIX par `build.mjs` (aucune modif build
pour le spike).

## Avant de coder : ADR

Créer un ADR (Kind `Feat`, Température **provisoire**, `Statut: acté` + citation
utilisateur) actant le spike-first et documentant les murs Wayland R1/R2, puis
mettre à jour `docs/dev/decisions/README.md`. (Cf. process de décision CLAUDE.md.)
