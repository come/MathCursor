# Backlog moteur (`engine/` — portage `forest`)

Idées d'évolution du moteur de reconnaissance, hors périmètre du portage fidèle
en cours (cf. [`../../PLAN.md`](../../PLAN.md) §3). Non priorisé. À transformer en
ADR + plan (`/mathcursor-plan`) au moment de l'implémentation.

---

## TODO

- [x] **`**` pour la puissance** — accepter `**` comme opérateur d'exposant
  (ex. `x**2` → `x^2`), en plus de `^`. Convention répandue (Python, etc.),
  naturelle au clavier sans `AltGr`. ✅ Fait 2026-06-18 (`sameAs` dans
  `symbols.json`, +3 fixtures) — ADR `2026-06-18-Feat-power-double-star`.

- [ ] **Mots-clés partiels (préfixe ≥ 3 lettres)** — autoriser tout préfixe d'au
  moins 3 lettres d'un mot-clé reconnu (ex. `lim` → `limite`, `der` → `dérivée`,
  `rac` → `racine`, `app` → `approx`…). Dans la popup, **afficher le mot-clé
  complet reconnu** et le **traiter comme s'il était saisi en entier**.
  → Vérifier l'absence de collisions de préfixes entre mots-clés ; définir la
  règle de désambiguïsation (plus court mot-clé ? proposer plusieurs candidats
  dans la popup ?).

- [x] **`approx` / « environ égal » (≈)** — faire fonctionner la saisie de `≈`
  via mot-clé (`approx`, `environegal`/`environ egal`) et/ou Unicode direct `≈`.
  Vérifier `vocabulary` (alias FR) + `render` LaTeX (`\approx`) + couverture
  `LatexToOmml`. ✅ Fait 2026-06-18 (`≈` sameAs approx + alias FR `environ`/
  `environegal`) — ADR `2026-06-18-Feat-approx-and-second-derivative`.

- [x] **Dérivée seconde** — vérifier que la dérivée seconde est correctement
  reconnue et rendue (ex. `f''(x)`, `\frac{d^2 f}{dx^2}`…). Ajouter une fixture
  de non-régression si manquante. ✅ Fait 2026-06-18 (`f''(x)`/`u''(t)` OK via
  postfixe `'` ; +fixture `u''(t)`. Notation Leibniz `\frac{d^2 f}{dx^2}` hors
  périmètre, pas de sténo dédiée) — ADR `2026-06-18-Feat-approx-and-second-derivative`.

- [ ] **Démo en mode réel** — refaire une démo qui mime vraiment l'ergo finale :
  grande zone de texte libre (canvas ? `contenteditable` ?) avec **curseur
  minimal** et **Entrée = passage à la ligne**. Pipeline live : détection de
  texte → popup au caret → **rendu KaTeX** de la zone reconnue. Objectif : sentir
  le flow réel (saisie au fil de l'eau, déclenchement, choix dans la popup,
  insertion) plutôt qu'un simple champ « input → output ».
