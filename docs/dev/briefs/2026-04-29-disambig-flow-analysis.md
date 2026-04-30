# Analyse — Flow de désambiguïsation : overkill ou bon choix ?

**Date :** 2026-04-29
**Auteur :** come + agent
**Statut :** **note d'analyse** (pas un brief d'implémentation, pas une ADR
actée). Sert à éclairer la question : faut-il simplifier la popup
bimodale en liste plate, ou le système actuel est-il proportionné ?

---

## 1. La question

> *"avec le merge successif des formules omaths, l'ancien systeme à base
> de liste simple et validation n'etait pas plus immediat ? avec un rank
> des choix qui soit intelligent, j'aimerai qu'on denombre les cas de
> desambiguisation et qu'on essaie de voir si effectivement c'etait pas
> overkill ?"*

Trois questions imbriquées :

1. Combien de cas de désambig y a-t-il vraiment, et à quelle fréquence ?
2. Le merge OMath rend-il le système d'apprentissage par règle moins
   pertinent ?
3. Une liste plate avec ranking intelligent serait-elle aussi bonne pour
   moins de complexité ?

## 2. Inventaire factuel

### 2.1. Patterns câblés (6, ~10 alternatives au total)

| Pattern (RuleId) | Trigger | Alternatives | Fréquence estimée (cours lycée) |
|------------------|---------|--------------|---------------------------------|
| `two-uppercase` (AB) | 2 majuscules | `\vec{AB}`, `(AB)`, `[AB]` | **élevée** en géométrie |
| `three-uppercase` (ABC) | 3 majuscules | `\widehat{ABC}`, `\triangle ABC` | modérée |
| `letter-sup-number` (x²) | Sup implicite par règle Number-tight | `x²` (default), `x_2` | rare |
| `v-as-forall` (V) | V isolé suivi d'espace/EOF | `V` identity, `\forall x \in R`, `\sqrt{x}` | rare (sauf chap. logique) |
| `e-as-exists` (E) | E isolé suivi d'espace/EOF | `E` identity, `\exists x` | très rare |
| `canonical-set` (R) | R/N/Z/Q/C isolés | `R` identity, `\mathbb{R}` | modérée |

### 2.2. Mécaniques du système actuel

- **`AlternativeGenerator.cs`** : 602 lignes. Scan AST + scan source raw +
  source-mutation (V→forall, R→bbR re-déclenchent le pipeline complet).
- **`AmbiguityDetector.cs`** : sélection rightmost + cascade des matches
  par RuleId.
- **`SuggestionPopupWindow.cs`** : 644 lignes. **Deux modes** :
  - Mode A (no-ambig) : ligne unique = formule finale.
  - Mode B (with-ambig) : section haute = alternatives en colonnes
    horizontales, section basse = formule finale, séparateur visuel.
  - Navigation 2D : ↑↓ entre zones, ←→ dans les alts.
- **Apprentissage** : deux caches mémorisés tant que la popup vit
  (reset à commit/Esc/sortie de zone) :
  - `_resolvedSubstitutions` (par texte exact) : si NER re-détecte le
    même tokens, applique le choix mémorisé.
  - `_rulePreferences` (par RuleId) : **cascade** — si l'utilisateur
    choisit `vec` pour le 1er `AB`, tous les `CD`, `EF` suivants dans
    la même zone sont auto-résolus en `vec` sans afficher la popup.

### 2.3. Système précédent (Office.js, archivé)

- **Task pane** (panneau Word latéral, toujours visible).
- **Liste verticale plate** de suggestions (display + label).
- Tab pour valider l'index sélectionné.
- **Pas de cascade**, pas d'apprentissage par règle, pas de
  source-mutation.
- Validation de chaque suggestion en string sub-LaTeX.

### 2.4. Merge OMath (rappel contexte)

- Côté adapter : quand on insère un OMath, on scanne gauche/droite pour
  les OMaths adjacents séparés par 0 ou 1 espace → fusion en un seul OMath.
- **Conséquence** : la fragmentation visuelle de "tape AB → convertit →
  tape CD → convertit → tape EF → convertit" donne un seul OMath
  contigu, pas trois OMaths côte à côte.

## 3. Coût du système actuel

### 3.1. Complexité code

- **2 fichiers core** (`AlternativeGenerator` 602 + `AmbiguityDetector`
  ~250) ≈ 850 lignes.
- **1 fichier UI** (`SuggestionPopupWindow`) ≈ 644 lignes.
- Couplage : la popup parle de RuleId au core via events
  (`SourceMutationRequested`).
- 4 catégories de scan dans AlternativeGenerator (AST, uppercase string,
  V/E source, canonical sets) — chacune avec ses edge cases.

### 3.2. Charge cognitive utilisateur

La popup bimodale demande à l'élève de comprendre **3 concepts** d'un
coup :

