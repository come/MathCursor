# Brief — Matrices et vecteurs colonnes

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-29
**Branche :** `lattice-engine`
**Public cible :** ce brief est un document de RÉFLEXION qui propose plusieurs
options et appelle des décisions explicites. À discuter avec come avant
d'écrire la moindre ligne de code.

---

## 1. Contexte

Aujourd'hui, le lattice engine couvre les expressions math linéaires (une
ligne, structure arborescente sans 2D) : fractions empilées, sommes,
intégrales, vecteurs `\vec{u}`, ensembles, intervalles…

**Trou identifié** : pas de support des objets *fondamentalement 2D* :
- **Vecteurs colonnes** ($\begin{pmatrix} 1 \\ 2 \\ 3 \end{pmatrix}$)
- **Matrices** ($\begin{pmatrix} 1 & 2 \\ 3 & 4 \end{pmatrix}$)
- **Déterminants** ($\begin{vmatrix} 1 & 2 \\ 3 & 4 \end{vmatrix}$)

Ces objets sont incontournables au programme **Terminale** (matrices, calcul
matriciel, écriture matricielle de système, transformation linéaire). En 3D,
les vecteurs sont aussi systématiquement écrits en colonne.

C'est le plus gros morceau syntaxique manquant. Et il **change la donne
côté lexer/parser** : on passe d'une grammaire 1D à une grammaire qui
admet une dimension verticale (séparateur de lignes).

## 2. Périmètre — V1 vs V2 vs hors scope

### V1 — minimal viable (objectif de ce brief principal)
- **Vecteur colonne** : `(a; b; c)` ou `colvec(a, b, c)` (à trancher §4)
- **Matrice 2×2 et 3×3** : syntaxe à trancher (§4)
- **Déterminant** : `det A` ou `det (1 2 ; 3 4)`
- **Transpose** : `A^T`
- **Produit matriciel** : `A B` (concaténation, comme variables)
- **Identité** : `I_n` (déjà supporté, à confirmer)

### V2 (brief séparé après V1 validé)
- Inverse `A^{-1}` (probablement déjà supporté via Sup)
- Norme vectorielle `||v||`
- Matrices 4+×n (rare au lycée)
- Augmented matrices `(A | b)`
- Notations Cramer ($\Delta_i$)
- Comatrice, cofacteurs

### Hors scope (jamais)
- Calcul matriciel symbolique (réduire, inverser, multiplier réellement)
- Tenseurs n-dimensionnels
- Bloc-matrices

## 3. Le défi central

Le lattice est aujourd'hui **strictement 1D** : une chaîne d'entrée → une
suite d'edges → un AST arborescent → du LaTeX. Les matrices imposent :

1. **Un séparateur de lignes** dans la syntaxe d'entrée. Le seul candidat
   évident côté clavier français est le `;` — déjà utilisé par les
   intervalles (`[0;1]`).
2. **Une dimension verticale dans l'AST** — un nouveau type de nœud
   `Matrix` avec un tableau 2D de cellules.
3. **Validation de cohérence** — toutes les lignes doivent avoir la même
   longueur, sinon erreur ou padding.
4. **Compatibilité WPF-Math** côté popup (à vérifier — `\begin{pmatrix}` est
   normalement supporté).

## 4. Options de syntaxe d'entrée — DÉCISION À ACTER

C'est le sujet le plus important. Cinq propositions, comparées sur
**ergonomie clavier**, **clarté visuelle**, **conflits avec syntaxe
existante**, **fluidité saisie au cours**.

### Option A — Parens + virgules + point-virgule (style MATLAB/papier)

**Saisie** :
```
(1 2 ; 3 4)              → matrice 2×2
(1 ; 2 ; 3)              → vecteur colonne 3
(1 2 3 ; 4 5 6 ; 7 8 9)  → matrice 3×3
det (1 2 ; 3 4)          → déterminant
```

- ✅ Ergonomie clavier : `(`, `;`, espaces — touches faciles AZERTY
- ✅ Clarté visuelle : ressemble à la notation papier des cours de Terminale
- ⚠️ Conflit : `(1; 2)` est aussi un intervalle français — **ambiguïté
  avec un vecteur colonne 2D**. Disambig possible : un `;` seul → intervalle ;
  deux `;` ou plus → vecteur. Mais (1;2) reste ambigu.
