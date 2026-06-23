# MathCursor — Règles de flow (référence)

Ce document est la **source de vérité** pour les règles de fonctionnement.
Avant d'ajouter du code dans le pipeline ou la popup, vérifier ici. Toute
duplication de logique doit être éliminée.

> **⚠️ Réfs d'archi/nommage partiellement datées.** Le pipeline est aujourd'hui
> orchestré par `ConversionController` (ex-`SuggestionService`) et appelle le moteur
> **PUR** `engine/MathCursor.Engine` (`ForestEngine`) + `serialization` (`LatexToOmml`).
> L'ancien `core-csharp` / contrat 4-interfaces n'existe plus (ADR 2026-06-23). Les
> règles de flow ci-dessous restent globalement valides ; pour l'archi, voir `CLAUDE.md`.

---

## 1. Lecture du contexte (`WordContextReader`)

**Règle absolue** : on ne lit jamais à travers un saut de ligne.

- Lecture bornée par le **paragraphe courant** : `Selection.Paragraphs[1].Range.Start/End`
- Demande N chars avant et/ou après le curseur, mais clampé aux bornes du paragraphe
- Implémenté UNE SEULE FOIS dans `Host/WordContextReader.cs`
- Utilisé par : `VstoDocumentHost.ReadContextAroundCaretAsync` et `SuggestionService.CheckContextAndUpdate`

**Conséquence** : un saut de ligne efface la popup et empêche toute conversion
de traverser un paragraphe.

---

## 2. Pipeline de détection (`ConversionPipeline.Convert`)

Étapes dans l'ordre. **Dès qu'une étape réussit, on s'arrête.**

### 2.1. Signal de sortie (early reject)

