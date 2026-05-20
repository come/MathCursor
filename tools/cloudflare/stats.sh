#!/usr/bin/env bash
# MathCursor — stats Analytics Engine. Tire les requêtes utiles via le SQL
# API de Cloudflare en utilisant le token + account_id de
# ~/.mathcursor/cloudflare.env.
#
# Usage :
#   tools/cloudflare/stats.sh           # toutes les vues en CLI
#   tools/cloudflare/stats.sh versions  # juste downloads par version
#   tools/cloudflare/stats.sh countries # par pays
#   tools/cloudflare/stats.sh days      # série temporelle 30 jours
#   tools/cloudflare/stats.sh referers  # top referers
#   tools/cloudflare/stats.sh sql "<requête>"  # SQL libre
#   tools/cloudflare/stats.sh html      # génère stats-local/index.html (privé)
#
# Prérequis : ~/.mathcursor/cloudflare.env avec CLOUDFLARE_API_TOKEN +
# CLOUDFLARE_ACCOUNT_ID (token doit avoir scope « Account Analytics : Read »).

set -euo pipefail

ENV_FILE="$HOME/.mathcursor/cloudflare.env"
if [ ! -f "$ENV_FILE" ]; then
  echo "ERREUR : $ENV_FILE introuvable." >&2
  exit 1
fi
# shellcheck source=/dev/null
source "$ENV_FILE"

if [ -z "${CLOUDFLARE_API_TOKEN:-}" ] || [ -z "${CLOUDFLARE_ACCOUNT_ID:-}" ]; then
  echo "ERREUR : CLOUDFLARE_API_TOKEN ou CLOUDFLARE_ACCOUNT_ID manquant dans $ENV_FILE." >&2
  exit 1
fi

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
API="https://api.cloudflare.com/client/v4/accounts/${CLOUDFLARE_ACCOUNT_ID}/analytics_engine/sql"

run_sql() {
  local sql="$1"
  curl -sS -X POST "$API" \
    -H "Authorization: Bearer ${CLOUDFLARE_API_TOKEN}" \
    --data "$sql FORMAT JSONEachRow"
}

# Concatène N lignes JSONEachRow en un tableau JSON. Empty input → "[]".
json_array() {
  local lines
  lines="$(cat)"
  if [ -z "$lines" ]; then echo "[]"; return; fi
  printf '[%s]' "$(printf '%s' "$lines" | paste -sd ',' -)"
}

show_versions() {
  echo "=== Downloads par version (30 derniers jours) ==="
  run_sql "SELECT blob1 AS file, count() AS downloads
           FROM mathcursor_downloads
           WHERE timestamp > NOW() - INTERVAL '30' DAY
           GROUP BY file ORDER BY downloads DESC LIMIT 20"
  echo
}

show_countries() {
  echo "=== Downloads par pays (30 derniers jours) ==="
  run_sql "SELECT blob3 AS country, count() AS downloads
           FROM mathcursor_downloads
           WHERE timestamp > NOW() - INTERVAL '30' DAY
           GROUP BY country ORDER BY downloads DESC LIMIT 30"
  echo
}

show_days() {
  echo "=== Downloads par jour (30 derniers jours) ==="
  run_sql "SELECT toStartOfInterval(timestamp, INTERVAL '1' DAY) AS day, count() AS downloads
           FROM mathcursor_downloads
           WHERE timestamp > NOW() - INTERVAL '30' DAY
           GROUP BY day ORDER BY day"
  echo
}

show_referers() {
  echo "=== Top referers (30 derniers jours) ==="
  run_sql "SELECT blob6 AS referer, count() AS hits
           FROM mathcursor_downloads
           WHERE blob6 != '' AND timestamp > NOW() - INTERVAL '30' DAY
           GROUP BY referer ORDER BY hits DESC LIMIT 20"
  echo
}

show_total() {
  echo "=== Total (30 derniers jours) ==="
  run_sql "SELECT count() AS total FROM mathcursor_downloads
           WHERE timestamp > NOW() - INTERVAL '30' DAY"
  echo
}

