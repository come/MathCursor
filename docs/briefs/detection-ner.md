# MathCursor — Détection des zones math (NER)

Ce document décrit la **chaîne de détection des expressions math** dans MathCursor :
quel modèle, quels fichiers, comment c'est branché dans Word, comment mettre à jour.

> **⚠️ Partiellement daté.** L'auto-détection NER n'est **pas branchée** dans la beta
> (déclenchement = **Ctrl+Espace** manuel). Les mentions `SuggestionService`/polling
> sont périmées (→ `ConversionController`, déclenchement explicite) ; chemins
> `D:\Software\DocMath\models` historiques. Conservé pour la **chaîne d'entraînement**
> du modèle NER (corpus `data/ner-corpus/`).

---

## 1. Vue d'ensemble

```
┌─────────────────────────────────────────────────────────────────┐
│  Word (paragraphe courant lu via WordContextReader)             │
│             │                                                    │
│             ▼                                                    │
│  SuggestionService (timer 200ms, polling le paragraphe)         │
│             │                                                    │
│             ▼                                                    │
│  MathNerDetector.Detect(text)                                   │
│             │                                                    │
│             ├──▶ SentencePieceTokenizer (text → token IDs)      │
│             │       │                                            │
│             │       ├──▶ Normalize (▁ + dummy prefix)           │
│             │       └──▶ Viterbi sur PieceTrie (vocab Unigram)  │
│             │                                                    │
│             ├──▶ ONNX Runtime (model_quantized.onnx)            │
│             │       └──▶ logits [1, seq_len, 3]                 │
│             │                                                    │
│             └──▶ DecodeBio (B-MATH / I-MATH / O → spans)        │
│                                                                  │
│             ▼                                                    │
│  IReadOnlyList<DetectedZone> (filtrées par seuil 0.85)          │
│             │                                                    │
│             ▼                                                    │
│  SuggestionPopupWindow (popup WPF transparente)                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## 2. Le modèle

**XLM-RoBERTa-base fine-tuné** sur un corpus FR/EN d'expressions mathématiques annotées.

- Architecture : `XLMRobertaForTokenClassification`
- 3 labels BIO : `O=0`, `B-MATH=1`, `I-MATH=2`
- Quantization int8 → ~265 Mo (.onnx)
- Tokenizer : SentencePiece **Unigram** (vocabulaire 250 000 pièces)
- Contexte max : 128 tokens (~80-100 mots)
- Multilingue natif : FR + EN, gère les pièges français (`scalaire`, `orthogonaux`, `racine` figurée…)

**Fichiers** (dossier `models/`, gitignored) :
| Fichier | Rôle | Taille |
|---|---|---|
| `model_quantized.onnx` | Réseau de neurones quantizé int8 | ~265 Mo |
| `sentencepiece.bpe.model` | Vocab + scores Unigram (protobuf SentencePiece) | ~5 Mo |
| `config.json` | Métadonnées modèle (id2label, vocab_size, etc.) | <1 Ko |
| `tokenizer.json` | Format HuggingFace (non utilisé en prod, ref) | ~16 Mo |
| `tokenizer_config.json` | Config tokenizer HF (non utilisé en prod, ref) | <1 Ko |
| `special_tokens_map.json` | Mapping spéciaux HF (non utilisé en prod, ref) | <1 Ko |
| `ort_config.json` | Config ONNX Runtime (non utilisé en prod, ref) | <1 Ko |

**En production seuls `model_quantized.onnx` et `sentencepiece.bpe.model` sont nécessaires.**

---

## 3. Où le code se trouve

Tout est dans `adapter-vsto/src/MathCursor/Detection/` :

```
adapter-vsto/src/MathCursor/Detection/
├── DetectedZone.cs              # POCO résultat (Start, End, Text, Confidence)
├── MathNerDetector.cs           # orchestrateur : tokenize → infer → decode BIO
└── Sp/                          # tokenizer SentencePiece pure C#
    ├── SentencePieceModel.cs    # parse .bpe.model (protobuf via Google.Protobuf)
    ├── PieceTrie.cs             # trie pour lookup rapide pendant Viterbi
    └── SentencePieceTokenizer.cs # normalisation + Viterbi + mapping SP→HF IDs
