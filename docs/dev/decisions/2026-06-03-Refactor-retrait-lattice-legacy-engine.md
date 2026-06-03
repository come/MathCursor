# Refactor — Retrait du moteur legacy Lattice (LatticeEngine + Lattice/)

**Date :** 2026-06-03
**Kind :** Refactor
**Température :** forte
**Statut :** acté
**Supersedes :** — (retire le fallback acté en P32, cf. ci-dessous)
**Lié à :** `2026-05-23-Feat-engine-v2-promotion` (P32, qui gardait le legacy en fallback), ADR moteur V2 from scratch

## Citation acté

> « Retrait direct + ADR » — utilisateur, 2026-06-03 (choix de stratégie : on
> assume que v2 couvre, suppression immédiate, tests verts en validation a
> posteriori, après cartographie complète de la surface de retrait).

## Contexte

Depuis **P32 (2026-05-23)**, `MathCursor.Engine` v2 est le moteur PRINCIPAL ;
l'ancien `LatticeEngine` (+ dossier `core-csharp/src/MathCursor.Core/Lattice/`,
~25 fichiers, ~3540 lignes) restait câblé comme **fallback** « pour les ~10% de
cas non couverts », marqué `[Obsolete]`, avec un kill-switch
`MATHCURSOR_ENGINE_V2=0`.

**Réalité du code (cartographie 2026-06-03) :** le legacy n'est en fait
**jamais consulté en résolution normale**. Dans `ZoneResolver.Resolve`, v2
(`_engineSource.TryResolve`) est tenté en premier et `EngineZoneSource` **ne
retourne jamais null** sauf **exception** (il synthétise une zone identité). Le
seul appel legacy effectif (`_engine.ConvertWithAmbiguity`) n'est donc atteint
que si v2 **throw**. `ZoneResolverLegacyFallbackTests` verrouille déjà
`LegacyFallbackCalls == 0` quand v2 répond. Le « ~10% » est un commentaire
d'époque P32, **sans cas concret tracé**.

Conclusion : le legacy est un attrape-exception coûteux (3540 lignes + double
chaîne de résolution + surcharges de ctor) pour un bénéfice nul en pratique. Le
garder freine la lisibilité et l'évolution du moteur.

## Décision

Retirer entièrement le moteur legacy Lattice. Sur exception v2, on **log +
retourne une zone identité** (dégradé gracieux), au lieu de basculer sur un
second moteur parallèle.

**Supprimé :**
- `core-csharp/src/MathCursor.Core/LatticeEngine.cs`
- `core-csharp/src/MathCursor.Core/Lattice/` (dossier entier)
- `core-csharp/src/MathCursor.Core/ILatexEngine.cs` (contrat obsolète)
- Tests legacy : `tests/.../Lattice/*`, `ZoneResolverLegacyFallbackTests`.

**Simplifié :**
- `ZoneResolver` : ctor sans `LatticeEngine`, `engineSource` requis (non-null) ;
  suppression de la branche fallback + des champs `LegacyFallbackCalls` /
  `LastResolveUsedLegacy`.
- `SuggestionService` ctor : sans param `engine`, sans kill-switch
  `MATHCURSOR_ENGINE_V2=0` (v2 toujours actif).
- `ThisAddIn` : suppression du field `_engine` + `LoadEmbedded`.

**Conservé (orthogonal à Lattice) :**
- `core-csharp/src/MathCursor.Core/Patterns/` (templates compositionnels, ne
  dépend pas de LatticeEngine).
- `IResolvedZoneSource`, l'adapter `MathCursor.Engine.Adapter`.
- `_vocab` (vient de `engineV2.Vocab` ; fallback `LocaleVocabulary.LoadEmbedded`
  conservé par sécurité).

## Tradeoff & alternatives écartées

- **Garder le fallback (statu quo P32)** : 3540 lignes mortes + double moteur +
  surcharges de ctor, pour 0 appel réel. Dette qui fige le moteur.
- **Log-first en prod 2-3 semaines avant retrait** (approche prudente) : écartée
  par l'utilisateur — la cartographie montre que le legacy ne fire que sur
  exception v2, et la suite de tests sert de preuve de couverture.

## Conséquences

- **Code touché** : ~20 fichiers (3 signatures de ctor : ZoneResolver,
  SuggestionService ; ThisAddIn ; + suppressions).
- **API publique** : ctors `ZoneResolver` / `SuggestionService` changent
  (param `engine` retiré). Interne au projet, pas d'impact externe.
- **Tests** : suppression des tests Lattice directs + LegacyFallback ;
  adaptation des ctors dans les tests ZoneResolver. Patterns intacts.
- **Risque** : si un cas réel dépendait du legacy (non tracé), il dégrade
  désormais en zone identité (pas de crash). À surveiller en usage réel.

## Plan d'exécution en étapes (cartographie 2026-06-03)

⚠️ **Piège identifié** : `Lattice/AlternativeGenerator.cs` mélange le générateur
legacy ET les **DTOs de résultat partagés** (`SourceMutation`,
`AmbiguityAlternative`, `AmbiguitySpot`, `AmbiguityMatch`) que **v2 consomme**
(`EngineToResolvedZone.cs`, `ResolvedZone.Spot/AllMatches`, popup, sidecar). On
ne peut donc PAS supprimer `Lattice/` en bloc. Étapes :

0. **Extraire les DTOs partagés** hors des fichiers à supprimer (nouveau fichier
   survivant, même namespace `MathCursor.Core.Lattice` pour zéro churn de
   `using`). Build + tests verts (move pur, zéro comportement). ← prérequis.
1. **Unwire** : `ZoneResolver` ne consulte plus `_engine` (sur exception v2 →
   zone identité) ; `engineSource` requis. Adapter ctors `ZoneResolver` /
   `SuggestionService`, retirer le kill-switch + champs `LegacyFallbackCalls` /
   `LastResolveUsedLegacy`, `ThisAddIn` sans `_engine`.
2. **Supprimer** les fichiers devenus non référencés : `LatticeEngine.cs`,
   `ILatexEngine.cs`, le reste de `Lattice/` (Lexer, Parser, Ast/, LatexRenderer*,
   LatticePathFinder, LatticeEdge, AlternativeGenerator-résiduel, Vocabulary,
   scanners Ambiguity NON utilisés par v2 — à confirmer fichier par fichier via
   le compilateur).
3. **Tests** : retirer `Lattice/*` + `ZoneResolverLegacyFallbackTests`, adapter
   ctors des tests ZoneResolver. Vert = preuve de couverture v2.

Chaque étape = commit séparé, build + tests verts entre chaque (pas de big-bang :
refactor du cœur de résolution, on avance par paliers vérifiables).

## Validation post-fix

Suite de tests complète verte (core + engine + adapter) APRÈS retrait = preuve
que v2 couvre tout le corpus testé. Build adapter VSTO vert. Batterie Word
inchangée (16/17, le fail intra-merge étant indépendant).
