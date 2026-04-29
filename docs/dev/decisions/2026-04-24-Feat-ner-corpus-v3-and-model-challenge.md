# Feat — Corpus NER v3 (fixtures projet + `²` + anti-FP) & challenge modèle

**Date :** 2026-04-24
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

1. Versionner dans `data/ner-corpus/` le corpus d'entraînement NER actuel
   (train/val/test + extensions synthétiques FR/EN + script de génération)
   jusqu'ici géré hors-repo sur Google Drive.
2. Versionner dans `tools/ner-training/` le notebook Colab de fine-tuning
   `train_mathcursor.ipynb` et les scripts de génération d'extensions v3.
3. Générer une v3 du corpus qui ajoute trois catégories :
   - **Fixtures projet** : les 47 cas de `specs/test-fixtures/phase1-zone-detection.json`
     convertis en JSONL BIO.
   - **Superscript `²`** (~150 cas FR+EN) : le caractère AZERTY `²` natif, absent
     du corpus actuel, à inclure partout où on l'attend (`x²`, `(x+1)²`,
     `f(x)=x²+1`, `πr²`…).
   - **Anti faux positifs** (~50 cas) : prose incluant "sin", "cos", "R", "N"
     utilisés dans un sens non-math, tirés des failles visibles dans le bloc
     hors-distribution du notebook actuel (ex : "mon frère a sin aine").
4. Challenger la taille du modèle : benchmark à 3 candidats sur le même corpus v3
   avec le même pipeline d'entraînement et d'export ONNX int8 :
   - `xlm-roberta-base` (baseline actuelle, 265 Mo int8)
   - `distilbert-base-multilingual-cased` (~75 Mo int8 attendu)
   - `microsoft/Multilingual-MiniLM-L12-H384` (~40 Mo int8 attendu)
5. Critère d'adoption du nouveau modèle : **F1 ≥ 0.98 sur test** ET
   **100% sur fixtures projet**. Sinon on reste sur XLM-R avec corpus v3
   enrichi (gain corpus seul).

## Pourquoi

### Versionner le corpus et le notebook
- Jusqu'à aujourd'hui, le corpus et le notebook vivaient uniquement dans
  Google Drive. Aucune reproductibilité, aucun historique, impossible de
  tracer pourquoi telle ligne a été ajoutée ou modifiée.
- Taille raisonnable (~2.3 Mo total pour tous les .jsonl) → aucun problème
  à commiter dans git.
- Le notebook Colab peut se commiter en `.ipynb` tel quel ; Drive reste
  l'endroit d'exécution (GPU T4), git la source de vérité.

### Pourquoi ces 3 catégories d'extensions
- **Fixtures projet** : `phase1-zone-detection.json` est la source de vérité
  cross-implémentations. Le modèle doit au moins réussir ces 47 cas.
  Aujourd'hui ils ne sont pas dans le corpus → aucune garantie qu'ils soient
  couverts en inférence.
- **Superscript `²`** : le tokenizer du PatternEngine pré-remplace `²` → `^2`
  côté conversion. Mais le détecteur NER, lui, voit le texte original Word
  (contenant `²`). Actuellement le corpus ne contient aucun `²` natif, donc
  XLM-R/SentencePiece le tokenise sans contexte d'entraînement → détection
  aléatoire. Ajouter ces cas comble un trou direct du produit (super
  fréquent chez les élèves FR AZERTY).
- **Anti faux positifs** : les sorties hors-distribution du notebook (bloc
  `real_world_sentences`) montrent plusieurs cas gênants — "mon frère a sin
  aine comme prénom" détecte "sin aine" à 0.97, "racine carrée d'un nombre
  négatif n'existe pas dans R" détecte "R" à 0.63, etc. Générer des
  contre-exemples explicites force le modèle à apprendre ces bordures.

### Pourquoi challenger XLM-R
- L'installer actuel pèse ~200 Mo, dominé à 99% par `model_quantized.onnx`
  (265 Mo après quantization int8). Problème UX pour la distribution MSI.
- XLM-R-base est fondé sur un vocab multilingue de 250k pièces utilisé à
  moins de 5% par notre domaine (math FR/EN) — très gaspillé.
- DistilBERT multilingue et MiniLM-L12 multilingue sont des candidats
  naturels : même API HuggingFace, même `Trainer`, export ONNX identique,
  taille int8 attendue 3x à 6x inférieure.
- **Bénéfice secondaire** : DistilBERT et MiniLM utilisent WordPiece (pas
  SentencePiece Unigram). `Microsoft.ML.Tokenizers` le tokenise nativement
  → on peut supprimer `Detection/Sp/` (tokenizer custom Unigram ~500 lignes
  C#). Moins de code à maintenir, normalisation NFKC complète gratuite.
- Stratégie reversible : si F1 tombe, on revient à XLM-R. Molle.

## Conséquences

### Ajoutés
- `data/ner-corpus/` — 6 .jsonl + `generate_extension.py` + README.md.
- `tools/ner-training/train_mathcursor.ipynb` — notebook actuel de fine-tuning.
- `tools/ner-training/build_v3_fixtures.py` — conversion fixtures → JSONL BIO.
- `tools/ner-training/build_v3_superscript2.py` — génération cas `²`.
- `tools/ner-training/build_v3_false_positives.py` — génération anti-FP.
- `tools/ner-training/train_mathcursor_benchmark.ipynb` — notebook dérivé qui
  teste les 3 modèles candidats.
- `tools/ner-training/README.md` — mode d'emploi reproductibilité.

### Non impactés tant que le benchmark n'a pas tranché
- `adapter-vsto/src/MathCursor/Detection/*.cs` — inchangé tant que XLM-R reste
  le modèle actif.
- `briefs/detection-ner.md` — sera mis à jour quand un nouveau modèle est adopté.
- Un ADR de suivi `Fix-ner-model-swap` sera créé si on passe à
  DistilBERT/MiniLM (avec superseding partiel des sections concernées).

### Évaluation
- Succès du corpus v3 seul : F1 baseline (XLM-R) ≥ 0.99 maintenu ET 100% des
  fixtures projet passent après retrain.
- Succès du challenge taille : un des deux petits modèles tient F1 ≥ 0.98 sur
  test + 100% fixtures projet. Sinon on conserve XLM-R (gain corpus seul).

## Alternatives considérées

- **Garder le corpus hors repo** : rejeté, rend le pipeline non reproductible
  et empêche PR review.
- **Re-écrire un tokenizer Unigram C# propre** (au lieu de swap modèle) :
  effort pur maintenance, ne règle pas le problème de taille (265 Mo).
- **Distiller XLM-R nous-mêmes** : plus de travail, rendement incertain,
  DistilBERT-mult existe déjà prêt à l'emploi.
- **Garder XLM-R en changeant juste la quantization (int4 ?)** : piste
  complémentaire mais pas encore stable côté ONNX Runtime CPU pour
  token-classification.

## Validé par l'utilisateur

Brief initial :
> "deja ca marche plutot tres bien, mais j'aimerai qu'on ajoute les cas de
> tests du projets global + les ² du clavier dans la detection. Peut etre
> qu'on relance un entrainement avec du coup plus de choses.. tu as les docs
> d'entrainement dans le repo ? et j'aimerai aussi challenger un peu le
> model de base parce que la c'est 200mo l'installer environ.. ne peux t'on
> pas reduire ?"

Validation des 4 décisions (commit corpus, commit notebook, 3 modèles,
anti-FP inclus) :
> "1. commit des fichier (pas le zip mais le contenu) 2. commit du notebook
> 3. les 3 modeles tres bien. 4. ok"

## Statut

acté
