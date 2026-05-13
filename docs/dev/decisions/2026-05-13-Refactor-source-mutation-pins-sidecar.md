# Refactor — Source-mutation pure pour les pins sidecar (élimine MC0006 du Core de prod)

**Date :** 2026-05-13
**Kind :** Refactor
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-05-13-Meta-mc0006-mc0009.md](2026-05-13-Meta-mc0006-mc0009.md) (introduit MC0006)
+ [2026-05-13-Refactor-ast-visitor.md](2026-05-13-Refactor-ast-visitor.md) (étape 4 Visitor)
+ commits `9ab248b` (ScanDecoratedTwoThreeUpper fix double-wrap)
+ Brief `MATHCURSOR_ARCHITECTURE_EXTENSIBILITE.md` (axe A + pattern interne)

## Citation acté

> « A » — utilisateur, 2026-05-13
> (après ré-évaluation du tradeoff sans biais temps : sur les
> critères qualité + perf + maintenabilité, A domine B sur 5 dimensions,
> équivalent sur 1, marginalement inférieur sur 1)
>
> « ok go! » — utilisateur, 2026-05-13
> (validation finale du plan 8 étapes en 3 sous-livraisons)
>
> Rappel principe utilisateur le même jour :
> « le temps IA est inférieur au temps humain donc les tradeoff sur le
> temps on prends la qualité + les perfs + la maintenabilité toujours,
> jamais le gain de temps »

## Contexte

La règle MC0006 (introduite ADR `2026-05-13-Meta-mc0006-mc0009`) capture
le pattern racine du bug double-wrap :

```csharp
// ZoneResolver.cs:205 — anti-pattern visible par MC0006
topLatex = topLatex.Substring(0, s.Start) + s.AltLatex + topLatex.Substring(s.End);
```

Le splice opère sur le texte DÉJÀ rendu (avec wraps `\left(...\right)`
posés par le parser à partir des délimiteurs source). Splicer une alt
qui est elle-même la décoration courante produit
`\left(\left(AB\right)\right)` (cas observé bug 11-05).

Le mécanisme `ApplyPreferences` (source-mutation pour les `_preferences`
session, ex. V→∀) existe déjà et fonctionne proprement par construction.
Mais les **pins sidecar** (`SpanPin`, `RulePin`, `SpanOverride`, hints
contextuels) ne passent pas par ce chemin — ils sont matérialisés via
le splice latex. Deux chemins parallèles pour le même objectif.

## Décision

**Unifier les deux chemins sur le modèle source-mutation pure.** Le
splice latex devient un fallback réservé uniquement aux alts qui n'ont
pas de forme source naturelle (bracket : `[AB]` est parsé en intervalle
FR, donc impossible à source-muter).

Concrètement, refacto en 4 systèmes interdépendants :

### Système 1 — Mutations sur les alts two-uppercase

`AlternativeGenerator.ScanUppercaseSequences` passe au scan source-based
(modèle des 7 autres scanners qui scannent déjà la source :
`ScanVAsForallEAsExists`, `ScanCanonicalSetLetters`, etc.).

Chaque alt reçoit une `SourceMutation` :
- Vec : `Mutation(offset, len, "vec " + pair)` → `\vec{AB}`
- Paren : `Mutation(offset, len, "(" + pair + ")")` → `\left(AB\right)`
- Bracket : **pas de Mutation** (`[AB]` parsé comme intervalle FR par
  le parser). Reste sur fallback latex-splice.

Idempotence : si la pair AB est déjà entourée de `(...)` user-explicites
dans la source, l'extension de span couvre le wrap (cf.
`ScanDecoratedTwoThreeUpper`), la mutation paren devient identité
no-op.

### Système 2 — `ApplyPreferences` étendu

Méthode actuelle : ne lit que `_preferences` session dict.

Nouvelle : `ApplyAllMutations(source, sessionPrefs, sidecar, hints)`
itère jusqu'à fixpoint, pour chaque match résout `ResolveBestAlt`
(déjà extrait), applique `Mutation` si présente, sinon laisse au
splice fallback.

### Système 3 — Pin v2 source-mutation-aware (offset tracking)

Le système Pin v2 actuel (`SpanPin`, `RulePin`, `SpanOverride`,
`MatchSignature`) repose sur des positions source-stables. Quand
`ApplyAllMutations` mute la source (`AB` → `vec AB` = +4 chars),
les pins en aval doivent shifter.

