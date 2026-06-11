# Entraînement NER MathCursor

Pipeline de fine-tuning du détecteur de zones math. Tout tourne sur Google
Colab (GPU T4 gratuit) avec les .jsonl de `data/ner-corpus/`.

## Fichiers

| Fichier | Rôle |
|---------|------|
| `train_mathcursor.ipynb` | Notebook de référence — fine-tune XLM-RoBERTa-base + export ONNX int8 |
| `train_mathcursor_benchmark.ipynb` | Dérivé qui teste 3 modèles (XLM-R, DistilBERT mult, MiniLM mult) |
| `generate_extension.py` | Script v2 — génère `extension_fr.jsonl` et `extension_en.jsonl` (2000 lignes synthétiques) |
| `build_v3_fixtures.py` | v3 — convertit `specs/test-fixtures/phase1-zone-detection.json` en JSONL |
| `build_v3_superscript2.py` | v3 — génère les cas `x²`, `(x+1)²`… (absents du corpus v2) |
| `build_v3_false_positives.py` | v3 — génère des contre-exemples pour réduire les faux positifs |
| `build_v4_keywords.py` | v4 — keyword math en tête de zone inclus dans le span (somme, frac…) |
| `build_v5_quant_letters.py` | v5 — quantificateurs lettres |
| `build_v6_recent_features.py` / `build_v6_1_targeted.py` | v6 — vecteurs, coordonnées |
| `build_regression_v1_gold.py` | corpus gold du test F1 anti-rechute (`MathNerInferenceTests`) |
| `build_v8_nary_short_forms.py` | v8 — iint/iiint, formes courtes n-aires, mots-clés nus, autocap Word (2026-06-11) |

> Dossier rapatrié de `D:\Software\DocMath\tools\ner-training` le 2026-06-11.
> Le script de `extension_v7_conjunction_at_start.jsonl` n'a pas été retrouvé
> (l'extension existe dans `data/ner-corpus/`, le générateur est perdu).

## Quel notebook pour ré-entraîner ?

**Le modèle déployé (`distilmult-v5`, DistilBERT multilingue WordPiece) sort de
`train_mathcursor_benchmark.ipynb`** — c'est lui qu'il faut relancer pour un
retrain de prod (il entraîne XLM-R + DistilBERT, compare, et zippe le gagnant
en `mathcursor-ner-v6-<short>.zip`). Le notebook « principal » ci-dessous est
la baseline XLM-R historique. Les deux sont à jour corpus v8 (2026-06-11) :
déposer TOUS les `.jsonl` de `data/ner-corpus/` dans le Drive, la cellule 1
échoue si un fichier manque.

Après retrain : dézipper en `distilmult-v6` et bumper le nom de dossier dans
`ThisAddIn.TryFindModelDir` + `NerCorpusFixture` (ils cherchent `distilmult-v5`
en dur), puis relancer `MathNerInferenceTests` (seuils F1 anti-rechute).

## Lancer un entraînement (notebook principal)

1. Ouvrir `train_mathcursor.ipynb` dans Google Colab.
2. Créer un dossier `MyDrive/mathcursor` dans ton Google Drive.
3. Y déposer `train.jsonl`, `val.jsonl`, `test.jsonl` depuis
   `data/ner-corpus/` (+ éventuellement les `extension_v3_*.jsonl`
   concaténés au train).
4. Runtime → Modifier le type d'exécution → **GPU T4**.
5. Runtime → Tout exécuter.
6. Récupérer `mathcursor-ner-int8.zip` dans le dossier Drive à la fin
   (archive du dossier `model-onnx-int8/`).

Durée d'entraînement : ~2-3 minutes sur T4 pour 6400 lignes × 4 epochs.

## Déploiement local

1. Dézipper `mathcursor-ner-int8.zip` dans
   `%LOCALAPPDATA%\MathCursor\models\` (ou `D:\Software\DocMath\models\` en dev).
2. Fichiers indispensables en prod : `model_quantized.onnx` et
   `sentencepiece.bpe.model` (XLM-R) **ou** `tokenizer.json` (DistilBERT/MiniLM).
3. Recharger Word — `ThisAddIn_Startup` recrée `MathNerDetector`.
4. Tester avec les 4 cas de référence de `briefs/detection-ner.md` §7.

## Hyperparamètres actuels (baseline XLM-R)

```python
MODEL_NAME = 'xlm-roberta-base'
MAX_LENGTH = 128
BATCH_SIZE = 16
LEARNING_RATE = 3e-5
NUM_EPOCHS = 4
WEIGHT_DECAY = 0.01
fp16 = True
```

## Métriques de référence (baseline XLM-R sur corpus v2)

```
Precision: 0.9949
Recall:    0.9962
F1:        0.9956
```

Testé aussi sur un petit set de phrases hors-distribution
(cf. notebook cell 19) — performances correctes mais quelques FP ("sin aine",
"pas 1") qui motivent l'extension `v3_false_positives`.

## Challenge taille (benchmark)

Voir `train_mathcursor_benchmark.ipynb`. Objectif : réduire le poids du
modèle quantizé int8 tout en gardant F1 ≥ 0.98 sur test et 100% sur
fixtures projet.

Tailles attendues après quantization int8 :

| Modèle | Paramètres | .onnx int8 | Tokenizer |
|--------|-----------|-----------|-----------|
| `xlm-roberta-base` (baseline) | 278M | 265 Mo | SentencePiece Unigram |
| `distilbert-base-multilingual-cased` | 134M | ~75 Mo | WordPiece |
| `microsoft/Multilingual-MiniLM-L12-H384` | 117M | ~40 Mo | WordPiece |

Si DistilBERT ou MiniLM tient le F1, bénéfice secondaire : on peut
supprimer `adapter-vsto/src/MathCursor/Detection/Sp/` (tokenizer Unigram
custom C#) car `Microsoft.ML.Tokenizers` gère WordPiece nativement.

## Dépendances (Python)

```
transformers
datasets
accelerate
seqeval
optimum[onnxruntime]
onnx
onnxruntime
sentencepiece  # pour XLM-R uniquement
protobuf
```

Pré-installées par le notebook. Désinstaller `diffusers` au démarrage
(pré-installé par Colab, casse l'environnement).
