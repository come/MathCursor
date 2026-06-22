# Fix — Alignement chaîne eqArr : 3 `&` (parité du walker natif) au lieu de 2

**Date :** 2026-06-22
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** — (amende l'alignement de [2026-06-10-Feat-multiline-chain-eqarr-architecture](2026-06-10-Feat-multiline-chain-eqarr-architecture.md), qui reste acté pour le reste)

## Citation acté

> « on avait déjà réussi à aligner via le walker, donc je veux bien qu'on passe
> sur un fonctionnement à 3& plutôt que 2 pour tester ? » — utilisateur, 2026-06-22

> « ok là c'est nickel ! » — utilisateur, 2026-06-22 (validation runtime Word)

## Contexte

Bug remonté via l'outil « Signaler un souci » (capture jointe) : dans une chaîne
d'équivalences multi-ligne, les signes de relation (`>` dans le cas remonté) **ne
s'alignaient pas verticalement** :

```
F(x-1) - f(x)        > 0
⇔ f(x)               > f(x-1)      ← le « > » de la 2ᵉ ligne décalé à droite
```

## Investigation (sans Word, via repro pur + git)

1. `ChainComposer.ComposeChain` produit une structure **correcte et uniforme** :
   chaque ligne d'une chaîne à connecteur = 3 colonnes `[conn & lhs & =rhs]`,
   **2 marques `&`** (forme « V4/V5 »). Le vrai LaTeX moteur se scinde bien au
   signe (`F(x-1)-f(x)>0` → `lhs=F(x-1)-f(x)`, `rhs=>0`). Donc **pas un bug de
   composition**.
2. Git : le cas `eqArr` du walker (`OmmlToOMathBuilder`) et l'écriture des runs
   sont **identiques** depuis le commit « alignement eqArr prouvé » (`0a2e615`).
   Donc l'alignement « prouvé » l'avait été via le **banc POC `ChainEqArrPoc`
   (InsertXML)** — qui n'est PAS le chemin de production (le pipeline
   hash-source-map a supprimé `InsertXML`, qui ferme le record undo).
3. **Cause racine** : l'`eqArr` natif de Word construit via le modèle objet
   (`Functions.Add(EqArray)` + texte des `&`) aligne les colonnes en
   **alternance droite / gauche / droite / gauche…**. La position du signe
   dépend donc de la **parité** du nombre de `&` :
   - **1 `&`** (chaînes de `=` sans connecteur) → signe en colonne **gauche** →
     aligné ✓ (cas qui « marchait déjà »).
   - **2 `&`** (chaînes à `⇔`/`⇒`, ancien code) → signe en colonne **droite** →
     les signes ne s'alignent plus ✗.
   L'import OMML (InsertXML du POC) honorait toutes les marques différemment,
   d'où l'illusion que le cas connecteur était validé en production.

## Décision

Pour les chaînes **à connecteur**, passer de **2 à 3 marques `&`** par ligne en
ajoutant une **colonne « pad » vide en tête** :

```
[pad & conn & lhs & =rhs]
```

La colonne pad vide **décale la parité** : le signe retombe dans une colonne
**gauche-alignée** → les signes s'alignent. Les colonnes `conn`/`lhs` restent
vides sur les lignes sans connecteur / les marqueurs-relation.

Rendu obtenu (validé en Word) :

```
        F(x-1) - f(x)  > 0
⇔               f(x)   > f(x-1)
⇔               f(x)   > 0
```

(signes alignés, `⇔` accolé à gauche, membre gauche aligné à droite contre le signe.)

Les chaînes **sans connecteur** (suite de `=`, **1 `&`**) sont **inchangées**
(elles s'alignaient déjà — parité gauche).

## Tradeoff & alternatives écartées

- **Passer tout en 1 `&` (connecteur fusionné dans le lhs)** : aligne aussi les
  signes (1 `&` = colonne gauche), mais **perd la colonne connecteur** (les `⇔`
  ne forment plus leur propre colonne). Écarté au profit du 3-`&` qui garde
  connecteur ET signe alignés — d'autant que **l'utilisateur a explicitement
  demandé à tester le 3-`&`** (« on avait déjà réussi à aligner via le walker »).
- **Revenir à `InsertXML` pour les blocs** : alignerait (chemin POC prouvé) mais
  `InsertXML` **ferme le record undo** (raison de sa suppression, cf.
  word-api-helpers §6). Non.
- **Forcer `Justification`/`Jc`** : le setter jette sur les eqArr frais (acquis
  M2/M4) ; n'agit pas sur la parité de toute façon.

## Conséquences

- **Code** : `Host/Blocks/ChainComposer.cs` — `Row3` ajoute une `Amp` (pad) en
  tête ; commentaires `ComposeChain` mis à jour (4 colonnes / 3 `&` pour le cas
  connecteur).
- **Tests** : `ChainComposerTests.Chain_with_connector_uses_four_columns_three_amp_marks`
  (ex-`..._two_amp_marks`) — attend 3 `&` et la forme `[pad & conn & lhs & =rhs]`
  (`&&f(x)&=2x+2-2`, `&&&=2x`, `&⇔&x&=1`). Adapter 20/20 sur le fichier, VSTO compile.
- **Équations existantes** : non re-rendues (OMML figé) — seules les **nouvelles**
  chaînes bénéficient du fix (comportement attendu, l'add-in ne réécrit jamais
  une équation déjà posée).
- **Systèmes** (`ComposeSystem`, accolade + `=` alignés) : 1 `&`, **inchangés**.

## Validation post-fix

- Unit : `ChainComposerTests` vert (structure 3-`&`).
- Runtime Word (utilisateur, doc neuf) : chaîne `F(x-1)-f(x)>0 / ⇔ f(x)>f(x-1) /
  ⇔ f(x)>0` → signes `>` alignés. ✓ « ok là c'est nickel ! »
