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