Mécanisme : tracker une liste `List<MutationDelta>` au fur et à mesure,
appliquer le shift aux pins en aval avant chaque itération du fixpoint.
Réutilise potentiellement le `SidecarMerger` qui gère déjà les offset-
shifts pour le cross-paragraphe merge.

Alternative envisagée : utiliser `MatchSignature` (rule + defaultLatex
+ occurrenceIdx) comme clé primaire, survit naturellement aux
mutations. Mais `SpanPin` legacy v1 reste offset-based — la migration
complète ou la double-stratégie sera évaluée pendant l'implémentation.

### Système 4 — Élagage du splice loop

Dans `ZoneResolver.Resolve(rawSource, globalCtx, sidecar)`, la boucle
splice ligne 205 ne tourne PLUS que pour les matches dont l'alt
résolue n'a pas de `Mutation` (bracket et alts complexes futures).

MC0006 disparaît du Core de prod pour le cas majoritaire (2 hits
existants éliminés). Hits résiduels sur bracket → ADR de suppression
ciblée séparée si pertinent.

## Tradeoff & alternatives écartées

(Évalués sur qualité + perf + maintenabilité uniquement. Le temps
d'implémentation n'est pas un critère.)

- **Option B — `SuppressMessage` + ADR Limit pour accepter le splice**.
  Rejeté : maintient deux chemins parallèles d'application des pins
  (drift potentiel permanent), préserve l'anti-pattern reconnu dans
  le Core. Dégrade qualité archi + maintenabilité long terme. La
  robustesse user-visible est certes assurée par
  `ScanDecoratedTwoThreeUpper`, mais ce dernier est un patch sur le
  symptôme, pas une élimination de la cause.

- **Option C — Refacto partiel (Mutations sur vec/paren, splice loop
  conservé en parallèle pour tout)**. Rejeté : dualité permanente
  même pour les alts qui ont une Mutation. Augmente la complexité
  globale sans gain proportionnel.

- **Désactiver MC0006 globalement via `.editorconfig`**. Rejeté :
  ferait disparaître la règle sur les futurs sites de splice non
  prévus. Perte de capacité du harnais.

## Conséquences

