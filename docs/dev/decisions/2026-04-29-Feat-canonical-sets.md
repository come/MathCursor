# Feat — Ensembles canoniques R/N/Z/Q/C avec modificateurs `*`/`+`/`-`

**Date :** 2026-04-29
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

Reconnaître les ensembles canoniques en deux phases :

### Phase 1 — désambig sur lettres isolées

Quand `R`, `N`, `Z`, `Q`, ou `C` apparaît **isolée** dans la source (critère
identique à V/E : précédée d'une non-lettre, suivie d'espace, EOF, ou
ponctuation/fermeture), une popup d'ambiguïté s'ouvre avec 2 alternatives :

- **Alt 0 (focus par défaut)** : `\mathbb{X}` (l'ensemble) — mutation source
  `R` → `bbR` qui passe par le keyword
- **Alt 1** : `X` lettre identity (variable)

Si l'utilisateur tape autre chose (sans Enter) la popup se ferme, R reste
lettre. Le focus par défaut sur l'ensemble est juste un signal visuel + un
défaut au moment de l'Enter, pas un auto-apply silencieux.

| Source | R isolé ? | Comportement |
|--------|-----------|-------------|
| `pi*R^2`, `pi*R²` | non (R suivi tight de `^`/`²`) | aucune popup, R reste atom |
| `2R+1` | non (R entouré ops tight) | aucune, R reste atom |
| `forall x R` | oui | popup R / `\mathbb{R}` |
| `x dans R, x ≥ 0` | oui (R suivi de `,`) | popup |
| `R` seul (Ctrl+Espace) | oui | popup |

Cohérent avec la convention V→∀ déjà en place : la lettre seule ne déclenche
ambig que dans un contexte où la sémantique « ensemble » est plausible.

### Phase 2 — modificateurs `*`/`+`/`-` tight

Quand un keyword `bbR`/`bbN`/etc. (post-mutation) est suivi tight d'un signe
`*`, `+`, ou `-` (et que rien n'est tight derrière le signe = espace, EOF,
ponctuation), le scope absorbe le signe comme modificateur typographique :

| Source | Rendu |
|--------|-------|
| `R*` | `\mathbb{R}^*` (réels non nuls) |
| `R+` | `\mathbb{R}^+` (réels positifs) |
| `R-` | `\mathbb{R}^-` (réels négatifs) |
| `R*+` ou `R+*` | `\mathbb{R}_+^*` (réels strictement positifs) |
| `R*-` ou `R-*` | `\mathbb{R}_-^*` (strictement négatifs) |

Ces formes ne déclenchent pas de popup ambig — la présence d'un modificateur
indique sans ambig que la lettre est un ensemble (pas une variable).
Concrètement le parser, après avoir consommé le keyword `bbR`, regarde
greedy les signes tight et construit le rendu directement.

### HORS scope (phase 3 ultérieure)

- `R-{1}` (R privé de 1) → `\mathbb{R} \setminus \{1\}` : demande de
  reconnaître `{...}` comme set extensionnel et `-` infix entre R et `{...}`
  comme `\setminus`. Plus complexe, brief séparé.
- Auto-apply de la pref défaut au moment de la détection (sans Enter user).
  À évaluer après usage si Enter à chaque ambig est lourd.

## Pourquoi

### Pourquoi pas keyword direct (R toujours `\mathbb{R}`)

Casserait `pi*R²` (aire de cercle) et toutes les formules de géométrie où
R = rayon, résistance, etc. La désambig avec critère « isolé » réserve la
sémantique ensemble aux contextes où elle a du sens.

### Pourquoi pas auto-apply de la pref défaut

L'auto-apply (R devient `\mathbb{R}` immédiatement sans clic, popup juste
pour annuler) demande un nouveau flag `AutoApplyDefault` + une boucle dans
`ZoneResolver.Resolve`. Pas trivial, et invisible pour l'utilisateur (ça
mute en silence). On commence sans : focus défaut sur ensemble + Enter
pour valider. Si à l'usage le Enter est lourd, on ajoute l'auto-apply dans
une PR ultérieure.

### Pourquoi modificateurs au niveau parser, pas via mutation

Les modificateurs `*`/`+`/`-` sont strictement attachés au keyword
ensemble (`R*`, `R+`). Pas d'ambig possible : la séquence est sans appel.
Les traiter dans le parser au moment où on consomme le keyword permet de
les absorber proprement sans nouveau dict de prefs ni round-trip popup.

## Conséquences

### Code (couche 1 — core)

- **Vocabulary.cs** : ajouter `bbR`, `bbN`, `bbZ`, `bbQ`, `bbC` keywords
  avec canonical = même nom. Note : on ajoute uniquement les versions `bb*`,
  pas les lettres seules R/N/Z/Q/C — ces dernières restent atom.
- **Parser.cs ParseScope** : nouveau case pour ces keywords. Greedy sur
  modificateurs tight `*`/`+`/`-` (sans rien tight derrière). Retour
  `Const("\\mathbb{R}")` ou variantes selon modificateurs.
- **AlternativeGenerator.cs** : nouveau scan `ScanCanonicalSetLetters`
  pour R/N/Z/Q/C isolés. Émet 2 alts (ensemble avec mutation
  `R` → `bbR`, lettre identity).
- **Nouveau RuleId** `RuleCanonicalSet`.

### Tests

- Parser : `bbR`, `bbR*`, `bbR+`, `bbR-`, `bbR*+`, `bbR+*`, `bbR*-`
- Renderer : rendus correspondants
- AlternativeGenerator : `R` isolé → 2 alts, `pi*R²` → no ambig, `forall x R`
  → ambig avec mutation R→bbR
- Pipeline : `forall x R` après mutation → `\forall x \in \mathbb{R}`
- Régression : `pi*R^2` reste `\pi R^{2}` (R atom)

## Validé par l'utilisateur

Direction (Phase 1 + 2 OK, hors scope `R-{1}`) :
> "et du coup R, R* R-{1} etc c'est comment ? R- aussi ca existe je crois ?
> c'est implementé deja ?"

Choix de la désambig sur lettres isolées + défaut focus sur ensemble :
> "desambig avec choix par defaut positionné sur l'ensemble et deuxieme
> choix la lettre / phase 1 et 2 ok"

Pas d'auto-apply (mécanisme actuel suffit) :
> "ok pour le comportement que tu propose juste le critère isolé + popup
> défaut sur ensemble"

## Statut

acté
