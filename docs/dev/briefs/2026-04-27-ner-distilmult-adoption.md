# Brief — Adoption DistilBERT-multilingual comme modèle NER baseline

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-27
**Contexte :** Branche `lattice-engine`. Suite immédiate du brief
[`2026-04-27-ner-retraining-keywords.md`](2026-04-27-ner-retraining-keywords.md).
**Public cible :** agent C#/MLOps autonome qui ne connaît pas le projet.

---

## 1. Pourquoi ce changement

Le benchmark v4 (corpus enrichi avec keywords math, voir ADR
[`2026-04-27-Feat-ner-corpus-v4-keywords.md`](../decisions/2026-04-27-Feat-ner-corpus-v4-keywords.md))
a tranché entre les 3 candidats :

| modèle | F1 test | F1 gold | onnx int8 | latence ms |
|--------|---------|---------|-----------|------------|
| xlmr (baseline) | 0.9962 | 0.8049 | 265.4 Mo | 12.3 |
| **distilmult** | 0.9981 | **1.0000** | **128.9 Mo** | **9.8** |
| minilm | 0.9980 | 0.8947 | 112.6 Mo | 8.7 |

**Validation manuelle** sur les 6 phrases positives + 5 distractors du brief
v4 keywords : **6/6 + 5/5** sur distilmult avec confidence ≥ 0.94.

**Bénéfices attendus en remplaçant XLM-R par distilmult :**
- Installer MSI : ~200 Mo → ~70 Mo (-65%)
- Latence inférence : ~50 ms → ~25 ms (estim. p95)
- Code C# tokenizer : ~500 lignes custom SentencePiece Unigram → 0 ligne custom
  (Microsoft.ML.Tokenizers gère WordPiece nativement avec NFKC propre)

## 2. Objectif

Remplacer XLM-R par `distilbert-base-multilingual-cased` (variant WordPiece) dans
le runtime VSTO, en conservant **strictement** l'API publique
`MathNerDetector.Detect(string) → IReadOnlyList<DetectedZone>`.

## 3. Livrables attendus

### a) Préparation modèle
1. Récupérer `mathcursor-ner-v4-distilmult.zip` depuis Google Drive
   `MyDrive/mathcursor/` (généré par
   `tools/ner-training/train_mathcursor_benchmark.ipynb` cell 20).
2. Vérifier le contenu : doit contenir
   - `model_quantized.onnx` (~129 Mo)
   - `vocab.txt` (vocab WordPiece DistilBERT)
   - `tokenizer.json` (config tokenizer fast)
   - `tokenizer_config.json` (paramètres : `do_lower_case=False`, etc.)
   - `special_tokens_map.json`
3. Dézipper dans :
   - `D:\Software\DocMath\models\distilmult-v4\` (dev local)
   - `%LOCALAPPDATA%\MathCursor\models\distilmult-v4\` (prod, à mettre à jour
     dans le MSI installer)
4. Mettre à jour le path par défaut dans la config de
   `adapter-vsto/src/MathCursor/Configuration/` (chercher l'endroit où le path
   actuel `xlmr-onnx-int8` est défini).

### b) Tokenizer C# — refonte

**À supprimer intégralement :**
- `adapter-vsto/src/MathCursor/Detection/Sp/` — tout le dossier (tokenizer
  SentencePiece Unigram custom, ~500 lignes).
- Tests associés `adapter-vsto/tests/MathCursor.Tests/Detection/Sp/` s'ils
  existent.

**À ajouter :**
- NuGet `Microsoft.ML.Tokenizers` (vérifier version supportant `BertTokenizer`
  ou `WordPieceTokenizer` — ≥ 0.22.0 ou ≥ 1.0).
- Si `BertTokenizer.Create()` ne suffit pas, écrire un wrapper léger qui :
  - Charge `vocab.txt` (un token par ligne)
  - Tokenize avec WordPiece (préfixe subword `##`)
  - Renvoie `(input_ids, attention_mask, offsets[])` où `offsets[i] =
    (charStart, charEnd)` dans le texte original — indispensable pour la
    reconstruction des spans MATH char-level.
