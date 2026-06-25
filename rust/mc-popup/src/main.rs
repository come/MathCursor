// MathCursor — coquille popup webview (wry + tao). Process persistant piloté par
// lignes JSON sur stdin ; events JSON sur stdout. Fenêtre borderless, topmost,
// non-activante. Rendu KaTeX local (offline).
//
// DEUX MODES :
//  - PASSIF (défaut, LibreOffice) : coords écran fournies dans `show`, clavier
//    géré par le host ; la coquille ne renvoie que les clics souris.
//  - ACTIF (`--active`, VSCode) : la coquille LIT le caret elle-même (MSAA
//    OBJID_CARET) et CAPTE le clavier (hook bas niveau WH_KEYBOARD_LL :
//    ↑↓/Entrée/Tab/Échap), car VSCode n'expose ni la position du caret ni
//    l'interception clavier. Reproduit le helper WPF caret-popup.
//
// argv : chemin du index.html (1er arg non-drapeau) ; `--active` pour le mode actif.
//
// stdin  : {"cmd":"show","candidates":[{"latex":".."}],"x":,"y":,"lineHeight":,"selectedIndex":}
//          {"cmd":"update","selectedIndex":N} | {"cmd":"close"} | {"cmd":"quit"}
// stdout : {"evt":"ready"} | {"evt":"commit","index":i} | {"evt":"dismiss"} | {"evt":"error","msg":..}

use std::borrow::Cow;
use std::io::{BufRead, Write};
use std::path::PathBuf;
use std::thread;

use tao::dpi::LogicalSize;
#[cfg(not(target_os = "windows"))]
use tao::dpi::PhysicalPosition;
use tao::event::Event;
use tao::event_loop::{ControlFlow, EventLoopBuilder};
use tao::window::WindowBuilder;
use wry::http::Response;
use wry::WebViewBuilder;
#[cfg(target_os = "windows")]
use tao::platform::windows::WindowExtWindows;

#[derive(Debug)]
enum UserEvent {
    Show { candidates: String, x: f64, y: f64, sel: i64, theme: String,
        col_delta: i64, font_size: f64, reposition: bool, show_latex: bool },
    Update { sel: i64 },
    Close,
    Quit,
    Ipc(String),
    // Mode actif (hook clavier) :
    Nav(i64),   // -1 / +1
    KeyCommit,
    KeyDismiss,
}

fn emit(obj: &str) {
    let mut out = std::io::stdout();
    let _ = writeln!(out, "{}", obj);
    let _ = out.flush();
}

fn json_escape(s: &str) -> String {
    serde_json::Value::String(s.to_string()).to_string()
}

// Cache la popup. En mode actif (Windows) la fenêtre est gérée par SetWindowPos
// (tao ne la « connaît » pas visible) → on cache via Win32 ShowWindow.
fn hide_popup(window: &tao::window::Window) {
    // Windows : SW_HIDE (cohérent avec l'affichage SetWindowPos, pour les 2 modes).
    #[cfg(target_os = "windows")]
    {
        active_mode::set_visible(false);
        active_mode::hide(window.hwnd() as isize);
    }
    #[cfg(not(target_os = "windows"))]
    window.set_visible(false);
}

fn serve_root(html_arg: &Option<String>) -> PathBuf {
    html_arg
        .clone()
        .map(PathBuf::from)
        .and_then(|p| p.parent().map(|d| d.to_path_buf()))
        .or_else(|| {
            std::env::current_exe()
                .ok()
                .and_then(|p| p.parent().map(|d| d.join("web")))
        })
        .unwrap_or_default()
}

fn guess_mime(p: &str) -> &'static str {
    if p.ends_with(".html") { "text/html; charset=utf-8" }
    else if p.ends_with(".css") { "text/css; charset=utf-8" }
    else if p.ends_with(".js") { "text/javascript; charset=utf-8" }
    else if p.ends_with(".woff2") { "font/woff2" }
    else if p.ends_with(".json") { "application/json" }
    else { "application/octet-stream" }
}

