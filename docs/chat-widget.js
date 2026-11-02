/**
 * Chat en direct du site MathCursor (cf. ADR 2026-11-02-Feat-site-live-chat).
 *
 *   <script src="/chat-widget.js" defer></script>
 *
 * Adapté du widget des démos (D:\DemosClient\hub\widget) : styles isolés en
 * Shadow DOM, présence « en ligne / absent », réponse automatique qui demande un
 * e-mail quand l'auteur est absent. Différences : même origine (/api/v1/chat),
 * FR/EN selon la langue du site (attribut lang de <html>, suivi en direct),
 * e-mail seulement (pas de téléphone), aucune mesure d'audience.
 *
 * Pilotage : window.MCChat.open() / close() / toggle().
 * Si le chat n'est pas configuré côté serveur (503), le widget ne s'affiche pas.
 */
function mcChatBoot() {
  'use strict';
  if (window.MCChat || !window.fetch || !window.crypto || !document.body?.attachShadow) return;

  const API = '/api/v1/chat';
  const STATUS_EVERY = 30000;
  const POLL_OPEN = 4000;
  const POLL_CLOSED = 20000;
  const STORE_KEY = 'mc-chat';
  const NAME = 'Côme';

  const T = {
    fr: {
      role: 'créateur de MathCursor',
      online: 'en ligne', away: 'absent',
      miniOnline: 'En ligne · vous répond directement', miniAway: 'Absent · laissez un message',
      hello: (on) => `Bonjour ! Je suis Côme, créateur de MathCursor. Une question sur l'installation, une formule qui ne passe pas, une idée ? ${on ? 'Écrivez-moi ici : je suis en ligne et je vous réponds directement.' : 'Écrivez-moi ici et laissez votre e-mail : je vous réponds dès que possible.'}`,
      teaser: (on) => `Une question sur MathCursor ? ${on ? 'Je vous réponds directement.' : 'Laissez-moi un message.'}`,
      placeholder: 'Votre message…', send: 'Envoyer', minimize: 'Réduire', write: 'Écrivez votre message…',
      openLabel: 'Ouvrir le chat avec Côme, créateur de MathCursor', panelLabel: 'Discuter avec le créateur de MathCursor',
      mailHint: 'Laissez votre e-mail (facultatif) si vous préférez une réponse par e-mail',
      mailAsk: 'Votre e-mail, pour recevoir la réponse',
      mailBad: 'Cette adresse e-mail semble incomplète.', mailFail: 'Envoi impossible, réessayez.',
      mailDone: (c) => `C'est noté, je vous réponds à ${c}`, edit: 'modifier',
      failed: "Échec de l'envoi · Réessayer", auto: 'Réponse automatique',
      legal: 'Vos messages ne sont lus que par moi, pour vous répondre, et effacés au bout de 12 mois. <a href="/privacy.html" target="_blank">Confidentialité</a>',
      quick: ["Je n'arrive pas à installer MathCursor", 'Une formule ne s’affiche pas comme je veux', 'Je suis prof, une question'],
      locale: 'fr-FR',
    },
    en: {
      role: 'creator of MathCursor',
      online: 'online', away: 'away',
      miniOnline: 'Online · replies directly', miniAway: 'Away · leave a message',
      hello: (on) => `Hi! I'm Côme, the creator of MathCursor. A question about installing, a formula that won't come out right, an idea? ${on ? "Write to me here: I'm online and will reply directly." : "Write to me here and leave your e-mail: I'll get back to you as soon as I can."}`,
      teaser: (on) => `A question about MathCursor? ${on ? "I'll reply directly." : 'Leave me a message.'}`,
      placeholder: 'Your message…', send: 'Send', minimize: 'Minimize', write: 'Write your message…',
      openLabel: 'Open the chat with Côme, creator of MathCursor', panelLabel: 'Chat with the creator of MathCursor',
      mailHint: 'Leave your e-mail (optional) if you prefer a reply by e-mail',
      mailAsk: 'Your e-mail, to receive the reply',
      mailBad: 'This e-mail address looks incomplete.', mailFail: 'Could not send, please try again.',
      mailDone: (c) => `Got it, I'll reply to ${c}`, edit: 'edit',
      failed: 'Not sent · Retry', auto: 'Automatic reply',
      legal: 'Only I read your messages, to reply to you; they are deleted after 12 months. <a href="/privacy.html" target="_blank">Privacy</a>',
      quick: ["I can't install MathCursor", "A formula doesn't come out right", "I'm a teacher, a question"],
      locale: 'en-GB',
    },
  };
  const lang = () => (document.documentElement.lang === 'en' ? 'en' : 'fr');
  const t = () => T[lang()];

  // ------------------------------------------------------------------ petits outils DOM
  function h(sel, attrs, ...children) {
    const [tagId, ...classes] = sel.split('.');
    const [tag, id] = tagId.split('#');
    const el = document.createElement(tag);
    if (id) el.id = id;
    if (classes.length) el.className = classes.join(' ');
    for (const [k, v] of Object.entries(attrs || {})) {
      if (v === undefined || v === null || v === false) continue;
      if (k.startsWith('on') && typeof v === 'function') el.addEventListener(k.slice(2), v);
      else el.setAttribute(k, v === true ? '' : String(v));
    }
    for (const c of children) if (c !== null && c !== undefined && c !== false) el.append(c);
    return el;
  }
  const PATHS = {
    chevron: '<path d="m9 6 6 6-6 6"/>',
    check: '<path d="m5 12 5 5 9-10"/>',
    mail: '<rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/>',
    send: '<path d="M4 12 20 4l-6 16-3-7z"/><path d="m11 13 9-9"/>',
    chat: '<path d="M4 5h16v11H9l-5 4z"/><path d="M8 9.5h8M8 12.5h5"/>',
  };
  function icon(name, size) {
    const s = size || 18;
    const wrap = document.createElement('span');
    wrap.innerHTML = `<svg xmlns="http://www.w3.org/2000/svg" width="${s}" height="${s}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${PATHS[name]}</svg>`;
    return wrap.firstElementChild;
  }

  // ------------------------------------------------------------------ état (persisté par navigateur)
  const state = (() => {
    try {
      const raw = JSON.parse(localStorage.getItem(STORE_KEY) || 'null');
      if (raw && raw.session) return raw;
    } catch (e) { /* stockage indisponible : conversation non persistée */ }
    const bytes = crypto.getRandomValues(new Uint8Array(16));
    return { session: [...bytes].map((b) => b.toString(16).padStart(2, '0')).join(''), root: null, after: 0, messages: [], contactSent: false };
  })();
  const save = () => { try { localStorage.setItem(STORE_KEY, JSON.stringify(state)); } catch (e) { /* ignoré */ } };

  // ------------------------------------------------------------------ structure
  const host = h('div#mc-chat');
  host.hidden = true; // affiché seulement si le serveur répond
  const shadow = host.attachShadow({ mode: 'open' });
  let online = false;

  const list = h('div.list', { role: 'log', 'aria-live': 'polite' });
  const input = h('textarea.input', { rows: '1', maxlength: '2000' });
  const sendBtn = h('button.send', { type: 'submit' }, icon('send', 18));
  const form = h('form.form', {}, input, sendBtn);
  const chips = h('div.chips');
  const mailInput = h('input.mail-input', { type: 'email', placeholder: 'E-mail', autocomplete: 'email' });
  const mailLabel = h('span');
  const mailRow = h('div.mail', {}, icon('mail', 15),
    h('span.mail-body', {}, mailLabel, h('span.mail-fields', {}, mailInput, h('button.mail-ok', { type: 'button', onclick: () => saveMail() }, 'OK'))));
  const mailDone = h('p.mail-done', { hidden: true });
  const avatar = () => h('span.avatar', {}, 'C', h('i.online'));
  const headAvatar = h('span.slot');
  const headName = h('b', {}, NAME);
  const headStatus = h('small');
  const minBtn = h('button.icon-btn.min', { type: 'button', onclick: () => close() }, icon('chevron', 17));
  const legal = h('p.legal');
  const panel = h('section.panel', { hidden: true },
    h('header.head', {}, headAvatar, h('span.who', {}, headName, headStatus), minBtn),
    list, chips, mailRow, mailDone, form, legal);

  const miniAvatar = h('span.slot');
  const miniStatus = h('small');
  const teaser = h('p.mini-msg');
  const miniWrite = h('span');
  const badge = h('span.badge', { hidden: true });
  const launcher = h('div.launcher', { role: 'button', tabindex: '0' },
    h('div.mini-head', {}, miniAvatar, h('span.who', {}, h('b', {}, NAME), miniStatus), h('span.up', { 'aria-hidden': 'true' }, icon('chevron', 15))),
    teaser,
    h('div.mini-input', { 'aria-hidden': 'true' }, miniWrite, h('span.send', {}, icon('send', 16))),
    h('span.fab', { 'aria-hidden': 'true' }, icon('chat', 22)),
    badge);
  launcher.addEventListener('click', () => open());
  launcher.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); open(); }
  });
  shadow.append(h('style', {}, CSS), panel, launcher);
  document.body.append(host);

  // ------------------------------------------------------------------ rendu
  /** Textes dépendant de la langue (rappelé quand le visiteur change FR/EN). */
  function applyLang() {
    const L = t();
    input.placeholder = L.placeholder;
    input.setAttribute('aria-label', L.placeholder);
    sendBtn.setAttribute('aria-label', L.send);
    minBtn.setAttribute('aria-label', L.minimize);
    minBtn.title = L.minimize;
    mailInput.setAttribute('aria-label', 'E-mail');
    miniWrite.textContent = L.write;
    launcher.setAttribute('aria-label', L.openLabel);
    panel.setAttribute('aria-label', L.panelLabel);
    legal.innerHTML = L.legal;
    chips.replaceChildren(...L.quick.map((q) => h('button.chip', { type: 'button', onclick: () => send(q) }, q)));
    setOnline(online);
  }

  function setOnline(v) {
    online = v;
    const L = t();
    for (const el of [panel, launcher]) el.classList.toggle('away', !v);
    headAvatar.replaceChildren(avatar());
    miniAvatar.replaceChildren(avatar());
    headStatus.textContent = `${L.role} · ${v ? L.online : L.away}`;
    miniStatus.replaceChildren(h('i.dot'), v ? L.miniOnline : L.miniAway);
    if (!unread) teaser.textContent = L.teaser(v);
    if (!panel.hidden) render();
  }

  const fmtTime = (at) => (at ? new Date(at).toLocaleTimeString(t().locale, { hour: '2-digit', minute: '2-digit' }) : '');
  function bubble(m) {
    const el = h(`div.msg.${m.from}`, {},
      h('p', {}, m.text),
      h('span.meta', {}, [m.from === 'team' ? (m.auto ? t().auto : 'Côme') : '', fmtTime(m.at)].filter(Boolean).join(' · ')));
    if (m.failed) {
      el.classList.add('failed');
      el.append(h('button.link', { type: 'button', onclick: () => retry(m) }, t().failed));
    }
    return el;
  }

  function render() {
    const L = t();
    list.replaceChildren(bubble({ from: 'team', text: L.hello(online), at: 0 }), ...state.messages.map(bubble));
    chips.hidden = state.messages.some((m) => m.from === 'me' && !m.failed);
    mailRow.hidden = !!state.contact;
    mailDone.hidden = !state.contact;
    const asked = !state.contact && state.messages.some((m) => m.auto);
    mailRow.classList.toggle('ask', asked);
    if (!mailLabel.dataset.error) mailLabel.textContent = asked ? L.mailAsk : L.mailHint;
    if (state.contact) {
      mailDone.replaceChildren(icon('check', 14), h('span', {}, L.mailDone(state.contact)),
        h('button.link', { type: 'button', onclick: () => { mailInput.value = state.contact; state.contact = undefined; save(); render(); mailInput.focus(); } }, L.edit));
    }
    list.scrollTop = list.scrollHeight;
  }

  // ------------------------------------------------------------------ réseau
  async function call(path, body) {
    const res = await fetch(`${API}${path}`, body === undefined ? undefined
      : { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) throw Object.assign(new Error(data.error || `Erreur ${res.status}`), { status: res.status });
    return data;
  }

  let sendingContact = false;
  async function sendContact() {
    if (!state.contact || !state.root || state.contactSent || sendingContact) return;
    sendingContact = true;
    try {
      await call('/contact', { session: state.session, root: state.root, email: state.contact });
      state.contactSent = true;
      save();
      render();
    } catch (err) {
      mailLabel.dataset.error = '1';
      mailLabel.textContent = err.message || t().mailFail;
    } finally {
      sendingContact = false;
    }
  }

  function saveMail() {
    const email = mailInput.value.trim();
    if (!email) return;
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
      mailLabel.dataset.error = '1';
      mailLabel.textContent = t().mailBad;
      return;
    }
    delete mailLabel.dataset.error;
    state.contact = email;
    state.contactSent = false;
    save();
    render();
    sendContact();
  }
  mailInput.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') { e.preventDefault(); saveMail(); if (state.contact) input.focus(); }
  });

  async function deliver(m, retried) {
    try {
      const data = await call('/send', { session: state.session, root: state.root, text: m.text, page: location.pathname, lang: lang() });
      state.root = data.root;
      m.failed = false;
      sendContact();
      setTimeout(poll, 300);
    } catch (err) {
      // conversation inconnue du serveur (purgée) : on en ouvre une nouvelle
      if (err.status === 403 && state.root && !retried) {
        state.root = null;
        state.after = 0;
        state.contactSent = false;
        return deliver(m, true);
      }
      m.failed = true;
    }
    save();
    render();
  }

  function send(text) {
    const s = String(text || '').trim();
    if (!s) return;
    if (mailInput.value.trim() && !state.contact) saveMail();
    const m = { id: `m${Date.now()}`, from: 'me', text: s, at: Date.now() };
    state.messages.push(m);
    save();
    render();
    deliver(m);
  }
  function retry(m) {
    m.failed = false;
    render();
    deliver(m);
  }

  let timer = 0;
  let unread = 0;
  function schedule() {
    clearTimeout(timer);
    timer = setTimeout(poll, !state.root ? STATUS_EVERY : panel.hidden ? POLL_CLOSED : POLL_OPEN);
  }
  async function poll() {
    clearTimeout(timer);
    try {
      if (!state.root) {
        const s = await call('/status');
        setOnline(!!s.online);
      } else {
        const q = new URLSearchParams({ session: state.session, root: state.root, after: String(state.after) });
        const data = await call(`/poll?${q}`);
        setOnline(!!data.online);
        if (data.messages.length) state.after = Math.max(state.after, ...data.messages.map((m) => m.n));
        const known = new Set(state.messages.map((m) => m.id));
        const fresh = data.messages.filter((m) => !known.has(m.id));
        if (fresh.length) {
          for (const m of fresh) state.messages.push({ id: m.id, from: 'team', auto: !!m.auto, text: m.text, at: m.at });
          render();
          if (panel.hidden) setUnread(unread + fresh.length);
        }
        save();
      }
      if (host.hidden) {
        host.hidden = false;
        setTimeout(() => launcher.classList.add('show'), 800);
      }
    } catch (err) {
      // chat non configuré côté serveur : on retire le widget, sans bruit
      if (err.status === 503 || err.status === 404) { host.remove(); return; }
      if (err.status === 403) { state.root = null; save(); }
    }
    schedule();
  }

  function setUnread(n) {
    unread = n;
    badge.hidden = !n;
    badge.textContent = String(n);
    const last = [...state.messages].reverse().find((m) => m.from === 'team');
    teaser.textContent = n && last ? last.text : t().teaser(online);
    launcher.classList.toggle('unread', !!n);
  }

  // ------------------------------------------------------------------ interactions
  form.addEventListener('submit', (e) => {
    e.preventDefault();
    send(input.value);
    input.value = '';
    input.style.height = '';
  });
  input.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); form.requestSubmit(); }
  });
  input.addEventListener('input', () => {
    input.style.height = '';
    input.style.height = `${Math.min(input.scrollHeight, 120)}px`;
  });
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && !panel.hidden) close();
  });

  function open() {
    panel.hidden = false;
    launcher.hidden = true;
    host.setAttribute('open', '');
    setUnread(0);
    render();
    poll();
    requestAnimationFrame(() => input.focus());
  }
  function close() {
    panel.hidden = true;
    launcher.hidden = false;
    host.removeAttribute('open');
    schedule();
  }

  // langue du site changée (boutons FR/EN) : textes du widget suivis en direct
  new MutationObserver(() => { applyLang(); if (!panel.hidden) render(); })
    .observe(document.documentElement, { attributes: true, attributeFilter: ['lang'] });

  window.MCChat = { open, close, toggle: () => (panel.hidden ? open() : close()) };
  applyLang();
  render();
  poll();
}