- Special tokens à gérer :
  - `[CLS]` (id 101 généralement) en début → label = -100 (ignoré)
  - `[SEP]` (id 102) en fin → label = -100
  - `[PAD]` (id 0) en padding → label = -100
  - `[UNK]` (id 100) si OOV
  - `[MASK]` (id 103) — non utilisé en inférence, à ignorer si rencontré
- Hyper-paramètres :
  - `do_lower_case = False` (modèle CASED)
  - `max_length = 128`
  - `truncation = True`
  - `padding = False` (on padde côté ONNX si besoin, ou on n'envoie qu'une
    seule séquence à la fois — voir l'usage actuel)

### c) MathNerDetector — adaptation

Fichier : `adapter-vsto/src/MathCursor/Detection/MathNerDetector.cs`.

Modifications attendues :
1. Constructeur charge `vocab.txt` + `tokenizer.json` du dossier modèle (au
   lieu de `sentencepiece.bpe.model`).
2. Méthode interne `Tokenize(string)` utilise le nouveau tokenizer WordPiece.
3. La pipeline d'inférence reste la même structure :
   1. Tokenize → `(input_ids, attention_mask, offsets)`
   2. Forward ONNX → logits `[seq_len, 3]`
   3. Argmax → labels BIO `[seq_len]`
   4. Reconstruction des spans char-level via `offsets` :
      ```
      pour chaque token i (en ignorant special tokens où offset == (0,0)) :
        cas B-MATH : ouvrir span avec start = offsets[i].start
        cas I-MATH si span ouvert : étendre end = offsets[i].end
        cas O si span ouvert : fermer span
      ```
4. **Particularité WordPiece** : les subwords (`##xxx`) ont des offsets
   contigus dans le texte original, donc la reconstruction marche pareil
   qu'avec SentencePiece. Pas de fusion explicite à faire.
5. API publique inchangée :
   ```csharp
   public IReadOnlyList<DetectedZone> Detect(string text)
   ```

### d) Tests d'intégration

Fichier : `adapter-vsto/tests/MathCursor.Tests/Detection/MathNerDetectorTests.cs`.

Tests à mettre à jour ou ajouter :
1. **Smoke test** : `Detect("On a somme k 1 n k^2")` retourne une liste avec
   1 `DetectedZone` couvrant `somme k 1 n k^2` (start=5, end=20), confidence
   ≥ 0.85.
2. **Cas positifs** (6 du brief v4 keywords) : tous doivent renvoyer un span
   MATH avec confidence ≥ 0.85.
3. **Cas distractors** (5 du brief v4 keywords) : aucun ne doit renvoyer de
   span MATH.
4. **Test perf** : latence sur 100 phrases du corpus test < 30 ms p95.
5. Garder les anciens tests qui couvraient le pipeline générique
   (encoding ASCII / unicode, troncation à 128 tokens, etc.).

## 4. Cas de test obligatoires

Phrases positives (span MATH, confidence ≥ 0.85) :
```
On a somme k 1 n k^2
Calculons somme k=0 n+1 cos 2x
Soit lim x 0 frac sin x x
On a frac a b strictement positif
Soit racine x+1 superieur a 0
On note int 0 1 x dx
```

Phrases distractors (aucun span MATH) :
```
La pyramide a un sommet visible
J'ai mis ma somme de côté
Les fractales ont une structure auto-similaire
La limite d'âge pour ce concours
Le racisme est inacceptable
```

Note observée pendant la validation : sur `Soit racine x+1 superieur a 0`,
distilmult absorbe `racine x+1 superieur a 0` (et pas juste `racine x+1`) avec
confidence 0.94. C'est plus *greedy* que strictement attendu mais
sémantiquement correct (l'inégalité fait partie de l'expression). Comportement
acceptable.

## 5. ADR de suivi à créer

Fichier : `docs/dev/decisions/2026-04-27-Feat-ner-model-distilmult-adoption.md`.

Template :
- **Kind** : Feat
- **Température** : molle (réversible — on peut revenir à XLM-R si problème)
- **Statut** : acté
- **Supersedes partiellement** : section "Garder XLM-R avec corpus v3" de
  [`2026-04-24-Feat-ner-corpus-v3-and-model-challenge.md`](../decisions/2026-04-24-Feat-ner-corpus-v3-and-model-challenge.md)
  (point 5 de la "Décision"). L'ADR v3 reste valide pour tout le reste, juste
  ce point passe à "remplacé par adoption distilmult".
- **Citation utilisateur** : ce thread + score validation 6/6 + 5/5.

## 6. Pointers utiles

| Fichier | Rôle |
|---------|------|
| `adapter-vsto/src/MathCursor/Detection/MathNerDetector.cs` | Détecteur (à adapter) |
| `adapter-vsto/src/MathCursor/Detection/Sp/` | Tokenizer SentencePiece (à supprimer) |
| `adapter-vsto/src/MathCursor/Host/SuggestionService.cs` | Consommateur (méthode `CheckAndUpdate`, polling 200ms — ne pas modifier) |
| `tools/ner-training/train_mathcursor_benchmark.ipynb` | Notebook qui a produit le modèle |
| `data/ner-corpus/extension_v4_keywords.jsonl` | Corpus d'entraînement v4 |
| `docs/dev/decisions/2026-04-27-Feat-ner-corpus-v4-keywords.md` | ADR corpus v4 |
| `docs/dev/decisions/2026-04-24-Feat-ner-corpus-v3-and-model-challenge.md` | ADR du challenge initial (anticipait WordPiece) |

## 7. Filet de sécurité — `ExtendZoneBackwardWithKeyword`

Méthode dans `SuggestionService.cs`. Devient redondante pour le cas
`somme k 1 n` (le NER absorbe maintenant directement). **Laisser tel quel pour
l'instant** — peut encore servir pour des patterns non couverts par v4.
À ré-évaluer après une semaine d'usage en production via les logs
`%APPDATA%\MathCursor\logs\mathcursor.log` (compter combien de fois la méthode
est déclenchée — si proche de 0, on peut supprimer).

## 8. Ce qu'il NE faut PAS faire

- ❌ Modifier la signature de `MathNerDetector.Detect(string)` — l'API publique
  reste identique au caractère près.
- ❌ Garder `Detection/Sp/` en parallèle "au cas où" — supprimer franchement,
  git permet de revenir.
- ❌ Hardcoder le path du modèle — utiliser le même mécanisme de configuration
  qu'aujourd'hui (chercher où `xlmr-onnx-int8` est référencé).
- ❌ Activer `do_lower_case=True` — distilmult est CASED.
- ❌ Toucher à `SuggestionService.cs` ou au reste de l'add-in (dépend de
  `MathNerDetector` via son API publique uniquement).
