# Corpus NER MathCursor

Jeu de données pour le fine-tuning du détecteur NER de zones math
(`MathNerDetector`). Format JSONL, annotations span-level avec label `MATH`.

## Format

Une ligne = un exemple :
```json
{"text": "on a f(x) = 2x + 1", "spans": [{"start": 5, "end": 18, "label": "MATH"}], "lang": "fr"}
```

- `text` : chaîne à annoter (prose, prose mixte, ou SMS)
- `spans` : liste de zones math avec offsets **caractères** dans `text` (end exclusif). Liste vide si aucune math.
- `lang` : `fr` ou `en`.

La conversion spans → BIO (labels `O`, `B-MATH`, `I-MATH`) se fait au moment
de l'entraînement, dans le notebook (`tokenize_and_align`).

## Fichiers

| Fichier | Contenu | Rôle |
|---------|---------|------|
| `train.jsonl` | 6399 lignes | Train set |
| `val.jsonl` | 798 lignes | Validation (early stopping + best model) |
| `test.jsonl` | 803 lignes | Hold-out pour métriques finales |
| `extension_fr.jsonl` | 1000 lignes FR | Extension synthétique : prose avec vocab math, prose mixte, SMS |
| `extension_en.jsonl` | 1000 lignes EN | Extension synthétique EN équivalente |
| `dataset_v2_all.jsonl` | ~8200 lignes | Concat de tout (train + val + test + extensions) |

