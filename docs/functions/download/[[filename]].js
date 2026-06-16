/**
 * Route /download/:filename
 *
 * Deux responsabilités :
 *   1. Log la tentative de download dans Analytics Engine (binding DOWNLOADS)
 *      pour avoir les stats par fichier, par jour, par pays, par user-agent.
 *   2. Stream le fichier depuis R2 (binding RELEASES) vers le client.
 *
 * Alias :
 *   /download/latest.exe → version définie par LATEST_VERSION ci-dessous.
 *   Quand on release 0.4.0, on bump cette constante et on redéploie.
 *   (Plus tard on pourra stocker ça dans un KV ou un json dans le bucket.)
 *
 * Range requests (resume) supportés : R2 gère les ranges nativement.
 */

const LATEST_VERSION = "0.10.3";
const LATEST_FILENAME = `MathCursor-Setup-${LATEST_VERSION}.exe`;

export async function onRequestGet(context) {
  const { env, request, params } = context;
  const urlFilename = Array.isArray(params.filename) ? params.filename.join("/") : params.filename;

  // Résolution alias : "latest.exe" → fichier versionné courant.
  const resolved = urlFilename === "latest.exe" ? LATEST_FILENAME : urlFilename;

  // Hygiène chemin : uniquement des noms plats, pas de subfolders ou ".." (R2 n'est
  // pas un FS hierarchique strict, mais autant refuser les patterns suspects).
  if (!/^[A-Za-z0-9._-]+\.exe$/.test(resolved)) {
    return new Response("Not found", { status: 404 });
  }

  // Support des Range requests pour permettre la reprise de download côté client.
  const rangeHeader = request.headers.get("range");
  const rangeOpt = rangeHeader ? parseRange(rangeHeader) : undefined;

  let r2Object;
  try {
    r2Object = rangeOpt
      ? await env.RELEASES.get(resolved, { range: rangeOpt })
      : await env.RELEASES.get(resolved);
  } catch (err) {
    return new Response("Upstream error", { status: 502 });
  }
  if (!r2Object) return new Response("Not found", { status: 404 });

  // Log dans Analytics Engine si le binding est présent. writeDataPoint est
  // fire-and-forget. Si DOWNLOADS n'est pas bindé (ex. AE pas encore activé
  // côté compte), on saute silencieusement.
  if (env.DOWNLOADS && typeof env.DOWNLOADS.writeDataPoint === "function") {
    try {
      env.DOWNLOADS.writeDataPoint({
        blobs: [
          resolved,
          urlFilename,                                           // ce qu'a tapé le client (latest.exe ou versionné)
          request.cf?.country || "??",
          request.cf?.colo || "??",
          (request.headers.get("user-agent") || "").slice(0, 200),
          request.headers.get("referer") || "",
        ],
        doubles: [r2Object.size || 0],
        indexes: [resolved],
      });
    } catch { /* analytics ne doit jamais bloquer le download */ }
  }

  const headers = new Headers();
  headers.set("Content-Type", "application/octet-stream");
  headers.set("Content-Disposition", `attachment; filename="${resolved}"`);
  headers.set("Cache-Control", "public, max-age=3600");
  if (r2Object.httpEtag) headers.set("ETag", r2Object.httpEtag);

  if (rangeOpt && r2Object.range) {
    const { offset, length } = r2Object.range;
    const end = offset + length - 1;
    headers.set("Content-Range", `bytes ${offset}-${end}/${r2Object.size}`);
    headers.set("Content-Length", String(length));
    headers.set("Accept-Ranges", "bytes");
    return new Response(r2Object.body, { status: 206, headers });
  }

  headers.set("Content-Length", String(r2Object.size));
  headers.set("Accept-Ranges", "bytes");
  return new Response(r2Object.body, { status: 200, headers });
}

/**
 * Parse minimal d'un Range header "bytes=X-Y". Renvoie un objet R2 range
 * ou undefined si le header est malformé (dans ce cas on sert le fichier entier).
 */
function parseRange(rangeHeader) {
  const m = /^bytes=(\d+)-(\d*)$/.exec(rangeHeader.trim());
  if (!m) return undefined;
  const offset = parseInt(m[1], 10);
  if (m[2] === "") return { offset };
  const end = parseInt(m[2], 10);
  if (end < offset) return undefined;
  return { offset, length: end - offset + 1 };
}

// HEAD : same metadata, no body. Utile pour les tools qui veulent vérifier la taille.
export async function onRequestHead(context) {
  const { env, params } = context;
  const urlFilename = Array.isArray(params.filename) ? params.filename.join("/") : params.filename;
  const resolved = urlFilename === "latest.exe" ? LATEST_FILENAME : urlFilename;
  if (!/^[A-Za-z0-9._-]+\.exe$/.test(resolved)) return new Response(null, { status: 404 });
  const obj = await env.RELEASES.head(resolved);
  if (!obj) return new Response(null, { status: 404 });
  const headers = new Headers();
  headers.set("Content-Type", "application/octet-stream");
  headers.set("Content-Length", String(obj.size));
  headers.set("Accept-Ranges", "bytes");
  if (obj.httpEtag) headers.set("ETag", obj.httpEtag);
  return new Response(null, { status: 200, headers });
}
