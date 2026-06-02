# Feat — Principe 5 : multi-chains (collisions par fork d'ordres de composition)

**Date :** 2026-05-30
**Kind :** Feat
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** [`2026-05-28-Refactor-rewriting-engine-v2-clean.md`](2026-05-28-Refactor-rewriting-engine-v2-clean.md) (Principe 5 que ce fix réalise), [`2026-05-29-Feat-collision-uppercase-seq.md`](2026-05-29-Feat-collision-uppercase-seq.md) (collisions même-span que ce mécanisme subsume)

## Citation acté

> « oui mais du coup ça me met le doute sur tout le reste ! commence par implémenter 5 » — utilisateur, 2026-05-30

(Constat : `1/x+1` ne collisionne pas en `\frac{1}{x}+1` vs `\frac{1}{x+1}`. Cause : le Principe 5 — beam search multi-chaînes — n'est pas implémenté ; le moteur fait une résolution mono-chaîne gloutonne.)

## Contexte

Le **Principe 5** de l'ADR moteur V2 dit : à chaque ambiguïté, le moteur
**fork** en plusieurs chaînes de résolution, garde top-K, retourne `best` +
`alternatives`. Conséquence attendue : les lectures concurrentes
(slurp/strict, ordres de composition) remontent toutes en collision.

Réalité : `RunPrimitivePhase` est une boucle point-fixe **mono-chaîne** — à
chaque tour, un seul meilleur match, appliqué en mutant la liste sur place.
Les seules `alternatives` produites viennent du **tie-break même-span**
(d'où `x2`/`AB` qui marchent, mais pas `1/x+1` qui exige deux **ordres**
différents : appliquer `+` d'abord → `1/(x+1)` ; appliquer `/` d'abord →
`1/x + 1`). Spans différents → jamais vus comme concurrents.

## Décision

Implémenter le fork multi-chaînes **au niveau de la phase primitive
top-level** :

1. **Exploration par fork** : à partir des Items (post-résolution
   structurelle), explorer les **ordres d'application** des règles primitives.
   Chaque ambiguïté (plusieurs matchs disponibles) fork une chaîne par
   candidat. Largeur de faisceau bornée (K=4) + plafond de sécurité. On
   collecte les **lectures terminales distinctes** (dédupliquées par LaTeX).

2. **Sélection du `best` — déterministe, inchangée** : le top reste calculé
   par la logique leftmost-longest gloutonne actuelle (= `RunPrimitivePhase`).
   **Garantie : les 166 tops golden sont préservés par construction.** Les
   autres lectures distinctes deviennent les `alternatives`.

3. **Unification des collisions** : le mécanisme subsume le tie-break
   même-span. `x2` (sup/sub), `AB` (produit/vecteur/paren) ET `1/x+1`
   (ordres de composition) émergent désormais du **même** fork. La
   registration d'alternatives par tie-break dans `RunPrimitivePhase` est
   retirée (sinon doublons).

### Pourquoi `best` déterministe et non « top-K par score »

L'ADR décrit un `best = chaîne au meilleur score`. Mais pour `1/x+1` les deux
lectures résolvent en 1 Item → critères de scoring (items résiduels, priorité
moyenne) à égalité → départage nécessaire = la **précédence** (PEMDAS). Or la
résolution gloutonne actuelle EST déjà ce départage (leftmost-longest applique
`/` avant le `+` plus à droite → PEMDAS). On la garde donc comme sélecteur de
`best` : zéro régression sur les 166 tops, et le départage de précédence est
gratuit. La sélection pure-par-score est un raffinement ultérieur si besoin.

### Périmètre

- Fork **top-level** uniquement. Les chunks (args d'anchor) restent
  mono-chaîne (bornage de complexité ; `1/x+1` nu est top-level).
- Phase primitive. La phase structurelle (anchors) reste mono-chaîne.

## Tradeoff & alternatives écartées

- **Flag `greedy_tail` + scanner de portée** (proposé puis retiré) : rejeté —
  contournement ad-hoc, réintroduit l'inélégance legacy. Le fork est le
  mécanisme général conçu.
- **Règle gourmande concurrente même-span** : rejeté — span différent, le
  tie-break ne les groupe pas ; et le tri span-desc volerait le top.
- **Beam pur (best = meilleur score)** : reporté — risque de régression sur
  les 166 tops (départage de précédence non trivial à scorer). Best
  déterministe d'abord.

## Conséquences

- **Code touché** : `Rewriting/RewriteEngine.cs` (explorateur de fork +
  rewire de la phase primitive top-level ; retrait de la registration tie-break).
- **Comportement** : `1/x+1` → top `\frac{1}{x}+1` + collision
  `\frac{1}{x+1}`. `x2`/`AB` inchangés (mêmes collisions, via fork désormais).
- **Tests** : 166 golden inchangés (tops préservés). Collisions `x2`/`AB`
  préservées. Nouveau : `1/x+1` collision. Bornage perf vérifié.
- **API publique** : inchangée (`RewriteResult.Alternatives`).

## Validation post-fix

`1/x+1` → 1 collision (`\frac{1}{x+1}`) ; `x2` → 1 (`x_{2}`) ; `AB` → 2 ;
`1/x` → 0. Suites engine + adapter vertes, pas d'explosion combinatoire.
