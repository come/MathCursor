/**
 * Chat en direct du site : logique partagée (préfixe `_` → module NON routé).
 *
 * Importé par :
 *   - api/v1/chat/[[path]].js        (visiteur : status, send, contact, poll)
 *   - admin/api/chat/[[path]].js     (auteur, derrière le Basic Auth de /admin)
 *
 * Adapté du relais des démos (D:\DemosClient\hub\relay\core.mjs), en version
 * mono-personne : une seule ligne de présence, pas de jeton (Basic Auth), pas de
 * mesure d'audience, e-mail seulement (pas de téléphone : public souvent mineur),
 * purge des conversations 12 mois après leur dernier message.
 *
 * Bindings :
 *   - CHAT_DB (D1)      : conversations, messages, présence
 *   - NOTIFY  (service) : Worker mathcursor-notify (send_email), facultatif
 *
 * Cf. ADR 2026-11-02-Feat-site-live-chat.
 */

export const MAX_TEXT = 2000;
const MAX_BODY = 16_384;
export const ONLINE_MS = 75_000;          // un onglet admin en arrière-plan n'est réveillé qu'une fois par minute
const RETENTION_MS = 365 * 86_400_000;   // 12 mois après le dernier message
const NOTIFY_EVERY_MS = 15 * 60_000;     // au plus un e-mail par conversation toutes les 15 min
export const AUTHOR = 'Côme';

const AUTO_REPLY = {
  fr: "Merci pour votre message ! Je ne suis pas disponible en direct pour le moment. "
    + "Laissez votre e-mail juste en dessous : je vous réponds dès que possible.",
  en: "Thanks for your message! I'm not available live right now. "
    + "Leave your e-mail just below: I'll get back to you as soon as I can.",
};

export class HttpError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

const enc = new TextEncoder();
const hex = (buf) => [...new Uint8Array(buf)].map((b) => b.toString(16).padStart(2, '0')).join('');
export const sha256 = async (s) => hex(await crypto.subtle.digest('SHA-256', enc.encode(String(s))));
export const clean = (s, max = MAX_TEXT) => String(s ?? '').replace(/\u0000/g, '').trim().slice(0, max);
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

// ------------------------------------------------------------------ stockage (D1)

const SCHEMA = [
  `CREATE TABLE IF NOT EXISTS conversations (id TEXT PRIMARY KEY, session TEXT NOT NULL, page TEXT, lang TEXT,
    country TEXT, contact TEXT, created_at INTEGER, updated_at INTEGER, admin_read INTEGER DEFAULT 0,
    archived INTEGER DEFAULT 0, notified_at INTEGER DEFAULT 0)`,
  `CREATE TABLE IF NOT EXISTS messages (id INTEGER PRIMARY KEY AUTOINCREMENT, conv TEXT NOT NULL, sender TEXT NOT NULL,
    text TEXT NOT NULL, at INTEGER NOT NULL)`,
  'CREATE INDEX IF NOT EXISTS messages_conv ON messages (conv, id)',
  'CREATE TABLE IF NOT EXISTS presence (id TEXT PRIMARY KEY, available INTEGER, last_seen INTEGER)',
];

const stores = new WeakMap();
export function storeFor(env) {
  const db = env.CHAT_DB;
  if (!db) throw new HttpError(503, 'Chat non configuré (CHAT_DB)');
  if (!stores.has(db)) stores.set(db, d1Store(db));
  return stores.get(db);
}

