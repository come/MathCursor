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

- [x] **Mots-clés partiels par préfixe** — taper un préfixe d'un mot-clé l'étend.
  ✅ Fait 2026-06-19 : **alias auto-générés** (`Vocabulary.AddPrefixAliases`) —
  pour chaque mot-clé/alias, tout préfixe **≥ 4 lettres** non ambigu devient un
  alias vers la cible, fusionné dans les maps d'alias. Réutilise `Canon` (zéro
  machinerie). Ambigus (`arc`, `sub`) → non générés (taper 1 lettre de plus).
  ≥4 (pas 3) pour éviter `for`/`per`/`uni`. ADR
  `2026-06-18-Feat-prefix-keyword-expansion`. (1ʳᵉ implé « popup multi-candidats »
  jugée usine à gaz par l'utilisateur → revertée au profit des alias auto.
  `app`→approx impossible — exact alias `appartient` ; `der`→dérivée sans cible.)

- [x] **`approx` / « environ égal » (≈)** — faire fonctionner la saisie de `≈`
  via mot-clé (`approx`, `environegal`/`environ egal`) et/ou Unicode direct `≈`.
  Vérifier `vocabulary` (alias FR) + `render` LaTeX (`\approx`) + couverture
  `LatexToOmml`. ✅ Fait 2026-06-18 (`≈` sameAs approx + alias FR `environ` ;
  forme collée `environegal` retirée — mot artificiel) — ADR
  `2026-06-18-Feat-approx-and-second-derivative`.

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
