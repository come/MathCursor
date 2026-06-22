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
| **Modèle NER embarqué** (`{app}\models`) | détection des zones math | _à confirmer / documenter (provenance du modèle entraîné)_ |

> Note : la liste « autres composants » est tenue à jour au mieux ; un audit
> licences complet des dépendances NuGet et du modèle NER reste un chantier à
> part (hors périmètre de l'ajout des polices).
