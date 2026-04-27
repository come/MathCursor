# Brief — Réentraînement NER : couvrir les keywords math en début de zone

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-27
**Contexte :** Pivot lattice-engine en cours, branche `lattice-engine`.
**Public cible :** agent ML/data autonome qui ne connaît pas le projet.

---

## 1. Le bug observé

Quand un élève tape `On a somme k 1 n` dans Word, l'add-in MathCursor doit
détecter `somme k 1 n` comme zone math. Aujourd'hui le NER ne détecte que
` 1 n` (sans absorber le mot-clé `somme` ni la variable `k`).

Reproduction (logs de l'add-in à `%APPDATA%\MathCursor\logs\mathcursor.log`) :

```
ner pick caret=12 target=[10..12] " k" (100 %) dist=0
ner extended target=[5..12] "somme k" (100 %)        ← OK ici, somme absorbé via patch C#
…
ner pick caret=15 target=[12..15] " 1 n" (97 %) dist=0
ner extended target=[12..15] " 1 n" (97 %)            ← bug : somme PAS absorbé
ner engine zone=" 1 n" suggestions=1                  ← moteur reçoit "1 n" → produit "1n", inutile
```

Le code `ExtendZoneBackwardWithKeyword` (adapter-vsto/src/MathCursor/Host/SuggestionService.cs)
ne fait qu'**un saut** en arrière. Il absorbe `somme` quand celui-ci touche
directement la zone, mais pas quand un mot intermédiaire comme `k` s'intercale.

**On ne veut PAS étendre artificiellement ce patch C# à N mots** — risque
d'absorber des mots non-math. La vraie solution est côté modèle : que le NER
classe `somme k 1 n` comme MATH d'un seul tenant.

## 2. Cause racine côté corpus

Couverture actuelle dans `data/ner-corpus/train.jsonl` (commit `60b8af4`) :

```
keyword | total | avec span MATH dans la phrase
somme   |   25  |   0     ← AUCUN positif math !
sum     |   72  |  53
lim     |  262  | 236
racine  |   18  |   5
frac    |   11  |   0     ← AUCUN positif math !
vec     |  342  | 285
int     |  187  | 139
```

Les 25 occurrences de `somme` sont toutes du sens commun ("somme d'argent",
"somme de côte"), spans vides. Idem `frac` (fractions/fractales). Le modèle a
donc appris que `somme` n'introduit *jamais* une zone math.

Pour `racine`, les 5 positifs sont en syntax langage naturel ("racine de 2"),
pas la syntax MathCursor `racine x` ou `racine x^2 + 1`.

## 3. Ce qu'on veut côté corpus

Ajouter ~300-500 exemples synthétiques **utilisant la syntax MathCursor**, où
le keyword math en début est inclus dans le span MATH. Exemples :

```jsonl
{"text": "On a somme k 1 n k^2", "spans": [{"start": 5, "end": 20, "label": "MATH"}], "lang": "fr"}
{"text": "Calculons somme k=0 n+1 cos2x", "spans": [{"start": 10, "end": 29, "label": "MATH"}], "lang": "fr"}
{"text": "frac a b avec a et b reels", "spans": [{"start": 0, "end": 8, "label": "MATH"}], "lang": "fr"}
{"text": "Soit racine x + 1 strictement positif", "spans": [{"start": 5, "end": 17, "label": "MATH"}], "lang": "fr"}
{"text": "lim x 0 frac sin x x = 1", "spans": [{"start": 0, "end": 24, "label": "MATH"}], "lang": "fr"}
```

Keywords à couvrir (avec leurs alias) :
- `somme`, `sum`
- `lim`, `limite`
- `int`, `integrale`, `intégrale`
- `racine`, `sqrt`, `rac`
- `frac`
- `vec`, `vecteur`
- `prod`, `produit`
- `forall`, `exists`, `inf`, `infini`

Variations à inclure :
- Avec ou sans `=` après la variable (`sum k 1 n` ET `sum k=1 n`)
- Avec ou sans flèche (`lim x 0` ET `lim x -> 0`)
- Body simple (atome) ET body composé (`f(x)`, `2x+1`, `cos x`)
- Préfixé par contexte naturel ("On a", "Soit", "Calculons", "Si", etc.)
- Quelques cas anglais ("Let sum k 1 n", "We have lim x 0")
- Distractors : "le sommet", "fractale", "limite d'âge" (déjà partiellement
  couverts mais à équilibrer)

## 4. Pipeline d'entraînement

Tout est documenté dans `tools/ner-training/README.md`. Résumé :

- **Modèle baseline :** `xlm-roberta-base`, fine-tuné 4 epochs, batch 16, lr 3e-5
- **Tokenizer :** SentencePiece Unigram (chargé direct par C# côté add-in)
- **Labels BIO :** O=0, B-MATH=1, I-MATH=2 (3 classes)
- **Notebook :** `tools/ner-training/train_mathcursor.ipynb` (Google Colab GPU T4)
- **Données :** `data/ner-corpus/train.jsonl` + `val.jsonl` + `test.jsonl`
- **Format JSONL :** `{"text": "...", "spans": [{"start": int, "end": int, "label": "MATH"}], "lang": "fr"|"en"}`
- **Métriques baseline (corpus v2) :** Precision 0.9949 / Recall 0.9962 / F1 0.9956
- **Export :** ONNX quantized int8, fichier `model_quantized.onnx` + `sentencepiece.bpe.model`

Scripts existants pour générer des extensions corpus :
- `tools/ner-training/generate_extension.py` (v2, 2000 lignes synthétiques)
- `tools/ner-training/build_v3_fixtures.py` (à partir de specs/test-fixtures/)
- `tools/ner-training/build_v3_superscript2.py` (cas `x²`)
- `tools/ner-training/build_v3_false_positives.py` (contre-exemples)

Pattern à suivre : créer `tools/ner-training/build_v4_keywords.py` qui génère
`data/ner-corpus/extension_v4_keywords.jsonl`, puis concaténer au train avant
fine-tuning.

## 5. Livrables attendus

1. **Script Python `tools/ner-training/build_v4_keywords.py`** qui génère des
   exemples synthétiques pour les keywords listés §3. Lecture obligatoire :
   `tools/ner-training/generate_extension.py` comme référence de style.
2. **Fichier `data/ner-corpus/extension_v4_keywords.jsonl`** produit par le
   script. ~300-500 lignes.
3. **Notebook `train_mathcursor.ipynb` mis à jour** pour inclure
   l'extension v4 dans le train (concat avec `train.jsonl`).
4. **Modèle réentraîné** exporté en `mathcursor-ner-v4.zip` (mêmes fichiers
   que la baseline : `model_quantized.onnx` + `sentencepiece.bpe.model`).
5. **Rapport métriques** dans le PR :
   - Precision/Recall/F1 sur `test.jsonl` actuel (pas de régression : F1 ≥ 0.99)
   - 100% détection sur les 5 cas de §3 (test manuel ou ajout fixtures)
   - Pas de FP sur "le sommet de la pyramide", "j'ai mis ma somme", "c'est une fractale"

## 6. Cas de test obligatoires (à valider avant merge)

Phrases à passer en inférence — toutes doivent renvoyer un span MATH couvrant
le keyword + body, avec confidence > 0.85 :

```
On a somme k 1 n k^2
Calculons somme k=0 n+1 cos 2x
Soit lim x 0 frac sin x x
On a frac a b strictement positif
Soit racine x+1 superieur a 0
On note int 0 1 x dx
```

Et phrases à NE PAS détecter (faux positifs à éviter) :

```
La pyramide a un sommet visible
J'ai mis ma somme de côté
Les fractales ont une structure auto-similaire
La limite d'âge pour ce concours
Le racisme est inacceptable
```

## 7. Déploiement

Une fois le modèle validé :
1. Dézipper `mathcursor-ner-v4.zip` dans `D:\Software\DocMath\models\` (dev) ou
   `%LOCALAPPDATA%\MathCursor\models\` (prod).
2. Recharger Word — `ThisAddIn_Startup` recrée `MathNerDetector` automatiquement.
3. Tester les 6 phrases positives §6 dans Word.

Le code C# côté add-in n'a **rien à changer** — l'API NER reste identique
(`MathNerDetector.Detect(string) → IReadOnlyList<DetectedZone>`).

## 8. Pointers utiles

- Code C# qui consomme le NER : `adapter-vsto/src/MathCursor/Detection/MathNerDetector.cs`
- Code C# qui appelle le NER : `adapter-vsto/src/MathCursor/Host/SuggestionService.cs`
  (méthode `CheckAndUpdate`, polling 200ms)
- Spec format JSONL : `tools/ner-training/README.md` §"Fichiers"
- ADR sur le pivot ML : `docs/dev/decisions/2026-04-24-Feat-ner-corpus-v3-and-model-challenge.md`
- Logs runtime de l'add-in : `%APPDATA%\MathCursor\logs\mathcursor.log` (préfixe `ner`)

## 9. Ce qu'il NE faut PAS faire

- ❌ Modifier `ExtendZoneBackwardWithKeyword` pour scanner N mots — patch
  fragile qui absorberait du langage naturel.
- ❌ Hardcoder une liste de keywords côté SuggestionService — l'algo lattice
  est fait pour être agnostique du vocabulaire ; les keywords sont gérés par
  `core-csharp/src/MathCursor.Core/Lattice/Vocabulary.cs`.
- ❌ Augmenter le seuil `DefaultThreshold = 0.85` — c'est un compromis déjà
  ajusté, le toucher décalerait la précision/rappel ailleurs.
- ❌ Changer de modèle baseline (XLM-R) sans benchmark complet — voir
  `train_mathcursor_benchmark.ipynb` pour le protocole de comparaison.