- ✅ Fluidité : `(1 2 3 ; 4 5 6 ; 7 8 9)` se tape en 21 caractères, pas de
  modale, pas de mot-clé long.

### Option B — Crochets + point-virgule

**Saisie** :
```
[1 2 ; 3 4]
[1 ; 2 ; 3]
det [1 2 ; 3 4]
```

- ✅ Lève l'ambiguïté avec parens (les parens restent groupement / intervalle ouvert)
- ❌ Conflit : `[0;1]` est un intervalle fermé. `[1; 2]` est ambigu (intervalle vs vecteur 2D).
- ⚠️ Visuellement crochets = matrice rectangulaire au lieu de parens —
  notation MATLAB / Numpy plus que notation française scolaire.

### Option C — Mot-clé explicite + délimiteurs au choix

**Saisie** :
```
mat (1 2 ; 3 4)         → \begin{pmatrix} ... \end{pmatrix}
mat [1 2 ; 3 4]         → \begin{bmatrix} ... \end{bmatrix}
colvec (1, 2, 3)        → vecteur colonne
det (1 2 ; 3 4)         → déjà ci-dessus
```

- ✅ Pas de conflit avec intervalles (le `mat` lève l'ambiguïté)
- ✅ Très lisible
- ❌ Plus de frappe (`mat ` + 4 chars en plus à chaque matrice)
- ❌ Friction au clavier — un élève qui prend des notes va trouver ça lourd
- ✅ V/E pattern : on a déjà accepté que les lettres-quantificateurs soient
  ambiguës. Pareil ici, mais avec un keyword on évite tout doute.

### Option D — Multi-ligne (séparateur = saut de ligne)

**Saisie** (sur plusieurs lignes dans Word) :
```
mat
1 2
3 4
```

- ✅ Ressemble à la transcription manuscrite
- ❌ **Casse fondamentalement la philosophie** « zone math sur une ligne »
  du moteur actuel (NER, Ctrl+Espace, lattice…). Les sauts de ligne sont
  des frontières dures aujourd'hui.
- ❌ Word gère mal les blocs multi-lignes dans une frappe au fil de l'eau.
- ❌ Fragile : un retour à la ligne accidentel casse tout.
- 🚫 **Rejeté** sauf si come pousse fort dans cette direction.

### Option E — Hybride : mot-clé optionnel + parens + double séparateur

**Saisie** :
```
(1 2 || 3 4)             → matrice 2×2 (`||` = sep ligne)
(1 || 2 || 3)            → vecteur colonne
mat (1 2 || 3 4)         → idem (mot-clé optionnel pour clarté)
det (1 2 || 3 4)
```

- ✅ Pas d'ambiguïté avec intervalles (le `||` n'est jamais un intervalle)
- ⚠️ Le `||` peut entrer en conflit avec la norme `||v||` (V2)
- ❌ Moins habituel — pas dans les conventions papier ni MATLAB

### Tableau récap

| Critère | A `;` parens | B `;` brackets | C keyword | D multi-line | E `\|\|` |
|---------|:---:|:---:|:---:|:---:|:---:|
| Ergonomie clavier | ★★★★ | ★★★★ | ★★★ | ★ | ★★★ |
| Clarté visuelle (papier) | ★★★★★ | ★★★ | ★★★★ | ★★★★★ | ★★ |
| Pas d'ambiguïté | ★★ | ★★ | ★★★★★ | ★★★★ | ★★★★ |
| Faisabilité technique | ★★★ | ★★★ | ★★★★★ | ★ | ★★★ |
| Cohérence existant | ★★★ | ★★★ | ★★★★ | ★ | ★★ |

### Ma recommandation

**Option A pour les matrices ≥ 2 lignes**, **Option C pour les vecteurs
colonnes**. Combinaison :

- `(1 2 ; 3 4)` → matrice (au moins 2 lignes → pas d'ambiguïté avec
  intervalle qui a forcément 2 éléments)
