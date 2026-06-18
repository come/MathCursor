# Feat — Mots-clés partiels par préfixe (≥ 3 lettres)

**Date :** 2026-06-18
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** `docs/dev/engine-backlog.md` (item #2), [2026-06-16-Feat-portable-engine-universal-vocab.md](2026-06-16-Feat-portable-engine-universal-vocab.md)

## Citation acté

> « 2, le plus costaud en effet » + arbitrages plan mode : « popup multi-candidats
> MAIS rajouter en dessous de chaque choix à quoi ça correspond » ; « Tout, grec
> inclus » ; « Tout d'un coup (P1+P2+P3) » — utilisateur, 2026-06-18

## Contexte

Beaucoup d'abréviations sont des **alias énumérés** (`som`, `rac`, `integ`…). On
veut un mécanisme **générique** : taper un préfixe ≥ 3 lettres d'un mot-clé
reconnu l'étend (`appro`→approx, `fora`→forall, `unio`→union, `sub`→subset…).
Si plusieurs mots-clés matchent → **popup**, chaque candidat étiqueté du mot-clé
complet. Familles : fonctions, opérateurs nommés, alias FR, **noms grecs**.

## Décision

### Index de préfixes (`EngineCulture`)
Par culture : ensemble « préfixable » = clés Vocab **alphabétiques** (→ elles-
mêmes, grec inclus) + alias alphabétiques (→ cible). `PrefixMatches(word)` :
≥ 3 lettres, alphabétique, **word PAS exact-connu** (y compris insensible à la
casse — `Int`→int reste l'intégrale), préfixe **strict**, **dédup par cible
canonique** (garde la forme la plus longue pour l'affichage). Trié déterministe.

### Génération par **substitution d'entrée** (≠ forking lexer)
**Choix d'archi (déviation assumée du plan初)** : plutôt que de forker le cœur du
lexer (mécanisme `choices`/`SplitPenalty` délicat, risqué), `ForestEngine.Run`
analyse l'**entrée originale** (lecture littérale — comportement historique
**inchangé**) **+** des variantes où chaque mot préfixe-extensible est remplacé
par un mot-clé candidat. Tous les candidats concourent dans `Finish` (tri par
coût). Bornes : `MaxPrefixSpots = 3`, `MaxVariants = 12`.
→ **Le littéral est toujours analysé tel quel ⇒ zéro régression possible** sur
l'existant ; les expansions ne font qu'**ajouter** des candidats.

### Étiquette popup (`EngineCandidate.Hint` → badge)
`EngineCandidate` porte un `Hint` optionnel (le mot-clé complet, ex. « arcsin »),
posé quand exactement un mot a été substitué dans la variante, conservé au dédup.
Plombé `ConversionController` → `SuggestionPopupWindow.ShowCandidates(…, hints)` →
badge « = arcsin » (prioritaire sur le badge d'aperçu `CandidateHints`).

### Garde-fous (faux positifs)
Exact-match prioritaire (y c. casse), ≥ 3 lettres, **lecture littérale toujours
candidate** (le mot non étendu reste une option, donc l'auto ne s'impose pas si
douteux), popup pour les ambigus. Constats : `app`→approx **impossible** (`app`
exact alias de `appartient`→in) ; `der`→dérivée **abandonné** (pas d'opérateur).

## Tradeoff & alternatives écartées

- **Forking N-aire du lexer** (plan initial) : écarté — toucher `LexAll`/
  `SplitPenalty` (cœur critique, 447 fixtures) était risqué pour un gain nul vs
  la substitution d'entrée, qui réutilise `Analyze` tel quel et garantit le
  littéral. Coût : N analyses (borné, seulement si mots ambigus présents).
- **Étiquette dérivée du LaTeX** (adapter seul) : écarté — le « quoi taper » vient
  proprement du moteur (`Hint`), pas d'un mapping inverse LaTeX→mot-clé partiel.
- **Expansion seulement si unique** : écarté (l'utilisateur veut le popup multi).

## Conséquences

- **Moteur (L1)** : `EngineCulture` (index + `PrefixMatches`), `ForestEngine`
  (`Run`→variantes, `CollectFromInput`, `BuildInputVariants`, `EngineCandidate.Hint`).
  Données (`symbols.json`/`cultures.json`) **inchangées** (l'index se dérive du
  Vocab/alias). Profite au futur port Python (même logique).
- **Adapter (L3)** : `ConversionController` (passe les hints), `SuggestionPopupWindow`
  (badge hint moteur).
- **Tests** : +3 fixtures (`a appro b`→`\approx`, `a unio b`→`\cup`, `a sub b`→
  popup [subset|subseteq]) → corpus **450**. Engine 21 / adapter 317 / serial 60
  verts ; **zéro régression** (447→450).
- **API publique** : `EngineCandidate.Hint` ajouté (rétro-compat, optionnel).

## Validation post-fix

`a appro b`→`a\approx b` (auto) ; `a sub b`→popup [subset|subseteq] étiquetés ;
`som`/`inclu`/`cos`/`Int` inchangés (exact-match) ; `abc` non étendu. Corpus
450/450. Manuel Word : `appro`+Ctrl+Espace ; `sub` → popup avec « = subset » /
« = subseteq ».

## Limites connues
- Préfixe autocapitalisé d'un mot-clé minuscule (« Appro » en début de phrase) non
  étendu (match casse-sensible) — mineur. `MaxVariants`/`MaxPrefixSpots` bornent
  les expressions à plusieurs mots ambigus (rare).
