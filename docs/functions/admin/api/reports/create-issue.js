/**
 * POST /admin/api/reports/create-issue?id=<UUID>
 *
 * Crée une issue GitHub à partir d'un rapport "Signaler une erreur" stocké
 * dans R2. Renvoie {ok: true, url: "https://github.com/..."} en succès.
 *
 * Configuration côté Cloudflare Pages → Settings → Environment variables :
 *   - GITHUB_TOKEN : PAT (fine-grained de préférence) avec scope « Issues:write »
 *                   sur le repo cible, ou classic PAT avec scope « repo ».
 *   - GITHUB_REPO  : owner/repo, ex: "come/MathCursor"
 *
 * Si l'une des 2 vars manque, on retourne 503 explicite.
 *
 * Auth : middleware /admin/_middleware.js gère le Basic Auth en amont.
 */

export async function onRequestPost({ request, env }) {
  if (!env.REPORTS_BUCKET) {
    return jsonError('REPORTS_BUCKET non bindé.', 503);
  }
  if (!env.GITHUB_TOKEN || !env.GITHUB_REPO) {
    return jsonError('GITHUB_TOKEN ou GITHUB_REPO non configuré côté Pages.', 503);
  }
  const id = new URL(request.url).searchParams.get('id');
  if (!id || !/^[a-f0-9-]{8,40}$/i.test(id)) {
    return jsonError('id manquant ou invalide.', 400);
  }

  // Charge le report depuis R2
  const key = await findKey(env, id);
  if (!key) return jsonError('Report introuvable.', 404);
  const obj = await env.REPORTS_BUCKET.get(key);
  if (!obj) return jsonError('Report introuvable (race).', 404);
  let report;
  try {
    report = JSON.parse(await obj.text());
  } catch (e) {
    return jsonError('Report JSON corrompu.', 500);
  }

  // Construit le titre + body markdown de l'issue
  const { title, body } = buildIssueContent(report, id);

  // POST GitHub Issues API
  const ghUrl = `https://api.github.com/repos/${env.GITHUB_REPO}/issues`;
  const ghResp = await fetch(ghUrl, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${env.GITHUB_TOKEN}`,
      Accept: 'application/vnd.github+json',
      'X-GitHub-Api-Version': '2022-11-28',
      'Content-Type': 'application/json',
      'User-Agent': 'mathcursor-admin',
    },
    body: JSON.stringify({
      title,
      body,
      labels: ['user-report'],
    }),
  });

  if (!ghResp.ok) {
    const detail = (await ghResp.text()).slice(0, 500);
    return jsonError(`GitHub API ${ghResp.status} : ${detail}`, 502);
  }
  const issue = await ghResp.json();
  return jsonOk({ ok: true, url: issue.html_url, number: issue.number });
}

async function findKey(env, id) {
  let cursor = undefined;
  const target = `${id}.json`;
  while (true) {
    const res = await env.REPORTS_BUCKET.list({ prefix: 'reports/', limit: 1000, cursor });
    for (const o of (res.objects || [])) {
      if (o.key.endsWith(target)) return o.key;
    }
    if (!res.truncated) return null;
    cursor = res.cursor;
  }
}

/**
 * Construit le titre court (1ère ligne du commentaire user, fallback source) et
 * le body markdown structuré pour faciliter le triage.
 */
function buildIssueContent(report, id) {
  const comment = (report.user_comment || '').trim();
  const source = (report.source_text || '').trim();
  const proposed = (report.proposed_latex || '').trim();
  const committed = (report.committed_latex || '').trim();
  const meta = report.metadata || {};
  const server = report._server || {};

  // Titre : 1ère ligne du commentaire, tronqué à 80 chars. Fallback source.
  let title;
  if (comment) {
    title = comment.split('\n')[0].slice(0, 80);
    if (comment.length > 80) title += '…';
  } else if (source) {
    title = '[report] ' + source.slice(0, 70);
  } else {
    title = `[report] ${id.slice(0, 8)}`;
  }

  // Body markdown
  const lines = [];
  lines.push('> Issue créée automatiquement depuis un rapport « Signaler une erreur ».');
  lines.push(`> Reçu le ${server.received_at || report.ts || '?'} depuis ${server.cf_country || '??'}/${server.cf_colo || '??'}.`);
  lines.push('');
  if (comment) {
    lines.push('## Commentaire utilisateur');
    lines.push('');
    lines.push(comment);
    lines.push('');
  }
  lines.push('## Dernière action');
  lines.push('');
  lines.push('**Ce qu\'a tapé l\'utilisateur :**');
  lines.push('```');
  lines.push(source || '(vide)');
  lines.push('```');
  lines.push('');
  lines.push('**Ce que MathCursor a proposé (LaTeX) :**');
  lines.push('```latex');
  lines.push(proposed || '(vide)');
  lines.push('```');
  lines.push('');
  lines.push('**Ce qui (serait) inséré dans Word :**');
  lines.push('```');
  lines.push(committed || '(vide — pas de commit)');
  lines.push('```');
  lines.push('');
  if (report.paragraph_context) {
    lines.push('## Paragraphe Word (contexte)');
    lines.push('');
    lines.push('```');
    lines.push(report.paragraph_context);
    lines.push('```');
    lines.push('');
  }
  lines.push('## Métadonnées');
  lines.push('');
  lines.push('| Champ | Valeur |');
  lines.push('|---|---|');
  lines.push(`| Version add-in | \`${report.version || '?'}\` |`);
  lines.push(`| Word | \`${meta.word_version || '?'}\` |`);
  lines.push(`| OS | \`${meta.os_version || '?'}\` |`);
  lines.push(`| .NET | \`${meta.dotnet_version || '?'}\` |`);
  lines.push(`| Report ID | \`${id}\` |`);
  lines.push('');
  if (report.log_tail) {
    lines.push('<details><summary>Log technique</summary>');
    lines.push('');
    lines.push('```');
    lines.push(report.log_tail.slice(-8000)); // GH issue body max ~65k chars
    lines.push('```');
    lines.push('');
    lines.push('</details>');
  }
  lines.push('');
  lines.push('---');
  lines.push(`Voir le rapport complet (avec screenshot si joint) dans le backoffice admin.`);

  return { title, body: lines.join('\n') };
}

function jsonOk(body) {
  return new Response(JSON.stringify(body), {
    status: 200, headers: { 'Content-Type': 'application/json' },
  });
}
function jsonError(msg, status = 500) {
  return new Response(JSON.stringify({ ok: false, error: msg }), {
    status, headers: { 'Content-Type': 'application/json' },
  });
}
