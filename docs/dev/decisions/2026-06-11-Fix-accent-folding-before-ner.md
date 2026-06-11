# Fix — Accents foldés en amont du NER/moteur (pas de diacritiques au lexer)

**Date :** 2026-06-11
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Fix-nbsp-keyword-case-tolerance.md](2026-06-10-Fix-nbsp-keyword-case-tolerance.md) (même famille : tolérance aux artefacts de frappe Word), [2026-06-11-Feat-nary-arity-variants.md](2026-06-11-Feat-nary-arity-variants.md) (session d'origine)

## Citation acté

> « enleve tous les accents non on peut les strip avant d'envoyer au ner » — utilisateur, 2026-06-11
> (en réponse à la proposition alternative : tolérance aux diacritiques dans le lexer)

## Contexte

Le lexer moteur jette `caractère inattendu: é` (Lexer.cs:242) : toute zone
contenant un mot accentué (« intégrale », omniprésent en français — Word
l'IMPOSE même par autocorrection quand l'élève tape « integrale ») faisait
crasher l'analyse, exception avalée par `ConversionController` → popup
masquée silencieusement. Le corpus NER v4 enseigne pourtant « intégrale … »
en positif depuis avril : le NER détectait des zones que le moteur ne savait
pas lire.

## Décision

Stripper les accents **en amont**, jamais dans le lexer :

- **Prod** : `AutocorrectNormalizer.FoldDiacritic` (lettres latines accentuées
  → lettre nue, mapping strictement 1:1 pour préserver l'invariant d'offsets).
  Appliqué par `WordContextReader.ReadCurrentParagraph`, donc UN seul point
  couvre NER, table de keywords du refiner et entrée moteur (auto-détection
  ET Ctrl+Espace).
- **Entraînement** : table `FOLD` (str.translate, 1:1) au chargement des
  `.jsonl` dans les deux notebooks — le modèle s'entraîne sur la même
  distribution que ce qu'il verra en prod ; les fichiers corpus restent
  accentués (lisibles, pas de régénération).
- **Tests locaux** : `NerCorpusFixture.LoadCorpus` folde pareil — les tests
  F1 exercent le même chemin que la prod.

Exclusions : `×` (U+00D7) et `÷` (U+00F7) — opérateurs du vocabulaire
moteur ; ligatures `œ`/`æ` — fold 2 lettres casserait l'invariant 1:1.

## Tradeoff & alternatives écartées

- **Tolérance aux diacritiques dans le lexer** (lettres accentuées dans la
  classe de mots + normalisation à la résolution d'alias) : touche la classe
  de caractères du lexer pour tous les flux, et il aurait fallu décider du
  sort des mots accentués non-alias (atome littéral ? erreur ?). Écartée par
  l'utilisateur au profit du strip amont, plus simple et au même endroit que
  les normalisations Word existantes (smart dashes, chars de contrôle).
- **Alias accentués dans le vocab** (`intégrale` → int) : ne suffit pas, le
  crash est au niveau caractère, avant la résolution d'alias.

## Conséquences

- **Code touché** :
  - `adapter-vsto/src/MathCursor/Host/AutocorrectNormalizer.cs` — `FoldDiacritic` + fast-path.
  - `adapter-vsto/tests/.../Host/WordContextReaderNormalizeTests.cs` — 3 tests verrouillant l'ANCIEN contrat (« accents préservés ») mis à jour vers le nouveau ; nouveaux cas accents/×÷.
  - `adapter-vsto/tests/.../Host/AutocorrectNormalizerTests.cs` — nouveaux tests fold (1:1, ×÷ intacts).
  - `adapter-vsto/tests/.../Detection/NerCorpusFixture.cs` — fold au chargement corpus.
  - `tools/ner-training/train_mathcursor*.ipynb` — `FOLD` dans `load_jsonl`.
  - `tools/ner-training/build_v8_nary_short_forms.py` — entrées accentuées retirées (doublons après fold) ; corpus v8 régénéré (298 lignes).
- **Tests** : adapter 245/245, moteur 384 fixtures vertes. Le moteur lui-même ne voit plus jamais d'accents via le pipeline popup ; il jette toujours sur un accent en entrée directe (assumé).
- **Sémantique** : la popup affiche le texte de zone foldé (« integrale » au lieu d'« intégrale ») — cosmétique, le texte du document n'est pas modifié tant qu'on ne commit pas (le commit remplace par l'OMath).

## Validation post-fix

1. Tests unitaires fold (longueur préservée, ×÷ intacts) — verts.
2. Après ré-entraînement (corpus foldé) : `MathNerInferenceTests` verts, et dans Word taper « intégrale 0 1 f(x) x » → popup `\int_0^1 f(x)\,dx`.