fn main() -> wry::Result<()> {
    // args : 1er non-drapeau = html ; --active = mode actif.
    let mut html_arg: Option<String> = None;
    let mut active = false;
    for a in std::env::args().skip(1) {
        if a == "--active" { active = true; }
        else if html_arg.is_none() { html_arg = Some(a); }
    }

    #[cfg(target_os = "windows")]
    {
        let mut p = std::env::temp_dir();
        p.push("mathcursor-popup-webview");
        std::env::set_var("WEBVIEW2_USER_DATA_FOLDER", p);
    }

    let event_loop = EventLoopBuilder::<UserEvent>::with_user_event().build();
    let proxy = event_loop.create_proxy();

    // Thread lecteur stdin -> UserEvent.
    let rdr_proxy = proxy.clone();
    thread::spawn(move || {
        let stdin = std::io::stdin();
        for line in stdin.lock().lines() {
            let line = match line { Ok(l) => l, Err(_) => break };
            let line = line.trim();
            if line.is_empty() { continue; }
            let v: serde_json::Value = match serde_json::from_str(line) {
                Ok(v) => v,
                Err(e) => { emit(&format!("{{\"evt\":\"error\",\"msg\":{}}}", json_escape(&format!("bad json: {}", e)))); continue; }
            };
            let cmd = v.get("cmd").and_then(|c| c.as_str()).unwrap_or("");
            let ev = match cmd {
                "show" => UserEvent::Show {
                    candidates: v.get("candidates").map(|c| c.to_string()).unwrap_or_else(|| "[]".into()),
                    x: v.get("x").and_then(|n| n.as_f64()).unwrap_or(0.0),
                    y: v.get("y").and_then(|n| n.as_f64()).unwrap_or(0.0)
                        + v.get("lineHeight").and_then(|n| n.as_f64()).unwrap_or(0.0),
                    sel: v.get("selectedIndex").and_then(|n| n.as_i64()).unwrap_or(0),
                    theme: v.get("theme").and_then(|t| t.as_str()).unwrap_or("auto").to_string(),
                    col_delta: v.get("colDelta").and_then(|n| n.as_i64()).unwrap_or(0),
                    font_size: v.get("fontSize").and_then(|n| n.as_f64()).unwrap_or(14.0),
                    reposition: v.get("reposition").and_then(|b| b.as_bool()).unwrap_or(true),
                    show_latex: v.get("showLatex").and_then(|b| b.as_bool()).unwrap_or(true),
                },
                "update" => UserEvent::Update { sel: v.get("selectedIndex").and_then(|n| n.as_i64()).unwrap_or(0) },
                "close" => UserEvent::Close,
                "quit" => UserEvent::Quit,
                _ => continue,
            };
            if rdr_proxy.send_event(ev).is_err() { break; }
        }
        let _ = rdr_proxy.send_event(UserEvent::Quit);
    });

    // Mode actif : capture le HWND de l'hôte (VSCode, au 1er plan AVANT notre
    // fenêtre) et installe le hook clavier global (Windows).
    #[cfg(target_os = "windows")]
    if active {
        active_mode::capture_owner();
        active_mode::install_keyboard_hook(proxy.clone());
        active_mode::start_foreground_watch(proxy.clone());
    }

    let window = WindowBuilder::new()
        .with_decorations(false)
        .with_always_on_top(true)
        .with_focused(false)
        .with_visible(false)
        .with_inner_size(LogicalSize::new(200.0, 80.0))
        .build(&event_loop)
        .unwrap();

    // NOACTIVATE (jamais voler le focus) + coins carrés : pour les DEUX modes
    // (VSCode actif ET LibreOffice passif). La popup vit À CÔTÉ de l'éditeur.
    #[cfg(target_os = "windows")]
    {
        let h = window.hwnd() as isize;
        active_mode::make_noactivate(h);
        active_mode::square_corners(h);
    }

    let root = serve_root(&html_arg);
    let ipc_proxy = proxy.clone();
    let webview = WebViewBuilder::new(&window)
        .with_custom_protocol("mc".into(), move |request| {
            let path = request.uri().path().trim_start_matches('/').to_string();
            let rel = if path.is_empty() { "index.html" } else { &path };
            match std::fs::read(root.join(rel)) {
                Ok(bytes) => Response::builder()
                    .header("Content-Type", guess_mime(rel))
                    .header("Access-Control-Allow-Origin", "*")
                    .body(Cow::Owned(bytes))
                    .unwrap(),
                Err(_) => Response::builder().status(404).body(Cow::Owned(Vec::new())).unwrap(),
            }
        })
        .with_url("mc://localhost/index.html")
        .with_ipc_handler(move |msg| { let _ = ipc_proxy.send_event(UserEvent::Ipc(msg.body().to_string())); })
        .with_initialization_script(
            "window.mcCommit=function(i){window.ipc.postMessage('commit:'+i)};\
             window.mcDismiss=function(){window.ipc.postMessage('dismiss')};",
        )
        .build()?;

    // État mode actif (géré sur le thread UI).
    let mut count: i64 = 0;   // nb de candidats affichés
    let mut sel: i64 = -1;    // -1 = aucun surligné (nav-mode opt-in)
    let mut engaged = false;  // 1re flèche pressée ?

    event_loop.run(move |event, _, control_flow| {
        *control_flow = ControlFlow::Wait;
        match event {
            Event::UserEvent(UserEvent::Show { candidates, x, y, sel: s, theme, col_delta, font_size, reposition, show_latex }) => {
                count = serde_json::from_str::<serde_json::Value>(&candidates)
                    .ok().and_then(|v| v.as_array().map(|a| a.len() as i64)).unwrap_or(0);
                sel = s;
                engaged = s >= 0;
                let _ = webview.evaluate_script(&format!(
                    "window.mcRender({},{},\"{}\",{})", candidates, sel, theme, show_latex));
                if active {
                    // Mode actif : on lit le caret (MSAA) et on ancre au DÉBUT du
                    // texte reconnu = caretX − colDelta × largeur_char. Figé tant que
                    // la zone ne change pas (reposition=false) → pas de dérive.
                    // La fenêtre est placée+dimensionnée+affichée par SetWindowPos au
                    // message "size:".
                    #[cfg(target_os = "windows")]
                    {
                        if reposition {
                            if let Some((cx, cy)) = active_mode::caret_screen_pos() {
                                let cw = font_size * 0.6 * window.scale_factor();
                                let tx = (cx - col_delta as f64 * cw).max(0.0);
                                active_mode::set_target(tx as i32, cy as i32);
                            }
                        }
                        // Affiche seulement si on a une position (caret absent au
                        // 1er coup → pas de show en (0,0) ; on resync l'extension).
                        if active_mode::has_target() {
                            active_mode::set_visible(true);
                        } else {
                            emit("{\"evt\":\"closed\"}");
                        }
                    }
                } else {
                    // Mode passif (LibreOffice) : coords écran fournies par l'hôte.
                    // Sur Windows on emprunte le MÊME chemin SetWindowPos que l'actif
                    // (tao set_inner_size reste sans effet → fenêtre non ajustée).
                    #[cfg(target_os = "windows")]
                    {
                        active_mode::set_target(x as i32, y as i32);
                        active_mode::set_visible(true);
                        // placé + dimensionné + affiché au message "size:".
                    }
                    #[cfg(not(target_os = "windows"))]
                    {
                        window.set_outer_position(PhysicalPosition::new(x, y));
                        window.set_visible(true);
                    }
                }
            }
            Event::UserEvent(UserEvent::Update { sel: s }) => {
                sel = s;
                let _ = webview.evaluate_script(&format!("window.mcSetIndex({})", sel));
            }
            Event::UserEvent(UserEvent::Nav(delta)) => {
                if count > 0 {
                    if !engaged { engaged = true; sel = 0; }
                    else { sel = (sel + delta).clamp(0, count - 1); }
                    let _ = webview.evaluate_script(&format!("window.mcSetIndex({})", sel));
                }
            }
            Event::UserEvent(UserEvent::KeyCommit) => {
                let idx = if engaged && sel >= 0 { sel } else { 0 };
                hide_popup(&window);
                emit(&format!("{{\"evt\":\"commit\",\"index\":{}}}", idx));
            }
            Event::UserEvent(UserEvent::KeyDismiss) => {
                hide_popup(&window);
                emit("{\"evt\":\"dismiss\"}");
            }
            Event::UserEvent(UserEvent::Close) => {
                hide_popup(&window);
            }
            Event::UserEvent(UserEvent::Quit) => {
                #[cfg(target_os = "windows")]
                active_mode::uninstall_hook();
                *control_flow = ControlFlow::Exit;
            }
            Event::UserEvent(UserEvent::Ipc(body)) => {
                if body == "loaded" {
                    emit("{\"evt\":\"ready\"}");
                } else if let Some(rest) = body.strip_prefix("size:") {
                    if let Ok(v) = serde_json::from_str::<serde_json::Value>(rest) {
                        let w = v.get("w").and_then(|n| n.as_f64()).unwrap_or(200.0);
                        let h = v.get("h").and_then(|n| n.as_f64()).unwrap_or(80.0);
                        // Win32 SetWindowPos (place + dimensionne + affiche, pixels
                        // physiques) pour les DEUX modes — déterministe, contrairement
                        // à tao set_inner_size resté sans effet.
                        #[cfg(target_os = "windows")]
                        { let _ = active; active_mode::place(window.hwnd() as isize, w as i32 + 2, h as i32 + 2); }
                        #[cfg(not(target_os = "windows"))]
                        { let _ = active; window.set_inner_size(LogicalSize::new(w + 2.0, h + 2.0)); }
                    }
                } else if let Some(rest) = body.strip_prefix("commit:") {
                    emit(&format!("{{\"evt\":\"commit\",\"index\":{}}}", rest));
                } else if body == "dismiss" {
                    emit("{\"evt\":\"dismiss\"}");
                }
            }
            _ => {}
        }
    });
}