- `colvec(1, 2, 3)` ou `cv(1, 2, 3)` → vecteur colonne explicite (lève
  l'ambig avec intervalle 2-élément)

**Pourquoi ce mix** :
- La matrice 2×n est sans ambiguïté (l'intervalle a 2 éléments séparés par
  UN `;`, jamais 2). Donc Option A pour les matrices fonctionne pile.
- Le vecteur colonne `(a; b)` est en collision frontale avec l'intervalle
  `(a; b)`. Il FAUT un keyword pour distinguer. D'où Option C limitée aux
  vecteurs.

**Cas exceptionnel** : `(1; 2; 3)` (3 éléments). Ce N'EST PAS un intervalle
(qui en a toujours 2). Donc on pourrait le traiter directement comme
vecteur colonne sans keyword. Mais on garde la cohérence : keyword
obligatoire pour les vecteurs, ambiguïté nulle pour les matrices `≥ 2×2`.

**À DÉCIDER** :
1. ⚠️ Option A pure, ou A+C mixé comme proposé, ou tout C (keyword
   obligatoire partout) ?
2. ⚠️ Le keyword vecteur colonne : `colvec`, `cv`, `vcol`, `col` ? Choisir
   un seul, idéalement court (saisie répétitive).
3. ⚠️ Faut-il aussi un keyword `mat` optionnel pour les cas où l'élève veut
   être explicite ? (cohérent avec les autres keywords-scope).

## 5. Architecture impactée

### 5.1. Lexer

- **Nouveau token** : `ROW_SEP` pour `;` à l'intérieur d'une matrice. Doit
  désambiguiser avec `;` d'intervalle. Stratégie : `;` est `ROW_SEP` quand
  on est entre des delimiteurs de matrice (parens contenant > 1 occurrence
  de `;` OU contenant un keyword `mat`/`colvec`).
- **Tokens existants** : espace = séparateur de colonnes, `,` = séparateur
  alternatif (à confirmer — actuellement les intervalles utilisent `,` ou `;`).

### 5.2. Vocabulary

- Ajouter keyword `colvec` (ou choix §4)
- Optionnel : `mat`, `pmatrix`, `bmatrix`
- Ajouter keyword `det` (déterminant) → wrap matrice en `\begin{vmatrix}`

### 5.3. Parser + AST

- **Nouveau nœud** : `Matrix(rows: AstNode[][], delim: 'paren'|'bracket'|'vbar')`
- **Nouveau nœud** : `ColumnVector(items: AstNode[])` — sucre syntaxique
  pour `Matrix` à 1 colonne
- Validation : toutes les lignes ont la même longueur. Sinon : Hole pour
  cellules manquantes (cohérent avec le reste du parser).

### 5.4. LatexRenderer

- `Matrix` paren → `\begin{pmatrix} ... \\ ... \end{pmatrix}`
- `Matrix` bracket → `\begin{bmatrix} ...`
- `Matrix` vbar (déterminant) → `\begin{vmatrix} ...`
- `ColumnVector` → `\begin{pmatrix} a \\ b \\ c \end{pmatrix}` (équivalent
  matrice colonne)

### 5.5. WpfMathAdapter (popup)

À **vérifier en premier** avant tout code : WPF-Math supporte-t-il
`\begin{pmatrix}` ? Si non, fallback de rendu nécessaire. Bref test :
charger `\begin{pmatrix} 1 & 2 \\\\ 3 & 4 \end{pmatrix}` dans la popup et
voir si ça rend ou si ça affiche un `.`

### 5.6. AlternativeGenerator

Ambigüités possibles à exposer en désambig :
- `(1 2 ; 3 4)` → matrice paren ou matrice bracket ? → 2 alternatives
- `(1 2 ; 3 4)` dans contexte `det …` → forcément `vmatrix` → pas
  d'alternative
- Matrices vs vecteur ligne `(1, 2, 3)` (déjà supporté ?) → désambig si
  `(1; 2; 3)` (3 éléments avec `;`)

### 5.7. ZoneResolver / SuggestionService

- La détection de zone math doit absorber les matrices entières
  (parenthèse ouvrante → fermante, `;` inclus)
- Ctrl+Espace iterative-extend déjà existant : compatible si les `;` sont
  dans la même phrase

### 5.8. Mode édition (revert source)

- Le source mémorisé doit inclure la syntaxe matrice complète
- Pas de problème particulier — c'est du texte ASCII

## 6. Ambiguïtés à résoudre

### 6.1. `;` interval vs row separator

| Saisie | Interpretation actuelle | V1 attendue |
|--------|-------------------------|-------------|
| `[0;1]` | Intervalle | Intervalle (inchangé) |
| `(0;1)` | Intervalle ouvert | Intervalle ouvert (inchangé) |
| `(0;1;2)` | (n'existe pas aujourd'hui) | Vecteur colonne 3 ? Ou erreur ? |
| `(1 2 ; 3 4)` | (mal parsé aujourd'hui) | Matrice 2×2 |
| `colvec(0, 1)` | (pas de keyword) | Vecteur colonne 2 (lève l'ambig) |

**Règle de désambig proposée** :
- 1 seul `;` à plat dans `(...)` ou `[...]` → intervalle
- 2 `;` ou plus → matrice / vecteur colonne
- `;` après un mot-clé `mat`/`colvec`/`det` → toujours row separator

### 6.2. Matrice 1×n vs vecteur ligne

`(1, 2, 3)` peut signifier :
- Coordonnées d'un point (déjà supporté ?)
- Vecteur ligne
- Liste

**Décision proposée** : laisser `(1, 2, 3)` = atome multi-élément (vecteur
ligne / point), et n'utiliser le row separator `;` que si on veut une
matrice colonne explicite. Cohérent avec la convention française.

## 7. Impact NER (corpus)

Le NER doit absorber les zones contenant des matrices comme zone math
unique. Cas typiques :
- "Soit A = (1 2 ; 3 4) une matrice" → MATH = `A = (1 2 ; 3 4)`
- "On calcule det (1 2 ; 3 4)" → MATH = `det (1 2 ; 3 4)`
- "Le vecteur colvec(1, 2, 3)" → MATH = `colvec(1, 2, 3)`

→ Brief NER v6 séparé après V1 lattice. ~150 lignes positives + ~30
distractors. Pas urgent : si on déclenche via Ctrl+Espace, le NER n'est
pas le bottleneck.

## 8. Cas de test obligatoires

### 8.1. Syntaxe basique
```
(1 2 ; 3 4)              → \begin{pmatrix} 1 & 2 \\ 3 & 4 \end{pmatrix}
(1 0 0 ; 0 1 0 ; 0 0 1)  → identité 3x3
colvec(1, 2, 3)          → \begin{pmatrix} 1 \\ 2 \\ 3 \end{pmatrix}
```

### 8.2. Avec opérations
```
A B                      → A B (produit matriciel implicite, pas de \cdot)
A^T                      → A^T
det (1 2 ; 3 4)          → \begin{vmatrix} 1 & 2 \\ 3 & 4 \end{vmatrix}
2 * (1 2 ; 3 4)          → 2 \cdot \begin{pmatrix}…\end{pmatrix}
```

### 8.3. Anti-régression
```
[0;1]                    → intervalle (inchangé)
(0;1)                    → intervalle ouvert (inchangé)
[0,1] U [2,3]            → union d'intervalles (inchangé)
vec u                    → \vec{u} (inchangé — pas confondu avec vecteur colonne)
```

### 8.4. Cas borderline
```
(1; 2)                   → ambig : intervalle ou vecteur 2 ? Décision §4 = intervalle
(1; 2; 3)                → matrice colonne 3 (3+ semicolons → matrice)
mat (1)                  → matrice 1×1 (cas dégénéré, valide)
(1 2 ; 3)                → ERREUR : lignes de longueurs incohérentes
```

## 9. Phasing — découper en PR petites

### Phase 1 — vecteurs colonnes seuls
- Keyword `colvec` (ou autre, §4)
- Nouveau nœud AST `ColumnVector`
- Renderer → `\begin{pmatrix} … \end{pmatrix}` 1 col
- Tests : 3-5 cas
- ~1 jour

### Phase 2 — matrices 2D parenthèses
- Lexer `;` row sep (avec règle de désambig §6.1)
- Nœud `Matrix`
- Renderer pmatrix
- Tests : 6-8 cas
- ~2 jours

### Phase 3 — déterminants
- Keyword `det` qui wrap une matrice en vmatrix
- Tests : 3-4 cas
- ~0.5 jour

### Phase 4 — alternatives & polish
- AlternativeGenerator pour pmatrix vs bmatrix
- Transpose `A^T` (probablement déjà OK via Sup)
- WPF-Math vérification rendu popup
- ~0.5-1 jour

### Phase 5 — NER corpus
- Brief séparé `2026-04-XX-ner-retraining-v7-matrices.md`
- 150-300 exemples synthétiques
- Retrain notebook
- ~1-2 jours (incluant retrain Colab)

**Total estimé V1 complet** : ~5-7 jours dev + retrain NER

## 10. Décisions à acter avec come avant code

1. **Option de syntaxe** (§4) : A pure, A+C mixé, ou tout C ?
2. **Keyword vecteur colonne** : `colvec`, `cv`, `vcol`, autre ?
3. **Délimiteurs matrices supportés** : `( ; )` only, ou aussi `[ ; ]` ?
   Si oui, comment désambiguiser de l'intervalle `[0;1]` ?
4. **Comportement sur lignes de longueurs incohérentes** : erreur, padding
   par Hole, ou interprétation greedy ?
5. **Ordre des phases** : faire vecteurs colonnes d'abord seuls, ou
   matrice complète d'un coup ?
6. **Le mode édition** : un revert sur `(1 2 ; 3 4)` revient au texte brut,
   confirmé OK ?
7. **Désactiver temporairement le pattern intervalle** pour `(...)` et
   garder `;` pour matrices uniquement ? (autre option d'évitement
   d'ambig, mais casse intervalles français)

## 11. Ce qu'il NE faut PAS faire

- ❌ Hardcoder la dimension max (« max 4×4 »). Le parser doit accepter
  n'importe quelle taille — la limite vient du rendu LaTeX, pas du parser.
- ❌ Implémenter du calcul matriciel symbolique (`A * B` qui multiplie
  vraiment les entrées). MathCursor ne fait que la **conversion notation
  → équation**, pas le calcul.
- ❌ Ajouter une UI dédiée matrices (« assistant matrice » dans la popup).
  Ça contredit l'esprit clavier-only. Si la syntaxe est trop dure à taper,
  on retravaille la syntaxe, on n'ajoute pas un wizard.
- ❌ Ship V1 sans tester WPF-Math. Si le rendu popup affiche un `.`,
  l'élève ne voit pas ce qu'il fait → expérience cassée.
- ❌ Casser les intervalles français existants (`[0;1]`, `]0;1[`,
  `[0;+inf[`). Tests anti-régression obligatoires §8.3.
- ❌ Étendre à V2 (inverse, augmented, norme `||·||`) dans la même PR que
  V1. Trop de surface, trop de risque. V2 = brief séparé après validation.
- ❌ Modifier le NER avant que la syntaxe lattice soit validée. Sinon on
  retrain pour rien.

## 12. Questions ouvertes / discussion

- L'utilisateur cible (lycéen Terminale + son prof) tape-t-il vraiment
  des matrices au clavier en cours ? Ou est-ce davantage pour les DM/DS
  rédigés ? L'ergonomie diffère (cours = vitesse, DM = précision).
- Vaut-il mieux avoir une syntaxe **moins ergonomique mais sans
  ambiguïté** (option C) qu'une **plus naturelle mais avec edge cases**
  (option A) ? Pour l'usage cours, l'ergonomie compte — pour l'usage
  DM/DS, la précision peut primer.
- Comment afficher les matrices dans la popup pendant la frappe ? La
  popup est étroite — une matrice 3×3 va déborder. Stratégie : taille
  réduite ? Scroll horizontal ? À tester avec WPF-Math.

---

**Prochaine étape** : discuter §10 (les 7 décisions) avec come, trancher,
puis je peux écrire le brief d'implémentation V1 *concret* (Phase 1 ou
Phase 1+2) avec spec figée et plan de PR.
