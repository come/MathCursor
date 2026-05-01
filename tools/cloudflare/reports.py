#!/usr/bin/env python3
"""
MathCursor — backoffice local pour les rapports "Signaler une erreur".

Lit le bucket R2 mathcursor-reports (cf. docs/functions/api/v1/report.js et
brief 2026-04-30-feedback-form-with-cloudflare-backend.md).

Usage :
  python tools/cloudflare/reports.py                  # idem `today`
  python tools/cloudflare/reports.py today            # rapports d'aujourd'hui
  python tools/cloudflare/reports.py list [PREFIX]    # rapports sous reports/PREFIX/ (défaut : tous)
  python tools/cloudflare/reports.py last N           # N derniers (par date de réception)
  python tools/cloudflare/reports.py show ID          # affiche le JSON formaté d'un rapport
  python tools/cloudflare/reports.py get ID           # télécharge JSON + PNG dans reports-local/ID/
  python tools/cloudflare/reports.py html [DAYS]      # dashboard local reports-local/index.html (défaut 30 j)
  python tools/cloudflare/reports.py delete ID        # supprime JSON + PNG du bucket (RGPD)

Prérequis :
  - ~/.mathcursor/cloudflare.env avec CLOUDFLARE_API_TOKEN + CLOUDFLARE_ACCOUNT_ID
  - Token avec scope « Workers R2 Storage : Edit » (Edit inclut Read).
  - Pas de dépendance Python externe : urllib + json natifs.
  - Pour get/delete : npx wrangler dispo (déjà setup pour ce projet).
"""

from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
import urllib.parse
import urllib.request
from datetime import datetime, timedelta, timezone
from html import escape as html_escape
from pathlib import Path
from typing import Iterator


# ============================================================================
# Setup env
# ============================================================================

ROOT = Path(__file__).resolve().parents[2]
ENV_FILE = Path.home() / ".mathcursor" / "cloudflare.env"
LOCAL_DIR = ROOT / "reports-local"
BUCKET = "mathcursor-reports"


def load_env() -> dict[str, str]:
    """Lit le fichier env (KEY=VALUE par ligne, # commentaires)."""
    if not ENV_FILE.exists():
        sys.exit(f"ERREUR : {ENV_FILE} introuvable.")
    env = {}
    for line in ENV_FILE.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        if "=" not in line:
            continue
        k, _, v = line.partition("=")
        # Strip un éventuel `export ` préfixe et les guillemets autour de la valeur
        k = k.strip().removeprefix("export ").strip()
        v = v.strip().strip('"').strip("'")
        env[k] = v
    return env


ENV = load_env()
TOKEN = ENV.get("CLOUDFLARE_API_TOKEN") or os.environ.get("CLOUDFLARE_API_TOKEN")
ACCOUNT_ID = ENV.get("CLOUDFLARE_ACCOUNT_ID") or os.environ.get("CLOUDFLARE_ACCOUNT_ID")
if not TOKEN or not ACCOUNT_ID:
    sys.exit(f"ERREUR : CLOUDFLARE_API_TOKEN ou CLOUDFLARE_ACCOUNT_ID manquant dans {ENV_FILE}.")

API = f"https://api.cloudflare.com/client/v4/accounts/{ACCOUNT_ID}/r2/buckets/{BUCKET}/objects"


# ============================================================================
# R2 via API REST
# ============================================================================

def _api_get(prefix: str = "", cursor: str = "") -> dict:
    """GET /objects avec pagination par cursor."""
    qs = {"per_page": "1000"}
    if prefix:
        qs["prefix"] = prefix
    if cursor:
        qs["cursor"] = cursor
    url = f"{API}?{urllib.parse.urlencode(qs)}"
    req = urllib.request.Request(url, headers={"Authorization": f"Bearer {TOKEN}"})
    with urllib.request.urlopen(req, timeout=30) as resp:
        body = resp.read().decode("utf-8")
    data = json.loads(body)
    if not data.get("success"):
        sys.exit(f"ERREUR API : {body}")
    return data


def list_objects(prefix: str = "") -> Iterator[dict]:
    """Yield {key, size, last_modified, etag, ...} pour chaque objet sous `prefix`."""
    cursor = ""
    while True:
        data = _api_get(prefix, cursor)
        for obj in data.get("result", []):
            yield obj
        cursor = (data.get("result_info") or {}).get("cursor") or ""
        if not cursor:
            return


