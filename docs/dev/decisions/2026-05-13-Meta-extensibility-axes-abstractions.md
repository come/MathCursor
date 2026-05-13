# Meta — Projet `MathCursor.Core.Abstractions` (5 axes d'extensibilité)

**Date :** 2026-05-13
**Kind :** Meta
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [docs/dev/architecture/cartography.md](../architecture/cartography.md)
+ briefs externes `MATHCURSOR_HARNESS_BRIEF.md` et `MATHCURSOR_ARCHITECTURE_EXTENSIBILITE.md` (téléchargés 2026-05-13, archivés hors repo).

## Décision

Création d'un nouveau projet `core-csharp/src/MathCursor.Core.Abstractions/`
(.NET Standard 2.0, zéro dépendance) qui héberge les **contrats
d'extensibilité** des 5 axes définis par le brief archi :

| Fichier | Axe | Rôle |
|---|---|---|
| `IConstructStrategy.cs` | A — vocabulaire math | Contrat d'une construction notationnelle (fraction, racine, intégrale…) |
| `IDomainParser.cs` | B — domaine | Contrat d'un parseur de domaine complet (math, chimie, logique…) |
| `ILocaleLexer.cs` | C — locale entrée | Lexique mot-clé naturel → symbole canonique (FR / EN / DE) |
| `ILocaleNER.cs` (+ `NamedEntity`) | C — locale entrée (NER) | Contrat d'un détecteur de zones math (multi-locale ou mono) |
| `IOutputSerializer<TFormat>.cs` (+ `SerializationResult`, `SerializationStatus`) | E — cible de sortie | Contrat sérialiseur AST → format (OMath, LaTeX, MathJax, UnicodeMath…) |
| `ParseContext.cs` | transverse | Porteur de la locale + domaine + propriétés scoped |

Pas d'`IAstVisitor` à cette étape : il dépend de `AstNode` (type Core) donc
son point d'extension naturel est dans `core-csharp/src/MathCursor.Core/Lattice/Ast/`,
qu'on traitera étape 4 du plan refacto.

**Ajout pur** : aucun type Core n'implémente encore. Aucun test ne casse
(935/944 Core préservé, 419/419 Adapter préservé — les 6 fails Core
sont les préexistants Corpus×2 + CrossMerge espacement×4).

## Pourquoi

- **Cartographie 2026-05-13** a identifié 2 dettes Niveau 1 (`Vocabulary.cs`
  qui mélange FR + EN ; `LatexRenderer.cs` switch exhaustif sur 18 types
  AST) et 3 dettes Niveau 2 (pas d'`IConstructStrategy`, pas
  d'`IOutputSerializer`, pas d'`ILocaleNER`). Sans abstractions formalisées,
  toute extension future (matrices, dérivées, chimie, EN, DE, raccourcis
  user, MathJax) débordera entre axes.

- **Doctrine "interfaces only"** déjà appliquée côté Adapter (cf. ADR
  2026-05-06 zone-merger-pipeline : `IZoneMerger` + `MergerPipeline`).
  Cette étape pose la même doctrine côté Core, à l'échelle des axes
  user-facing.

- **Ajout pur = risque nul.** Le projet existe, les interfaces sont
  posées, mais aucun type existant n'a changé. On peut reverter en
  supprimant un dossier si l'orientation se révèle mauvaise.

- **Préparation harnais Phase 0+1.** Les règles MC du harnais (notamment
  MC0002 — VSTO leak dans le Core) s'appuieront sur la frontière
  Abstractions/Core/Adapter pour faire la séparation mécanique.

## Tradeoff & alternatives écartées

- **Mettre les interfaces directement dans `MathCursor.Core`** plutôt que
  dans un projet séparé. Rejeté : le risque est de coupler les contrats
  à des types Core et de mélanger contrat et implémentation. Un projet
  dédié force l'isolation (zéro `using MathCursor.Core` autorisé dans
  Abstractions).

- **Spécialiser `IOutputSerializer<TAstRoot, TFormat>`** dès maintenant.
  Rejeté : couplerait Abstractions au type `AstNode` du Core, brise
  l'isolation. Le type AST passe en `object` à l'étape 2 ; l'étape 3
  pourra re-typer via méthode d'extension ou overload si pertinent.

- **Pré-créer `IAstVisitor` ici** sous forme dépouillée (marker
  interface). Rejeté : ne sert à rien sans les méthodes typées par
  `AstNode`. Sera créé étape 4 dans `MathCursor.Core/Lattice/Ast/`
  comme classe abstraite `AstVisitor<TResult>`.

## Conséquences

- Nouvelle frontière de dépendance : `Adapter → Core → Abstractions` +
  `Adapter → HostContract`. Le projet Abstractions est terminal (aucune
  dépendance sortante).

- Pas de référence ajoutée depuis Core vers Abstractions à cette étape.
  L'étape 3 ajoutera la référence et fera implémenter les contrats par
  les types existants (`LatticeEngine` → `IDomainParser`,
  `LatexToUnicodeMath` → `IOutputSerializer<string>`, etc.).

- La couche Adapter pourra référencer Abstractions directement quand un
  type Adapter aura besoin d'implémenter un contrat (typiquement
  `MathNerDetector` → `ILocaleNER` à l'étape 5).

- Build : 0 erreur, 0 régression test. La sln intègre le nouveau
  projet avec GUID `B1000004-...`.

## Validé par l'utilisateur

> « 1. on commit d'abord / 2. on garde notre format / 3 fais ce qu'il faut
> ce que tu pense etre juste »
>
> « oui je valide » (validation de la cartographie + plan refacto
> étapes 2-5 + activation MC0002 à l'étape 5)

## Plan refacto — état d'avancement

- [x] **Étape 1** — Cartographie (`docs/dev/architecture/cartography.md`)
- [x] **Étape 2** — Interfaces Abstractions (cet ADR)
- [ ] **Étape 3** — Implémentation par types existants (LatticeEngine,
  LatexToUnicodeMath, Vocabulary…) — 1.5j prévu
- [ ] **Étape 4** — Visitor sur AST (refacto `LatexRenderer` switch) — 1j
- [ ] **Étape 5** — Sortir chaînes FR du Core (`locales/fr/keywords.yaml`
  + `FrenchLocaleLexer`) — 0.5j. **Activation MC0002 à ce stade.**
- [ ] **Étape 6** — `DomainRouter` (placeholder math-only) — 0.5j
- [ ] **Étape 7** — `ShortcutResolver` (overlay YAML user) — 0.5j
- [ ] **Étape 8** — Test d'intégration extensibilité (EmptySetStrategy +
  Unicode serializer factice + raccourci user) — 0.5j

En parallèle (continu) : harnais Phases 0-8 (analyzers MC0001-5,
diff summarizer, Tier 2/3, agents/skills, feedback loop).