// Styles isolés (Shadow DOM), couleurs du site. Placement réglable par la page :
// --mc-chat-right, --mc-chat-bottom, --mc-chat-z.
var CSS = `
:host { all: initial; --accent: #4d7cff; --accent-ink: #2f5be0; --accent-soft: #eef3ff; --ink: #16233a; --ink-2: #263246;
  --muted: #5a6578; --line: #e2e7f0; --line-2: #cfd6e3; --panel: #fff;
  --right: var(--mc-chat-right, 20px); --bottom: var(--mc-chat-bottom, 16px);
  font-family: "Inter", system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; font-size: 14px; line-height: 1.45; color: var(--ink); }
* { box-sizing: border-box; }
p { margin: 0; }
button { font: inherit; cursor: pointer; }
[hidden] { display: none !important; }
svg { display: block; }
a { color: inherit; }
button:focus-visible, a:focus-visible, input:focus-visible, [tabindex]:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
.avatar { width: 40px; height: 40px; border-radius: 50%; flex: none; display: grid; place-items: center; position: relative; background: var(--ink); color: #fff; font-weight: 700; font-size: 15px; }
.avatar .online { position: absolute; right: -1px; bottom: -1px; width: 11px; height: 11px; border-radius: 50%; background: #34d27b; border: 2px solid var(--panel); }
.slot { display: contents; }
.icon-btn { border: 0; background: transparent; width: 30px; height: 30px; border-radius: 8px; display: grid; place-items: center; color: var(--muted); flex: none; }
.icon-btn:hover { background: rgba(22,35,58,.06); color: var(--ink); }
.link { border: 0; background: none; color: var(--accent-ink); font-size: 12.5px; font-weight: 600; display: inline-flex; gap: 5px; align-items: center; padding: 2px 0; }
.panel { position: fixed; right: var(--right); bottom: var(--bottom); z-index: var(--mc-chat-z, 2147483000); width: 380px; height: min(600px, calc(100dvh - 100px));
  display: flex; flex-direction: column; background: var(--panel); border: 1px solid var(--line); border-radius: 18px;
  box-shadow: 0 2px 6px rgba(22,35,58,.08), 0 24px 60px rgba(22,35,58,.22); overflow: hidden; animation: chat-in .22s ease-out; }
@keyframes chat-in { from { opacity: 0; transform: translateY(12px) scale(.98); } }
.head { display: flex; align-items: center; gap: 12px; padding: 14px 12px 14px 16px; border-bottom: 1px solid var(--line); }
.who { flex: 1; min-width: 0; display: flex; flex-direction: column; }
.who b { font-size: 14.5px; }
.who small { color: var(--muted); font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.min svg { transform: rotate(90deg); }
.list { flex: 1; overflow-y: auto; padding: 16px; display: flex; flex-direction: column; gap: 10px; background: #f6f8fc; }
.msg { max-width: 85%; display: flex; flex-direction: column; gap: 3px; }
.msg p { padding: 10px 13px; border-radius: 16px; font-size: 13.5px; line-height: 1.45; white-space: pre-wrap; overflow-wrap: anywhere; }
.msg.team { align-self: flex-start; }
.msg.team p { background: var(--panel); border: 1px solid var(--line); border-bottom-left-radius: 5px; }
.msg.me { align-self: flex-end; align-items: flex-end; }
.msg.me p { background: var(--accent); color: #fff; border-bottom-right-radius: 5px; }
.msg.failed p { opacity: .55; }
.msg .link { font-size: 12px; color: #c4332f; }
.meta { font-size: 11px; color: var(--muted); padding: 0 4px; }
.chips { display: flex; flex-wrap: wrap; gap: 6px; padding: 10px 14px 0; }
.chip { border: 1px solid #b8c9ff; background: var(--panel); color: var(--accent-ink); border-radius: 999px; padding: 6px 11px; font-size: 12.5px; font-weight: 550; text-align: left; }
.chip:hover { background: var(--accent-soft); }
.form { display: flex; align-items: flex-end; gap: 8px; margin: 10px 12px 6px; padding: 6px 6px 6px 12px; border: 1px solid var(--line-2); border-radius: 16px; }
.form:focus-within { border-color: var(--accent); box-shadow: 0 0 0 3px var(--accent-soft); }
.input { flex: 1; border: 0; resize: none; font: inherit; font-size: 14px; line-height: 1.4; padding: 6px 0; max-height: 120px; outline: none; background: transparent; color: var(--ink); }
.send { flex: none; width: 36px; height: 36px; border-radius: 11px; border: 0; background: var(--accent); color: #fff; display: grid; place-items: center; }
.send:hover { background: var(--accent-ink); }
.legal { font-size: 11px; color: var(--muted); text-align: center; padding: 0 14px 10px; }
.mail { display: flex; gap: 10px; align-items: flex-start; margin: 10px 12px 0; padding: 9px 11px; border-radius: 13px; background: rgba(22,35,58,.04); color: var(--muted); }
.mail > svg { flex: none; margin-top: 2px; }
.mail-body { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 5px; font-size: 12px; line-height: 1.35; }
.mail-fields { display: flex; gap: 8px; align-items: center; }
.mail-input { flex: 1; min-width: 0; font: inherit; font-size: 13px; color: var(--ink); border: 0; border-bottom: 1px solid var(--line-2); background: transparent; padding: 3px 0; outline: none; }
.mail-input:focus { border-bottom-color: var(--accent); }
.mail-ok { flex: none; height: 26px; padding: 0 10px; border: 0; border-radius: 8px; background: var(--accent); color: #fff; font-size: 12px; font-weight: 600; }
.mail.ask { background: var(--accent-soft); color: var(--accent-ink); box-shadow: inset 0 0 0 1px #b8c9ff; }
.mail.ask .mail-body { font-weight: 600; }
.mail-done { display: flex; align-items: center; gap: 6px; margin: 10px 14px 0; font-size: 12px; color: var(--ink-2); }
.mail-done svg { color: #1f8a5b; flex: none; }
.mail-done .link { font-size: 12px; margin-left: auto; }
.away .avatar .online, .away .dot { background: #a5adb8 !important; }
.launcher { position: fixed; right: var(--right); bottom: var(--bottom); z-index: var(--mc-chat-z, 2147483000); width: 300px;
  display: flex; flex-direction: column; gap: 10px; padding: 12px; cursor: pointer; border: 1px solid var(--line); border-radius: 18px; background: var(--panel);
  box-shadow: 0 2px 6px rgba(22,35,58,.08), 0 16px 40px rgba(22,35,58,.18);
  opacity: 0; transform: translateY(14px); pointer-events: none; transition: opacity .35s, transform .35s, box-shadow .2s; }
.launcher.show { opacity: 1; transform: none; pointer-events: auto; }
.launcher:hover { box-shadow: 0 0 0 3px var(--accent-soft), 0 16px 40px rgba(22,35,58,.2); }
.mini-head { display: flex; align-items: center; gap: 10px; }
.mini-head .avatar { width: 36px; height: 36px; font-size: 13px; }
.mini-head .who b { font-size: 13.5px; }
.mini-head .who small { display: inline-flex; align-items: center; gap: 5px; }
.dot { width: 6px; height: 6px; border-radius: 50%; background: #34d27b; flex: none; }
.up { display: grid; place-items: center; width: 28px; height: 28px; border-radius: 9px; background: var(--accent-soft); color: var(--accent-ink); flex: none; }
.up svg { transform: rotate(-90deg); }
.mini-msg { display: none; padding: 9px 12px; border-radius: 14px 14px 14px 5px; background: var(--accent-soft); font-size: 13px; font-weight: 550; line-height: 1.45; color: var(--ink);
  -webkit-line-clamp: 3; -webkit-box-orient: vertical; overflow: hidden; }
.launcher.unread .mini-msg { display: -webkit-box; }
.mini-input { display: flex; align-items: center; justify-content: space-between; gap: 8px; padding: 5px 5px 5px 12px; border: 1px solid var(--line-2); border-radius: 13px; color: var(--muted); font-size: 13px; }
.mini-input .send { width: 30px; height: 30px; border-radius: 9px; }
.launcher:hover .mini-input { border-color: var(--accent); }
.fab { display: none; }
.badge { position: absolute; top: -7px; right: -7px; min-width: 21px; height: 21px; padding: 0 6px; border-radius: 11px; background: #e5484d; color: #fff; font-size: 11.5px; font-weight: 700; display: grid; place-items: center; border: 2px solid #fff; }
@media (max-width: 860px) {
  .panel { inset: 0; width: auto; height: 100dvh; border-radius: 0; border: 0; }
  .launcher { width: auto; right: 12px; bottom: 14px; padding: 0; border: 0; border-radius: 50%; background: none; box-shadow: none; }
  .launcher > :not(.fab):not(.badge) { display: none !important; }
  .fab { display: grid; place-items: center; width: 52px; height: 52px; border-radius: 50%; background: var(--accent); color: #fff; box-shadow: 0 8px 22px rgba(77,124,255,.4); }
}
`;

if (document.body) mcChatBoot();
else document.addEventListener("DOMContentLoaded", mcChatBoot);