def list_keys(prefix: str = "") -> Iterator[str]:
    for obj in list_objects(prefix):
        key = obj.get("key")
        if key:
            yield key


def _wrangler_cmd(*args: str) -> str:
    """Construit une ligne de commande wrangler avec quotes safe pour shell."""
    quoted = " ".join(f'"{a}"' if (" " in a or "/" in a) else a for a in args)
    return f"npx --yes wrangler {quoted}"


def _wrangler_env() -> dict[str, str]:
    """Env enrichie avec les credentials CF (wrangler les exige en mode
    non-interactif)."""
    sub_env = os.environ.copy()
    sub_env["CLOUDFLARE_API_TOKEN"] = TOKEN
    sub_env["CLOUDFLARE_ACCOUNT_ID"] = ACCOUNT_ID
    return sub_env


def fetch_object_bytes(key: str) -> bytes:
    """Télécharge le contenu d'un objet via wrangler (binaire-safe).
    Utilise shell=True : sous Windows npx est `npx.cmd`, et `shell=False` ne
    le résout pas systématiquement depuis Python."""
    cmd = _wrangler_cmd("r2", "object", "get", f"{BUCKET}/{key}", "--remote", "--pipe")
    res = subprocess.run(cmd, shell=True, capture_output=True, env=_wrangler_env())
    if res.returncode != 0:
        stderr = res.stderr.decode("utf-8", errors="replace")[:500]
        raise RuntimeError(f"wrangler get failed: {stderr}")
    return res.stdout


def delete_object(key: str) -> None:
    cmd = _wrangler_cmd("r2", "object", "delete", f"{BUCKET}/{key}", "--remote")
    res = subprocess.run(cmd, shell=True, capture_output=True, env=_wrangler_env())
    if res.returncode != 0:
        stderr = res.stderr.decode("utf-8", errors="replace")[:500]
        raise RuntimeError(f"wrangler delete failed: {stderr}")


def find_json_key(id_query: str) -> str | None:
    """Trouve la key R2 du JSON pour un id (UUID partiel ou complet)."""
    for key in list_keys("reports/"):
        if key.endswith(".json") and id_query in key:
            return key
    return None


# ============================================================================
# Sous-commandes CLI
# ============================================================================

def cmd_today(_args: list[str]) -> None:
    today = datetime.now().strftime("%Y-%m-%d")
    print(f"=== Rapports du {today} ===")
    count = 0
    for obj in list_objects(f"reports/{today}/"):
        if not obj["key"].endswith(".json"):
            continue
        count += 1
        rid = obj["key"].rsplit("/", 1)[-1].removesuffix(".json")
        print(f"{obj.get('last_modified', '?')}  {rid}  ({obj['size']}B)  {obj['key']}")
    print(f"\nTotal : {count}")


def cmd_list(args: list[str]) -> None:
    prefix = args[0] if args else "reports/"
    print(f"=== Rapports sous {prefix} ===")
    count = 0
    for obj in list_objects(prefix):
        if not obj["key"].endswith(".json"):
            continue
        count += 1
        rid = obj["key"].rsplit("/", 1)[-1].removesuffix(".json")
        print(f"{obj.get('last_modified', '?')}  {rid}  {obj['key']}")
    print(f"\nTotal : {count}")


def cmd_last(args: list[str]) -> None:
    n = int(args[0]) if args else 10
    items = [
        obj for obj in list_objects("reports/")
        if obj["key"].endswith(".json")
    ]
    items.sort(key=lambda o: o.get("last_modified", ""), reverse=True)
    print(f"=== {n} derniers rapports ===")
    for obj in items[:n]:
        rid = obj["key"].rsplit("/", 1)[-1].removesuffix(".json")
        print(f"{obj.get('last_modified', '?')}  {rid}  {obj['key']}")


def cmd_show(args: list[str]) -> None:
    if not args:
        sys.exit("ID requis")
    key = find_json_key(args[0])
    if not key:
        sys.exit(f"Aucun rapport trouvé pour : {args[0]}")
    print(f"# Source : {key}\n")
    body = fetch_object_bytes(key)
    try:
        parsed = json.loads(body)
        print(json.dumps(parsed, indent=2, ensure_ascii=False))
    except Exception:
        sys.stdout.buffer.write(body)


