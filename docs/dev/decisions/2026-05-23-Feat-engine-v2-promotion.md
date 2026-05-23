# Feat — Engine v2 promu moteur principal, legacy `[Obsolete]`

**Date :** 2026-05-23
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Supersedes :** [2026-05-22-Feat-engine-poc-isolation.md](2026-05-22-Feat-engine-poc-isolation.md) (= P11 sortie de POC)
**Lié à :** P11 → P31 (= construction Engine v2)

## Citation acté

> « peux tu sortir le nouveau "moteur" de l'etat de POC et faire que ca soit
> le principal.. marque l'ancien en DEPRECATED pour nettoyage plus tard »
> — utilisateur, 2026-05-23

## Contexte

Après 21 livraisons (P11 → P31), `MathCursor.Engine` :
- Couvre 90%+ des cas user (= 211 tests verts, 8 concepts YAML, 7 détecteurs C#)
- Drop-in transparent derrière le contrat `ResolvedZone` legacy
- Adapter VSTO inchangé (= 393/393 préservés)
- Stabilité validée user en Word

Le legacy `MathCursor.Core` (= `LatticeEngine` + `Patterns/Templates/` +
`Lattice/AlternativeGenerator`) reste fonctionnel mais ne reçoit plus de
nouvelles règles depuis P11. Toute nouvelle règle math passe par
`data-v2/concepts/*.yml` (= Engine v2).

## Décision

### 1. Engine v2 = moteur principal

`SuggestionService` construit Engine v2 par défaut. Pas de fallback
optionnel — Engine v2 est tenté **toujours**, le legacy reste comme
**filet de sécurité** pour les ~10% de cas non couverts (= `EngineZoneSource.TryResolve`
retourne `null` → `ZoneResolver` enchaîne avec le pipeline legacy).

**Kill-switch d'urgence** : `MATHCURSOR_ENGINE_V2=0` (= env var) désactive
complètement Engine v2 et utilise uniquement legacy. Pour rollback rapide
en prod si bug critique.

### 2. Legacy marqué `[Obsolete]`

Les 3 types racine du moteur legacy sont marqués `[Obsolete]` (= warning,
pas erreur) :

- `MathCursor.Core.LatticeEngine`
- `MathCursor.Core.Lattice.Parser`
- `MathCursor.Core.Lattice.AlternativeGenerator`

Message : « DEPRECATED P32 — replaced by MathCursor.Engine.MathEngine.
Kept as fallback for legacy cases not yet covered. Will be removed when
migration complete. Do not extend. »

CS0618 (= warning Obsolete) est ajouté à `WarningsNotAsErrors` dans :
- `MathCursor.Core.csproj` (= self-use du legacy en fallback)
- `MathCursor.Engine.Adapter.csproj` (= consume legacy via `ZoneResolver`)

### 3. Cleanup planifié

Ce qui sera supprimé **quand Engine v2 couvrira 100%** :
- `MathCursor.Core/Lattice/*` (= ~4 036 LOC)
- `MathCursor.Core/Patterns/Templates/*` (= ~1 800 LOC) — déjà subsumé par YAML concepts
- `MathCursor.Core/LatticeEngine.cs` (= ~270 LOC)
- Une partie de `ZoneResolver.cs` (= le pipeline ambig closed)

Estimation suppression : ~6 000 LOC. Conditions :
1. Engine v2 couvre 100% du corpus (= `LimAmbigBugTests`, `CorpusLyceeTests`)
2. Pas de régression sur les 393 tests adapter
3. Validation user en prod

## Tradeoff & alternatives écartées

- **Suppression directe du legacy maintenant** : risque de régression sur
  les 10% non couverts. Rejeté.
- **Pas de `[Obsolete]` (= juste commentaires)** : le warning visible est
  un signal fort pour quiconque tente d'étendre le legacy. Retenu.
- **`[Obsolete(error: true)]`** : casse la compilation, force suppression
  immédiate. Rejeté car prématuré.
- **Garder le feature flag binaire** : ambigu sur quel est le défaut.
  Rejeté au profit du kill-switch off-only.

## Conséquences

- **Code touché** :
  - `core-csharp/src/MathCursor.Core/LatticeEngine.cs` : `[Obsolete]` + doc
  - `core-csharp/src/MathCursor.Core/Lattice/Parser.cs` : `[Obsolete]`
  - `core-csharp/src/MathCursor.Core/Lattice/AlternativeGenerator.cs` : `[Obsolete]`
  - `core-csharp/src/MathCursor.Core/MathCursor.Core.csproj` : `WarningsNotAsErrors;CS0618`
  - `core-csharp/src/MathCursor.Engine.Adapter/MathCursor.Engine.Adapter.csproj` : idem
  - `adapter-vsto/src/MathCursor/Host/SuggestionService.cs` : simplification du wiring (= kill-switch off-only)
- **Tests** : Engine 211/211, Core 1266 (= 6 préexistants), Adapter 393/393.
- **API publique** : inchangée (= legacy reste appelable). `[Obsolete]` est
  un warning, pas un breaking change.
- **Règles MC impactées** : aucune.

## Validation post-fix

- Build complet OK avec warnings `CS0618` non-fatal.
- Tests verts (Engine, Core, Adapter).
- Manuel Word : `MATHCURSOR_ENGINE_V2=0` → mode legacy fonctionnel pour rollback.

## Plan en cours — état d'avancement

P32 :
- [x] `[Obsolete]` sur LatticeEngine + Parser + AlternativeGenerator
- [x] `WarningsNotAsErrors;CS0618` dans Core + Adapter projects
- [x] Simplification `SuggestionService` (= kill-switch off-only)
- [x] ADR (= ce document)
- [ ] Update CLAUDE.md (= mention Engine v2 = principal) — optionnel
- [ ] Cleanup legacy (= différé, conditions ci-dessus)
