# Feat — Lettres grecques, micro, ², ³ et √ tapés en Unicode

**Date :** 2026-10-14
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-16-Feat-portable-engine-universal-vocab](2026-06-16-Feat-portable-engine-universal-vocab.md) (vocab en données), [2026-06-15 raccourcis grecs `@`](README.md)

## Citation acté

> « rapide et sur go pour le 6. » puis, sur le plan Unicode (grec, µ, ², ³, √ en données), « ok go ! » — utilisateur, 2026-10-14

## Contexte

Analyse de deux cours tapés par un testeur dys (physique + maths, 2026-10-14) :
des lignes comme `V=𝞴/T`, `T=V*𝞴`, `𝞴=0,025m` sont restées en texte brut. Le
testeur insère les lettres grecques par le menu Symbole de Word. Or le lexer
(C# et Rust) ne connaît que l'ASCII + quelques symboles déclarés (`≤ × ½ °`…) :
`λ`, `μ`, `π`, `Δ`, `α`, `µ` (micro), `²`, `√` → « caractère inattendu » →
décision `erreur` → aucune popup. (Pas de plantage : `analyze.rs` rattrape la
panique ; côté Word l'exception est rattrapée par le contrôleur.)

## Décision

Tout passe par la **table de vocabulaire** (`data/engine/symbols.json`,
`units.json`), donc parité C#/Rust automatique :

- **Lettres grecques Unicode** = symboles-atomes (comme `½`) : minuscules
  α…ω → `\alpha`…`\omega` (le glyphe décide : `ε` → `\varepsilon`, `ϵ` →
  `\epsilon`, `φ` → `\varphi`, `ϕ` → `\phi`, `ϑ` → `\vartheta`) ; majuscules
  ayant un glyphe propre (Γ Δ Θ Λ Ξ Π Σ Υ Φ Ψ Ω). Les majuscules identiques au
  latin (Α Β Ε…) et `ο` ne sont pas déclarées (pas de commande LaTeX).
- **`µ` (U+00B5, micro)** = `\mu`. **Unités micro** après un nombre
  (`20µs`, `3 μm`) = unités composées (`µm µs µg µL µA µV µF µW µJ µmol`, avec
  `µ` et `μ`) → `20\,\mathrm{\mu s}`-like via le connecteur unité existant.
- **Exposants / indices Unicode** (`⁰`…`⁹ ⁺ ⁻ ⁼ ⁽ ⁾ ⁿ`, `₀`…`₉ ₊ ₋ ₌ ₍ ₎ ₙ`) :
  ils ONT le sens d'une mise en forme Indice/Exposant → **même chemin** que
  les marqueurs `ScriptMarkers` (groupe exact + `_`/`^` sticky), table dans
  `ScriptMarkers.TryScriptChar` (miroir Rust `script_char`). `x²` → `x^{2}`,
  `x⁻¹` → `x^{-1}`, `H₂O` → `H_{2}O`, `uₙ₊₁` → `u_{n+1}`. (Un postfixe `°`-like
  a été écarté : les modèles de rendu lisent `{2}` comme un argument, et il ne
  couvrait ni `⁻¹` ni les indices.)
- **`√`** = `sameAs: sqrt`. Seul ajout de code : le lexer accepte désormais
  un caractère-symbole de forme **préfixe** (aujourd'hui : infixe, postfixe,
  atome seulement), en miroir C# + Rust.

## Tradeoff & alternatives écartées

- **Normaliser dans l'adapter** (`λ` → `lambda` avant le moteur) : casse
  l'invariant 1 caractère = 1 caractère de `AutocorrectNormalizer` (offsets
  Word) et ne profite pas à LibreOffice/VSCode.
- **Accepter toute lettre Unicode comme nom** : rendu indéfini (quel LaTeX pour
  un caractère inconnu ?), ouvre la prose non latine au moteur.

Hors périmètre : les lettres « mathématiques » `𝞴` (U+1D7B4…, hors plan
de base, 2 unités UTF-16) — normalisation adapter à part ; `10-n` lu en
notation scientifique — chantier séparé.

## Conséquences

- **Code touché** : `data/engine/symbols.json`, `data/engine/units.json`,
  `ScriptMarkers.cs`, `Lexer.cs` + `lexer.rs` (préfixe-symbole, run de
  caractères exposant/indice partageant `EmitScript` / `emit_script` avec les
  marqueurs).
- **Tests** : +15 fixtures (517, C# + Rust) ; elles passent aussi par les
  pipelines WpfMath/OMML du gate adapter. Comparaison ancien/nouveau moteur
  (8 233 entrées) : 15 différences, toutes des entrées contenant les nouveaux
  caractères (avant : `erreur`), 0 ailleurs.
- **Non couverts** : `ˣ` et autres lettres modificatives, `𝞴` mathématiques.

## Validation post-fix

Dans Word : `V=λ/T`, `Δx=2`, `T=20µs`, `x²+1` (touche `²`), `√2` → popup et
conversion.
