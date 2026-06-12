# Feat — Points de suspension mathématiques : … ⋯ ⋮ ⋱ (atomes)

**Date :** 2026-06-12
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Feat-symbols-latex-aliases-lot1.md](2026-06-10-Feat-symbols-latex-aliases-lot1.md) (même pattern : symboles lycée + noms LaTeX nus en alias)

## Citation acté

> « est ce que dans notre truc on peut rajouter les dots (verticaux / horizontaux et en biais) » — utilisateur, 2026-06-12
> Choix au cadrage : « ... » → points bas (\ldots) partout, v1 atomes seulement (pas de liste à virgules top-level). Plan approuvé.

## Contexte

Aucun point de suspension n'existait : `...` se lexait en trois `\cdot` infixes (lecture bruit), `…` (U+2026 — l'autocorrection Word transforme `...` en `…` par défaut !) jetait « caractère inattendu », `vdots`/`ddots` inconnus. Cas d'usage lycée : suites (`1 + 2 + ... + n`, `u_1 + ... + u_n`) et matrices génériques (cellules ⋮ ⋱ ⋯).

## Décision

### Quatre atomes (factory `Lit`, pattern `inf` → `\infty `)

| Saisie | LaTeX | Word |
|---|---|---|
| `...`, `…`, `dots`, `ldots` | `\ldots` | … (U+2026) |
| `cdots`, `c...` | `\cdots` | ⋯ (U+22EF) |
| `vdots`, `v...` | `\vdots` | ⋮ (U+22EE) |
| `ddots`, `d...` | `\ddots` | ⋱ (U+22F1) |

Raccourcis lettre+points (demande user : « et evidemment alias "..." / "v..." / "d..." ») :
lookahead du lexer après un run de lettres — si la clé « mot... » existe, le
mot absorbe les points qui suivent (`...` tapé OU `…` autocorrigé), avec repli
minuscule pour l'autocapitalisation (`V...` → ⋮).

`...` → **points bas partout** (zéro popup supplémentaire) ; les centrés ⋯ se demandent explicitement par `cdots`. Atomes ordinaires : légaux en opérande (`1 + ... + n`), en cellule de matrice, entre parenthèses — Parser/Score/Renderer intouchés.

### Lexer : scan symbole étendu

Le scan opérateur (longueurs {2,1}) passe à {3,2,1} et accepte la shape `atom` — couvre `...` (3 chars) et `…` (clé Vocab directe, pattern Unicode collé-copié). `.` seul reste `\cdot`, décimales et unités (`4.5`, `2 m.s-1`) inchangées.

### Sérialisation

Table `Symbols` de `LatexToOmml` : 4 entrées vers les caractères Unicode. Les cellules de matrice passent déjà sans restriction.

## Tradeoff & alternatives écartées

- **« ... » intelligent (popup [\ldots, \cdots] selon contexte)** : typographiquement plus juste entre opérateurs, mais une popup de plus à chaque frappe de `...` — rejeté par l'utilisateur (fluidité d'abord, `cdots` au clavier pour les puristes).
- **Lecture « liste à virgules » top-level (`1, 2, ..., n`)** : reportée à l'acté de cet ADR (**Limit**, fixture-erreur au corpus), puis **levée le jour même** par [2026-06-12-Feat-comma-tuples-bare-lists-repere.md](2026-06-12-Feat-comma-tuples-bare-lists-repere.md) (listes nues gardées anti-prose) — `1, 2, ..., n` est désormais AUTO, la fixture-limite s'est retournée.

## Conséquences

- **Code touché** : `Vocabulary.cs` (4 `Lit` + clés `...`/`…` + alias `ldots`), `Lexer.cs` (scan {3,2,1} + shape atom), `LatexToOmml.cs` (table `Symbols` +4).
- **Aperçu popup (WpfMath)** : sondé — WpfMath 2.1 rend `\ldots`/`\cdots` nativement mais ignore `\vdots`/`\ddots` (commande ET caractères ⋮/⋱), et l'extraction TextBlock Unicode est impossible (les dots vivent DANS les cellules de matrice). Dégradation APERÇU seulement, via `WpfMathAdapter.LiteralSubs` : `\vdots` → `:`, `\ddots` → `\cdots` — Word reçoit les vrais glyphes via OMML. Même famille d'approximations que `\mathbb` → `|X`.
- **Tests** : fixtures +12 (`...`/`…`, mots-clés, raccourcis `v...`/`d...`, expressions à dots, 2 matrices génériques, fixture-limite `1, 2, ..., n` → erreur) → corpus 400 ; `LatexToOmmlTests.Symbols_map_to_unicode` +4 ; `PopupLatexCoverageAuditTests` vert (preuve du pipeline popup complet).
- **NER** : non touché — si les suites en prose avec `...` ne sont pas détectées chez les beta-testeurs, extension corpus v9.

## Validation post-fix

1. Probe moteur : `...`/`…`/mots-clés + non-régressions `u.v`, `4.5`, `4,5`, `2 m.s-1`.
2. Suites moteur + adapter vertes (audits popup/OMML compris).
3. Word : `1 + 2 + ... + n` → conversion avec … ; matrice avec ⋮/⋱ en cellules ; `...` autocorrigé en `…` par Word au vol → zone toujours convertie.
