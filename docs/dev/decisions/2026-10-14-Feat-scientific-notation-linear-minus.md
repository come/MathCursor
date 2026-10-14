# Feat — Notation scientifique linéaire : « 2,5×10-3 » propose 2,5×10⁻³

**Date :** 2026-10-14
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-10-14-Feat-unicode-greek-micro-symbols](2026-10-14-Feat-unicode-greek-micro-symbols.md) (même analyse de cours testeur)

## Citation acté

> « et le *10-6 en proposition alternative est ce que c'est une bonne idée ? ou c'est un contournement non souhaitable (vraie question !) » puis, après la réponse (règle de lecture générale, popup jamais auto), « go chantier 10-n » — utilisateur, 2026-10-14

## Contexte

Cours de physique d'un testeur dys : `1*10-4`, `2,0*10-5s`, `6,5×10-5`,
`3,24*10-3`… partout. Il écrit `10-4` pour 10⁻⁴. Le moteur lisait une
soustraction (`2{,}5\times 10-3`), seule lecture proposée.

## Décision

**Règle de lecture**, pas un cas particulier : dans le motif strict
`nombre × 10-entier` (multiplication TAPÉE = l'opérateur qui porte le rôle
`implicit`, soit `*`/`×` ; espaces autour admis ; `10` littéral, `-` collé à
`10` et suivi d'un chiffre), le `-` porte **deux lectures** :

1. **exposant négatif** (`·sci`, infixe le plus serré, rendu `{0}^{-{1}}`)
   → `2{,}5\times 10^{-3}` ;
2. **soustraction** (lecture actuelle).

- Le motif est déclaré dans les **données** (`"sciAlt": "·sci"` sur `-` dans
  `symbols.json`) ; le lexer (C# + Rust) ne fait que vérifier le contexte.
- **Jamais auto** : deux lectures de même coût → popup, notation
  scientifique en premier. Une soustraction n'est jamais transformée en
  silence.
- `·sci` est **`tight`** : visuellement atomique (pas de pénalité
  d'imbrication sous `×`, comme une décoration) et, règle étendue des
  préfixes tight aux infixes tight dans le parser (C# + Rust), opérande droite
  d'**un seul morceau** (`10^{-3}`, jamais `10^{-(3m)}`).
- Hors motif (`10-4` seul, `x×10-3`, `2/10-3`, `10 - 3` espacé) : inchangé.

## Tradeoff & alternatives écartées

- **Ordre conditionnel** (scientifique en premier seulement si mantisse
  décimale ou unité derrière, piste évoquée) : le motif strict suffit déjà à
  signer l'intention (personne n'écrit `3×10-6` pour 24) ; une règle de plus
  pour un gain marginal.
- **Auto** : transformerait une soustraction légitime sans prévenir.
- **Imposer `10^-3`** : contraire au but (que la saisie linéaire de l'élève
  marche).

## Conséquences

- **Code touché** : `data/engine/symbols.json` (`·sci`, `sciAlt`),
  `Vocabulary.cs` / `vocab.rs` (champ `sciAlt`), `Lexer.cs` / `lexer.rs`
  (contexte du motif), `Parser.cs` / `parser.rs` (infixe tight).
- **Tests** : +9 fixtures (526 ; motif, hors motif dont `2/10-3`).
  Comparaison ancien/nouveau moteur sur 9 681 entrées (corpus généré + 1 500
  variantes proches du motif : `/`, `+`, `x×`, espaces, unités) : 65
  différences, toutes dans le motif (dont `2 * 10-3` : auto `2×(10−3)` →
  popup), 0 ailleurs. Une 1re version (critère « même priorité que × »)
  laissait passer `2/10-3` : corrigée en « opérateur du rôle implicit ».
- Le jeu de candidats contient aussi `(2{,}5\times 10)^{-3}` en dernier (bruit
  de la forêt, coût supérieur).

## Extension 1 — grandeur à exposant devant une unité

> « 3*10-3 ms il me rajoute des parenthese sur le 10-3 cd'est chiant un peu » — utilisateur, 2026-10-14

Défaut **antérieur** (aussi avec `^` : `3*10^8 m/s` → `3\times (10^{8})\,\mathrm{m/s}`,
`x2 m` → `(x^{2})\,\mathrm{m}`). Un script à opérandes atomiques (x², 10⁻³ ;
`Score.AtomicScript` étendu aux infixes `tight`) se lit d'un bloc devant
une unité : **ni parenthèses** au rendu (`LatexRenderer.Child`), **ni
inversion** ni **imbrication** au score (`ScriptQuantity`). Miroir Rust.
Résultat : `3*10-3 ms` → 1er choix `3\times 10^{-3}\,\mathrm{ms}` ;
`3*10^8 m/s` → `3\times 10^{8}\,\mathrm{m/s}` en auto ; `x2 m` → `x^{2}\,\mathrm{m}`.
Écarté : marquer le connecteur d'unité `tight` (essayé : 134 premiers choix
changés hors périmètre, ex. `(1+2)/10-3m`, et 700 auto → popup absurdes).

## Extension 2 — « 2,5×103 » propose 2,5×10³

> « aie et le *103 on le fait marcher ?? » puis « 1. c'est sur » (le nombre d'abord) — utilisateur, 2026-10-14

Dans `nombre × 10x` (même amorce « nombre × » que le motif `10-n`,
`SciLead`), un entier qui commence par `10` suivi d'un chiffre non nul (`103`,
`108`, `1012` ; pas `100`, `1000`, `120`, `102,5`) a deux lectures d'atome
(mécanisme existant `Syms`) : **le nombre d'abord** (c'est un vrai nombre :
un prof qui tape `3*105` ne doit pas voir 3×10⁵ en tête), puis `10^{x}`.

## Bilan de non-régression (les trois volets + Unicode)

Comparaison ancien/nouveau moteur sur **16 625 entrées** (corpus généré +
variantes `/`, `+`, `x×`, espaces, unités, `10x`) : différences uniquement
dans les familles visées — Unicode 15, `10-n` 77, `10x` 90, exposant devant
unité 252 (parenthèses retirées, ou unité sortie de l'exposant : `10^35m` →
`10^{35}\mathrm{m}`) — **0 ailleurs**. 535 fixtures, C# = Rust.

## Validation post-fix

Dans Word : `2,5*10-3` → popup, 1er choix 2,5×10⁻³ ; `10-4` → 6 inchangé ;
`3*(10-6)` inchangé.
