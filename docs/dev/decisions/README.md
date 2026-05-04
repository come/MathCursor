# Journal de décisions — MathCursor

Une entrée par décision produit ou technique, dans un fichier séparé,
nommé `YYYY-MM-DD-<Kind>-<slug>.md`.

Format et conventions : voir
[2026-04-24-Meta-adr-format.md](2026-04-24-Meta-adr-format.md).

## Kinds
`Feat-` · `Fix-` · `UX-` · `Release-` · `Meta-` · `Test-`

## Températures
- **[forte]** — structurelle, coûte cher à changer. On n'y revient qu'avec un très bon argument.
- **[molle]** — décision de travail, ouverte à révision si bon argument.
- **[provisoire]** — "on fait comme ça aujourd'hui mais on reverra". À re-examiner activement.

## Statuts
`proposé` · `acté` · `retracté`

## Index chronologique (plus récent en haut)

### 2026-05-04
- `[molle]` Meta — [Refactor du pipeline cross-merge (4 phases séquentielles)](2026-05-04-Meta-cross-merge-pipeline-refactor.md)

### 2026-05-01
- `[molle]` Feat — [Backoffice admin (reports + stats) en ligne sur Cloudflare avec Basic Auth](2026-05-01-Feat-admin-backoffice-cloudflare.md)

### 2026-04-30
- `[molle]` Fix — [Fonction trigo + Number tight + Group avale la suite (`cos2(x)+1`)](2026-04-30-Fix-trig-func-power-tight-arg.md)
- `[molle]` Feat — [Le point `.` comme opérateur de multiplication (rendu `\cdot`)](2026-04-30-Feat-dot-as-multiplier.md)
- `[molle]` Feat — [Multiplication explicite `*` rendue selon culture (`×` ou `·`)](2026-04-30-Feat-explicit-mult-times-vs-cdot.md)
- `[molle]` Feat — [Formulaire "Signaler une erreur" pré-rempli + backend Cloudflare](2026-04-30-Feat-feedback-form-cloudflare-backend.md) (Supersedes partiellement feedback-bundle-whatsapp du 23-04)
- `[molle]` Feat — [Associativité de `*` pilotée par sa tightness](2026-04-30-Feat-asterisk-tightness-associativity.md)
- `[molle]` Feat — [Juxtaposition tight = groupement, ops explicites = PEMDAS (avec alt désambig)](2026-04-30-Feat-tight-implicit-mult-grouping.md) (Supersedes revert-tight-as-grouping)
- `[molle]` Feat — [Séparateur `;` pour coordonnées en notation française](2026-04-30-Feat-french-semicolon-coordinates.md)
- `[molle]` Fix — [Refactor `LatexToUnicodeMath` en parser → AST → émetteur (anti-absorption Word OMath)](2026-04-30-Fix-latex-to-unicodemath-refactor.md)
- `[molle]` Feat — [Précédence math standard pour `/` collé (revert tight-as-grouping)](2026-04-30-Feat-revert-tight-as-grouping.md) (Supersedes tight-as-grouping du 29-04) `retracté`