```

### 3.1. `DetectedZone.cs`
POCO immutable retourné par le détecteur :
```csharp
public sealed class DetectedZone
{
    public int Start { get; }       // offset caractère dans le texte original
    public int End { get; }         // offset caractère (exclusif)
    public string Text { get; }     // sous-chaîne du texte original
    public double Confidence { get; } // moyenne softmax sur les tokens du span
}
```

### 3.2. `MathNerDetector.cs`
La classe principale. Construit avec un dossier modèle :
```csharp
var det = new MathNerDetector("D:\\Software\\DocMath\\models", threshold: 0.85);
await det.WarmUpAsync(); // ~500ms, à faire au startup
var zones = det.Detect("on a f(x) = 2x + 1");
// → [{ Start: 5, End: 18, Text: "f(x) = 2x + 1", Confidence: 0.94 }]
```

Pipeline interne `Detect(text)` :
1. **Tokenize** via `SentencePieceTokenizer.Encode(text)` → liste de `(id, charStart, charEnd)`
2. **Truncate** à 128 tokens max (limite du modèle)
3. **Inférence ONNX** : prépare `input_ids` + `attention_mask` (tenseurs int64 [1, n]),
   appelle `_session.Run()`, récupère logits `[1, seq, 3]`
4. **Argmax + softmax** par token → label BIO + confidence
5. **DecodeBio** : parcourt les tokens (skip <s>, <pad>, </s>, <unk>),
   regroupe les séquences B-MATH … I-MATH I-MATH … en spans avec offsets caractères
6. **Filter** : ne garde que les spans avec `Confidence >= threshold` (0.85 par défaut)

### 3.3. `Sp/SentencePieceModel.cs`
Charge le fichier `sentencepiece.bpe.model` (protobuf binaire de Google) et expose :
- `Pieces` : liste de `{Text, Score, Type}` indexée par SP ID (250 000 entrées)
- `SpUnkId`, `SpBosId`, `SpEosId` : IDs internes SP pour les tokens spéciaux
- `EscapeWhitespaces`, `AddDummyPrefix`, etc. : flags de normalisation

Parse le protobuf via `Google.Protobuf.CodedInputStream`. Pas de codegen : on lit
champ par champ avec les bons numéros (cf. `sentencepiece_model.proto` officiel).

### 3.4. `Sp/PieceTrie.cs`
Trie de caractères → pièces. Pour chaque position du texte normalisé,
`MatchAt(text, pos)` énumère toutes les pièces qui démarrent à `pos`,
en O(K) où K = longueur max d'une pièce (~20 chars). Indispensable pour
que Viterbi tourne en temps raisonnable sur 250 000 pièces.

### 3.5. `Sp/SentencePieceTokenizer.cs`
Algorithme :
1. **Normalise** le texte :
   - Si `add_dummy_prefix` : préfixe avec `▁` (U+2581)
   - Si `escape_whitespaces` : remplace ` ` par `▁`
   - Garde un mapping `normalisé → original` pour reconstruire les offsets après
2. **Viterbi** (programmation dynamique) :
   - Pour chaque position `i`, calcule `bestScore[i]` = score max pour atteindre `i`
   - À chaque position, énumère via `PieceTrie` les pièces qui matchent
   - Garde le segment `(start, length, pieceId)` qui maximise le score
   - Si aucun match → fallback `<unk>` avec pénalité (worst score - 10)
3. **Reconstruction** depuis la fin → liste de pièces avec leur SP ID
4. **Mapping SP → HF** (convention HuggingFace XLMRobertaTokenizer) :
   - SP id 0 (unknown SP) → HF id 3 (`<unk>`)
   - SP id N (N > 0) → HF id N + 1 (offset fairseq)
   - Ajoute `<s>` (id 0) en début, `</s>` (id 2) en fin
5. **Renvoie** `IReadOnlyList<Token>` avec `(Id, CharStart, CharEnd)` pointant sur le texte ORIGINAL

---

## 4. Comment c'est branché dans Word

### 4.1. Démarrage (`ThisAddIn.ThisAddIn_Startup`)
```csharp
var modelDir = FindModelDir();   // %LOCALAPPDATA%\MathCursor\models | bin\models | D:\...
_ner = new MathNerDetector(modelDir);
_ = _ner.WarmUpAsync();          // ~500ms async, ne bloque pas Word

_suggestions = new SuggestionService(this.Application, _ner);
_suggestions.Install();          // démarre le polling 200ms