def cmd_get(args: list[str]) -> None:
    if not args:
        sys.exit("ID requis")
    key = find_json_key(args[0])
    if not key:
        sys.exit(f"Aucun rapport trouvé pour : {args[0]}")
    full_id = key.rsplit("/", 1)[-1].removesuffix(".json")
    out_dir = LOCAL_DIR / full_id
    out_dir.mkdir(parents=True, exist_ok=True)

    print(f"Téléchargement JSON → {out_dir / 'report.json'}")
    (out_dir / "report.json").write_bytes(fetch_object_bytes(key))

    png_key = key.removesuffix(".json") + ".png"
    if any(k == png_key for k in list_keys(png_key)):
        print(f"Téléchargement PNG  → {out_dir / 'screenshot.png'}")
        (out_dir / "screenshot.png").write_bytes(fetch_object_bytes(png_key))
    else:
        print("(pas de screenshot joint)")
    print(f"\nOK → {out_dir}/")


def cmd_delete(args: list[str]) -> None:
    if not args:
        sys.exit("ID requis")
    key = find_json_key(args[0])
    if not key:
        sys.exit(f"Aucun rapport trouvé pour : {args[0]}")
    print(f"Suppression du rapport : {key}")
    ans = input("Confirmer ? [y/N] ").strip().lower()
    if ans not in ("y", "yes"):
        print("Annulé.")
        return
    delete_object(key)
    png_key = key.removesuffix(".json") + ".png"
    if any(k == png_key for k in list_keys(png_key)):
        delete_object(png_key)
        print("Screenshot supprimé aussi.")
    print("Fait.")


# ============================================================================
# HTML dashboard
# ============================================================================