generate_html() {
  local out_dir="$ROOT/stats-local"
  mkdir -p "$out_dir"

  # Récupère les 5 datasets en JSON arrays
  local total versions countries days referers
  total=$(run_sql "SELECT count() AS total FROM mathcursor_downloads
                   WHERE timestamp > NOW() - INTERVAL '30' DAY" | json_array)
  versions=$(run_sql "SELECT blob1 AS file, count() AS downloads
                      FROM mathcursor_downloads
                      WHERE timestamp > NOW() - INTERVAL '30' DAY
                      GROUP BY file ORDER BY downloads DESC LIMIT 20" | json_array)
  countries=$(run_sql "SELECT blob3 AS country, count() AS downloads
                       FROM mathcursor_downloads
                       WHERE timestamp > NOW() - INTERVAL '30' DAY
                       GROUP BY country ORDER BY downloads DESC LIMIT 30" | json_array)
  days=$(run_sql "SELECT toStartOfInterval(timestamp, INTERVAL '1' DAY) AS day, count() AS downloads
                  FROM mathcursor_downloads
                  WHERE timestamp > NOW() - INTERVAL '30' DAY
                  GROUP BY day ORDER BY day" | json_array)
  referers=$(run_sql "SELECT blob6 AS referer, count() AS hits
                      FROM mathcursor_downloads
                      WHERE blob6 != '' AND timestamp > NOW() - INTERVAL '30' DAY
                      GROUP BY referer ORDER BY hits DESC LIMIT 20" | json_array)

  local generated
  generated="$(date '+%Y-%m-%d %H:%M:%S')"

  cat > "$out_dir/index.html" <<HTMLEND
<!DOCTYPE html>
<html lang="fr">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>MathCursor — Stats locales</title>
  <meta name="robots" content="noindex,nofollow" />
  <script src="https://cdn.jsdelivr.net/npm/chart.js@4.4.0/dist/chart.umd.min.js"></script>
  <style>
    :root {
      --paper:       #faf9f6;
      --paper-low:   #f4f3f1;
      --paper-rim:   #e3e2e0;
      --paper-white: #ffffff;
      --ink:         #00236f;
      --ink-soft:    #264191;
      --ink-light:   #b6c4ff;
      --red:         #bb0027;
      --yellow:      #fde047;
      --text:        #1a1c1a;
      --text-muted:  #444651;
      --grid-line:   #e5e7eb;
      --font-head:   'Space Grotesk', system-ui, -apple-system, 'Segoe UI', sans-serif;
      --font-body:   'Inter', system-ui, -apple-system, sans-serif;
      --font-mono:   ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      background: var(--paper);
      color: var(--text);
      font-family: var(--font-body);
      font-size: 16px;
      line-height: 1.5;
      background-image:
        linear-gradient(var(--grid-line) 1px, transparent 1px),
        linear-gradient(90deg, var(--grid-line) 1px, transparent 1px);
      background-size: 32px 32px;
    }
    .container { max-width: 1200px; margin: 0 auto; padding: 32px 24px; }
    h1, h2, h3 { font-family: var(--font-head); color: var(--ink); margin: 0; }
    h1 { font-size: 32px; margin-bottom: 4px; }
    .lead { color: var(--text-muted); margin: 0 0 24px; font-size: 14px; }
    .lead .private { color: var(--red); font-weight: 600; }
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 20px; }
    @media (max-width: 768px) { .grid { grid-template-columns: 1fr; } }
    .card {
      background: var(--paper-white);
      border: 1px solid var(--paper-rim);
      border-radius: 6px;
      padding: 20px;
    }
    .card.full { grid-column: 1 / -1; }
    .card h3 { font-size: 14px; margin-bottom: 12px; text-transform: uppercase; letter-spacing: 0.05em; }
    .total-big {
      font-family: var(--font-head);
      font-size: 64px;
      color: var(--ink);
      font-weight: 700;
      line-height: 1;
    }
    table.referers { width: 100%; border-collapse: collapse; font-size: 13px; }
    table.referers th, table.referers td {
      text-align: left; padding: 6px 8px;
      border-bottom: 1px solid var(--paper-rim);
    }
    table.referers td.num { text-align: right; font-variant-numeric: tabular-nums; }
    table.referers td.url {
      font-family: var(--font-mono);
      font-size: 11.5px;
      max-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    canvas { max-width: 100%; }
    .empty { color: var(--text-muted); font-style: italic; font-size: 13px; }
  </style>
</head>
<body>
  <div class="container">
    <h1>MathCursor — Stats</h1>
    <p class="lead">
      <span class="private">⚠ Privé · ne pas partager</span> ·
      30 derniers jours · généré le ${generated} ·
      relance avec <code>tools/cloudflare/stats.sh html</code>
    </p>

    <div class="grid">
      <div class="card">
        <h3>Total</h3>
        <div class="total-big" id="total-num">…</div>
      </div>
      <div class="card">
        <h3>Par version</h3>
        <canvas id="chart-versions" height="180"></canvas>
      </div>
      <div class="card full">
        <h3>Par jour</h3>
        <canvas id="chart-days" height="100"></canvas>
      </div>
      <div class="card">
        <h3>Par pays</h3>
        <canvas id="chart-countries" height="220"></canvas>
      </div>
      <div class="card">
        <h3>Top referers</h3>
        <table class="referers" id="table-referers">
          <thead><tr><th>Referer</th><th class="num">Hits</th></tr></thead>
          <tbody></tbody>
        </table>
        <div id="referers-empty" class="empty" hidden>(aucun referer enregistré)</div>
      </div>
    </div>
  </div>

  <script>
    // --- Données injectées par stats.sh html ---
    const TOTAL = ${total};
    const VERSIONS = ${versions};
    const COUNTRIES = ${countries};
    const DAYS = ${days};
    const REFERERS = ${referers};
    // --- Fin données ---

    const COLORS = {
      ink: '#00236f', inkSoft: '#264191', inkLight: '#b6c4ff',
      red: '#bb0027', yellow: '#fde047',
      palette: ['#00236f','#264191','#b6c4ff','#bb0027','#fde047','#888','#aaa','#ccc'],
    };

    // Total
    const t = TOTAL[0] ? Number(TOTAL[0].total) : 0;
    document.getElementById('total-num').textContent = t.toLocaleString('fr-FR');

    // Par version (donut)
    if (VERSIONS.length > 0) {
      new Chart(document.getElementById('chart-versions'), {
        type: 'doughnut',
        data: {
          labels: VERSIONS.map(r => r.file),
          datasets: [{
            data: VERSIONS.map(r => Number(r.downloads)),
            backgroundColor: COLORS.palette,
          }],
        },
        options: {
          plugins: { legend: { position: 'bottom', labels: { boxWidth: 12, font: { size: 11 } } } },
        },
      });
    }

    // Par pays (bar horizontale)
    if (COUNTRIES.length > 0) {
      const top = COUNTRIES.slice(0, 12);
      new Chart(document.getElementById('chart-countries'), {
        type: 'bar',
        data: {
          labels: top.map(r => r.country || '?'),
          datasets: [{ data: top.map(r => Number(r.downloads)), backgroundColor: COLORS.ink }],
        },
        options: {
          indexAxis: 'y',
          plugins: { legend: { display: false } },
          scales: { x: { beginAtZero: true } },
        },
      });
    }

    // Par jour (line)
    if (DAYS.length > 0) {
      new Chart(document.getElementById('chart-days'), {
        type: 'line',
        data: {
          labels: DAYS.map(r => (r.day || '').slice(5, 10)),
          datasets: [{
            data: DAYS.map(r => Number(r.downloads)),
            borderColor: COLORS.ink,
            backgroundColor: COLORS.inkLight,
            fill: true,
            tension: 0.25,
          }],
        },
        options: {
          plugins: { legend: { display: false } },
          scales: { y: { beginAtZero: true } },
        },
      });
    }

    // Top referers
    const tbody = document.querySelector('#table-referers tbody');
    if (REFERERS.length === 0) {
      document.getElementById('referers-empty').hidden = false;
      document.getElementById('table-referers').hidden = true;
    } else {
      tbody.innerHTML = REFERERS.slice(0, 10).map(r => {
        const safe = (r.referer || '').replace(/[<>&]/g, c => ({'<':'&lt;','>':'&gt;','&':'&amp;'}[c]));
        return '<tr><td class="url" title="'+safe+'">'+safe+'</td><td class="num">'+r.hits+'</td></tr>';
      }).join('');
    }
  </script>
</body>
</html>
HTMLEND

  echo "Dashboard généré : $out_dir/index.html"
  echo
  echo "Ouvrir dans le browser :"
  case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*) echo "  start \"\" \"$out_dir/index.html\"" ;;
    Darwin) echo "  open \"$out_dir/index.html\"" ;;
    *) echo "  xdg-open \"$out_dir/index.html\"" ;;
  esac
}

case "${1:-all}" in
  all)        show_total; show_versions; show_countries; show_days; show_referers ;;
  versions)   show_versions ;;
  countries)  show_countries ;;
  days)       show_days ;;
  referers)   show_referers ;;
  total)      show_total ;;
  html)       generate_html ;;
  sql)        shift; run_sql "${1:-SELECT 1}"; echo ;;
  *)          echo "Usage : $0 [all | versions | countries | days | referers | total | html | sql \"<query>\"]" >&2; exit 1 ;;
esac