- **Code touché** :
  - `core-csharp/src/MathCursor.Core/Lattice/AlternativeGenerator.cs`
    — `ScanUppercaseSequences` réécrit en scan source-based, alts
    enrichies de `SourceMutation`. Évaluation de `ScanDecoratedTwoThreeUpper`
    redondance/conservation pour Group two-upper.
  - `core-csharp/src/MathCursor.Core/Lattice/Vocabulary.cs`
    — keyword `triangle` ajouté si absent (pour permettre la mutation
    source de l'alt triangle de `RuleThreeUppercase`).
  - `core-csharp/src/MathCursor.Core/ZoneResolver.cs`
    — `ApplyPreferences` étendu / `ApplyAllMutations` créé. Boucle
    splice réduite. Offset tracking implémenté.
  - `core-csharp/src/MathCursor.Core/Resolution/SidecarMerger.cs`
    — potentielle extraction du mécanisme offset-shift.
  - Tests existants `Resolution/*Tests.cs` (~13) : adaptation des
    invariants Pin v2 pour le source-mut-aware.

- **Tests** :
  - Core : 935/944 verts (6 préexistants) avant refacto → cible : pas
    de régression nette. Adaptation des tests Pin v2 attendue (mêmes
    invariants, source mutée).
  - Adapter : 419/419 verts à préserver.
  - Analyzer : 27/27 verts.
  - Nouveaux tests à ajouter : couverture mutation+pin combinés,
    offset tracking, idempotence sur sources pré-wrappées.

- **API publique** :
  - `LatticeEngine.ConvertWithAmbiguity` : signature inchangée,
    comportement préservé.
  - `ZoneResolver.Resolve(rawSource, globalCtx, sidecar)` : signature
    inchangée. `AmbiguityAlternative.Mutation` plus souvent non-null,
    comportement consommateurs préservé.
  - `BaseTopLatex` / `TopLatex` : sémantique préservée.

- **Règles MC impactées** :
  - **MC0006** : disparaît côté `ZoneResolver:205` (objectif principal).
    Reste sur hits résiduels (bracket fallback, sites de test légitimes).
  - **MC0009** : aucun impact (pas de SuppressMessage prévu pour ce
    refacto).
  - **MC0001** : aucun impact direct.

- **WarningsNotAsErrors** dans `MathCursor.Core.csproj` :
  `MC0006` retiré une fois les hits Core éliminés.

## Validation post-refacto

```bash
# 1. Build sln : pas de hit MC0006 sur ZoneResolver
dotnet build MathCursor.sln 2>&1 | grep "MC0006.*ZoneResolver"
# → 0 ligne attendue.

# 2. Tests core préservés
dotnet test core-csharp/tests/MathCursor.Core.Tests/MathCursor.Core.Tests.csproj
# → 935/944 verts (6 préexistants).

# 3. Tests adapter préservés
dotnet test adapter-vsto/tests/MathCursor.Tests/MathCursor.Tests.csproj
# → 419/419 verts.

# 4. Analyzer
dotnet test analyzers/MathCursor.Analyzers.Tests/MathCursor.Analyzers.Tests.csproj
# → 27/27 verts.

# 5. Bench perf (optionnel — étape 8 du plan)
# Mesurer le temps de résolution sur un corpus typique.
# Acceptable : +0-5 ms par résolution (négligeable pour l'UX).
# Bloquant si : > +20 ms (refacto à reconsidérer).
```

## Fenêtre de réversibilité

Cet ADR pose une refonte structurelle (Température forte). Conditions
qui justifieraient sa retraction via un nouvel ADR superseding :

1. **Régression perf** > +20 ms par résolution sur le corpus typique.
2. **Régression robustesse** : un cas user-facing que `ApplyAllMutations`
   ne couvre pas et que l'ancien splice latex aurait couvert.
3. **Complexité Pin v2 inacceptable** : si l'offset tracking devient
   un foyer de bugs récurrents, on peut revenir à la dualité splice +
   source-mut comme architecture pérenne (vs unification).

Les conditions 1-3 sont peu probables sur la base du diagnostic actuel
(ApplyPreferences fonctionne déjà pour V→∀, le mécanisme est éprouvé).

## Plan d'exécution — 3 sous-livraisons

### Sous-livraison 1 — Mutations sur alts (étapes 1-3 du plan)

1. Vocabulary : keyword `triangle` (vérifier `vec`, `angle`, `widehat`
   présents).
2. `ScanUppercaseSequences` converti en scan source-based.
3. `MakeUpperSpot` ajoute `SourceMutation` pour vec et paren.
4. Tests existants préservés + nouveaux tests sur les Mutations émises.
5. Commit S1.

### Sous-livraison 2 — `ApplyPreferences` étendu (étapes 4-5)

1. Nouvelle méthode `ApplyAllMutations(source, sessionPrefs, sidecar,
   hints)` qui itère fixpoint.
2. Offset tracking pour Pin v2 source-mut-aware.
3. Tests Pin v2 adaptés + nouveaux tests mutation+pin.
4. Commit S2.

### Sous-livraison 3 — Élagage splice + bench (étapes 6-8)

1. `ZoneResolver.Resolve` : boucle splice réduite aux alts sans Mutation.
2. Évaluation `ScanDecoratedTwoThreeUpper` : conservation pour Vec/Angle,
   simplification possible pour Group.
3. `MC0006` retiré de `WarningsNotAsErrors` Core.csproj.
4. Bench perf.
5. Commit S3.

## Plan refacto / harnais — état d'avancement

**Refacto archi extensibilité** :
- [x] Étape 1 — Cartographie
- [x] Étape 2 — Abstractions
- [ ] Étape 3 — Implémentation par types existants (optionnel)
- [x] Étape 4 — Visitor sur AST
- [→] **En cours (cet ADR)** — Source-mutation pour les pins
- [ ] Étape 5 — Sortir chaînes FR du Core + activation MC0002
- [ ] Étapes 6-8 — DomainRouter, ShortcutResolver, test intégration

**Harnais** :
- [x] Phase 0+1 — Analyzer setup + MC0001
- [x] Phase 2 — Directory.Build.props généralise
- [x] Phase 2.5 — MC0006 + MC0009
- [x] Phase 3 — Skills `/mathcursor-plan` + `/mathcursor-adr`
- [ ] Phase 5 — Diff summarizer
- [ ] Phases 4, 6-9