Si le texte se termine par un de ces signaux → `Success = false`, pas de
détection :
- `\t` (tab manuel — notre Tab intercepté n'est PAS dans le texte)
- `  ` (2+ espaces consécutifs)

Un seul espace est toléré (utilisateur en pause).

**Saut de ligne / nouveau paragraphe** : géré nativement par Word via le
bornage paragraphe de `WordContextReader` (§1). Pas besoin d'y penser ici —
le contexte ne contient JAMAIS de `\r`/`\n` en production.

### 2.2. Zone math (chemin principal)

```
Tokenizer → Scorer → ZoneDetector
```

- **Tokenizer** : découpe en tokens catégorisés (letter, greekLetter, digit, operator, paren, comma, dot, whitespace, mathSymbol, unknown). Multi-char ops (`>=`, `<=>`, `->`) groupés. Math italic Unicode (U+1D400+) normalisé en ASCII.

- **Scorer** : score 0..1 par token (mathiness). Données déclaratives :
  - **Stopwords multilingues** (FR/EN/DE/ES/IT/PT) → 0.0 (coupe la zone)
  - **Math functions** (`sin`, `cos`, `lim`, `sqrt`...) → 0.95
  - **Math keywords** (`alpha`, `pi`, `vec`, `inf`...) → 0.95
  - **Greek letters** (α β γ...) → 0.95
  - **Operators** (`+ - * / ^ = < >`) → 0.9
  - **Math symbols Unicode** (∫ ∑ ≥ ∈ ∀...) → 1.0
  - **Digits** → 0.8
  - **Parens** → 0.7 (0.9 si après une lettre = function call)
  - **Comma** → 0.5
  - **Dot** → 0.8 si après digit (décimal), sinon 0.1
  - **Lettres simples** : selon contexte voisin et longueur

- **ZoneDetector** : remonte depuis la fin tant que score ≥ 0.5. Threshold = 0.5. Pour valider, doit contenir au moins une **feature math** (operator, mathSymbol, greekLetter, paren≥0.7).

Si zone trouvée :
1. **Preprocess** : `SymbolMatcher.ReplaceAllInText(zone.Normalized)` remplace tous les patterns symboliques (alpha → α, beta → β, etc.) dans la zone
2. **Lex + Parse** → AST math
3. **OmmlSerializer** → OMML XML

Retour : `Equation { Source=zone.Raw, UnicodeFallback=preprocessed, Omml=ommlPkg }`.

### 2.3. Fallback symbol-only

Si pas de zone math (ex: mot seul comme "alpha"), on tente
`SymbolMatcher.FindSymbol(text)` (end-anchored).

Retour : `Equation { Source=match.Raw, UnicodeFallback=match.Replacement, Omml=null }`.

### 2.4. Échec

Aucune des étapes n'a abouti → `Success = false`.

---

## 3. Données déclaratives

Tout ce qui est "knowledge" est en table/dictionnaire/regex, jamais en code conditionnel :

| Quoi | Où |
|---|---|
| Patterns symboliques (Vx(R, vec AB, alpha, ≥...) | `Symbols/SymbolMatcher.cs` table `Patterns` |
| Stopwords multilingues | `ZoneDetection/Scorer.cs` set `Stopwords` (à porter dans `data/stopwords.json`) |
| Math functions/keywords | `ZoneDetection/Scorer.cs` sets `MathFunctions`, `MathKeywords` |
| Sets ensembles (ℝ ℕ ℤ...) | `Symbols/SymbolMatcher.cs` dict `Sets` |
| Greek lower/upper | `Tokenization/Tokenizer.cs` sets `GreekLower`, `GreekUpper` |
| Math symbols Unicode | `Tokenization/Tokenizer.cs` set `MathSymbols` |

**Règle** : nouveau symbole = nouvelle entrée dans une table. Jamais un `if` inline.

---

## 4. Popup (`SuggestionService`)

- Polling 200ms via `DispatcherTimer`
- À chaque tick : lit contexte via `WordContextReader`, appelle `ConversionPipeline.Convert`
  - `Success` → `ShowPopup(equation.UnicodeFallback)` (popup contient ce que Tab produira)
  - `!Success` → `HidePopup()`
- `Application.WindowDeactivate` → hide + pause timer
- `Application.WindowActivate` → resume timer
- Pas de polling quand Word n'a pas le focus

---

## 5. Hook clavier (`KeyboardInterceptor`)

- WH_KEYBOARD thread-local sur le thread UI Word (pas de hook global)
- Touches gérées : Tab, Enter, Up, Down, Escape
- Pour chaque, callback retourne `true` (consommer) ou `false` (laisser passer)

| Touche | Si popup pas visible | Si popup visible (display mode) | Si popup visible (nav mode) |
|---|---|---|---|
| **Tab** | `TryConvertAtCaret` (peut convertir explicitement) | Convert + hide | Convert + hide |
| **Enter** | passe à Word (saut de para) | passe à Word | Convert + hide |
| **Down** | passe (curseur ↓) | entre en nav mode (opacité 0.5→0.7) | sélection +1 |
| **Up** | passe (curseur ↑) | passe | sélection −1 |
| **Esc** | passe | hide popup | hide popup |

---

## 6. Insertion d'équation (`VstoDocumentHost.InsertEquationAsync`)

1. Calcule la plage à remplacer : `[caretPos − zone.Text.Length, caretPos]`
2. `replaceRange.Text = linearText + " "` (espace final pour rester inline et pas en display mode)
3. `mathRange.OMaths.Add` + `BuildUp` natif Word
4. Cherche l'OMath créé via `doc.OMaths` filtré sur `rng.Start <= zoneStart && rng.End > zoneStart`
5. Position curseur : `OMath.Range.End + 1` (juste après l'espace trailing)
6. Si `Selection.OMaths.Count > 0` (Word a placé curseur dans l'OMath) → **un seul** saut vers `Selection.OMaths[1].Range.End + 1`. Pas d'itération.

---

## 7. Architecture des couches (rappel)

```
adapter-vsto/MathCursor                  ← plateforme (Word interop, hook, popup WPF, orchestration)
   ↓ appelle directement
engine/MathCursor.Engine                 ← moteur PUR (Lexer, Parser/Forest, Score, LatexRenderer)
serialization/MathCursor.Serialization   ← LaTeX → OMML
```

**Règle dure** : `engine/` + `serialization/` n'ont aucune référence à
`Microsoft.Office.*` / WPF (netstandard2.0). L'adapter les appelle **en direct** —
pas d'interface d'inversion (ancien contrat host-contract supprimé, ADR 2026-06-23).
