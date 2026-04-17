# math-addon — État du projet (v1.1.1)

## Architecture

```
Word Document
  ↕ Office.js (Word.run + context.sync)
Task Pane (Vue 3 + TypeScript + Vite)
  ├── watcher.ts   — cerveau (1034 lignes)
  ├── App.vue      — UI panneau latéral
  └── main.ts      — bootstrap
```

### Deux boucles de polling
- **Fast tick (50ms)** : détecte Tab → convertit, détecte curseur dans OMath → décompose
- **Slow tick (500ms)** : affiche les suggestions dans la task pane

### Pipeline de conversion (texte → OMath)
```
Texte → Scanner (backward) → Tokenizer → AST → Render → OOXML → insertOoxml
```

### Pipeline de décomposition (OMath → texte)
```
Curseur dans OMath → getOoxml → DOMParser → omathNodeToText → insertText
```

---

## Ce qui FONCTIONNE ✅

### Conversion Tab → OMath
- [x] Expressions simples : `1/2`, `x^2`, `f(x)` → OMath propre
- [x] Expressions complexes : `f(x)=3x+1/(4x+4)^2` → fraction + exposant
- [x] Juxtaposition : `f(x)`, `2x`, `3(a+b)` → multiplication implicite
- [x] Précédence math : `^` > `*/` > `+-=`
- [x] Parenthèses imbriquées : `(a+b+c)/(d+1)` → parens retirées dans les fractions
- [x] Crochets : `[a+b]/[c+d]`
- [x] Opérateur en tête : `= (b+1)/(b+10^5)` → le `=` est accepté
- [x] Préprocessing exposant : `10 5` → `10^5`, `x 2` → `x^2`
- [x] Symboles grecs dans les expressions : `alpha`, `pi`, `inf` → α, π, ∞
- [x] Case insensitive : `PI` = `pi` = `Pi`
- [x] `^` échappé en `^^` pour Word search

### Symboles (regex patterns)
- [x] Quantificateurs : `Vx(R` → ∀x∈ℝ (très flexible : `pt x dans R`, `qq x c N`...)
- [x] Variables multiples : `Vx,y(R` → ∀x,y∈ℝ
- [x] Ensembles : `(R` → ∈ℝ, `!(R` → ∉ℝ, `sub R` → ⊂ℝ, `AuB` → A∪B
- [x] Opérateurs : `>=` → ≥, `<=` → ≤, `!=` → ≠, `=>` → ⟹, `<=>` → ⟺
- [x] Vecteurs : `vec AB` → OMath flèche au-dessus (groupChr)
- [x] Segments : `seg AB` → OMath barre au-dessus
- [x] Angles : `ang ABC` → ∠ABC
- [x] 15 lettres grecques : alpha→ω
- [x] Limites : `lim->inf`, `lim->0+`
- [x] Dérivées : `f'` → f′, `f''` → f″
- [x] Tous en OMath (pas du texte Unicode brut)

### Délimiteurs
- [x] Backtick `` ` `` : `On a `f(x)=1/x` → ne convertit que `f(x)=1/x`
- [x] Dollar `$` : idem
- [x] Double espace `  ` : → remplacé par simple espace
- [x] Configurable : `{ delim, replace }` dans le code

### Détection de frontière texte/math
- [x] Scanner backward : s'arrête aux mots 2+ lettres (sauf mots math connus)
- [x] Détection OMath existant : `seenOMath` flag pour ne pas inclure le texte avant
- [x] Normalisation OMath → ASCII : chars math italic U+1D400+ → a-z/A-Z
- [x] Normalisation parens OMath : `,` → `(`, `.` → `)` (heuristique contextuelle)
- [x] En-dash/em-dash → `-`

### Infrastructure
- [x] Vite + Vue 3 + TypeScript
- [x] HTTPS dev server avec office-addin-dev-certs
- [x] Sideload Word desktop via dossier partagé
- [x] manifest.xml fonctionnel
- [x] Task pane avec suggestions, debug, référence

---

## Ce qui NE MARCHE PAS ❌

### Remplacement Tab → OMath cassé
- **Le remplacement ne se fait pas** actuellement
- Le debug montre que le parsing (tokenize → AST → XML) fonctionne
- Hypothèse : le `para.search()` ne trouve pas le texte à cause de caractères spéciaux
- Ou : le `doReplace()` échoue silencieusement
- **À investiguer** : ajouter du debug dans `doReplace` pour voir si `search` retourne des résultats