_keyboard = new KeyboardInterceptor
{
    OnTabPressed = () => false,    // pass-through
    OnEnterPressed = () => false,  // pass-through
    OnUpPressed = () => false,
    OnDownPressed = () => false,
    OnEscapePressed = HandleEscapePressed,  // seul handler actif
};
_keyboard.Install();
```

**Important** : Tab/Enter ne déclenchent AUCUNE conversion en mode validation.
On observe juste ce que le modèle détecte. Esc cache la popup.

### 4.2. Polling (`SuggestionService.CheckAndUpdate`)
Toutes les 200 ms :
1. Lit le **paragraphe courant** via `WordContextReader.ReadCurrentParagraph()`
   (borné par `Selection.Paragraphs[1].Range`, jamais cross-line)
2. Skip si paragraphe inchangé depuis le dernier check
3. **Lance l'inférence sur thread pool** (`Task.Run`) pour ne pas bloquer le thread UI
4. **Retour sur le thread UI** via `Dispatcher.BeginInvoke` pour mettre à jour la popup
5. Convertit `DetectedZone[]` → `SymbolChoice[]` (label = pourcentage de confiance)
6. Affiche la popup ou la cache si zéro zone

### 4.3. Recherche du dossier modèle (`ThisAddIn.FindModelDir`)
Tente dans l'ordre :
1. Variable d'environnement `MATHCURSOR_MODEL_DIR`
2. `%LOCALAPPDATA%\MathCursor\models\`
3. `<bin>\models\` (à côté de l'exe)
4. `D:\Software\DocMath\models\` (dev fallback hardcodé)

Le 1er qui contient `model_quantized.onnx` gagne.

---

## 5. Performances

| Étape | Temps |
|---|---|
| Chargement modèle (startup, async) | ~2-3 s parsing protobuf + ~500 ms warm-up ONNX |
| Tokenisation Unigram d'un paragraphe (~80 tokens) | ~5-10 ms |
| Inférence ONNX (paragraphe ~80 tokens) | ~30-80 ms (CPU) |
| Décodage BIO + filtrage | <1 ms |
| **Total Detect() après warm-up** | **~50-100 ms** |

Le polling 200 ms laisse une marge confortable. Si l'inférence prend plus longtemps que
le tick (charge CPU élevée), `_inferenceInFlight` empêche l'empilement.

---

## 6. Comment mettre à jour le modèle

### 6.1. Re-fine-tuning sur Colab
1. Notebook `mathcursor-ner-train` (référence à conserver)
2. Augmenter le corpus FR/EN avec les faux positifs / négatifs observés
3. Réentraîner XLM-RoBERTa-base
4. Quantize en int8 (ONNX Runtime ou Optimum)
5. Export :
   - `model_quantized.onnx`
   - `sentencepiece.bpe.model`
   - `config.json`
6. Zip le tout, transmettre

### 6.2. Déploiement local
1. Extraire dans `D:\Software\DocMath\models\` (ou path standard)
2. Recharger Word (l'add-in re-init le détecteur au prochain Startup)
3. Tester via les 4 cas de référence (cf. §7)

### 6.3. Évolutions futures envisageables
- Ajouter labels supplémentaires (B-VECT, B-EQUATION, B-LIMIT…) pour pré-typer l'expression
- Augmenter `max_position_embeddings` au-delà de 128 si besoin de paragraphes longs
- Distillation vers un modèle plus petit (DistilXLM-R) pour réduire latence
- ONNX Runtime DirectML / CUDA pour accélération GPU si disponible

---

## 7. Tests de référence (validation visuelle dans Word)

```
"on a f(x) = 2x + 1"                         → 1 zone, popup s'affiche
"j'ai oublié mon cahier d'exercices"         → 0 zone, popup fermée
"soit f(x) = 2x+1 et g(x) = 3x-1"            → 2 zones, popup s'affiche
"le produit scalaire de deux vecteurs"       → 0 zone (piège français), popup fermée
```

---

## 8. Limites connues

- **Faux positifs occasionnels** : le modèle peut signaler une zone math là où il n'y en a pas réellement. Acceptable en mode validation, à améliorer par re-fine-tuning.
- **Notre tokenizer C# n'implémente pas la normalisation NFKC complète** ni les transformations `precompiled_charsmap` du SentencePiece original. Pour des inputs ASCII / latin standard ça marche ; des caractères exotiques pourraient produire des tokens différents de ceux attendus par le modèle.
- **Tab/Enter ne convertissent rien** : c'est le mode validation. La conversion sera ré-activée plus tard une fois la détection validée.
- **Modèle pas distribué dans git** (.gitignore). Doit être présent sur la machine de dev/prod.
- **Multilingue limité à FR + EN** dans la version actuelle. DE/ES/IT viendront avec un nouveau modèle si besoin.

---

## 9. Dépendances NuGet

| Package | Version | Rôle | Taille |
|---|---|---|---|
| `Microsoft.ML.OnnxRuntime` | 1.20.1 | Moteur d'inférence ONNX (CPU) | ~80 Mo natifs |
| `Google.Protobuf` | 3.28.3 | Lecture du `.bpe.model` (CodedInputStream) | ~700 Ko |

Tout reste managed sauf le natif ONNX Runtime. Pas de dépendance externe à compiler ni à distribuer séparément.

---

## 10. Pourquoi ce design (vs alternatives)

**Pourquoi pas Microsoft.ML.Tokenizers ?**
Sa classe `SentencePieceTokenizer` ne supporte que **BPE**. XLM-RoBERTa utilise
**Unigram** (algorithme différent). Incompatible.

**Pourquoi pas BlingFire ?**
Le `.bin` officiel pour XLM-R n'est pas hébergé à un endroit stable / téléchargeable
fiablement. Aurait introduit une dépendance externe fragile.

**Pourquoi pas un wrapper natif SentencePiece-CSharp ?**
Aurait nécessité une DLL native supplémentaire à distribuer + maintenir. Notre impl
managed est lisible, contrôlable, testable.

**Pourquoi pas un subprocess Python ?**
Latence IPC, complexité gestion processus, dépendance Python sur les machines clientes.
Hors-scope pour un produit Word desktop.
