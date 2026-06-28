# Feat — Systèmes d'équations « { » : modèle matrice (`;` / Maj+Entrée), hors moteur

**Date :** 2026-06-26
**Kind :** Feat
**Température :** molle (modèle de saisie à valider à l'usage ; backport Word conditionnel)
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Feat-multiline-chain-eqarr-architecture](2026-06-10-Feat-multiline-chain-eqarr-architecture.md) (systèmes Word incrémentaux + `ComposeSystem`), [2026-06-25-Feat-chaining-port-libreoffice-vscode](2026-06-25-Feat-chaining-port-libreoffice-vscode.md) (réutilise `compose_chain`)

## Citations acté

> « on va differer de word pour ça, les systèmes seront gérés en multilignes avec
> Maj+entree OU ; comme les matrices » — utilisateur, 2026-06-25/26

> « go Systemes ! en multilignes via Maj+entree ou ; si c'est concluant on le
> backportera dans Word, j'ai une bonne intuition » — utilisateur, 2026-06-26

Affinage (2026-06-28, validé en session) : `{` générique + Maj+Entrée standard.

> « il faut que ca soit suffisamment générique pour lever la popup quand je fais
> A { ou f(x) = { … plus le maj entree je prefere qu'on soit en mode standard,
> pas remplacer par un ; donc la popup doit detecter le caractere du maj entree
> plutot » — utilisateur, 2026-06-28

## Contexte

Après les chaînes, on porte les **systèmes d'équations** (accolade `{` englobant
des équations alignées) vers LibreOffice + VSCode. Word les fait en **incrémental**
(`CommitSystemLine`, create-or-extend, `{` requis sur la ligne courante ET au-dessus,
ADR 2026-06-10). L'utilisateur choisit **explicitement de diverger** pour les hôtes
phase 2 : modèle **matrice** — une **zone plate**, lignes séparées par `;` (déjà le
séparateur de lignes du moteur, rank 2) ou **Maj+Entrée** (qui insère `;`).

Le moteur renvoie « erreur » sur un `{` non fermé → le système vit **hors moteur**
(comme les relations/chaînes). Pas d'état incrémental → bien plus simple que les
chaînes (zone unique = un transform, comme une matrice).

## Décision

### 1. Composition dans le cœur Rust (réutilise `compose_chain`)

`compose_system(line, cu)` (`rust/mc-engine/src/chain.rs`) : trouve l'accolade `{`
NON fermée (ouvreur, n'importe où via `find_unclosed_brace`) ; le **préfixe** avant
`{` est analysé « comme d'hab » par le moteur (relation finale rattachée, ex.
`f(x) =`) ; le **reste** après `{` est découpé par `;`, composé via `compose_chain`
(alignement existant) et **enveloppé d'une accolade gauche** :
- LaTeX : `<préfixe> \left\{ \begin{aligned} … \end{aligned} \right.` (`\right.` = délim. droit invisible).
- StarMath : `<préfixe> left lbrace matrix{ … } right none` (`right none` = pas de délim. droit).

Pur wrapping de chaîne → **aucune modif des renderers du moteur**. Exposé par le
verbe stdio `COMPOSE_SYSTEM\t<culture>\t<line>`.

### 2. Détection hors moteur + saisie (générique)

Une zone contenant une accolade `{` **non fermée** n'importe où → système (pas
seulement en tête : `f(x) = {…`, `A {…`, `{ …`). Détection adapter (`find_open_brace`,
miroir de `find_unclosed_brace`). Le système = **UN candidat** (le bloc composé),
recomposé live à chaque frappe. Insertion : StarMath composé (LibreOffice), bloc
`<préfixe> \left\{…\right.` display multi-ligne (VSCode). **Pas d'état de chaîne**
(un système ne s'étend pas après commit).

### 3. Maj+Entrée = saut de ligne STANDARD, détecté comme séparateur

Maj+Entrée n'est **pas** intercepté : il fait son saut de ligne normal. L'adapter
lit la zone (qui contient alors un saut de ligne) et **convertit les sauts de ligne
en `;`** (séparateur de lignes du moteur) avant `compose_system` → le saut de ligne
devient une nouvelle ligne du système, **sans remplacer le comportement standard**.
LibreOffice : Maj+Entrée = saut DANS le ¶ (le `_KeyHandler` laisse passer, popup
maintenue) → `para_text` contient `\n` → converti en `;`. VSCode (à faire) : Maj+Entrée
= vraie nouvelle ligne du doc → détection multi-ligne (plus complexe, traité après).

## Tradeoff & alternatives écartées

- **Incrémental façon Word** (ligne `{` par ligne `{`) : l'utilisateur veut
  explicitement le modèle matrice plat (plus simple, cohérent avec les matrices).
- **Système dans le moteur** (parser un `{` ouvert) : casse la règle P1 (le moteur
  ignore le multiligne ; `{` non fermé = erreur). Reste hors moteur.
- **Nouvelle structure de rendu (NType::System)** : inutile — le wrapping accolade
  est une concaténation de chaîne au-dessus de `compose_chain`.
- **Maj+Entrée global** : casserait le saut de ligne normal → scopé « popup ouverte ».

## Conséquences

- **Rust** : `chain.rs` (`compose_system` + tests), `bin/analyze.rs` (verbe `COMPOSE_SYSTEM`).
- **LibreOffice** : `rust_clients.py` (`compose_system`), `mathcursor.py` (détection `{`,
  commit système, `_KeyHandler` Shift+RETURN).
- **VSCode** : `engine.ts` (`composeSystem`), `extension.ts` (détection + commit bloc),
  `popup.ts` (context key) + `mc-popup` hook (SHIFT), `package.json` (keybinding + commande).
- **API** : verbe stdio `COMPOSE_SYSTEM` (rétro-compatible). Renderers moteur inchangés.
- **Backport Word** : chantier séparé ultérieur si le modèle est concluant à l'usage.

## Validation post-fix

- `cargo test -p mc-engine` (tests `compose_system`) + smoke `COMPOSE_SYSTEM`.
- POC StarMath `left lbrace matrix{…} right none` dans Writer (avant câblage).
- Manuel : `{ 2x+y=5 ; x-y=1` → système accolade aligné ; Maj+Entrée ajoute une ligne,
  des deux côtés (LibreOffice + VSCode).