### Décomposition OMath → texte
- La détection `font.name === "Cambria Math"` fonctionne (debug confirme)
- Le `omathXmlToText` n'a pas été testé (le flow boucle avant d'arriver là)
- Le `font.set({ name: "" })` pour reset la font n'est peut-être pas suffisant
- **Boucle** : le fast tick (50ms) re-détecte Cambria Math après décomposition
- `isReplacing` flag ajouté mais pas suffisant si la font persiste

### Content Controls
- CC wrappant l'OMath → affiche "Click or tap here to enter text" → **abandonné**
- Les CC ne sont pas compatibles avec les objets OMath dans Word

### Bookmarks
- `sel.getBookmarks(true, true)` retourne `[]` quand le curseur est dans un OMath
- Les bookmarks posés sur `insertOoxml` result ne sont pas retrouvables via la sélection
- `document.settings` fonctionne pour le stockage mais les bookmarks ne sont pas retrouvés

### OMath XML garbled
- Les expressions complexes (`f(x)=2*x+1/(2+X)`) rendaient `𝑓()=2×𝑥+,1-2+𝑋.)`
- **Causé par** : `^` non échappé dans Word search → fixé avec `^^`
- **Causé par** : OOXML trop verbeux ou mal structuré → simplifié
- **Causé par** : manque de `ctrlPr` dans les éléments OMath → ajouté
- Pas 100% résolu pour toutes les expressions

---

## Ce qui a été TESTÉ 🧪

| Expression | Parse | Insertion | Rendu |
|------------|-------|-----------|-------|
| `1/2` | ✅ | ✅ (v0.1.8+) | ✅ fraction |
| `x^2` | ✅ | ✅ | ✅ exposant |
| `f(x)=1/x` | ✅ | ⚠️ intermittent | ⚠️ parfois garbled |
| `H(x)=f(x)/(x+2)^2` | ✅ AST correct | ✅ (après fix `^^`) | ✅ |
| `F(x)=3x+1` | ✅ | ❌ ne remplace pas (v1.1.1) | — |
| `(a+b+c)/(d+1)` | ✅ | ❌ (anciennes versions) | — |
| `10^5+1/12` | ✅ | ✅ | ✅ |
| `vec AB` | ✅ | ✅ | ✅ flèche au-dessus |
| `Vx,y(R` | ✅ | ✅ | ✅ ∀x,y∈ℝ |
| `pi` | ✅ | ✅ | ✅ π |
| `>=` | ✅ | ✅ | ✅ ≥ |
| Clic dans OMath → décompose | ✅ détection | ⚠️ boucle | — |

---

## Problèmes connus à résoudre

1. **Le remplacement Tab ne fonctionne plus** — régression depuis les changements CC/bookmark/décomposition
2. **Décomposition OMath boucle** — le fast tick re-détecte Cambria Math après remplacement
3. **Normalisation OMath parens** — heuristique `,`→`(` et `.`→`)` fragile, dépend du contexte
4. **Scanner inclut du texte** — "On a f(x)" : le "a" passe comme variable
5. **Bookmarks non retrouvables** dans les OMath via `getBookmarks()`

---

## Fichiers

| Fichier | Rôle | Lignes |
|---------|------|--------|
| `src/taskpane/watcher.ts` | Polling, scanner, parser, render, remplacement | ~1034 |
| `src/taskpane/App.vue` | UI task pane | ~440 |
| `src/taskpane/main.ts` | Bootstrap Office.onReady | 7 |
| `src/taskpane/patterns.ts` | Ancien système de patterns (plus utilisé par watcher) | ~760 |
| `manifest.xml` | Config add-in Word | 15 |
| `vite.config.ts` | Dev server HTTPS | 20 |
| `CLAUDE.md` | Instructions projet | ~70 |

---

## Prochaines étapes prioritaires

1. **Fixer le remplacement Tab** — c'est la base, tout le reste en dépend
2. **Stabiliser la décomposition** — sans boucle
3. **Nettoyer le code** — beaucoup de code mort (patterns.ts, bookmarks, CC)
4. **Tester systématiquement** — chaque expression, chaque cas
