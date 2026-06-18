# Backlog moteur (`engine/` — portage `forest`)

Idées d'évolution du moteur de reconnaissance, hors périmètre du portage fidèle
en cours (cf. [`../../PLAN.md`](../../PLAN.md) §3). Non priorisé. À transformer en
ADR + plan (`/mathcursor-plan`) au moment de l'implémentation.

---

## TODO

- [ ] **`**` pour la puissance** — accepter `**` comme opérateur d'exposant
  (ex. `x**2` → `x^2`), en plus de `^`. Convention répandue (Python, etc.),
  naturelle au clavier sans `AltGr`.

- [ ] **Mots-clés partiels (préfixe ≥ 3 lettres)** — autoriser tout préfixe d'au
  moins 3 lettres d'un mot-clé reconnu (ex. `lim` → `limite`, `der` → `dérivée`,
  `rac` → `racine`, `app` → `approx`…). Dans la popup, **afficher le mot-clé
  complet reconnu** et le **traiter comme s'il était saisi en entier**.
  → Vérifier l'absence de collisions de préfixes entre mots-clés ; définir la
  règle de désambiguïsation (plus court mot-clé ? proposer plusieurs candidats
  dans la popup ?).

- [ ] **`approx` / « environ égal » (≈)** — faire fonctionner la saisie de `≈`
  via mot-clé (`approx`, `environegal`/`environ egal`) et/ou Unicode direct `≈`.
  Vérifier `vocabulary` (alias FR) + `render` LaTeX (`\approx`) + couverture
  `LatexToOmml`.

- [ ] **Dérivée seconde** — vérifier que la dérivée seconde est correctement
  reconnue et rendue (ex. `f''(x)`, `\frac{d^2 f}{dx^2}`…). Ajouter une fixture
  de non-régression si manquante.

- [ ] **Démo en mode réel** — refaire une démo qui mime vraiment l'ergo finale :
  grande zone de texte libre (canvas ? `contenteditable` ?) avec **curseur
  minimal** et **Entrée = passage à la ligne**. Pipeline live : détection de
  texte → popup au caret → **rendu KaTeX** de la zone reconnue. Objectif : sentir
  le flow réel (saisie au fil de l'eau, déclenchement, choix dans la popup,
  insertion) plutôt qu'un simple champ « input → output ».