// ───────────────────────── mode actif (Windows) ─────────────────────────
#[cfg(target_os = "windows")]
mod active_mode {
    use std::sync::atomic::{AtomicBool, Ordering};
    use std::sync::Mutex;
    use tao::event_loop::EventLoopProxy;
    use super::UserEvent;

    use windows::core::{Interface, GUID, VARIANT};
    use windows::Win32::Foundation::{HWND, LPARAM, LRESULT, WPARAM};
    use windows::Win32::Graphics::Dwm::{
        DwmSetWindowAttribute, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_DONOTROUND,
        DWM_WINDOW_CORNER_PREFERENCE,
    };
    use windows::Win32::UI::Accessibility::{AccessibleObjectFromWindow, IAccessible};
    use windows::Win32::UI::Input::KeyboardAndMouse::{
        GetKeyState, VK_CONTROL, VK_DOWN, VK_ESCAPE, VK_RETURN, VK_TAB, VK_UP,
    };
    use windows::Win32::UI::WindowsAndMessaging::{
        CallNextHookEx, GetForegroundWindow, GetWindowLongPtrW, SetWindowLongPtrW,
        SetWindowPos, SetWindowsHookExW, ShowWindow, UnhookWindowsHookEx, GWL_EXSTYLE, HHOOK, HWND_TOPMOST,
        KBDLLHOOKSTRUCT, LLKHF_ALTDOWN, SET_WINDOW_POS_FLAGS, SWP_NOACTIVATE,
        SWP_SHOWWINDOW, SW_HIDE, WH_KEYBOARD_LL, WM_KEYDOWN, WM_SYSKEYDOWN,
        WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW,
    };

