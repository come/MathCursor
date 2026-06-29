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
maintenue) → `para_text` contient `\n` → converti en `;`. VSCode : Maj+Entrée = vraie
nouvelle ligne du doc → **détection multi-ligne** (`detectSystem` remonte depuis la
ligne du caret jusqu'à une accolade `{` non fermée, borné à 8 lignes, stop sur ligne
vide) ; le hook clavier `mc-popup` est rendu **conscient de SHIFT** (Maj+Entrée laissé
passer, ni commit ni fermeture) ; le bloc remplace la plage multi-ligne en `\[…\]`.

## Tradeoff & alternatives écartées

- **Incrémental façon Word** (ligne `{` par ligne `{`) : l'utilisateur veut
  explicitement le modèle matrice plat (plus simple, cohérent avec les matrices).
- **Système dans le moteur** (parser un `{` ouvert) : casse la règle P1 (le moteur
  ignore le multiligne ; `{` non fermé = erreur). Reste hors moteur.
- **Nouvelle structure de rendu (NType::System)** : inutile — le wrapping accolade
  est une concaténation de chaîne au-dessus de `compose_chain`.
- **`{` en tête seulement** : trop strict — `f(x) = {` (fonction par morceaux) doit
  ouvrir un système ⇒ accolade non fermée n'importe où + préfixe.
- **Maj+Entrée → `;` intercepté** : l'utilisateur veut le **saut de ligne standard** ;
  l'adapter convertit le saut en `;` à la lecture plutôt que de remplacer la touche.

## Conséquences

- **Rust** : `chain.rs` (`compose_system` générique + `find_unclosed_brace` +
  `split_trailing_relation`/`render_prefix` + tests), `bin/analyze.rs` (verbe `COMPOSE_SYSTEM`).
- **LibreOffice** : `rust_clients.py` (`compose_system`), `mathcursor.py` (`_find_open_brace`,
  détection `{` générique, commit système, `_KeyHandler` Maj+Entrée laissé passer).
- **VSCode** : `chain.ts` (`findUnclosedBrace`), `engine.ts` (`composeSystem`),
  `extension.ts` (`detectSystem` multi-ligne + commit bloc), `mc-popup` `main.rs`
  (hook conscient de SHIFT). (Pas de keybinding/context key : Maj+Entrée standard.)
- **API** : verbe stdio `COMPOSE_SYSTEM` (rétro-compatible). Renderers moteur inchangés.
  Gate `fixtures.json` 456/456 intacte.
- **Livré et validé** sur **LibreOffice ET VSCode**. Reste différé : la passe « resserrer
  le `=` » (espacement de colonne du `matrix` StarMath, commun chaînes+systèmes).
- **Backport Word** : chantier séparé ultérieur si le modèle est concluant à l'usage.

## Validation post-fix

- `cargo test -p mc-engine` (tests `compose_system`) + smoke `COMPOSE_SYSTEM`.
- POC StarMath `left lbrace matrix{…} right none` dans Writer (avant câblage).
- Manuel : `{ 2x+y=5 ; x-y=1` → système accolade aligné ; Maj+Entrée ajoute une ligne,
  des deux côtés (LibreOffice + VSCode).