1. La section haute = "ce qui est ambigu, choisis".
2. La section basse = "ce qui sera inséré, valide".
3. Les flèches = comportement 2D (↑↓ entre zones, ←→ dans les alts).

Pour un élève PAP (cf. cible produit), c'est non-trivial. Le proto
Office.js avec sa liste plate + Tab était plus simple à expliquer en 1
phrase.

### 3.3. Surface de tests

Les tests existants couvrent les patterns un par un. Les **interactions**
(cascade, source-mutation re-render, navigation 2D) sont mal couvertes
par les tests automatiques (beaucoup de tests d'intégration manuels).

## 4. Bénéfice réel du système actuel

Trois choses qu'une liste plate ne ferait pas :

### 4.1. Cascade par RuleId (cas `two-uppercase`)

**Vrai gain.** Élève en exercice de géométrie : "soit AB, BC, CD trois
côtés de…". Avec le système actuel, il choisit `vec` une fois pour `AB`
→ `BC` et `CD` sont auto-résolus. **Économise N-1 clics** où N = nombre
d'occurrences.

Dans une session de 30 minutes avec 5-10 paires de majuscules, ça fait
4-9 clics évités. Multiplié sur la durée d'usage : **bénéfice réel**.

### 4.2. Source-mutation (cas `V`/`E`/`R`/`N`)

**Sans alternative simple.** Quand l'utilisateur choisit "V → ∀", le
système ne fait pas un sub-LaTeX local — il **réécrit la source** (`V x R`
→ `forall x R`) et **relance le pipeline** complet. C'est important parce
que le contexte parser change : `forall` est un keyword scope qui
consomme `x` et `R` comme args, ce qu'une sub-LaTeX au niveau rendu ne
saurait pas faire.

Une liste plate qui ne fait que des sub-LaTeX **casserait** ce cas. Il
faudrait soit garder la source-mutation en parallèle, soit reculer sur
les patterns scope (perte de fonctionnalité).

### 4.3. Apprentissage par texte exact

Quand le NER re-détecte la même zone (le user a tapé une touche puis
revient), le choix précédent est ré-appliqué. **Gain modeste** : utile à
la marge mais pas l'argument fort.

## 5. Alternative envisagée : liste plate + ranking intelligent

À quoi ça ressemblerait :

```
┌─ Popup au caret ──────────────────┐
│ ▶ \vec{AB}    ← top du ranking    │
│   (AB)                            │
│   [AB]                            │
└───────────────────────────────────┘
```

Une seule colonne, navigation ↑↓, Enter pour valider. Plus de section
"finale" — la formule s'insère directement quand on choisit.

### 5.1. Ranking intelligent — sources possibles

- **Compteur d'usage local** (par RuleId, persisté dans
  `%APPDATA%\MathCursor\preferences.json`). Le 1er choix de l'utilisateur
  remonte dans le ranking pour les sessions suivantes.
- **Heuristique contextuelle** : V suivi d'espace + lettre + lettre
  majuscule → `\forall` plus probable que `√`. ABC en début de phrase →
  `\triangle` plus probable. Etc.
- **Default opinionné** : `vec` pour 2 majuscules (statistique lycée FR).

### 5.2. Ce qu'on perd

- **Cascade RuleId** : choisir `vec` pour 1 AB ne pré-résout plus les
  `CD`/`EF`. L'élève voit la popup pour chaque occurrence.
  - **Mitigation** : si le ranking persistant met `vec` en haut après le
    1er choix, l'élève fait `Enter Enter Enter` rapidement. C'est 3
    touches au lieu de 0, mais 3 touches uniformes vs un comportement
    "magique" qui peut surprendre.
- **Mode no-ambig direct** : aujourd'hui si pas d'ambig, la popup montre
  juste la formule finale. Avec une liste plate, on aurait
  toujours **au moins** une ligne (la formule finale = top de la liste,
  Enter direct). Acceptable.

### 5.3. Ce qu'on garde

- **Source-mutation** pour V/E/R/N : la popup déclenche toujours
  `SourceMutationRequested(ruleId, mutation)` quand on choisit l'option.
  Le mécanisme moteur reste intact.
- **Validation molle** (commit Word seulement à la dernière touche).

### 5.4. Coût

- Refonte popup : ~2 jours (suppression du mode bimodal, recâblage
  navigation 1D, ranking persistant).
- Suppression cascade : ~0.5 jour (retirer `_rulePreferences` et la
  cascade dans `ResolveCurrentAltIfFocused`).