    const OBJID_CARET: u32 = 0xFFFF_FFF8;

    static PROXY: Mutex<Option<EventLoopProxy<UserEvent>>> = Mutex::new(None);
    static VISIBLE: AtomicBool = AtomicBool::new(false);
    static OWNER: Mutex<isize> = Mutex::new(0);   // HWND de VSCode
    static TARGET_POS: Mutex<(i32, i32)> = Mutex::new((0, 0)); // écran, sous le caret
    static HOOK: Mutex<isize> = Mutex::new(0);    // HHOOK clavier (pour désinstaller)

    pub fn has_target() -> bool { *TARGET_POS.lock().unwrap() != (0, 0) }

    pub fn uninstall_hook() {
        let h = { *HOOK.lock().unwrap() };
        if h != 0 {
            unsafe { let _ = UnhookWindowsHookEx(HHOOK(h as *mut core::ffi::c_void)); }
            *HOOK.lock().unwrap() = 0;
        }
    }

    pub fn set_visible(v: bool) { VISIBLE.store(v, Ordering::SeqCst); }
    pub fn set_target(x: i32, y: i32) { *TARGET_POS.lock().unwrap() = (x, y); }

    /// Cache la fenêtre (Win32) — cohérent avec l'affichage par SetWindowPos.
    pub fn hide(hwnd: isize) {
        unsafe { let _ = ShowWindow(HWND(hwnd as *mut core::ffi::c_void), SW_HIDE); }
    }

