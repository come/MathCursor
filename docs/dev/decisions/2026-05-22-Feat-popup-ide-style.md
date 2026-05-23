# Feat — Popup IDE-style : composition top-level + 2 candidats + voir plus (P14+P15)

**Date :** 2026-05-22
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-05-22-Feat-whitespace-sep-and-pratt-tiers.md](2026-05-22-Feat-whitespace-sep-and-pratt-tiers.md) (P13), [2026-05-21-Feat-pattern-ranker.md](2026-05-21-Feat-pattern-ranker.md) (P10)

## Citation acté

> « la composition ne marche pas » + « ok ca marche bien on continue ? »
> + « on abandonne la desambiguisation par petit bouton, a la place on
> doit avoir les choix les plus probables (2 presentés max + voir plus)
> dans la popup » — utilisateur, 2026-05-22

## Contexte

Après P13, `lim x 0 f + lim x 1 g` matchait juste `\lim_{x \to 0} f` (=
1ère ancre seule). Le `+ lim x 1 g` était ignoré car `MathEngine.Resolve`
cherchait une **seule** règle depuis startIndex=0.

Brief v5 §3 attendait : 1ère lim stoppe son body sur la 2e ancre, puis
les 2 lim sont composées via `+` au niveau supérieur → `\lim_1 f + \lim_2 g`.

En parallèle, user souhaite remplacer la "désambig par petit bouton" par
une popup IDE-style : 2 candidats max affichés + bouton "+ N autres".

## Décision

### P14 — Parsing top-level avec composition

`MathEngine.Resolve` devient un **parseur séquentiel** :

```
parseTopLevel(tokens):
  loop:
    skipSep
    if anchor at current pos → match best rule → operand
    else                     → parse atom/group via StackParser → operand
    skipSep
    if infixe top-level → push to ops list ; continue
    else                → break
  
  emit: concat operands avec ops entre, sans espace autour de +/-
```

Permet la composition naturelle :
- `lim x 0 f + lim x 1 g` → `\lim_{x \to 0} f+\lim_{x \to 1} g`
- `lim x 0 f(x)` (seul) → inchangé
- `1 + 2` (no anchor) → fallback flat = `1+2`

### P15.1 — Tracking collisions sur la 1ère ancre

Le tracking de candidats alternatifs est conservé pour la 1ère ancre
rencontrée (= matériel pour la popup "2 + voir plus"). Si plusieurs
règles matchent à la même position, toutes sont exposées dans
`EngineResult.Collisions` triées par span coverage + nb slots remplis.

### P15.2 — Popup WPF : 2 + "voir plus"

`SuggestionPopupWindow.BuildAltCells` :
- Si `_alternatives.Count <= 2` → affiche tout (= legacy).
- Si > 2 → affiche les 2 premières + un bouton bordered `+ N autres`.
- Click sur le bouton → `_altsExpanded = true` + rebuild ; affiche toutes.
- Reset `_altsExpanded = false` à chaque `Show()` (= nouvelle zone).

Const interne `MaxAltsCollapsed = 2`. Style du bouton : padding 6×2,
texte 11pt couleur bleu (= cohérent ergo IDE), curseur main au hover.

## Tradeoff & alternatives écartées

- **Vraie Pratt top-level avec arbre** : plus puissant mais demande
  refactor parser/emitter. Le concat séquentiel suffit pour les cas
  user actuels. P16+ si besoin.
- **3 candidats par défaut** : 2 est plus lisible PAP, "voir plus"
  trivial pour le 3e.
- **Toggle inline (= chevron à côté du dernier item)** : moins visible
  qu'un bouton dédié. Rejeté ergo.
- **Modal "+N autres"** : flow trop coûteux pour un choix trivial.

## Conséquences

- **Code touché** :
  - `core-csharp/src/MathCursor.Engine/MathEngine.cs` (= refonte Resolve + TryAllAnchorMatches + BuildCandidates)
  - `adapter-vsto/src/MathCursor/UI/SuggestionPopupWindow.cs` (= `_altsExpanded` + bouton "+ N autres")
- **Tests** : 83/83 engine verts. Adapter 393/393 verts. 2 CollisionTests réactivés (= skip P14 levé).
- **API publique** : inchangée. `EngineResult.Collisions` continue de porter les alternatives.
- **Règles MC impactées** : aucune.

## Validation post-fix

- Test `Body_greedy_until_next_anchor_composes_via_infix` → `\lim_{x \to 0} f+\lim_{x \to 1} g` ✓.
- Tests CollisionTests réactivés et verts.
- Build VSTO vert.
- Manuel Word : taper `lim x 0 f + lim x 1 g` doit afficher la composition complète dans la popup.

## Plan en cours — état d'avancement

P14-P15 — Composition + popup IDE-style :
- [x] P14 Parsing top-level avec composition
- [x] P15.1 Tracking collisions réactivé
- [x] P15.2 Popup WPF 2 + voir plus
- [x] P15.3 ADR (= ce document)