- Tests : ~1 jour (les tests d'intégration popup sont à refaire).

**Total estimé** : ~3-4 jours.

## 6. Évaluation pattern par pattern

| Pattern | Cascade utile ? | Source-mutation utile ? | Verdict simplification |
|---------|-----------------|-------------------------|------------------------|
| two-uppercase (AB) | **OUI** (géométrie répétée) | non | mieux **garder cascade** |
| three-uppercase (ABC) | partielle (peu fréquent) | non | acceptable de simplifier |
| letter-sup-number (x²) | non (rare) | non | acceptable de simplifier |
| v-as-forall (V) | non (rare) | **OUI** (scope ∀) | garder source-mutation |
| e-as-exists (E) | non (très rare) | **OUI** (scope ∃) | garder source-mutation |
| canonical-set (R) | non (modéré) | **OUI** (rendu \mathbb) | garder source-mutation |

**Lecture** : la cascade par RuleId apporte un vrai gain sur **un seul
pattern** (`two-uppercase`). La source-mutation est nécessaire pour
**3 patterns** (V/E/R) et est compatible avec une liste plate.

## 7. Influence du merge OMath (correctif après échange)

**Lecture initiale (erronée)** : je pensais que le merge OMath était
orthogonal au flow de désambig parce qu'il opère côté Word post-commit.

**Lecture corrigée** : le merge OMath ouvre un nouveau pattern
d'utilisation — **commit tôt et souvent**, laisser le merge fusionner
derrière. Conséquence directe sur la désambig :

- Si chaque commit traite **un fragment court** (1-2 caractères de math,
  type `AB`), la popup correspondante a **0 ou 1 ambig** au plus.
- Plus de "grappe d'ambig dans une zone NER large". La cascade par
  RuleId — qui sert exactement à éviter de re-popuper sur le 2e/3e
  occurrence du même pattern dans la même zone — **devient inutile**.

C'est une **vraie inversion de l'analyse** : le merge OMath n'est pas
orthogonal, il **rend la cascade obsolète** dans le scénario
"commit-fréquent".

### 7.1. Conditions pour que ça marche

Le commit doit effectivement être déclenché tôt. Trois mécanismes :

1. **UX inciter au Ctrl+Espace par mot court** (pas par phrase). Pas
   d'effort code, mais demande discipline utilisateur.
2. **Auto-commit silencieux** quand zone non-ambiguë + séparateur
   tapé. Cohérent avec l'ergo "popup silencieuse jusqu'à interaction"
   (cf. ADR 2026-04-24-UX-popup-silent-until-interaction).
3. **Découper les zones NER multi-mots** en sous-zones côté detector.
   Plus invasif, à n'envisager que si (1) et (2) ne suffisent pas.

**Recommandation** : (2) + (1) en complément. Auto-commit gère 80% des
cas, l'utilisateur reprend la main avec Ctrl+Espace pour le reste.

### 7.2. Cas qui résistent

- **Multi-ambig intrinsèque dans une zone** : ex `forall x R AB > 0`
  contient `forall` (V/E source-mutation) + `AB` (two-uppercase).
  RuleIds différents → la cascade ne nous aurait pas aidés ici de
  toute façon. **Pas une régression** par rapport à l'existant.
- **Répétition même RuleId dans une saisie continue rapide** : ex
  `(A,B) appartient (C,D) appartient (E,F)` tapé d'un trait. Si le NER
  détecte une grosse zone et le commit n'est pas fragmenté, on perd la
  cascade. **À mesurer en usage réel** ; mitigation = ranking
  persistant qui rend chaque popup à 1 touche (`Enter` direct).

## 8. Verdict honnête (mis à jour avec angle commit-tôt)

### 8.1. Système actuel : surinvesti dans le scénario commit-tôt

Si on adopte le pattern "commit tôt + merge OMath" (cf. §7), **toute la
sophistication de la cascade par RuleId perd son utilité**. Chaque popup
ne traite plus qu'un seul cas d'ambig à la fois ; la mémoire par règle
ne se déclenche jamais (par construction).

### 8.2. Ce qui reste justifié

- **Source-mutation** : indispensable pour `forall`/`exists`/canonical
  set (irremplaçable par sub-string LaTeX). Garde quel que soit le choix.
- **Ranking par alt** : utile cross-session. Si l'utilisateur a choisi
  `vec` pour AB la dernière fois, mettre `vec` en top de la liste cette
  fois → 1 Enter au lieu de 2-3 Down + Enter.

### 8.3. Ce qui devient redondant ou inutile

- **Popup bimodale** (alts haut + finale bas) : remplaçable par liste
  plate verticale.
- **Navigation 2D** (←→ pour alts, ↑↓ pour zones) : remplaçable par
  ↑↓ classique.
- **Cascade par RuleId** : ne se déclenche plus en commit-tôt.
- **`_rulePreferences` cache session** : remplaçable par un ranking
  persistant simple (compteur d'usage par alt).

### 8.4. Où c'était overkill, où c'était bien vu

- **Overkill** : popup bimodale, navigation 2D, cascade par RuleId,
  cache session par règle. Tout ce qui est **dimensionné pour des
  zones longues à grappe d'ambig** — qui n'existent plus si on commit
  tôt.
- **Bien vu** : la **source-mutation** (V→forall, R→bbR) qui réécrit
  la source et relance le pipeline. Architecture solide,
  irremplaçable.
- **Faux problème initial** : ce n'est pas le moteur de désambig qui
  est trop sophistiqué, c'est le **modèle d'usage attendu** (zones
  longues, donc grappes) qui est dépassé par le merge OMath.

## 9. Recommandation (deux changements liés)

### 9.1. Côté flow : commit-tôt + merge OMath

Adopter explicitement le pattern "commit tôt et souvent" comme
règle-produit :

- **Auto-commit silencieux** quand zone non-ambiguë + séparateur tapé
  (espace + lettre normale). Pas de popup, le merge OMath fusionne
  visuellement avec ce qui précède si pertinent.
- **Ctrl+Espace** reste pour les cas où l'auto-commit n'est pas
  déclenché (zone ambiguë, ou flux long sans séparateur clair).

**Coût** : ~1 jour (logique d'auto-commit côté `SuggestionService`,
condition de déclenchement, tests d'intégration).

### 9.2. Côté popup : refonte en liste plate

- Fusionner `_altsRow` et `_finalContainer` de
  `SuggestionPopupWindow.cs` en **une seule liste verticale**.
- 1ère ligne = formule par défaut (= top du ranking persistant).
- Lignes suivantes = alternatives par ordre de préférence
  (cross-session, persisté dans `%APPDATA%\MathCursor\preferences.json`).
- Navigation ↑↓, Enter pour valider la ligne courante.
- **Supprimer** la cascade par RuleId et le cache session.
- **Garder** le mécanisme `SourceMutationRequested` côté événement.

**Coût** : ~1-1.5 jour (refacto popup + suppression code cascade +
ajout ranking persistant).

### 9.3. Total

~2-2.5 jours pour les deux changements ensemble. **Ordre recommandé :**

1. D'abord la liste plate (UI seule, retrait cascade) : pas de
   régression visible si zones courtes naturelles.
2. Ensuite l'auto-commit : on teste que les zones sont bien fragmentées
   et que le merge OMath fait son travail.

### 9.4. Risques résiduels

- **Cas `(A,B) appartient à (C,D)…` tapé d'un trait** sans Ctrl+Espace
  intermédiaire : si NER détecte une zone unique, on perd la cascade
  qui aurait évité 2-3 popups. **Mitigation** : ranking persistant
  rend chaque popup à 1 Enter. Dégradation de 0 clic à 2-3 Enter.
  Acceptable ?
- **Régression visible pour les utilisateurs habitués** au mode
  bimodal : toi en premier. À tester en usage personnel avant de
  pousser aux beta-testeurs.

## 10. Questions ouvertes pour la décision

1. **As-tu observé l'élève PAP utiliser la popup actuelle ?** Si oui,
   est-ce qu'il comprenait la séparation haut/bas ? Si non → biais
   d'analyse, on raisonne dans le vide.
2. **Le ranking persistant cross-session t'intéresse-t-il ?** (= la
   préférence `vec` pour AB est mémorisée même après fermeture Word).
   C'est un ajout par-dessus la cascade actuelle, peu coûteux.
3. **Le mode no-ambig actuel (formule seule) suffit-il ?** Ou faut-il
   afficher "rien à choisir, c'est juste ça" pour rassurer ? Le proto
   Office.js avait toujours une liste, même avec 1 seul élément.
4. **Quels patterns ajouter à 6 mois ?** Si on en prévoit 10+, la
   cascade par RuleId scale plutôt bien. Si on plafonne à 6-7, le coût
   maintenance ne se rembourse pas.

## 11. Note finale (mise à jour)

Première lecture (erronée) : "le moteur est correct, la popup
sur-investit". Vrai mais incomplet.

Lecture corrigée après échange : **le moteur entier (cascade +
preferences par règle) a été dimensionné pour un modèle d'usage "zones
longues, grappes d'ambig"** qui devient obsolète si on adopte
"commit-tôt + merge OMath". La cascade n'est pas mauvaise, elle est
**dimensionnée pour le mauvais scénario d'usage**.

Conséquence : la simplification touche **deux endroits**, pas un :

1. **Côté popup** : liste plate (retire bimodal, retire cascade UI).
2. **Côté flow** : auto-commit silencieux (réduit la taille des zones
   traitées par chaque popup).

**Source-mutation reste intact** — c'est le seul élément du moteur de
désambig qui est *intrinsèquement* nécessaire (pas un compromis
d'usage).

Le proto Office.js n'avait pas tort — il n'avait juste pas le merge
OMath ni l'auto-commit pour rendre la liste plate viable. Avec ces deux
mécanismes côté adapter, le proto serait revenu à la mode.
