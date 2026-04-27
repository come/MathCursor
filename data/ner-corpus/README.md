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

Les fichiers `extension_v3_*.jsonl` seront ajoutés par les scripts de
génération v3 (voir `tools/ner-training/build_v3_*.py`) :
- `extension_v3_fixtures.jsonl` — fixtures `specs/test-fixtures/phase1-zone-detection.json`
- `extension_v3_superscript2.jsonl` — cas du caractère AZERTY `²`
- `extension_v3_false_positives.jsonl` — contre-exemples pour réduire les FP

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
