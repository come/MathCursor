# Feat — Sidecar de résolutions + doctrine d'architecture en couches

**Date :** 2026-05-06
**Kind :** Feat
**Température :** forte
**Statut :** acté

## Décision

### A. Refactor désambiguïsation : `ResolutionSidecar`

Remplacer les **deux mécanismes parallèles** actuels de désambiguïsation
(UI-substitution éphémère côté popup, source-mutation côté ZoneResolver)
par **un seul mécanisme persistant** : un *sidecar de résolutions* attaché
à chaque source brute.

1. **Type `ResolutionSidecar`** (POCO sérialisable JSON) en `MathCursor.Core.Resolution` :
   - `SpanPins` : choix explicites par offset (« cet `AB` à offset 0 a été
     résolu en vec »). Réversibles, pas verrouillés.
   - `ZoneVotes` : compteurs `(rule → altIdx → count)` qui boostent le
     ranking des futurs spans non-résolus dans la même zone, sans figer.
2. **Nouvelle API resolver** `ZoneResolver.Resolve(source, sidecar)` :
   - Pipeline normal sur `source` (préférences `V→∀` etc. inchangées).
   - Applique les `SpanPins` en post-render (substitue le LaTeX du span
     par l'alt pinée).
   - Applique les `ZoneVotes` en boost de ranking pour les ambigs
     non-pinées : `score = base + α × votes[rule][alt]` avec `α≈0.4`.
3. **Cross-merge** : `SidecarMerger.Merge(parts, offsetShifts)` recalibre
   les pins selon position dans la mergedSource et somme les votes par
   rule/alt. Une part `null` ou `Empty` est ignorée silencieusement (cas
   OMath créé avant l'introduction du sidecar).
4. **Persistance** : le store (`IEquationStore`) accepte source + sidecar
   sérialisé en JSON dans le CustomXMLPart à côté de la source brute.
   Au reload, sidecar relu et appliqué — le choix vec survit reload Word,
   copy-paste, undo, edit OMath.
5. **Couverture** : tous les `Rule*` deviennent transparents — pas
   seulement `RuleTwoUppercase`. Les 8 Rules aujourd'hui « LaTeX-only »
   (cf. cause racine) sont corrigés d'un coup.
6. **Phasage** :
   - **Phase 1** (~5h) : sidecar POCO + SpanPins + `Resolve(source, sidecar)`
     + tests RED→GREEN. Couvre single-line + cross-merge basique.
   - **Phase 2** (~3h) : ZoneVotes + auto-resolve par boost. Gain UX.
   - **Phase 3** (~3h) : persist store + edit OMath round-trip.

### B. Doctrine d'architecture en 7 couches

Chaque module appartient à **une couche** unique. Stabilité décroissante
en montant :

| Couche | Rôle | Stabilité |
|---|---|---|
| **L0** Cœur lattice (math pure) | Lexer, LatticePathFinder, Parser, LatexRenderer, AST | **immuable** |
| **L1** Désambiguïsation et résolution | ZoneResolver, AlternativeGenerator, SourceMutation, ResolutionSidecar | **doit être solide comme L0** ← cible refactor |
| **L2** Persistance | `IEquationStore` (POCO + JSON) | stable |
| **L3** Détection (sources) | MathNerDetector, WordContextReader, AutocorrectNormalizer, manual trigger | NER se réentraîne, API stable |
| **L4** Orchestration / cycle de vie | SuggestionService, KeyboardInterceptor, ListModeStateMachine, mergers | changeant — chaque feature ajoute des transitions |
| **L5** UI / Présentation | SuggestionPopupWindow, FeedbackDialog, Ribbon | volatile par design (UX évolue) |
| **L6** Plateforme / Bridge | ThisAddIn (VSTO), VstoEquationStore, WpfMathAdapter | lié à Word, stable une fois écrit |
| **L7** Backoffice / Telemetry | /admin/*, FeedbackBundle, /api/v1/report | isolé, additif |

### C. Principes durs (à respecter pour tout futur changement)

1. **Stabilité décroissante en montant.** L0 jamais touchée. L1 rare et
   uniquement par ajout (cf. principe 3). L4-L5 peuvent évoluer.
2. **Inversion de dépendances stricte.** L_n connaît L_{n-1} via interface,
   jamais l'inverse. Concrètement : pas de `using Microsoft.Office.*` en
   L0/L1.
3. **Core strict, extension par interface — pas d'empilement de `if`.**
   Anti-pattern interdit en L0/L1 :
   ```csharp
   if (rule == "two-uppercase") doVec();
   else if (rule == "three-uppercase") doTriangle();
   // …
   ```
   Pattern requis : interface dans le core, classe par variante,
   dispatch via registry/DI :
   ```csharp
   public interface IAlternativeRule { string Apply(...); }
   // 8 classes, 1 par rule, dispatch via Dictionary<string, IAlternativeRule>
   ```
4. **Logique métier ne vit jamais en L5 (UI).** La popup PRÉSENTE et
   COLLECTE le choix. Elle ne décide pas ; elle alimente le sidecar via
   L1.
5. **Persistance neutre (POCO + JSON).** Aucun type plateforme dans les
   POCO de L1/L2. Permet portage phase 2 (Office.js) sans toucher L0-L4.
6. **Tests par couche.** Refactor d'une couche = tests de cette couche
   RED→GREEN, les autres couches GREEN inchangées (régression check).

### D. Roadmap induite (hors scope direct mais conditionnée)

- **Découpage L4** : SuggestionService (god object 2400 LoC) à découper
  en `EditModeService`, `CrossMergeService`, `CommitPipelineService` —
  faisable une fois L1 stable, pas avant.
- **Phase 2 Office.js** : remplace L6 (VSTO bridge) par L6' (Office.js
  bridge en TypeScript). L0-L5 inchangés grâce à la doctrine. Le sidecar
  JSON traverse les 2 mondes sans modif.
- **Future L1.5** (preference learning model) : modèle ML qui ranke les
  alts à partir d'historique global. Couche additive purement, pas de
  modif L0-L1.
- **Nouveaux Rules** : 1 fichier classe + enregistrement dans le registry,
  ZoneResolver pas touché. La doctrine garantit que ça reste vrai.

## Pourquoi

### Cause racine du bug user 06-05

Aujourd'hui, deux mécanismes coexistent en couche L1/L5 :
- **Mécanisme A** (UI substitution) : la popup `SuggestionPopupWindow`
  remplace `AB→\vec{AB}` dans son `_resolvedSubstitutions`. Marche
  single-line. **N'est pas persisté** : dès qu'on re-pipeline (cross-merge,
  edit OMath, undo, copy-paste, reload), le choix est perdu.
- **Mécanisme B** (source mutation) : alt porte une `Mutation` qui modifie
  la source (`V x R` → `forall x R`). Persiste tant que `_preferences` du
  ZoneResolver vit. Couvre `RuleVAsForall`, `RuleEAsExists`,
  `RuleCanonicalSet`. **8 Rules sur 11 sans Mutation** — fragiles dès le
  premier re-pipeline.

Le test cross-merge (`AB+BC=CD\n= CH+HD` avec choix vec) reproduit
exactement le bug : ligne 1 commit en vec via mécanisme A → cross-merge
re-pipeline depuis source brute → mécanisme A perdu, mécanisme B
inopérant pour `RuleTwoUppercase` → top render sans vec.

### Pourquoi 3 fixes locaux écartés

1. **Préfixe magique côté lex** (`vAB` au lex) : encode le choix dans la
   source brute, casse la lisibilité, combinatoire pour les rules à 4+ alts,
   invasif L0.
2. **Transporter le LaTeX rendu au cross-merge** : marche local mais ne
   résout pas edit OMath, copy-paste, undo, reload — c'est tout L4-en-aval
   qui aurait son propre patch.
3. **Post-substitution UI-side réjouée** : remet du UI-side dans le Core,
   ne survit pas au reload Word.

Aucune ne résout la **cause racine** : la non-persistance du choix de
désambig à côté de la source.

### Pourquoi sidecar + boost (vs pin rigide)

Le sidecar avec `SpanPins` seuls = pin rigide qui ne s'adapte pas. L'user
qui ajoute un nouveau span dans la zone doit re-désambiguer. Frustrant.

`ZoneVotes` ajoute un mécanisme **probabiliste local** : le choix muscle
le ranking sans le figer. Trois votes vec dans la zone → futur span
nouveau tombe auto sur vec. Si l'user veut paren sur ce nouveau span, il
clique → vote paren ajouté → équilibre. Pas de doctrine "rule globale" qui
prendrait le pas hors zone, juste un boost local proportionnel.

### Pourquoi la doctrine en couches en même temps

Le bug est en L1 mais la **dette** est en L1+L5 (couplage UI-logique).
Refactor sans formaliser la doctrine = les futures features re-créent
les mêmes asymétries. Ancrer les principes durs dans cet ADR = filet
pour tous les ADR à venir : un brief future qui propose "logique métier
dans la popup" est rejeté d'office.

### Alternatives écartées (architecture)

- **Architecture monolithique inchangée + patches** : court terme moins
  cher, long terme drift garanti.
- **Refactor en hexagonal/clean strict** : 7 couches mappées à hexagonal
  (domain/application/adapters), surdimensionné pour notre taille (~30k
  LoC), serait du gold-plating.
- **Plus de couches** (L1.5 alternatives, L4.5 commit pipeline...) :
  rejeté MVP — on garde 7, on ajoute en L1.5/L4.5 si vraie valeur émerge.

### Validation par tests

Tests RED→GREEN posés AVANT l'implémentation (test-first) :

- 8 tests `ResolutionSidecarTests` (POCO) — **GREEN dès maintenant**
- 7 tests `SidecarMergerTests` (fusion pure) — **GREEN dès maintenant**
- 6 tests `ZoneResolverWithSidecarTests` (API future) — **4 RED, 2 Skip**

Les 4 RED encodent le contrat post-refactor. Quand ils passent GREEN,
le refactor Phase 1 est livré. Les 2 Skip (zone-boost auto-resolve, stale
pin fallback) deviennent GREEN en Phase 2.

15 tests d'ancrage GREEN sécurisent la fondation pendant la refacto.

## Statut + Citation

> ca me plait.. pourrait t'on faire qu'un choix de desambuigisation
> "muscle" fort l'ordre de ranking, mais n'empeche pas de changer d'avis
> apres si besoin ? et ca doit vivre au niveau paragraphe ou zone à mon
> avis.. ne jamais devenir une regle figée mais additionner les probas
> avec l'occurence pour conserver la flexibilité.
>
> l'idée pour moi dans l'architecture globale c'est d'avoir un systeme
> en couches.. tres robustes pour les couches basses -> puis evolutives
> et facilement ajoutables dans les couches plus hautes.. j'imagine le
> systeme de desambuigisation assez bas car c'est pas loin du coeur du
> systeme.
>
> les couches basses doivent etre core strict et concis => extensibles
> via des classes d'interfaces si besoin d'adapter temporairement.. pas
> empiler les ifs
>
> nickel !

— come, 2026-05-06

## Conséquences / suivi

- **Migration des OMaths existants** : pas de migration. Au reload, OMath
  sans sidecar = traité comme `ResolutionSidecar.Empty`. L'user qui veut
  récupérer son vec re-clique. Acceptable.
- **Format JSON du sidecar** : versionné dès le départ via un champ
  `version: 1` dans le JSON pour permettre évolutions futures.
- **Performance** : ajout négligeable. Une lookup pin = O(n) sur ~5-10
  pins par zone. Boost ranking = 1 dict lookup par ambig.
- **Compatibilité phase 2 Office.js** : facilité (POCO JSON sérialisable).
- **Anti-patterns à monitorer** :
  - Si un futur PR ajoute `if (rule == "X")` dans L0/L1 → rejet.
  - Si un futur PR ajoute logique métier dans `SuggestionPopupWindow` →
    rejet, faut alimenter le sidecar et passer par L1.
  - Si un futur PR ajoute `Microsoft.Office.*` en référence dans
    `core-csharp/` → rejet hard (la règle existe déjà en CLAUDE.md).
- **Suite ADR** : un futur ADR `2026-05-XX-Meta-suggestion-service-decoupage`
  formalisera le découpage L4 quand on s'attaquera au god object.