HTML_TEMPLATE = """<!DOCTYPE html>
<html lang="fr">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>MathCursor — Reports</title>
  <meta name="robots" content="noindex,nofollow" />
  <style>
    :root {{
      --paper:       #faf9f6;
      --paper-low:   #f4f3f1;
      --paper-rim:   #e3e2e0;
      --paper-white: #ffffff;
      --ink:         #00236f;
      --red:         #bb0027;
      --yellow:      #fde047;
      --text:        #1a1c1a;
      --text-muted:  #636571;
      --font-head:   'Space Grotesk', system-ui, sans-serif;
      --font-body:   'Inter', system-ui, sans-serif;
      --font-mono:   ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
    }}
    * {{ box-sizing: border-box; }}
    body {{ margin: 0; background: var(--paper); color: var(--text);
           font-family: var(--font-body); font-size: 15px; line-height: 1.5; }}
    .container {{ max-width: 980px; margin: 0 auto; padding: 32px 24px; }}
    h1 {{ font-family: var(--font-head); color: var(--ink); margin: 0 0 4px; font-size: 28px; }}
    .lead {{ color: var(--text-muted); margin: 0 0 24px; font-size: 13px; }}
    .lead .private {{ color: var(--red); font-weight: 600; }}
    .filter input {{
      width: 100%; max-width: 480px; padding: 8px 12px; font-size: 14px;
      border: 1px solid var(--paper-rim); border-radius: 4px;
      font-family: var(--font-body);
    }}
    .filter {{ margin-bottom: 16px; }}
    .report {{
      background: var(--paper-white); border: 1px solid var(--paper-rim);
      border-radius: 6px; padding: 18px 20px; margin-bottom: 20px;
    }}
    .report-head {{
      display: flex; justify-content: space-between; align-items: baseline;
      gap: 16px; flex-wrap: wrap;
      border-bottom: 1px solid var(--paper-rim); padding-bottom: 8px; margin-bottom: 12px;
    }}
    .report-time {{ font-family: var(--font-mono); font-size: 12px; color: var(--text-muted); }}
    .report-meta {{ font-size: 12px; color: var(--text-muted); }}
    .report-meta code {{ background: var(--paper-low); padding: 1px 4px; border-radius: 3px; }}
    .field {{ margin-bottom: 12px; }}
    .field-label {{
      font-family: var(--font-head); font-size: 11px; text-transform: uppercase;
      letter-spacing: 0.05em; color: var(--text-muted); margin-bottom: 3px;
    }}
    .field-value {{
      font-family: var(--font-mono); font-size: 13px;
      background: var(--paper-low); padding: 8px 10px; border-radius: 4px;
      white-space: pre-wrap; word-break: break-word;
    }}
    .field-value.empty {{ color: var(--text-muted); font-style: italic; }}
    .comment {{
      background: #fff8d6; border-left: 3px solid var(--yellow);
      padding: 10px 12px; border-radius: 0 4px 4px 0;
      font-size: 14px; white-space: pre-wrap;
    }}
    details summary {{ cursor: pointer; font-size: 12px; color: var(--text-muted); margin-top: 8px; }}
    details pre {{
      background: var(--paper-low); padding: 8px 10px; border-radius: 4px;
      font-size: 11px; overflow-x: auto; white-space: pre-wrap;
    }}
    .screenshot {{
      max-width: 100%; max-height: 400px; margin-top: 8px;
      border: 1px solid var(--paper-rim); border-radius: 4px; cursor: zoom-in;
    }}
    .screenshot.zoomed {{ max-height: none; cursor: zoom-out; }}
    .empty-state {{ text-align: center; padding: 48px 24px; color: var(--text-muted); font-style: italic; }}
    .id-link {{ font-family: var(--font-mono); font-size: 11px; color: var(--text-muted); }}
  </style>
</head>
<body>
  <div class="container">
    <h1>MathCursor — Reports</h1>
    <p class="lead">
      <span class="private">⚠ Privé · ne pas partager</span> ·
      {total} rapport(s) depuis {since} · généré le {generated} ·
      relance avec <code>python tools/cloudflare/reports.py html [JOURS]</code>
    </p>

    <div class="filter">
      <input type="text" id="filter-input" placeholder="Filtrer (id, version, contenu commentaire...)" />
    </div>

    <div id="reports-list"></div>
    <div id="empty-state" class="empty-state" hidden>(aucun rapport sur la période)</div>
  </div>

  <script>
    const REPORTS = {reports_json};

    function escape(s) {{
      return String(s ?? '').replace(/[<>&]/g, c => ({{'<':'&lt;','>':'&gt;','&':'&amp;'}}[c]));
    }}

    function renderReport(r) {{
      const ts = (r._server && r._server.received_at) || r.ts || r._date || '?';
      const country = (r._server && r._server.cf_country) || '??';
      const colo = (r._server && r._server.cf_colo) || '??';
      const meta = (r.metadata) || {{}};

      const sourceText = r.source_text || '';
      const proposed = r.proposed_latex || '';
      const committed = r.committed_latex || '';
      const comment = r.user_comment || '';

      let html = '<div class="report">';
      html += '<div class="report-head">';
      html += '<div><span class="report-time">' + escape(ts) + '</span> · ';
      html += '<span class="id-link">' + escape(r._id) + '</span></div>';
      html += '<div class="report-meta">';
      html += 'v<code>' + escape(r.version || '?') + '</code> · ';
      html += escape(country) + '/' + escape(colo) + ' · ';
      html += 'Word <code>' + escape(meta.word_version || '?') + '</code>';
      html += '</div>';
      html += '</div>';

      if (comment) {{
        html += '<div class="field">';
        html += '<div class="field-label">Commentaire utilisateur</div>';
        html += '<div class="comment">' + escape(comment) + '</div>';
        html += '</div>';
      }}

      const renderField = (label, val) => {{
        const cls = val ? '' : ' empty';
        const txt = val || '(vide)';
        return '<div class="field"><div class="field-label">' + label + '</div>' +
               '<div class="field-value' + cls + '">' + escape(txt) + '</div></div>';
      }};
      html += renderField('Ce que tu as tapé', sourceText);
      html += renderField('Ce que MathCursor a proposé (LaTeX)', proposed);
      html += renderField('Ce qui (serait) inséré dans Word', committed);

      if (r.paragraph_context) {{
        html += '<details><summary>Paragraphe Word (contexte)</summary>';
        html += '<pre>' + escape(r.paragraph_context) + '</pre></details>';
      }}

      if (r._has_screenshot) {{
        html += '<details open><summary>Capture d\\'écran</summary>';
        html += '<img class="screenshot" src="' + escape(r._screenshot) + '" ';
        html += 'onclick="this.classList.toggle(\\'zoomed\\')" alt="screenshot" />';
        html += '</details>';
      }}

      if (r.log_tail) {{
        html += '<details><summary>Log technique (' + r.log_tail.length + ' chars)</summary>';
        html += '<pre>' + escape(r.log_tail) + '</pre></details>';
      }}

      html += '</div>';
      return html;
    }}

    function render(filter) {{
      const list = document.getElementById('reports-list');
      const empty = document.getElementById('empty-state');
      const f = (filter || '').toLowerCase();
      const filtered = f ? REPORTS.filter(r => JSON.stringify(r).toLowerCase().includes(f)) : REPORTS;
      if (filtered.length === 0) {{
        list.innerHTML = ''; empty.hidden = false;
      }} else {{
        empty.hidden = true;
        list.innerHTML = filtered.map(renderReport).join('');
      }}
    }}

    document.getElementById('filter-input').addEventListener('input', e => render(e.target.value));
    render('');
  </script>
</body>
</html>
"""