function d1Store(db) {
  let ready = null;
  const init = () => (ready ??= db.batch(SCHEMA.map((s) => db.prepare(s))).catch((e) => { ready = null; throw e; }));
  const q = async (sql, ...args) => (await init(), db.prepare(sql).bind(...args));
  return {
    async createConv(c) {
      await (await q('INSERT INTO conversations (id, session, page, lang, country, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?)',
        c.id, c.session, c.page, c.lang, c.country, c.at, c.at)).run();
    },
    async getConv(id) {
      return (await q('SELECT * FROM conversations WHERE id = ?', id)).first();
    },
    async updateConv(id, fields) {
      const keys = Object.keys(fields);
      await (await q(`UPDATE conversations SET ${keys.map((k) => `${k} = ?`).join(', ')} WHERE id = ?`,
        ...keys.map((k) => fields[k]), id)).run();
    },
    async addMessage(m) {
      const r = await (await q('INSERT INTO messages (conv, sender, text, at) VALUES (?, ?, ?, ?) RETURNING id',
        m.conv, m.sender, m.text, m.at)).first();
      return { ...m, id: r.id };
    },
    async messages(conv, after = 0) {
      return (await (await q('SELECT id, sender, text, at FROM messages WHERE conv = ? AND id > ? ORDER BY id', conv, after)).all()).results;
    },
    async listConvs() {
      return (await (await q(`SELECT c.id, c.page, c.lang, c.country, c.contact, c.created_at, c.updated_at, c.admin_read, c.archived,
          (SELECT text FROM messages WHERE conv = c.id ORDER BY id DESC LIMIT 1) AS last_text,
          (SELECT sender FROM messages WHERE conv = c.id ORDER BY id DESC LIMIT 1) AS last_sender,
          (SELECT COUNT(*) FROM messages WHERE conv = c.id AND sender = 'visitor' AND id > c.admin_read) AS unread
        FROM conversations c ORDER BY c.updated_at DESC LIMIT 300`)).all()).results;
    },
    async presence() {
      return (await q("SELECT available, last_seen FROM presence WHERE id = 'author'")).first();
    },
    async setPresence(available, now) {
      await (await q(`INSERT INTO presence (id, available, last_seen) VALUES ('author', ?, ?)
        ON CONFLICT(id) DO UPDATE SET available = excluded.available, last_seen = excluded.last_seen`,
      available ? 1 : 0, now)).run();
    },
    async purge(before) {
      await init();
      await db.batch([
        db.prepare('DELETE FROM messages WHERE conv IN (SELECT id FROM conversations WHERE updated_at < ?)').bind(before),
        db.prepare('DELETE FROM conversations WHERE updated_at < ?').bind(before),
      ]);
    },
  };
}

export async function authorOnline(store, now = Date.now()) {
  const p = await store.presence();
  return !!p && !!p.available && now - p.last_seen < ONLINE_MS;
}

// ------------------------------------------------------------------ HTTP

export async function readJson(request) {
  const text = await request.text();
  if (text.length > MAX_BODY) throw new HttpError(413, 'Message trop long');
  try {
    return JSON.parse(text || '{}');
  } catch {
    throw new HttpError(400, 'JSON invalide');
  }
}

export function json(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' },
  });
}

/** Exécute une route ; HttpError → statut et message, le reste → 500 générique. */
export async function respond(fn) {
  try {
    return json(await fn());
  } catch (e) {
    const status = e instanceof HttpError ? e.status : 500;
    if (status >= 500) console.error(e);
    return json({ error: e instanceof HttpError ? e.message : 'Erreur serveur' }, status);
  }
}

/** Chemin de la route après le préfixe donné, sans slash final : « /send », « /admin/sync »… */
export function routeKey(request, prefix) {
  const path = new URL(request.url).pathname.replace(/\/+$/, '');
  return `${request.method} ${path.slice(path.indexOf(prefix) + prefix.length) || '/'}`;
}

// Limite de débit en mémoire : « au mieux » (plusieurs instances Cloudflare).
const hits = new Map();
export function rateLimit(key, max = 20, windowMs = 60_000) {
  const now = Date.now();
  const list = (hits.get(key) ?? []).filter((t) => now - t < windowMs);
  if (list.length >= max) throw new HttpError(429, 'Trop de messages, patientez une minute.');
  list.push(now);
  hits.set(key, list);
  if (hits.size > 5000) hits.clear();
}

function requireSession(session) {
  if (typeof session !== 'string' || !/^[a-f0-9]{32,64}$/.test(session)) throw new HttpError(400, 'Session invalide');
}

/** La conversation existe et appartient à cette session (empreinte du secret de session). */
async function ownConv(store, session, id) {
  requireSession(session);
  const conv = id ? await store.getConv(String(id)) : null;
  if (!conv || conv.session !== (await sha256(session))) throw new HttpError(403, 'Conversation inconnue');
  return conv;
}

const forVisitor = (m) => ({ n: m.id, id: `m${m.id}`, text: m.text, at: m.at, auto: m.sender === 'auto' });
const lang = (v) => (v === 'en' ? 'en' : 'fr');

/**
 * Alerte e-mail (Worker mathcursor-notify, liaison NOTIFY). Jamais bloquante :
 * sans liaison ou en cas d'erreur, le message est quand même enregistré.
 */
async function notify(env, store, conv, text, now) {
  if (!env.NOTIFY || now - (conv.notified_at || 0) < NOTIFY_EVERY_MS) return;
  try {
    await store.updateConv(conv.id, { notified_at: now });
    const res = await env.NOTIFY.fetch('https://notify/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ conv: conv.id, page: conv.page, contact: conv.contact, text: text.slice(0, 500) }),
    });
    if (!res.ok) console.error('notify', res.status, await res.text());
  } catch (e) {
    console.error('notify', e);
  }
}

// ------------------------------------------------------------------ routes visiteur