- ❌ Toucher à `core-csharp/` — c'est la couche métier, indépendante du
  détecteur NER.

## 9. Validation finale (ordre d'exécution)

1. `cd D:\Software\DocMath && dotnet build MathCursor.sln` → compile sans
   erreur ni warning C#.
2. `dotnet test adapter-vsto/tests/MathCursor.Tests/` → tous les tests passent,
   notamment les 6+5 cas brief.
3. Lancer Word avec l'add-in en mode dev (F5 dans Visual Studio sur
   `MathCursor.csproj`).
4. Tester manuellement dans Word : taper les 6 phrases positives + 5
   distractors → comportement conforme.
5. Vérifier les logs `%APPDATA%\MathCursor\logs\mathcursor.log` — préfixe
   `ner` — pas d'erreur, latences attendues (~25 ms par phrase).
6. Mesurer la taille de l'installer MSI après build → cible ~70 Mo.
7. Une fois validé : créer un commit séparé par lot logique
   (1) modèle déployé, (2) tokenizer C# remplacé, (3) MathNerDetector adapté +
   tests, (4) ADR créé.

## 10. Estimations

- Préparation modèle : 30 min
- Tokenizer C# (lecture Microsoft.ML.Tokenizers + intégration) : 4-6 h
- MathNerDetector + tests : 3-4 h
- Validation manuelle dans Word : 1 h
- ADR : 30 min
- **Total estimé** : 1-2 jours

Si Microsoft.ML.Tokenizers ne supporte pas WordPiece de manière satisfaisante
(à valider en premier !), prévoir 1 jour supplémentaire pour écrire un wrapper
WordPiece minimal en C# pur (~150-200 lignes, beaucoup plus simple que
SentencePiece Unigram).
