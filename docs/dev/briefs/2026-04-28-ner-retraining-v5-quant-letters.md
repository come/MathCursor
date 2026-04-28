# Brief — Réentraînement NER v5 : quantificateurs ∀/∃ et lettres ambiguës isolées

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-28
**Contexte :** Branche `lattice-engine`. Suite directe des briefs v3
([fixtures](2026-04-27-ner-retraining-keywords.md)) et v4
([keywords math](2026-04-27-ner-retraining-keywords.md)). Le moteur lattice
gère désormais le scope `forall`/`exists` (ADR
[2026-04-28-Feat-forall-scope-source-mutation.md](../decisions/2026-04-28-Feat-forall-scope-source-mutation.md))
mais le NER ne détecte pas ces patterns.
**Public cible :** agent ML/data autonome qui ne connaît pas le projet.

---

## 1. Le bug observé

Quand l'utilisateur tape `V x R` (intention : `\forall x \in R`) dans Word,
l'add-in MathCursor ne détecte rien comme zone math. Donc le moteur lattice
n'est jamais invoqué et la popup d'ambiguïté ne s'ouvre pas — l'utilisateur
ne peut même pas désambiguïser via Ctrl+Espace.

Cas observés en production qui plantent :

```
V x R                 → ∅ détecté         (attendu : "V x R" comme MATH)
V                     → ∅ détecté         (attendu : "V" comme MATH)
E y N                 → ∅ détecté         (attendu : "E y N" comme MATH)
forall x R            → ∅ détecté         (attendu : "forall x R" comme MATH)
forall x dans R       → ∅ détecté         (attendu : "forall x dans R" comme MATH)
exists y N            → ∅ détecté         (attendu : "exists y N" comme MATH)
soit V un volume      → ∅ ✓               (correct : pas de math, V est un mot)
```

Constat utilisateur (extrait conversation 2026-04-28) :
> « le NER est pas assez flex pour voir le truc »

## 2. Ce que le moteur lattice attend désormais

Le moteur lattice accepte deux nouvelles syntaxes au-delà du brief v4 :