export const visitorRoutes = {
  /** Disponibilité de l'auteur (pastille du widget). */
  'GET /status': async ({ store }) => ({ online: await authorOnline(store), name: AUTHOR }),

  /** Message du visiteur. Crée la conversation au premier message. */
  'POST /send': async ({ request, store, env, ip, country, waitUntil }) => {
    const body = await readJson(request);
    requireSession(body.session);
    const text = clean(body.text);
    if (!text) throw new HttpError(400, 'Message vide');
    rateLimit(`v:${ip}`);
    const now = Date.now();
    let conv;
    if (body.root) {
      conv = await ownConv(store, body.session, body.root);
      await store.updateConv(conv.id, { updated_at: now, archived: 0 });
    } else {
      conv = {
        id: crypto.randomUUID(), session: await sha256(body.session),
        page: clean(body.page, 80) || null, lang: lang(body.lang), country: country || null, at: now,
      };
      await store.createConv(conv);
    }
    await store.addMessage({ conv: conv.id, sender: 'visitor', text, at: now });
    if (!(await authorOnline(store, now))) {
      // absent : une réponse automatique, une seule fois, tant qu'aucun e-mail n'a été laissé
      if (!conv.contact) {
        const already = (await store.messages(conv.id)).some((m) => m.sender === 'auto');
        if (!already) await store.addMessage({ conv: conv.id, sender: 'auto', text: AUTO_REPLY[lang(conv.lang)], at: now + 1 });
      }
      waitUntil(notify(env, store, conv, text, now));
    }
    return { root: conv.id };
  },

  /** E-mail laissé par le visiteur (jamais de téléphone). */
  'POST /contact': async ({ request, store, ip }) => {
    const body = await readJson(request);
    rateLimit(`v:${ip}`);
    const conv = await ownConv(store, body.session, body.root);
    const email = clean(body.email, 200);
    if (!email || !EMAIL_RE.test(email)) throw new HttpError(400, 'E-mail invalide');
    const now = Date.now();
    await store.updateConv(conv.id, { contact: email, updated_at: now });
    await store.addMessage({ conv: conv.id, sender: 'system', text: `E-mail laissé : ${email}`, at: now });
    return { ok: true };
  },

  /** Nouveaux messages de l'auteur après `after`, et disponibilité. */
  'GET /poll': async ({ request, store }) => {
    const url = new URL(request.url);
    const conv = await ownConv(store, url.searchParams.get('session'), url.searchParams.get('root'));
    const after = Number(url.searchParams.get('after') ?? 0) || 0;
    const list = (await store.messages(conv.id, after)).filter((m) => m.sender === 'admin' || m.sender === 'auto');
    return { messages: list.map(forVisitor), online: await authorOnline(store), name: AUTHOR };
  },
};

// ------------------------------------------------------------------ routes admin (derrière Basic Auth)

export const adminRoutes = {
  /**
   * Appelé toutes les quelques secondes par admin/chat.html : signale la présence, purge les
   * conversations de plus de 12 mois, renvoie la liste et, si `conv` est donné, son fil (lu si `read`).
   */
  'POST /sync': async ({ request, store }) => {
    const body = await readJson(request);
    const now = Date.now();
    await store.setPresence(body.available !== false, now);
    await store.purge(now - RETENTION_MS);
    let messages = null;
    if (body.conv) {
      const conv = await store.getConv(String(body.conv));
      if (conv) {
        messages = await store.messages(conv.id);
        const lastVisitor = messages.filter((m) => m.sender === 'visitor').at(-1);
        if (body.read && lastVisitor && lastVisitor.id > conv.admin_read) await store.updateConv(conv.id, { admin_read: lastVisitor.id });
      }
    }
    return { convs: await store.listConvs(), messages, online: body.available !== false };
  },

  'POST /reply': async ({ request, store }) => {
    const body = await readJson(request);
    const conv = await store.getConv(String(body.conv ?? ''));
    if (!conv) throw new HttpError(404, 'Conversation introuvable');
    const text = clean(body.text);
    if (!text) throw new HttpError(400, 'Message vide');
    const now = Date.now();
    const m = await store.addMessage({ conv: conv.id, sender: 'admin', text, at: now });
    await store.updateConv(conv.id, { updated_at: now });
    return { message: m };
  },

  'POST /archive': async ({ request, store }) => {
    const body = await readJson(request);
    await store.updateConv(String(body.conv ?? ''), { archived: body.archived === false ? 0 : 1 });
    return { ok: true };
  },

  /** Fermeture de l'onglet admin (sendBeacon) : passe « absent » tout de suite. */
  'POST /away': async ({ store }) => {
    await store.setPresence(false, Date.now());
    return { ok: true };
  },
};
