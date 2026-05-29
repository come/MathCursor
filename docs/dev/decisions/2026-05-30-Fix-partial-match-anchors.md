# Fix — Partial-match des anchors (typing-flow à carrés) enfin réalisé

**Date :** 2026-05-30
**Kind :** Fix
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** [`2026-05-28-Refactor-rewriting-engine-v2-clean.md`](2026-05-28-Refactor-rewriting-engine-v2-clean.md) (Principe 4 que ce fix réalise enfin)

## Citation acté

> « oui go ! » puis « faire le correctif core » — utilisateur, 2026-05-30

(Après diagnostic montrant que `1/som` produisait `\frac{1}{sum}` au lieu du squelette à carrés que l'ADR moteur V2 décrit.)

## Contexte

Le **Principe 4** de l'ADR moteur V2 (« partial match obligatoire en typing
flow ») n'était **réalisé ni dans les données ni dans le moteur** :

1. **Données** : aucune règle YAML ne portait le flag `allow_partial: true`
   pourtant rendu obligatoire pour les anchors par l'ADR §7/11. Le partial-match
   était donc dormant. Symptôme observé : `1/som` → `\frac{1}{sum}` (le mot
   « sum » littéral au dénominateur), `incomplet=false`, aucun carré.

2. **Moteur** : en activant le flag, le chemin partial des anchors structurels
   (`TryMatchAnchor`, à capture de chunks) partait en **récursion infinie →
   stack overflow**. Deux causes :
   - un Item partiel produit gardait `SourceText="sum"` et **re-matchait** la
     règle somme sur lui-même (le literal matchait un Item déjà résolu) ;
   - une règle anchor tentée à une position où son mot-clé est **absent**
     continuait quand même sous `allow_partial` et capturait un chunk
     (= tout le fragment), que `resolveChunk` re-résolvait à l'identique.

## Décision

### Correctif moteur (2 gardes dans `RewriteMatcher`)

1. **Un `Literal` ne matche qu'un `TokenItem` brut**, jamais un Item déjà
   résolu (`items[i] is TokenItem && items[i].SourceText == lit.Text`). Un
   mot-clé/opérateur/délimiteur est toujours un token de saisie, jamais une
   expression produite. Tue la ré-absorption d'un anchor partiel par lui-même.

2. **Le partial ne s'active qu'APRÈS que l'anchor a matché** : tous les
   fallbacks `allow_partial` de `TryMatchAnchor` deviennent
   `rule.AllowPartial && anyLiteralMatched`. Une règle tentée là où son mot-clé
   est absent renvoie `null` immédiatement, **avant** toute capture de chunk /
   `resolveChunk` → plus de récursion sur le fragment entier.

`ApplyTemplate` émettait déjà `\square` pour un slot manquant : la machinerie
de rendu était complète, il ne manquait que ces deux gardes.

### Activation données

`allow_partial: true` ajouté aux règles anchor (ADR §7/11) : `sum`, `prod`,
`lim`, `int`/`int`-indéf/`derive`/`iint`, `vec`, `sqrt`/`sqrt`-n-ième,
`forall`/`exists` (formes longues). **Pas** sur `frac` (vestigial per ADR) ni
sur les formes courtes forall/exists (l'entrée complète sans body y reste nette).

## Tradeoff & alternatives écartées

- **Marquer les Items partiels comme « non re-matchables » via un flag** :
  rejeté — le vrai invariant est plus simple et plus fort : un literal matche un
  token, pas une expression. La garde `TokenItem` l'exprime directement.
- **Gérer la récursion par un compteur de profondeur** : rejeté — masque le
  symptôme. La garde `anyLiteralMatched` supprime la cause (capture sans anchor).

## Conséquences

- **Code touché** : `Rewriting/RewriteMatcher.cs` (2 gardes), 5 fichiers
  `data/concepts/*.yml` (flag sur les anchors).
- **Comportement** : `1/som` lève `\frac{1}{\sum_{\square=\square}^{\square}\square}`
  (`incomplet=true`), et chaque frappe remplit un carré jusqu'au complet —
  exactement le Principe 4. Lock par `TypingFlowE2eTests` (remplissage
  progressif + squelettes bare-anchor).
- **Tests** : Engine 166/166, Adapter 33+ (typing-flow inclus). Golden
  inchangé (full match toujours préféré au partial via le scoring).
- **API publique** : inchangée.

## Limite connue (suivi séparé)

En exécutant les tests en parallèle (xUnit), un flake intermittent a révélé un
**cache `static Dictionary` non thread-safe** dans `Tokenizer`
(`_multiCharCache`). Sans impact en production (add-in VSTO mono-thread STA),
mais à rendre thread-safe pour fiabiliser la suite. Non corrigé ici (hors scope
du typing-flow). À traiter dans un fix dédié.

## Validation post-fix

`som`/`1/som` → squelette à carrés `incomplet=true` ; remplissage progressif
vérifié pas-à-pas ; pas de crash. Suites vertes.