Les fichiers `extension_v3_*.jsonl` et suivants sont générés par les scripts
`tools/ner-training/build_v*.py` (un script par extension, sauf v7 dont le
script n'a pas été retrouvé lors du rapatriement DocMath → MathCursor) :
- `extension_v3_fixtures.jsonl` — fixtures `specs/test-fixtures/phase1-zone-detection.json`
- `extension_v3_superscript2.jsonl` — cas du caractère AZERTY `²`
- `extension_v3_false_positives.jsonl` — contre-exemples pour réduire les FP
- `extension_v4_keywords.jsonl` — keyword math en tête de zone (somme, frac…)
- `extension_v5_quant_letters.jsonl` — quantificateurs lettres (V x, E x…)
- `extension_v6_recent_features.jsonl` + `v6_1_targeted` — vecteurs, coordonnées
- `extension_v7_conjunction_at_start.jsonl` — conjonction en début de zone
- `extension_v8_nary_short_forms.jsonl` — **iint/iiint (0 occurrence avant v8),
  formes courtes des n-aires (`sum k f(k)`, `int f(x) x`, `lim u_n`), mots-clés
  NUS en fin de frappe (`int` seul → squelette, parité avec `lim`),
  autocapitalisation Word (`Iint 0 1…`). Rev2 (post-bench du 2026-06-11,
  gold distilmult tombé à 0.9323) : + conventions gold (quantifié français =
  UN span, quantifieur 100 % prose = formule seule, deux formules = deux
  spans) + négatifs token-seul (`x`/`2` seuls ≠ math — la rev1 faisait
  prédire `x`@0.91). Rev3 : + variantes ALIAS moteur (integrale/intégrale/
  integ/integral, limite/lmt, produit/som — sync Vocabulary 2026-06-11,
  `produit` était un chemin mort moteur+refiner). 298 lignes. Baseline
  distilmult-v5 (rev3 à 304 lignes, avant fold) : F1 0.865 — cible
  post-retrain ≥ 0.95 ET gold ≥ 0.99.
- `extension_v9_relation_markers.jsonl` — **marqueurs de relation ≈ en tête de
  ligne** (`approx`/`environ`/`env`/`≈`, **0 occurrence avant v9** comme iint
  avant v8) : la ligne entière marqueur inclus = UNE zone MATH (`environ f(x)` →
  `≈ f(x)` côté adapter). Autocap Word (`Env`/`Environ`/`Approx`). Distractors
  PROSE : `environ`/`env`/`approx` en sens commun (`environ 50 personnes`,
  `l'environnement`, `l'enveloppe`, `env folder`, `approximate cost`) =
  spans=[]. Marqueur SEUL absent des positifs (prose fréquente). 211 lignes,
  192 positifs / 19 négatifs. ADR 2026-06-19-Fix-environ-env-line-start-approx-marker.
- `extension_v10_recent_syntaxes.jsonl` — **syntaxes de saisie moteur récentes,
  0 occurrence avant v10** : (1) raccourci grec `@` (`@a`→α, `@p`→π/φ/ψ, `@theta`,
  `@D`→Δ, `2@p`, `@a+@b`, `r@e^(i@t)`) — le NER n'avait JAMAIS vu de `@` ; (2)
  fractions vulgaires `½ ¾ ⅓ ⅔ ¼` (release 0.11.0) ; (3) puissance `**` (`x**2`,
  backlog #1). **Distractors homonymes CRITIQUES** (sinon faux positifs massifs) :
  `@`+handle/email/mention (`@marie`, `jean@exemple.fr`, `@everyone`) et gras
  markdown `**texte**` = spans=[]. NB : les lettres grecques EN TOUTES LETTRES
  (alpha/pi/theta…) étaient déjà couvertes (pi 599, alpha 320) — seule la forme
  `@` manquait. 204 lignes, 184 positifs / 20 négatifs. Commits `f054ad9`
  (`**`), `ba503d1` (varphi), `bfbf1ad` (0.11.0 fractions).

- `extension_v11_matrices.jsonl` + `extension_v11_keywords.jsonl` +
  `extension_v11_intervals.jsonl` — **3 angles morts comblés** (analyse de
  couverture 2026-06-23 : tous à 0 ou quasi-0 sur 12 824 exemples) :
  - **matrices** (112 l.) — **0 vraie matrice (≥2 colonnes) dans tout le corpus
    avant v11** ; 2×2→5×5 + vecteurs colonne + tuples/points, cellules
    **complexes** (fractions, puissances, indices, fonctions, décimales,
    négatifs), séparateur `,`/espace, délimiteurs `()`/`[]`, isolé+prose+autocap
    FR/EN. Distractors : parenthèses de prose (`(cahier, stylo, règle)`).
  - **keywords** (264 l.) — mots-clés en **TÊTE DE LIGNE** jamais couverts :
    décorations (bar/conj, abs/module, hat, racine, floor/ceil, norme, det,
    dim, nabla, partial), lycée (pgcd/ppcm, parmi, pourtout/ilexiste FR pleins),
    marqueurs (rond, congru, ±). **Distractors homonymes** (point critique) :
    bar=comptoir, module=cours, conj=conjugaison, racine=du mot, angle=pièce,
    parmi=préposition, rond=rond-point, floor=étage, hat=chapeau.
  - **intervals** (121 l.) — renfort `[a;b]`/`]a;b[`, bornes décimales/±inf/
    fractions, appartenance, unions ; distractors `;` de prose + citations `[3]`.
  Générés par `tools/ner-training/build_v11_{matrices,keywords,intervals}.py`.

> **⚠️ Liste d'entraînement corrigée (2026-06-23)** : la liste `EXTENSIONS` de
> `train_mathcursor.ipynb` s'arrêtait à **v8** → **v9 (marqueurs relation) et
> v10 (@grec/fractions) n'avaient jamais été inclus dans un retrain**. v9, v10
> et v11 (×3) ajoutés. `dataset_v2_all.jsonl` = snapshot périmé (8000 l.) **non
> utilisé** par le notebook (lit train/val/test + `EXTENSIONS`).

## Accents : FOLDÉS partout (décision 2026-06-11)

Le lexer moteur jette « caractère inattendu: é » → plutôt que d'apprendre
les diacritiques au lexer, les accents sont **strippés en amont** (mapping
1:1, offsets préservés) :
- **prod** : `AutocorrectNormalizer.FoldDiacritic` (appliqué par
  `WordContextReader`, donc avant NER + refiner + moteur) ;
- **entraînement** : table `FOLD` au chargement des `.jsonl` dans les deux
  notebooks (`load_jsonl`) — le modèle voit la même distribution que le
  runtime ; les fichiers corpus restent accentués (lisibles), inutile de
  les régénérer ;
- **tests locaux** : `NerCorpusFixture.LoadCorpus` folde pareil.
× et ÷ ne sont jamais foldés (opérateurs du vocabulaire moteur).

## Provenance

- `train.jsonl`, `val.jsonl`, `test.jsonl`, `dataset_v2_all.jsonl` : annotés à
  la main sur corpus FR/EN de cours de maths lycée (sources hétérogènes,
  synthétiques pour la plupart).
- `extension_fr.jsonl`, `extension_en.jsonl` : générés par
  `tools/ner-training/generate_extension.py` (graine 123, 2000 lignes au total).

## Règle

**Ne pas modifier les splits train/val/test existants sans raison.** Les
métriques publiées dans `metrics.json` y sont rattachées. Ajouter des
données = créer un fichier `extension_*.jsonl` et le concaténer au moment
de l'entraînement dans le notebook.

Pour régénérer `dataset_v2_all.jsonl` après ajout :
```bash
cat train.jsonl val.jsonl test.jsonl extension_fr.jsonl extension_en.jsonl \
    extension_v3_*.jsonl > dataset_v2_all.jsonl
```
