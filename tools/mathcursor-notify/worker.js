// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme de Percin
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

/**
 * Worker mathcursor-notify : envoie un e-mail à l'auteur quand un visiteur écrit
 * dans le chat du site et que l'auteur n'est pas en ligne.
 *
 * Sans adresse publique (workers_dev = false) : appelé uniquement par le projet
 * Pages `mathcursor` via la liaison de service NOTIFY (functions/_chat.js).
 *
 * Bindings / variables :
 *   - MAIL (send_email)  : Email Routing de mathcursor.com
 *   - FROM (var)         : expéditeur sur le domaine, ex. chat@mathcursor.com
 *   - TO   (secret)      : adresse de l'auteur, vérifiée dans Email Routing
 *                          (secret : jamais dans le dépôt, qui est public)
 *
 * Cf. ADR 2026-11-02-Feat-site-live-chat.
 */
import { EmailMessage } from 'cloudflare:email';

const ADMIN_URL = 'https://mathcursor.com/admin/chat.html';

/** En-tête encodé RFC 2047 (accents dans le sujet). */
const encodeHeader = (s) => `=?UTF-8?B?${b64(s)}?=`;
function b64(s) {
  const bytes = new TextEncoder().encode(s);
  let bin = '';
  for (const b of bytes) bin += String.fromCharCode(b);
  return btoa(bin);
}
/** Corps base64 coupé à 76 caractères par ligne (RFC 2045). */
const wrap = (s) => s.replace(/.{1,76}/g, '$&\r\n');

function mime({ from, to, subject, text }) {
  return [
    `From: MathCursor <${from}>`,
    `To: <${to}>`,
    `Subject: ${encodeHeader(subject)}`,
    `Message-ID: <${crypto.randomUUID()}@mathcursor.com>`,
    `Date: ${new Date().toUTCString()}`,
    'MIME-Version: 1.0',
    'Content-Type: text/plain; charset=UTF-8',
    'Content-Transfer-Encoding: base64',
    '',
    wrap(b64(text)),
  ].join('\r\n');
}

export default {
  async fetch(request, env) {
    if (request.method !== 'POST') return new Response('Not found', { status: 404 });
    if (!env.MAIL || !env.TO || !env.FROM) return new Response('Notify non configuré (MAIL, TO, FROM)', { status: 503 });
    let body;
    try {
      body = await request.json();
    } catch {
      return new Response('JSON invalide', { status: 400 });
    }
    const text = String(body.text ?? '').slice(0, 500);
    const lines = [
      'Nouveau message sur le chat de mathcursor.com :',
      '',
      text,
      '',
      body.page ? `Page : ${String(body.page).slice(0, 80)}` : null,
      body.contact ? `E-mail laissé : ${String(body.contact).slice(0, 200)}` : 'Pas encore d\'e-mail laissé.',
      '',
      `Répondre : ${ADMIN_URL}#${encodeURIComponent(String(body.conv ?? ''))}`,
    ].filter((l) => l !== null);
    const raw = mime({ from: env.FROM, to: env.TO, subject: 'Chat MathCursor : nouveau message', text: lines.join('\n') });
    try {
      await env.MAIL.send(new EmailMessage(env.FROM, env.TO, raw));
    } catch (e) {
      return new Response(`Envoi impossible : ${e.message}`, { status: 502 });
    }
    return new Response('ok');
  },
};