    /// Place + dimensionne + affiche la fenêtre en une fois (Win32, pixels
    /// physiques) — déterministe, contrairement à tao set_inner_size.
    pub fn place(hwnd: isize, w: i32, h: i32) {
        if !VISIBLE.load(Ordering::SeqCst) { return; } // pas de show si caché (ex. caret absent)
        unsafe {
            let (x, y) = *TARGET_POS.lock().unwrap();
            let _ = SetWindowPos(
                HWND(hwnd as *mut core::ffi::c_void), HWND_TOPMOST,
                x, y, w, h,
                SET_WINDOW_POS_FLAGS(SWP_NOACTIVATE.0 | SWP_SHOWWINDOW.0),
            );
        }
    }

    /// Mémorise la fenêtre au premier plan (= VSCode) au démarrage, avant que la
    /// popup n'existe. On lit le caret SUR CE HWND (pas GetForegroundWindow, qui
    /// pourrait renvoyer la popup).
    pub fn capture_owner() {
        unsafe { *OWNER.lock().unwrap() = GetForegroundWindow().0 as isize; }
    }

    /// Cache la popup dès que VSCode n'est plus au premier plan (Alt+Tab vers une
    /// autre appli). La popup étant NOACTIVATE, le 1er plan reste VSCode tant qu'on
    /// l'utilise ; s'il change, on ferme.
    pub fn start_foreground_watch(proxy: EventLoopProxy<UserEvent>) {
        std::thread::spawn(move || loop {
            std::thread::sleep(std::time::Duration::from_millis(200));
            if VISIBLE.load(Ordering::SeqCst) {
                let fg = unsafe { GetForegroundWindow().0 as isize };
                let owner = *OWNER.lock().unwrap();
                if fg != owner {
                    VISIBLE.store(false, Ordering::SeqCst);
                    super::emit("{\"evt\":\"closed\"}"); // resync l'extension
                    let _ = proxy.send_event(UserEvent::Close);
                }
            }
        });
    }

    /// WS_EX_NOACTIVATE + TOOLWINDOW : la popup ne prend jamais le focus.
    pub fn make_noactivate(hwnd: isize) {
        unsafe {
            let h = HWND(hwnd as *mut core::ffi::c_void);
            let ex = GetWindowLongPtrW(h, GWL_EXSTYLE);
            SetWindowLongPtrW(h, GWL_EXSTYLE,
                ex | WS_EX_NOACTIVATE.0 as isize | WS_EX_TOOLWINDOW.0 as isize);
        }
    }

