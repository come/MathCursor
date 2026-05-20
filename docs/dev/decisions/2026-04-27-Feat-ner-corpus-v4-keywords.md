# Feat — Corpus NER v4 (keywords math en début de zone)

**Date :** 2026-04-27
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

1. Ajouter au corpus NER une extension `data/ner-corpus/extension_v4_keywords.jsonl`
   (~300-500 lignes synthétiques) où le **keyword math** en début de zone est
   inclus dans le span MATH. Mots couverts : `somme`/`sum`, `lim`/`limite`,
   `int`/`integrale`/`intégrale`, `racine`/`sqrt`/`rac`, `frac`, `vec`/`vecteur`,
   `prod`/`produit`, `forall`, `exists`, `inf`/`infini`.
2. Générer cette extension via `tools/ner-training/build_v4_keywords.py`
   (mêmes conventions que `build_v3_*.py` : `random.seed`, `make_span`,
   `validate`, sortie JSONL au format corpus).
3. Concaténer cette extension au train avant fine-tuning, dans
   `tools/ner-training/train_mathcursor.ipynb`. Val/test inchangés.
4. Garder **XLM-R baseline** comme modèle entraîné — la réduction de taille
   modèle (minilm/distilmult/int4/distillation) est un axe distinct, à
   re-traiter après validation de cette extension v4.
5. Critère d'adoption :
   - **F1 ≥ 0.99 sur `test.jsonl`** (non-régression vs baseline)
   - **100% détection** sur les 6 phrases keyword listées dans le brief
     (`On a somme k 1 n k^2`, `Calculons somme k=0 n+1 cos 2x`,
     `Soit lim x 0 frac sin x x`, `On a frac a b strictement positif`,
     `Soit racine x+1 superieur a 0`, `On note int 0 1 x dx`)
   - **Zéro FP** sur 5 distractors (`la pyramide a un sommet visible`,
     `j'ai mis ma somme de côté`, `les fractales ont une structure auto-similaire`,
     `la limite d'âge pour ce concours`, `le racisme est inacceptable`).

## Pourquoi

### Cause racine identifiée dans le corpus

Couverture actuelle de `data/ner-corpus/train.jsonl` (commit `60b8af4`,
diagnostic du brief) :

```
keyword | total | avec span MATH dans la phrase
somme   |   25  |   0     ← AUCUN positif math
sum     |   72  |  53
lim     |  262  | 236
racine  |   18  |   5      (positifs en syntax langage naturel "racine de 2",
                            pas en syntax MathCursor "racine x")
frac    |   11  |   0     ← AUCUN positif math
vec     |  342  | 285
int     |  187  | 139
```

`somme` et `frac` ont 0 occurrence positive. Le modèle a appris que ces mots
n'introduisent **jamais** une zone math. Conséquence runtime : sur
`On a somme k 1 n`, le NER ne renvoie que ` 1 n`, ce qui produit un OMath
inutile (`1n`).

### Pourquoi la fix doit être côté corpus, pas côté C#

Le code `ExtendZoneBackwardWithKeyword`
(`adapter-vsto/src/MathCursor/Host/SuggestionService.cs`) fait déjà un saut
en arrière pour absorber `somme` quand il touche directement la zone. L'étendre
à N mots (cas `somme k 1 n` où `k` s'intercale) absorberait du langage naturel
arbitraire — patch fragile. La fix correcte est que le NER classe
`somme k 1 n` comme MATH d'un seul tenant.

### Pourquoi pas changer de modèle maintenant

Le bug "keywords pas absorbés" est indépendant du choix de modèle : il
viendrait aussi de minilm ou distilmult tant que le corpus n'a pas la
couverture. Enrichir d'abord, benchmarker la taille ensuite, sinon on
multiplie les rounds (corpus → modèle → corpus → modèle).

L'ADR
[2026-04-24-Feat-ner-corpus-v3-and-model-challenge.md](2026-04-24-Feat-ner-corpus-v3-and-model-challenge.md)
avait posé ce critère ("F1 ≥ 0.98 test ET 100% fixtures"). Le benchmark v3
a montré que les 3 candidats ratent **tous** le critère gold (~0.89-0.91 sur
fixtures), donc la décision a été de garder XLM-R en attendant. Cet ADR ne
revisite pas ce choix : il enrichit le corpus.

## Conséquences

### Ajoutés
- `tools/ner-training/build_v4_keywords.py` — script de génération.
- `data/ner-corpus/extension_v4_keywords.jsonl` — sortie du script.
- Cellule mise à jour dans `train_mathcursor.ipynb` pour concaténer v4 au train.

### Non impactés
- `adapter-vsto/src/MathCursor/Detection/MathNerDetector.cs` — l'API NER reste
  identique.
- `adapter-vsto/src/MathCursor/Host/SuggestionService.cs` — `ExtendZoneBackwardWithKeyword`
  reste tel quel. Si le NER absorbe `somme k 1 n` directement, ce patch
  devient redondant pour ce cas mais reste utile pour les autres (à évaluer
  après retrain, hors scope de cet ADR).
- val.jsonl et test.jsonl — non touchés. La métrique F1 test reste comparable.

### Évaluation
- Succès : F1 ≥ 0.99 sur test + 6/6 cas keywords détectés (conf > 0.85) +
  0/5 FP sur distractors.
- Échec partiel : F1 conservé mais cas keywords manqués → augmenter le volume
  ou diversifier les paraphrases.
- Échec total : F1 < 0.99 (régression) → l'extension v4 est trop typée, à
  rééquilibrer avec plus de distractors.

## Alternatives considérées

- **Étendre `ExtendZoneBackwardWithKeyword` à N mots** : rejeté par le brief §1,
  patch fragile, absorbe du langage naturel.
- **Hardcoder une liste de keywords côté `SuggestionService`** : rejeté §9 du
  brief — l'algo lattice est conçu pour être agnostique du vocabulaire.
- **Augmenter le seuil `DefaultThreshold = 0.85`** : rejeté §9 — compromis
  précision/rappel déjà ajusté.
- **Cibler minilm/distilmult d'abord (ce qu'on était parti pour faire)** :
  rejeté — on travaille sur la cause racine (corpus) avant le levier
  d'optimisation (taille modèle).

## Validé par l'utilisateur

Brief détaillé dans
[`docs/dev/briefs/2026-04-27-ner-retraining-keywords.md`](../briefs/2026-04-27-ner-retraining-keywords.md)
(spec destinée à un agent ML autonome, livrables §5, cas de test §6,
non-fait §9).

Direction "enrichir le train" :
> "oui on va enrichir le train , il faut que le model soit plus petit"

Recadrage proposé en deux objectifs distincts (A = corpus v4 keywords sur
XLM-R, B = réduction taille modèle ultérieurement), validation pour démarrer A :
> "switch sur lattice engine et demarre A"

## Statut

acté
