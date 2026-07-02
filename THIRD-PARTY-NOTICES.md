# Composants tiers — MathCursor

MathCursor est distribué sous **GNU GPL v3** (voir [`LICENSE`](LICENSE)).

Il **embarque** ou **redistribue** les composants tiers ci-dessous, qui restent
sous **leurs licences respectives**. Leur inclusion relève de l'**agrégat** au
sens de la GPL v3 §5 (fichiers séparés, non liés au binaire) : la GPL de
MathCursor ne s'étend pas à ces composants, et réciproquement.

## Polices math embarquées (installeur)

Installées par utilisateur lors du setup (cf. ADR
`docs/dev/decisions/2026-06-22-Feat-math-font-selector.md`). Les textes de
licence sont fournis dans `adapter-vsto/installer/fonts/` et déposés à
l'installation dans `{app}\fonts-licenses`.

| Police | Auteur / Copyright | Licence | Source |
|---|---|---|---|
| **Latin Modern Math** | GUST e-foundry (Bogusław Jackowski, Janusz M. Nowacki, P. Strzelczyk) | GUST Font License (= LaTeX Project Public License 1.3c) | https://www.gust.org.pl/projects/e-foundry/lm-math |
| **STIX Two Math** | The STIX Fonts Project Authors ; « STIX Fonts™ » est une marque de l'IEEE | SIL Open Font License 1.1 | https://www.stixfonts.org/ |

Conditions respectées :
- les fontes sont embarquées **non modifiées** (aucun « Reserved Font Name » /
  renommage en jeu) ;
- chaque licence + l'avis de copyright est fournie avec la police (OFL clause 2,
  LPPL) ;
- les fontes ne sont **pas vendues isolément** (OFL clause 1).

## Autres composants redistribués (binaires de l'add-in)

| Composant | Usage | Licence |
|---|---|---|
| **WpfMath** / **XamlMath.Shared** | rendu LaTeX dans la popup WPF | MIT |
| **Microsoft.ML.OnnxRuntime** (+ natifs) | inférence du modèle de détection (NER) | MIT |

## Modèle NER embarqué (`{app}\models`)

Le modèle de détection des zones math (NER) livré avec MathCursor est un
classifieur spécialisé **entraîné par l'auteur**, dérivé d'un modèle de base
pré-entraîné open source :

| Élément | Provenance | Licence |
|---|---|---|
| **Modèle de base** : `distilbert-base-multilingual-cased` | Hugging Face — V. Sanh, L. Debut, J. Chaumond, T. Wolf (*DistilBERT, a distilled version of BERT*, 2019) | **Apache License 2.0** — texte intégral dans [`licenses/Apache-2.0.txt`](licenses/Apache-2.0.txt) |
| **Poids fine-tunés** (détection B-MATH / I-MATH) | © 2026 Côme Percin — entraînement maison | GNU GPL v3 (avec le reste de MathCursor) |
| **Données de fine-tuning** | Corpus généré avec l'aide d'un assistant IA, usage autorisé ; propriété de l'auteur | — |

DistilBERT est distribué sous **Apache License 2.0**, permissive et
**compatible GPLv3** : sa redistribution au sein de MathCursor (agrégat, §5)
est conforme. Conformément à l'Apache 2.0, l'attribution des auteurs de la base
est conservée ci-dessus et le texte de la licence est fourni dans
[`licenses/Apache-2.0.txt`](licenses/Apache-2.0.txt). Aucun fichier `NOTICE`
n'accompagne le modèle de base amont ; le cas échéant il serait propagé ici.