    /// Coins carrés (désactive l'arrondi Win11) — DWMWA_WINDOW_CORNER_PREFERENCE.
    pub fn square_corners(hwnd: isize) {
        unsafe {
            let h = HWND(hwnd as *mut core::ffi::c_void);
            let pref: DWM_WINDOW_CORNER_PREFERENCE = DWMWCP_DONOTROUND;
            let _ = DwmSetWindowAttribute(
                h, DWMWA_WINDOW_CORNER_PREFERENCE,
                &pref as *const _ as *const core::ffi::c_void,
                core::mem::size_of::<DWM_WINDOW_CORNER_PREFERENCE>() as u32,
            );
        }
    }

    pub fn install_keyboard_hook(proxy: EventLoopProxy<UserEvent>) {
        if let Ok(mut g) = PROXY.lock() { *g = Some(proxy); }
        // Hook bas-niveau servi par la pompe de messages du thread UI (tao).
        unsafe {
            if let Ok(h) = SetWindowsHookExW(WH_KEYBOARD_LL, Some(hook_proc), None, 0) {
                if let Ok(mut g) = HOOK.lock() { *g = h.0 as isize; }
            }
        }
    }

    fn post(ev: UserEvent) {
        // lock().ok() (jamais unwrap dans le hook : un Mutex empoisonné ferait
        // paniquer le callback FFI).
        if let Ok(g) = PROXY.lock() {
            if let Some(p) = g.as_ref() { let _ = p.send_event(ev); }
        }
    }

    unsafe extern "system" fn hook_proc(code: i32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
        if code >= 0 && VISIBLE.load(Ordering::SeqCst) {
            let msg = wparam.0 as u32;
            if msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN {
                let kb = &*(lparam.0 as *const KBDLLHOOKSTRUCT);
                let vk = kb.vkCode;
                let alt = (kb.flags.0 & LLKHF_ALTDOWN.0) != 0;
                let ctrl = (GetKeyState(VK_CONTROL.0 as i32) as u16 & 0x8000) != 0;
                if !alt && !ctrl {
                    if vk == VK_UP.0 as u32 { post(UserEvent::Nav(-1)); return LRESULT(1); }
                    if vk == VK_DOWN.0 as u32 { post(UserEvent::Nav(1)); return LRESULT(1); }
                    if vk == VK_RETURN.0 as u32 || vk == VK_TAB.0 as u32 { post(UserEvent::KeyCommit); return LRESULT(1); }
                    if vk == VK_ESCAPE.0 as u32 { post(UserEvent::KeyDismiss); return LRESULT(1); }
                }
            }
        }
        CallNextHookEx(HHOOK(std::ptr::null_mut()), code, wparam, lparam)
    }

    /// Position ÉCRAN (pixels physiques) du caret de la fenêtre au premier plan
    /// (VSCode) via MSAA OBJID_CARET. Renvoie (x, y_bas-du-caret).
    pub fn caret_screen_pos() -> Option<(f64, f64)> {
        unsafe {
            let owner = *OWNER.lock().unwrap();
            if owner == 0 { return None; }
            let hwnd = HWND(owner as *mut core::ffi::c_void);
            let iid: GUID = IAccessible::IID;
            let mut pacc: Option<IAccessible> = None;
            let hr = AccessibleObjectFromWindow(
                hwnd, OBJID_CARET, &iid,
                &mut pacc as *mut _ as *mut *mut core::ffi::c_void,
            );
            if hr.is_err() { return None; }
            let acc = pacc?;
            // CHILDID_SELF = VARIANT VT_I4(0).
            let child = VARIANT::from(0i32);
            let (mut l, mut t, mut w, mut h) = (0i32, 0i32, 0i32, 0i32);
            acc.accLocation(&mut l, &mut t, &mut w, &mut h, &child).ok()?;
            if l == 0 && t == 0 && w == 0 && h == 0 { return None; }
            Some((l as f64, (t + h) as f64))
        }
    }
}