def cmd_html(args: list[str]) -> None:
    days = int(args[0]) if args else 30
    since_dt = datetime.now(timezone.utc) - timedelta(days=days)
    since = since_dt.strftime("%Y-%m-%d")

    screenshots_dir = LOCAL_DIR / "screenshots"
    screenshots_dir.mkdir(parents=True, exist_ok=True)

    print(f"Génération du dashboard pour les rapports depuis {since}...")

    # Index par préfixe (set des PNG keys connus pour éviter le re-API)
    all_objects = list(list_objects("reports/"))
    json_objs = [o for o in all_objects if o["key"].endswith(".json")]
    png_keys = {o["key"] for o in all_objects if o["key"].endswith(".png")}

    enriched_reports = []
    for obj in json_objs:
        key = obj["key"]
        m = re.match(r"^reports/(\d{4}-\d{2}-\d{2})/", key)
        if not m:
            continue
        date_part = m.group(1)
        if date_part < since:
            continue

        rid = key.rsplit("/", 1)[-1].removesuffix(".json")

        try:
            body = fetch_object_bytes(key).decode("utf-8")
            data = json.loads(body)
        except Exception as e:
            print(f"  WARN  skip {rid} : {e}")
            continue

        # Télécharge le PNG associé en local s'il existe
        png_key = key.removesuffix(".json") + ".png"
        local_png_rel = f"screenshots/{rid}.png"
        local_png_abs = LOCAL_DIR / local_png_rel
        has_screenshot = False
        if png_key in png_keys:
            if not local_png_abs.exists():
                try:
                    local_png_abs.write_bytes(fetch_object_bytes(png_key))
                except Exception as e:
                    print(f"  WARN  png {rid} : {e}")
            has_screenshot = local_png_abs.exists()

        data["_id"] = rid
        data["_key"] = key
        data["_date"] = date_part
        data["_screenshot"] = local_png_rel
        data["_has_screenshot"] = has_screenshot
        enriched_reports.append(data)

    # Tri desc par received_at puis par _date
    enriched_reports.sort(
        key=lambda r: ((r.get("_server") or {}).get("received_at") or r.get("_date") or ""),
        reverse=True,
    )

    out = HTML_TEMPLATE.format(
        total=len(enriched_reports),
        since=html_escape(since),
        generated=html_escape(datetime.now().strftime("%Y-%m-%d %H:%M:%S")),
        reports_json=json.dumps(enriched_reports, ensure_ascii=False),
    )
    out_path = LOCAL_DIR / "index.html"
    out_path.write_text(out, encoding="utf-8")

    print(f"Dashboard généré : {out_path}")
    print(f"Rapports inclus  : {len(enriched_reports)}")
    print()
    print("Ouvrir dans le browser :")
    if sys.platform == "win32":
        print(f'  start "" "{out_path}"')
    elif sys.platform == "darwin":
        print(f'  open "{out_path}"')
    else:
        print(f'  xdg-open "{out_path}"')


# ============================================================================
# Dispatch
# ============================================================================

COMMANDS = {
    "today":  cmd_today,
    "list":   cmd_list,
    "last":   cmd_last,
    "show":   cmd_show,
    "get":    cmd_get,
    "delete": cmd_delete,
    "html":   cmd_html,
}


def main() -> None:
    cmd = sys.argv[1] if len(sys.argv) > 1 else "today"
    args = sys.argv[2:]
    fn = COMMANDS.get(cmd)
    if not fn:
        print(__doc__, file=sys.stderr)
        sys.exit(2)
    fn(args)


if __name__ == "__main__":
    main()
