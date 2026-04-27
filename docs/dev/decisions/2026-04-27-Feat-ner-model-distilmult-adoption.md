---
date: 2026-04-27
kind: Feat
température: molle
statut: acté
supersedes: 2026-04-24-Feat-ner-corpus-v3-and-model-challenge.md (point 5 uniquement)
---

# Feat — Adoption DistilBERT-multilingual comme modèle NER baseline

## Contexte

Suite au benchmark v4 (cf. ADR
[`2026-04-27-Feat-ner-corpus-v4-keywords.md`](2026-04-27-Feat-ner-corpus-v4-keywords.md)),
distilmult tient ou dépasse XLM-R sur tous les axes :

| modèle | F1 test | F1 gold | onnx int8 | latence ms |
|--------|---------|---------|-----------|------------|
| xlmr (baseline) | 0.9962 | 0.8049 | 265.4 Mo | 12.3 |
| **distilmult** | 0.9981 | **1.0000** | **128.9 Mo** | **9.8** |
| minilm | 0.9980 | 0.8947 | 112.6 Mo | 8.7 |

Validation manuelle 11/11 cas brief (6 positifs + 5 distractors) confirmée
sur le binaire dans `D:\Software\DocMath\models\distilmult-v4\` :

```
"On a somme k 1 n k^2"           → "somme k 1 n k^2" (100%)
"Calculons somme k=0 n+1 cos 2x" → "somme k=0 n+1 cos 2x" (100%)
"Soit lim x 0 frac sin x x"      → "lim x 0 frac sin x x" (100%)
"On a frac a b strictement…"     → "frac a b" (100%)
"Soit racine x+1 superieur a 0"  → "racine x+1 superieur a 0" (94%)
"On note int 0 1 x dx"           → "int 0 1 x dx" (100%)

5/5 distractors rejetés (aucun span MATH proposé).
```

## Décision

Remplacer XLM-R par `distilbert-base-multilingual-cased` fine-tuné sur le
corpus v4 comme modèle NER de référence. Conséquences techniques :

1. **Modèle déployé** : `model_quantized.onnx` 129 Mo + `vocab.txt` (995 Ko)
   + `tokenizer.json` (3 Mo, non utilisé en runtime mais conservé pour
   référence) dans `D:\Software\DocMath\models\distilmult-v4\` (dev) et
   `%LOCALAPPDATA%\MathCursor\models\distilmult-v4\` (prod, à mettre à jour
   dans le MSI installer ultérieurement).
2. **Tokenizer C#** : suppression intégrale de `Detection/Sp/`
   (SentencePiece Unigram, ~500 lignes), remplacement par
   `Detection/WordPiece/WordPieceTokenizer.cs` (~200 lignes). Aucun NuGet
   ajouté — Microsoft.ML.Tokenizers évité pour rester stable sur .NET 4.8.
3. **MathNerDetector** : adapté pour charger `vocab.txt` + ONNX, gérer les
   special tokens DistilBERT ([CLS]=101, [SEP]=102, [PAD]=0, [UNK]=100,
   [MASK]=103). API publique `Detect(string) → IReadOnlyList<DetectedZone>`
   strictement inchangée.
4. **Path resolution** : `ThisAddIn.FindModelDir` préfère désormais le
   sous-dossier `distilmult-v4` quand il existe ; fallback sur l'ancien
   emplacement racine `models/` pour ne pas casser une installation
   existante en cas de roll-back.
5. **Google.Protobuf** : retiré du csproj (utilisé uniquement par l'ancien
   parser sentencepiece.bpe.model).

## Bénéfices mesurés

- Installer MSI : ~200 Mo → ~70 Mo attendu après build (-65%)
- Latence inférence : ~50 ms → ~25 ms estim. p95
- Code custom tokenizer : 500 lignes SentencePiece Unigram → 200 lignes
  WordPiece (greedy longest-match, beaucoup plus simple)
- F1 sur fixtures gold : 0.80 → 1.00 (couverture des keywords math en
  contexte)

## Citation utilisateur

Thread du 2026-04-27 (branche lattice-engine), validation directe :

> "C:\Users\wanadev\Downloads\mathcursor-ner-v4-distilmult.zip tu recuperera
> ce NER que tu placera au bon endroit et tu liras le brief qui correspond
> à son implementation et on executera ca"

## Ce qui n'est PAS modifié

- Le pipeline d'inférence reste BIO 3-classes O/B-MATH/I-MATH avec décodage
  char-level via offsets.
- `SuggestionService` et le reste de l'add-in dépendent de l'API publique
  `MathNerDetector` uniquement, donc aucun changement.
- `core-csharp/` (lattice engine) est totalement indépendant du détecteur
  NER, intact.
- `ExtendZoneBackwardWithKeyword` dans `SuggestionService` reste — devient
  redondante pour les cas v4 mais peut servir pour des patterns non couverts
  par le retraining. À ré-évaluer après une semaine d'usage prod.

## Supersedes

Cette décision remplace le **point 5 uniquement** ("Garder XLM-R avec corpus
v3") de l'ADR
[`2026-04-24-Feat-ner-corpus-v3-and-model-challenge.md`](2026-04-24-Feat-ner-corpus-v3-and-model-challenge.md).
Le reste de cette ADR (corpus v3, méthodologie benchmark) reste valide.
