# Feat — Corpus NER v11 : matrices, mots-clés en tête de ligne, intervalles (+ fix liste d'entraînement)

**Date :** 2026-06-23
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Lié à :** [2026-06-19-Fix-environ-env-line-start-approx-marker.md](2026-06-19-Fix-environ-env-line-start-approx-marker.md) (modèle v9)

## Citation acté

> « refaire une version de NER avec du corpus genre des lignes qui commencent par des mots clés : environ approx bar etc » … « les matrices 44 55 33 avec des expressions complexes à l'interieur » … « matrices ok, tuples, mais aussi les mots clés en debut de ligne » … « rajouter partial, les intervalles, rond congru dim +- etc le plus le mieux » — utilisateur, 2026-06-23

## Contexte

Analyse de couverture du corpus NER (12 824 exemples) : plusieurs familles à **0
ou quasi-0**, surtout en tête de ligne, et **sans distracteurs homonymes** (→
risque de faux positifs au retrain). Même logique que v8 (`iint`) / v9 (`approx`).

- **Matrices** : **0 vraie matrice (≥2 colonnes)** dans tout le corpus (les seuls
  `;` étaient des intervalles). Le NER n'a jamais vu de matrice.
- **Mots-clés décorations/lycée en tête** : `bar`, `conj`, `abs`, `module`, `hat`,
  `racine`, `floor`/`ceil`, `det`, `dim`, `nabla`, `partial`, `pgcd`/`ppcm`,
  `parmi`, `pourtout`/`ilexiste`, `rond`, `congru`, `±` — tous absents, plusieurs
  à fort sens commun (bar, module, conj, angle, racine, parmi, rond, floor, hat).
- **Intervalles** : sous-représentés.

Trouvé au passage : la liste `EXTENSIONS` de `train_mathcursor.ipynb` s'arrêtait à
**v8** — **v9 et v10 n'avaient jamais été inclus dans un retrain**.

## Décision

Créer `extension_v11_{matrices,keywords,intervals}.jsonl` (générateurs
`tools/ner-training/build_v11_*.py`, modèle v9 : positifs tête-de-ligne + autocap
+ prose + **distracteurs homonymes**, FR/EN, offsets validés). 497 lignes
(matrices 112 / keywords 264 / intervals 121).

Tuples/points `(1,2)`,`(x,y,z)` = **positifs** (math ambigu tuple/matrice côté
moteur — ne pas casser les coordonnées de v6).

**Corriger la liste d'entraînement** : ajouter v9, v10 ET v11 (×3) à `EXTENSIONS`
dans `train_mathcursor.ipynb`.

## Tradeoff & alternatives écartées

- **Positifs sans distracteurs homonymes** : retrain = faux positifs massifs (bar/module/parmi…). Les deux ensemble, impérativement.
- **`dataset_v2_all.jsonl`** : snapshot périmé (8000 l.) **non utilisé** par le notebook → laissé tel quel (pas régénéré).
- **Le retrain lui-même** : hors périmètre — c'est un job notebook (Colab/GPU), exécuté par l'auteur. Cet ADR livre le **corpus** + la liste corrigée.

## Conséquences

- **Données** : 3 nouveaux `.jsonl` + 3 générateurs `build_v11_*.py`.
- **Notebook** : `EXTENSIONS` complétée (v9, v10, v11×3) — corrige l'oubli v9/v10.
- **Activation** : rappel — le NER (auto-détection) n'est **pas branché** dans la beta (déclenchement = Ctrl+Espace → `SpanComputer`, déjà testé sur matrices). Ce corpus prépare l'activation / un meilleur modèle.
- **À retrain** : cible F1 ≥ baseline ; surveiller particulièrement les faux positifs sur les homonymes (bar/module/parmi) et la détection matrice entière.

## Addendum 2026-06-23 — chargement robuste (glob)

La cause racine de l'oubli v9/v10 = liste `EXTENSIONS` codée en dur dans les
notebooks. Corrigé en profondeur : **les deux** notebooks (`train_mathcursor.ipynb`
ET `train_mathcursor_benchmark.ipynb`) **auto-découvrent** tous les
`extension_*.jsonl` par **glob** — plus aucune liste à maintenir, donc plus
d'extension oubliée (`extension_fr`/`extension_en`, 2000 l. synthétiques, sont
désormais incluses elles aussi). `regression_v1_gold.jsonl` (hold-out d'éval) ne
matche pas `extension_*` → reste hors train. `dataset_v2_all.jsonl` (snapshot
périmé, référencé nulle part) **supprimé**.