### 2026-04-29
- `[molle]` Test — [Audit follow-up : combler les angles morts de tests (cleanup + désambig + corpus patho + adapter tests + NER inference)](2026-04-29-Test-audit-followup.md)
- `[molle]` Feat — [Vecteur/point + coordonnées au clavier (`u(1, 2)` / `u (1 2)` / `A(1, 2)`)](2026-04-29-Feat-vector-coordinates-shorthand.md)
- `[molle]` Feat — [Fusionner OMath adjacents lors d'une conversion](2026-04-29-Feat-merge-adjacent-omaths.md)
- `[molle]` Feat — [Détection `=>` / `<=>` / `<==` et conversion en flèches math](2026-04-29-Feat-implication-equivalence-arrows.md)
- `[molle]` Feat — [Extension itérative de la zone via Ctrl+Espace répété](2026-04-29-Feat-iterative-zone-expansion-ctrl-space.md)
- `[molle]` Feat — [Décomposition modulaire de `forall`/`exists` (∀ + var + ∈ + set)](2026-04-29-Feat-forall-modular-decomposition.md) (Supersedes scope forall du 28-04)
- `[molle]` Feat — [Définition de fonction au clavier `f:x->expr` → `f : x ↦ expr`](2026-04-29-Feat-function-definition.md)
- `[molle]` Feat — [Ensembles canoniques R/N/Z/Q/C avec modificateurs `*`/`+`/`-`](2026-04-29-Feat-canonical-sets.md)
- `[molle]` Feat — [Juxtaposition tight = groupement implicite pour `/`, `^`, `_`](2026-04-29-Feat-tight-as-grouping.md) `retracté`
- `[molle]` Feat — [Composition d'intervalles : union (`U`/`union`) et intersection (`inter`)](2026-04-29-Feat-interval-union-intersection.md)

### 2026-04-28
- `[molle]` Feat — [Notation intervalle française au clavier (`[a,b[`, `]a,b]`...)](2026-04-28-Feat-interval-notation.md)
- `[forte]` Meta — [Refactor : ZoneResolver, point d'entrée unique pour la résolution de zone](2026-04-28-Meta-zone-resolver-refactor.md)
- `[forte]` Feat — [Quantificateur ∀/∃ via scope clavier + désambiguïsation par mutation source](2026-04-28-Feat-forall-scope-source-mutation.md) `retracté`

### 2026-04-27
- `[molle]` Feat — [Corpus NER v4 (keywords math en début de zone)](2026-04-27-Feat-ner-corpus-v4-keywords.md)

### 2026-04-24
- `[molle]` Feat — [Corpus NER v3 (fixtures projet + `²` + anti-FP) & challenge modèle](2026-04-24-Feat-ner-corpus-v3-and-model-challenge.md)
- `[molle]` UX — [Popup silencieuse jusqu'à la première interaction utilisateur](2026-04-24-UX-popup-silent-until-interaction.md)
- `[molle]` Feat — [Revert popup vers WpfMath + substitutions ciblées](2026-04-24-Feat-popup-revert-wpfmath.md) (Supersedes WebView2)
- `[forte]` Feat — [Rendu popup via WebView2 + KaTeX (remplace WPF-Math)](2026-04-24-Feat-popup-webview-katex.md) `retracté`
- `[molle]` Feat — [Caractère `²` (AZERTY) traité comme `^2`](2026-04-24-Feat-superscript-two-keyboard.md)
- `[molle]` Feat — [`exp(x)` rendu comme `e^x`](2026-04-24-Feat-exp-as-power-e.md)
- `[molle]` Fix — [`\widehat` non converti + tests de conformance de rendu (OMath & WPF)](2026-04-24-Fix-widehat-omath-conformance.md)
- `[molle]` Fix — [Cert importé uniquement dans TrustedPublisher (pas Root)](2026-04-24-Fix-cert-trustedpublisher-only.md)
- `[molle]` UX — [L'installer importe le certificat lui-même (plus de PowerShell)](2026-04-24-UX-installer-imports-cert.md)
- `[molle]` Feat — [Déploiement Cloudflare Pages + R2 + Analytics Engine](2026-04-24-Feat-cloudflare-deployment.md)
- `[forte]` Meta — [Process de décision rappelé dans CLAUDE.md](2026-04-24-Meta-process-decision-claude-md.md)
- `[molle]` Meta — [Format des ADR + température de décision](2026-04-24-Meta-adr-format.md)
- `[molle]` Feat — [Feedback in-popup "Signaler une erreur"](2026-04-24-Feat-feedback-in-popup.md) (scaffold HTTP = provisoire dedans)
- Release — [0.3.0](2026-04-24-Release-0.3.0.md)

### 2026-04-23
- `[molle]` Feat — [Patterns sum/product/integral séparateur espace + angle ABC](2026-04-23-Feat-sum-angle-patterns.md)
- `[molle]` Fix — [Span Ctrl+Espace respecte brackets/parens](2026-04-23-Fix-span-respects-brackets.md)
- `[molle]` Feat — [Matching préfixe (propositions partielles)](2026-04-23-Feat-partial-matching.md)
- Release — [0.2.0](2026-04-23-Release-0.2.0.md)
- `[molle]` UX — [Alignement OMath respecte le paragraphe parent](2026-04-23-UX-alignment-respect-paragraph.md)
- `[molle]` Feat — [Trigger explicite Ctrl+Espace](2026-04-23-Feat-trigger-ctrl-space.md)
- `[molle]` Feat — [Feedback via bundle zip + groupe WhatsApp](2026-04-23-Feat-feedback-bundle-whatsapp.md)
- `[molle]` Fix — [Rendu LaTeX : fallback sur `HasError`](2026-04-23-Fix-latex-render-fallback.md)
- `[molle]` Fix — [Position popup via GetGUIThreadInfo](2026-04-23-Fix-popup-position-guithreadinfo.md)