### 2.1. Lettres-quantificateurs isolées
- `V` (suivi d'espace ou EOF) → propose désambig vers `∀` ou `√`
- `E` (suivi d'espace ou EOF) → propose désambig vers `∃`

Ces lettres seules nécessitent que le NER les marque comme zone MATH **même
si elles sont entourées de texte naturel** ou seules. Ex : `Soit V > 0`,
`On pose V un nombre`, `V x R` (où V doit être MATH au minimum).

### 2.2. Scope quantificateur explicite
- `forall <var> <set>` → `\forall var \in set`
- `exists <var> <set>` → `\exists var \in set`
- Variantes avec keyword `in`/`appartient`/`dans` entre var et set
- Args manquants matérialisés par des Holes (`\square`)

Cas typiques à reconnaître comme zone MATH unique :

```
forall x R
forall x in R
forall x dans R
forall x R, x > 0
exists y N
exists y dans N tel que y > 5
∀ x ∈ R                    (Unicode litéral, rare mais possible)
```

## 3. Cause racine côté corpus

À vérifier sur le corpus actuel `data/ner-corpus/train.jsonl` :

```bash
# attendu : compter les occurrences positives (avec span MATH) de
# "V ", "E ", "forall", "exists", "∀", "∃"
```

Hypothèse : les keywords `forall`/`exists` étaient dans le brief v4 mais sans
exemples *en syntax MathCursor* (pattern `forall <var> <set>` avec args). Et
les lettres isolées V/E n'ont jamais été couvertes.

## 4. Ce qu'on veut côté corpus

Ajouter ~200-400 exemples synthétiques **utilisant la syntax MathCursor** :

### 4.1. Lettres-quantificateurs isolées V/E

Cas positifs (V/E comme zone MATH) :

```jsonl
{"text": "Soit V > 0 un volume", "spans": [{"start": 5, "end": 10, "label": "MATH"}], "lang": "fr"}
{"text": "On pose V x R une relation", "spans": [{"start": 8, "end": 13, "label": "MATH"}], "lang": "fr"}
{"text": "Pour tout V dans R+", "spans": [{"start": 10, "end": 19, "label": "MATH"}], "lang": "fr"}
{"text": "E y N tel que y est pair", "spans": [{"start": 0, "end": 5, "label": "MATH"}], "lang": "fr"}
```

Distractors (V/E pas math) :

```jsonl
{"text": "le volume V est constant", "spans": [], "lang": "fr"}
{"text": "Voiture V12 super puissante", "spans": [], "lang": "fr"}
{"text": "E pour effectif", "spans": [], "lang": "fr"}
{"text": "il a obtenu une note E en physique", "spans": [], "lang": "fr"}
```

### 4.2. Scope quantificateur `forall`/`exists`

Cas positifs (la zone MATH inclut le keyword + var + set) :

```jsonl
{"text": "On a forall x R", "spans": [{"start": 5, "end": 15, "label": "MATH"}], "lang": "fr"}
{"text": "Soit forall x in R+ on a x^2 ≥ 0", "spans": [{"start": 5, "end": 32, "label": "MATH"}], "lang": "fr"}
{"text": "Pour exists y N tel que y > 0", "spans": [{"start": 5, "end": 15, "label": "MATH"}], "lang": "fr"}
{"text": "On note forall x dans R, x ≥ -1", "spans": [{"start": 8, "end": 31, "label": "MATH"}], "lang": "fr"}
{"text": "Théorème : forall n N*, n+1 > n", "spans": [{"start": 12, "end": 31, "label": "MATH"}], "lang": "fr"}
```

Anglais (corpus est multilingue FR/EN) :

```jsonl
{"text": "Let forall x R, x+0 = x", "spans": [{"start": 4, "end": 23, "label": "MATH"}], "lang": "en"}
{"text": "We have exists y N such that y > 5", "spans": [{"start": 8, "end": 17, "label": "MATH"}], "lang": "en"}
```

Distractors :

```jsonl
{"text": "il existe une solution", "spans": [], "lang": "fr"}
{"text": "for all practical purposes", "spans": [], "lang": "en"}
```

### 4.3. Variations à inclure
- Avec et sans keyword `in`/`appartient`/`dans` entre var et set
- Set simple (`R`, `N`, `Z`) ET set composé (`R+`, `R*`, `[0,1]`, `[0,1[`)
- Var simple (`x`) ET var composée (`(x,y)`, `x_n`)
- Préfixé par contexte naturel ("On a", "Soit", "Théorème :", "Pour")
- Suivi de relation : `forall x R, x ≥ 0` (la zone MATH va jusqu'à `0`)
- Quelques cas où V/E sont MATH mais isolés (V seul Ctrl+Espace activé)

## 5. Livrables attendus

1. **Script Python** `tools/ner-training/build_v5_quant_letters.py` qui génère
   les exemples synthétiques. Lecture obligatoire :
   `tools/ner-training/build_v4_keywords.py` comme référence de style.
2. **Fichier** `data/ner-corpus/extension_v5_quant_letters.jsonl`. ~200-400 lignes.
3. **Notebook** `train_mathcursor.ipynb` mis à jour pour concaténer v5 au train
   (en plus de v3+v4 déjà incluses).
4. **Modèle réentraîné** exporté en `mathcursor-ner-v5.zip` :
   `model_quantized.onnx` + `sentencepiece.bpe.model`.
5. **Rapport métriques** dans le PR :
   - F1 ≥ 0.99 sur `test.jsonl` actuel (non-régression vs v4)
   - 100% détection sur les cas §6 (positifs)
   - 0/n FP sur les distractors §6

## 6. Cas de test obligatoires (à valider avant merge)

### Positifs — la zone MATH doit couvrir au minimum les caractères indiqués

```
V x R                               → MATH au moins sur [V x R]
V                                   → MATH sur V
forall x R                          → MATH sur [forall x R]
forall x dans R                     → MATH sur [forall x dans R]
exists y N                          → MATH sur [exists y N]
On a forall x R, x ≥ 0              → MATH sur [forall x R, x ≥ 0]
Soit V > 0                          → MATH au moins sur [V > 0]
E y N tel que y > 5                 → MATH au moins sur [E y N]
```

### Négatifs (distractors) — aucune zone MATH attendue

```
le volume V est constant
soit V un volume
Voiture V12 super puissante
E comme effectif
il a obtenu un E en physique
il existe une solution
for all practical purposes
"toutes les voies ferrées"
"le V de la victoire"
```

## 7. Pipeline d'entraînement

Tout est documenté dans `tools/ner-training/README.md`. Résumé :
- **Modèle baseline :** `xlm-roberta-base`, fine-tuné 4 epochs, batch 16, lr 3e-5
- **Tokenizer :** SentencePiece Unigram (chargé direct par C# côté add-in)
- **Labels BIO :** O=0, B-MATH=1, I-MATH=2 (3 classes)
- **Notebook :** `tools/ner-training/train_mathcursor.ipynb` (Google Colab GPU T4)
- **Données :** `data/ner-corpus/train.jsonl` + `val.jsonl` + `test.jsonl`
- **Format JSONL :** `{"text": "...", "spans": [{"start": int, "end": int, "label": "MATH"}], "lang": "fr"|"en"}`
- **Métriques baseline (corpus v4) :** à reporter du dernier run dispo
- **Export :** ONNX quantized int8

Scripts de référence pour la génération :
- `tools/ner-training/build_v4_keywords.py` (le plus proche en style)
- `tools/ner-training/build_v3_*.py` (variantes thématiques)
- `tools/ner-training/generate_extension.py` (v2, gros volume synthétique)

## 8. Déploiement

Une fois le modèle validé :
1. Dézipper `mathcursor-ner-v5.zip` dans `D:\Software\DocMath\models\` (dev) ou
   `%LOCALAPPDATA%\MathCursor\models\` (prod).
2. Recharger Word — `ThisAddIn_Startup` recrée `MathNerDetector` automatiquement.
3. Tester les cas §6 dans Word :
   - Cas positifs : la popup s'ouvre quand l'utilisateur tape la phrase puis fait Ctrl+Espace
   - Cas négatifs : pas de popup

Le code C# côté add-in n'a **rien à changer** — l'API NER reste identique.

## 9. Pointers utiles

- ADR moteur scope forall : `docs/dev/decisions/2026-04-28-Feat-forall-scope-source-mutation.md`
- Brief v4 (référence de style) : `docs/dev/briefs/2026-04-27-ner-retraining-keywords.md`
- Code C# qui consomme le NER : `adapter-vsto/src/MathCursor/Detection/MathNerDetector.cs`
- Code C# qui appelle le NER : `adapter-vsto/src/MathCursor/Host/SuggestionService.cs`
- Spec format JSONL : `tools/ner-training/README.md` §"Fichiers"
- Vocabulary lattice (synonymes des keywords) :
  `core-csharp/src/MathCursor.Core/Lattice/Vocabulary.cs`
- Logs runtime : `%APPDATA%\MathCursor\logs\mathcursor.log` (préfixe `ner`)

## 10. Ce qu'il NE faut PAS faire

- ❌ Modifier `ExtendZoneBackwardWithKeyword` pour scanner les lettres V/E
  isolées — patch fragile qui absorberait du langage naturel.
- ❌ Hardcoder une liste de patterns côté SuggestionService — l'algo lattice
  est agnostique du vocabulaire, les patterns sont déduits du parser.
- ❌ Baisser `DefaultThreshold = 0.85` — c'est un compromis précision/rappel
  ajusté empiriquement.
- ❌ Élargir `max_position_embeddings` au-delà de 128 — hors scope, pas le bug.
- ❌ Changer de modèle baseline (XLM-R → autre) — la réduction de taille est
  un sujet distinct, à traiter après ce retrain.

## 11. Pré-requis avant de démarrer

- Lire le brief v4 (`2026-04-27-ner-retraining-keywords.md`) pour comprendre le
  style des extensions et la convention BIO.
- Lire l'ADR forall (`2026-04-28-Feat-forall-scope-source-mutation.md`) pour
  comprendre comment le moteur consomme ces patterns côté C#.
- Vérifier que l'extension v4 est bien intégrée dans `train.jsonl` actuel
  (sinon il faut concaténer v3+v4+v5 ensemble).

## 12. Phases ultérieures (HORS SCOPE de ce brief, mentionnées pour cohérence)

Ces patterns viendront dans des briefs v6+ une fois l'algo lattice étendu :
- Ensembles canoniques en désambig (`R` → `\mathbb{R}`, `N` → `\mathbb{N}`, etc.)
- Intervalles français `[a,b]`, `[a,b[`, `]a,b]` reconnus comme zone math
- Union/intersection : `U` entre intervalles → `\cup`, `inter` → `\cap`
- Notations primes/étoiles : `R*`, `R+`, `f'(x)`

Pour l'instant le brief v5 ne couvre QUE les quantificateurs ∀/∃ et les
lettres-quantificateurs isolées V/E. Reste petit pour faciliter le diff et
l'évaluation incrémentale.
