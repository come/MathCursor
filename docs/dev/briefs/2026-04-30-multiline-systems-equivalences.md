# Brief — Systèmes d'équations + chaînes d'équivalences (multi-lignes au commit)

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-30
**Branche :** `lattice-engine`
**Public cible :** agent C# autonome qui ne connaît pas le projet, intervient
sur la couche adapter VSTO (`adapter-vsto/src/MathCursor/Host/`) ET le core
(`core-csharp/src/MathCursor.Core/`).
**Briefs liés :**
- [`2026-04-29-merge-adjacent-omaths.md`](2026-04-29-merge-adjacent-omaths.md) (fusion intra-paragraphe, à étendre)
- [`2026-04-29-iterative-zone-expansion-ctrl-space.md`](2026-04-29-iterative-zone-expansion-ctrl-space.md) (mécanisme similaire, expansion paragraphe)

---

## 1. Le besoin

L'élève écrit naturellement les systèmes d'équations et les chaînes
d'équivalences/égalités sur **plusieurs lignes**, avec un marqueur en
début de chaque ligne :

```
{ x + y = 5
{ 2x - y = 1

2x + 1 = 5
   ⇔ 2x = 4
   ⇔ x = 2
```

Aujourd'hui MathCursor traite chaque paragraphe Word indépendamment.
Résultat : 2 OMath séparés au lieu d'un système empilé avec accolade qui
s'étend ; ou 3 OMath empilés avec leurs `=` désalignés. Visuellement
décevant et pédagogiquement faux.

L'utilisateur veut **garder son flow "commit régulier" (un Ctrl+Espace
par ligne)** mais que la fusion se fasse automatiquement quand la ligne
N+1 commence par un marqueur structurel (`{`, `<=>`, `=>`, `<=`, `=`).

**Règle de fermeture explicite** : un **paragraphe vide** (= un Enter
supplémentaire entre deux lignes) **casse la chaîne** sans ambiguïté.
C'est le signal intuitif que l'élève donne quand il a fini son
raisonnement et veut commencer autre chose. Tout marqueur en tête de
paragraphe APRÈS la ligne vide ouvrira un NOUVEAU bloc multi-ligne, pas
une extension du précédent.

## 2. Spec syntaxe — pattern unifié

### 2.1. Trois cas d'usage, un mécanisme

| Marqueur début ligne N+1 | Sémantique | Rendu LaTeX |
|--------------------------|------------|-------------|
| `{` | Système d'équations | `\begin{cases} ... \\ ... \end{cases}` |
| `<=>`, `⇔` | Équivalences enchaînées | `\begin{align*}` avec `\Leftrightarrow` au début de chaque ligne et `&` aligné sur `=` |
| `=>`, `⇒` | Implications enchaînées | idem avec `\Rightarrow` |
| `<=`, `⇐` | Implications inversées | idem avec `\Leftarrow` |
| `=` (en début de ligne, pas opérateur de relation) | Chaîne d'égalités algébriques | `\begin{align*}` avec `&=` aligné |

### 2.2. Flow commit régulier

```
[Ligne 1 dans Word]  { x+y=5  ⏎ Ctrl+Espace
                     → OMath créé : \begin{cases} x+y=5 \end{cases}
                     (accolade visible, 1 équation)

[Ligne 2 dans Word]  2x-y=1   ⏎ Ctrl+Espace
                     → MathCursor détecte le paragraphe ¶-1 = OMath cases
                       → MERGE : remplace les 2 ¶ par un seul OMath
                       \begin{cases} x+y=5 \\ 2x-y=1 \end{cases}
                       (accolade s'étend visuellement)

[Ligne 3 dans Word]  z=2      ⏎ Ctrl+Espace
                     → idem, ajoute la 3e ligne au cases existant.

[Ligne 4 dans Word]  Donc le couple solution est…
                     → texte non-math, fin de zone, le système est verrouillé.
```

### 2.3. Fermeture du bloc multi-ligne

Pas de marqueur explicite (`}` ou autre). La fermeture se déclenche
**implicitement** sur l'un des trois signaux :

1. **Paragraphe vide** (Enter en plus) — signal le plus clair, validé
   par l'utilisateur comme convention principale.
2. **Paragraphe sans marqueur en tête** (du texte introductif normal,
   ou une nouvelle équation indépendante).
3. **Marqueur incompatible avec le mode du bloc** (ex: `{` après une
   chaîne d'équivalences ouverte par `<=>`).

Tout paragraphe APRÈS le signal de fermeture qui commence par un
marqueur ouvre un **NOUVEAU** bloc, pas une extension de l'ancien.

### 2.4. Cas exhaustifs

| Saisie multi-lignes | Rendu attendu |
|---------------------|---------------|
| `{ x+y=5` ⏎ `2x-y=1` | `\begin{cases} x+y=5 \\ 2x-y=1 \end{cases}` |
| `{ x+y=5` ⏎ `2x-y=1` ⏎ `z=0` | `\begin{cases} x+y=5 \\ 2x-y=1 \\ z=0 \end{cases}` |
| `2x+1=5` ⏎ `<=> 2x=4` ⏎ `<=> x=2` | `\begin{align*} 2x+1 &= 5 \\ \Leftrightarrow 2x &= 4 \\ \Leftrightarrow x &= 2 \end{align*}` |
| `f(x) = 2x+1` ⏎ `= 2(x + 1/2)` ⏎ `= 2x+1` | `\begin{align*} f(x) &= 2x+1 \\ &= 2(x+1/2) \\ &= 2x+1 \end{align*}` |
| `forall x in R, x^2 >= 0` ⏎ `=> ...` | merge align* avec implication (cas mixte quantif + chaîne) |

### 2.5. Cas neutres (pas de merge)

| Saisie | Pourquoi pas de merge |
|--------|-----------------------|
| `f(x) = 2x+1` ⏎ `g(x) = x^2` | `g(x) = ...` ne commence pas par marqueur → 2 OMath séparés |
| `Soit f(x) = 1` ⏎ `On a f(0) = 1` | texte introductif présent → pas marqueur clean |
| `x = 5` ⏎ paragraphe vide ⏎ `y = 3` | paragraphe vide entre = barrière |
| `{` (vide) ⏎ `x+y=5` | premier paragraphe = juste `{`, pas une équation valide. Comportement : on essaie quand même `\begin{cases} \square \end{cases}` ? Ou on rejette ? V1 : rejette (le 1er Ctrl+Espace ne convertit pas un `{` solo). |

## 3. Désambig — interaction avec le merge intra-paragraphe existant

### 3.1. Précédence : intra-paragraphe gagne toujours

MathCursor a déjà un mécanisme de **merge intra-paragraphe** (ADR
`Feat-merge-adjacent-omaths` 29-04) : quand l'utilisateur commit un
nouveau OMath et qu'il y en a un juste avant **sur la même ligne**, les
deux fusionnent dans la même équation. C'est ce qui permet de taper
`lim x 0 sin(x)/x =` ⏎ Ctrl+Espace, puis `1` ⏎ Ctrl+Espace, et obtenir
un seul OMath `lim_{x→0} sin(x)/x = 1`.

Le nouveau merge **cross-paragraphe** introduit par ce brief s'applique
**uniquement** quand l'intra-merge n'a rien à manger sur la ligne
courante. Concrètement :

```
Cas A — intra-merge gagne (existant, INCHANGÉ par ce brief) :

[¶ N]  lim x 0 sin(x)/x =  ⏎ Ctrl+Espace   → OMath_A créé
[¶ N]  OMath_A 1            ⏎ Ctrl+Espace   → `1` mergé dans OMath_A
                                              (même ligne, pas de cross-merge)


Cas B — cross-merge (NOUVEAU, ce brief) :

[¶ N]   2x+1=5              ⏎ Ctrl+Espace   → OMath_B créé
[¶ N+1] <=> 2x=4            ⏎ Ctrl+Espace   → pas d'OMath sur la ligne courante,
                                              ¶ N a un OMath au-dessus,
                                              `<=>` est un marqueur compatible
                                              → cross-merge déclenche
                                              → align* à 2 lignes
```

**Pas de conflit possible** entre les deux : l'intra-merge regarde la
**même ligne**, le cross-merge regarde **le paragraphe précédent**. Ils
ne pointent jamais sur le même endroit.

### 3.2. Conditions pour déclencher le cross-merge

**Pré-conditions communes** (sinon pas de merge) :
- Pas d'OMath sur la même ligne avant (sinon §3.1, intra-merge gagne)
- Pas de paragraphe vide entre ¶ N-1 et ¶ courant (§1, ligne vide = barrière)

Ensuite la décision dépend de **l'état du ¶ précédent**, qui détermine
si la ligne courante doit avoir un marqueur ou peut être nue. Quatre
branches possibles :

#### Branche A — ¶ N-1 est déjà un `MultiLineBlock` cases

Cas typique : ¶ N-1 est un système ouvert par `{` (peut-être avec déjà
plusieurs lignes empilées). Comportement de la ligne courante :

| Ligne courante | Action |
|----------------|--------|
| Équation valide (sans marqueur) | **EXTEND** cases (append ligne) — c'est le cas typique du système qui grandit |
| Commence par `{` | EXTEND aussi (le `{` redondant est ignoré, ou avalé comme partie du contenu) |
| Commence par `<=>`, `=>`, `<=`, `=` | **CLOSE** cases (verrouillé), puis nouvelle action selon Branche C/D |
| Texte non-math | CLOSE cases, ¶ courant non transformé |

#### Branche B — ¶ N-1 est déjà un `MultiLineBlock` align*

Cas typique : chaîne d'équivalences déjà en cours.

| Ligne courante | Action |
|----------------|--------|
| Commence par marqueur align (`<=>`, `=>`, `<=`, `=`) | **EXTEND** align* |
| Commence par `{` | CLOSE align*, ouvre un nouveau cases standalone |
| Équation sans marqueur | CLOSE align*, ¶ courant devient OMath standalone (la chaîne est terminée) |

#### Branche C — ¶ N-1 est un OMath simple (1 ligne, pas un MultiLineBlock)

Cas typique : `2x+1=5` ou `f(x) = 2x+1` — équations standalone existantes.

| Ligne courante | Action |
|----------------|--------|
| Commence par marqueur align (`<=>`, `=>`, `<=`, `=`) | **WRAP** : ¶ N-1 + ¶ courant → align* à 2 lignes |
| Commence par `{` | Pas de merge. Le `{` ouvre un nouveau cases standalone |
| Sans marqueur | Pas de merge (standalone, comportement actuel) |

#### Branche D — ¶ N-1 n'est pas un OMath

Cas typique : ¶ texte (« Soit f définie par : »), ¶ vide, ou début de
document.

Pas de merge possible quel que soit le marqueur. Si la ligne courante
commence par `{`, elle ouvre un nouveau cases standalone (1 ligne). Si
elle commence par un autre marqueur, le marqueur est traité comme
caractère normal (`<=>` reste un opérateur dans une équation, etc.).

#### Récap visuel

```
SYSTÈME (commence par {) :
  [¶ N]   { x+y=5         ← Branche D ou C → ouvre cases
  [¶ N+1] 2x-y=1          ← Branche A (¶ N en cases mode) → EXTEND
  [¶ N+2] z=0             ← Branche A → EXTEND
  [¶ N+3] (texte)         ← Branche A → CLOSE cases

ÉQUIVALENCE (marqueur sur lignes 2+) :
  [¶ N]   2x+1=5          ← Branche D ou C → OMath simple
  [¶ N+1] <=> 2x=4        ← Branche C (¶ N OMath simple + marqueur align) → WRAP en align*
  [¶ N+2] <=> x=2         ← Branche B (¶ N en align mode) → EXTEND
  [¶ N+3] (texte)         ← Branche B → CLOSE align*
```

### 3.3. Pas de critère spécial pour `=`

Règle uniforme pour TOUS les marqueurs (`{`, `<=>`, `=>`, `<=`, `=`) :
le merge déclenche **dès que** :
- Le ¶ précédent est un OMath
- ET la ligne courante commence par un marqueur

Aucune analyse du contenu de l'OMath précédent. Validé par utilisateur :
*« toujours merge si paragraphe précédent = oMaths ET ma ligne courante
commence par = { <=> etc »*.

Tradeoff accepté : un utilisateur qui taperait `= 5x+1` voulant un OMath
standalone (avec un Hole en lhs) se retrouvera mergé avec le ¶
précédent. Considéré rare et de toute façon la cascade alt §3.5 (Phase
3) couvrira ce cas. La simplicité de règle l'emporte.

### 3.4. Mix de modes `cases` vs `align*`

Si le ¶ N-1 est en mode `cases` (ouvert par `{`) et le ¶ courant
commence par `<=>`, on **ne mixe pas** les deux dans le même bloc :

- Le ¶ N-1 reste verrouillé tel quel
- Le ¶ courant ouvre un NOUVEAU bloc align*

L'élève voit deux blocs OMath consécutifs (système + chaîne
d'équivalences). Cas rare en pratique.

### 3.5. Cascade alt désambig — filet de sécurité différé

**V1 : merge par défaut, sans alt.** L'élève subit le merge automatique
quand les conditions §3.2 sont remplies. Pas de moyen d'annuler.

**Phase 3 (différé, à ré-évaluer si l'usage le demande)** : popup propose
une alt **"garder séparés"** au moment du merge :

- Default : merge (cases ou align*)
- Alt 1 : 2 OMaths standalone (annule le merge)

Sticky preference (cf. ADR `zone-resolver-refactor`) : si l'élève choisit
"garder séparés" une fois, les merges suivants dans la même zone
respectent ce choix.

**Trigger pour activer Phase 3** : retour utilisateur indiquant que les
merges automatiques sont "trop agressifs" ou se déclenchent dans des cas
indésirés. Tant que personne ne se plaint, on garde la simplicité V1
(merge inconditionnel sur les conditions §3.2).

## 4. Architecture impactée

### 4.1. Adapter VSTO — détection cross-paragraphe

**Contrainte fondamentale** : Word OMath est un bloc inline contenu
dans **un seul paragraphe** Word. `\begin{cases}` et `\begin{align*}`
de LaTeX deviennent des matrices `■(...)` ou `█(...)` UnicodeMath, mais
toujours **dans un OMath unique, dans un ¶ unique**. Il n'existe PAS
de format Word où un OMath couvre plusieurs paragraphes.

→ **Le merge cross-¶ DOIT collapser** les paragraphes Word en un seul
au moment du commit. La ligne supplémentaire (Enter qui sépare ¶ N-1 et
¶ N) est supprimée. Côté édition Ctrl+E, on inverse : split sur `\n` de
la source brute → recréation de ¶ Word multi-ligne (cf. §4.8).

**Aujourd'hui** : `SuggestionService.ApplyZones` traite UN paragraphe.
Le `_contextReader.ReadCurrentParagraph()` retourne le texte du
paragraphe courant uniquement.

**À ajouter** :
- Lecture du **paragraphe précédent** : son range OMath (le cas échéant)
  + son texte source brut (récupéré depuis `EquationStore` /
  CustomXMLParts via le bookmark `mcEq_*`).
- Au moment du commit (Ctrl+Espace ou auto-commit), AVANT d'insérer le
  nouvel OMath :
  1. Vérifier si paragraphe précédent contient un OMath à nous (bookmark
     présent).
  2. Lire son texte source brut.
  3. Vérifier si le **paragraphe courant** commence par un marqueur de
     merge (`{`, `<=>`, `=>`, `<=`, `=` qualifié).
  4. Si oui : construire la source fusionnée (avec un séparateur de
     ligne — voir §4.2 sur la repr interne) et lancer le pipeline sur
     cette source unifiée.
  5. Remplacer **les deux paragraphes** par un seul paragraphe contenant
     le nouvel OMath.

### 4.2. Représentation interne — séparateur de ligne dans la source brute

Choix du séparateur dans la source brute stockée :

- **Option A** : `\n` littéral (newline). Simple, universel.
- **Option B** : token spécial `;;` ou `\\\\` (LaTeX-like).

Recommandation **A**. La source brute peut être multi-lignes ; le parser
tokenizer la traite ligne par ligne, le pattern détecté décide du wrap.

Exemple de source brute pour un système 2 lignes :
```
{ x+y=5
2x-y=1
```

Le parser voit `\n` au top-level, recognize que le contexte est un
système ouvert (par `{` initial), génère un `Bin("\\\\", ...)` ou un
nouveau noeud AST `MultiLineBlock` avec un mode `cases` / `align`.

### 4.3. AST — nouveau nœud `MultiLineBlock`

**Nouveau** :
```csharp
public sealed class MultiLineBlock : AstNode
{
    public string Mode { get; }  // "cases" | "align"
    public IReadOnlyList<AstNode> Lines { get; }
    public IReadOnlyList<string> LinePrefix { get; }
    // LinePrefix[i] = "" pour première ligne, "\\Leftrightarrow" / "\\Rightarrow" / "" pour suivantes
}
```

`Lines[i]` est l'AST de l'équation/expression à la ligne i.
`LinePrefix[i]` permet de gérer les implications enchaînées avec leur
flèche en début de ligne.

### 4.4. Lexer / Parser

- **Lexer** : émet un token `EdgeType.LineBreak` (NEW) sur `\n` source.
- **Parser** : nouveau `TryParseMultiLineBlock` au top de `ParseRelation`
  qui regarde si la sequence commence par un marqueur de merge.
  Stratégie spéculative : sauvegarde `_i`, tente le pattern, restore en
  cas d'échec.

### 4.5. LatexRenderer

Nouveau cas dans `Render` switch :
```csharp
MultiLineBlock mb => RenderMultiLineBlock(mb),
```

`RenderMultiLineBlock` produit `\begin{cases} ... \end{cases}` ou
`\begin{align*} ... \end{align*}` avec les `\\\\` séparateurs et les `&`
d'alignement (pour align*) sur le `=` de chaque ligne.

### 4.6. LatexToUnicodeMath

Vérifier que `\begin{cases}` et `\begin{align*}` sont déjà gérés dans
`ConvertEnvironments`. `\begin{cases}` l'est déjà. `\begin{align*}` à
ajouter — Word UnicodeMath utilise `\eqarray(...)` ou `■(...)` selon les
versions.

### 4.7. Mode édition revert

Lorsque l'utilisateur fait Ctrl+E sur un OMath multi-lignes, le revert
restaure la **source brute multi-lignes** dans le paragraphe courant
**ET** crée les paragraphes additionnels nécessaires (puisque le merge
avait collapsé N paragraphes en 1).

Stratégie : le bookmark stocke la source brute avec `\n`, le revert
split sur `\n` et insère un paragraph break entre chaque ligne.

## 5. Cas de test obligatoires

### 5.1. xUnit — Parser/Renderer (couche core)

```csharp
[Fact]
public void System_two_equations_renders_cases()
    => Assert.Equal(
        "\\begin{cases} x+y=5 \\\\ 2x-y=1 \\end{cases}",
        RenderTop("{ x+y=5\n2x-y=1"));

[Fact]
public void Equivalence_chain_renders_align_with_arrows()
    => Assert.Equal(
        "\\begin{align*} 2x+1 &= 5 \\\\ \\Leftrightarrow 2x &= 4 \\\\ \\Leftrightarrow x &= 2 \\end{align*}",
        RenderTop("2x+1=5\n<=> 2x=4\n<=> x=2"));

[Fact]
public void Equality_chain_renders_align()
    => Assert.Equal(
        "\\begin{align*} f(x) &= 2x+1 \\\\ &= 2(x+1/2) \\end{align*}",
        RenderTop("f(x)=2x+1\n= 2(x+1/2)"));

[Fact]
public void Single_brace_alone_no_block()
    // `{ x+y=5` SANS deuxième ligne reste un cases à 1 ligne
    => Assert.Equal(
        "\\begin{cases} x+y=5 \\end{cases}",
        RenderTop("{ x+y=5"));

[Fact]
public void Mixed_marker_priority_first_wins()
    // { en première, <=> en seconde → système ferme avant <=>
    // (ou system-compatible mode)
    => Assert.NotNull(RenderTop("{ x+y=5\n<=> 2x=4"));
```

### 5.2. xUnit — Adapter (mock VSTO)

- Test merge cross-paragraph avec mock `_contextReader` qui retourne 2
  paragraphes : 1 OMath existant + 1 paragraph avec marqueur.
- Test fermeture sur paragraph vide.
- Test fermeture sur texte non-math.
- Test cascade : 5 paragraphes consécutifs, tous mergent.

### 5.3. Anti-régression

- OMath simple (1 ligne, pas de marqueur) → comportement actuel inchangé.
- 2 OMath consécutifs sans marqueur → restent séparés (`f(x)=1` puis
  `g(x)=2`).
- Le merge intra-paragraphe (cf. ADR `Feat-merge-adjacent-omaths`)
  continue à marcher.

## 6. Pointers utiles

| Fichier | Rôle |
|---------|------|
| `adapter-vsto/src/MathCursor/Host/SuggestionService.cs` | `ApplyZones` à étendre pour cross-paragraph (autour ligne 448+) |
| `adapter-vsto/src/MathCursor/Host/ContextReader.cs` (à créer ?) | Lecture paragraphe précédent + son OMath bookmark |
| `core-csharp/src/MathCursor.Core/Lattice/Lexer.cs` | Token `LineBreak` |
| `core-csharp/src/MathCursor.Core/Lattice/Parser.cs` | `TryParseMultiLineBlock` |
| `core-csharp/src/MathCursor.Core/Lattice/Ast/AstNodes.cs` | Nouveau `MultiLineBlock` |
| `core-csharp/src/MathCursor.Core/Lattice/LatexRenderer.cs` | `RenderMultiLineBlock` |
| `core-csharp/src/MathCursor.Core/LatexToUnicodeMath.cs` | Vérifier `\begin{align*}` |
| `docs/dev/briefs/2026-04-29-merge-adjacent-omaths.md` | Brief de référence pour le mécanisme de merge |

## 7. Ce qu'il NE faut PAS faire (V1)

- ❌ Tracker l'extension cross-paragraphe **en temps réel** (avant
  commit). Trop fragile (typages partiels, undo Word) ET explicitement
  rejeté par l'utilisateur ("on reste ligne à ligne, pas de visu
  système qui grandit"). On ne touche **qu'au moment du commit** d'un
  nouveau paragraphe : la popup montre la ligne courante telle quelle,
  le merge avec les ¶ précédents se produit après Ctrl+Espace.
- ❌ Marqueur de fermeture explicite (`}` ou autre). La fermeture est
  toujours implicite (paragraphe vide / sans marqueur / marqueur
  incompatible — cf. §2.3). Décision validée par l'utilisateur.
- ❌ Auto-detect chaîne `=` standalone (sans marqueur explicite) : trop
  ambigu. L'utilisateur DOIT taper `=` en début de ligne s'il veut une
  chaîne d'égalités.
- ❌ Mix `{` + `<=>` dans le même bloc. V1 = un mode unique par bloc,
  défini par le marqueur de la première ligne.
- ❌ Système avec plus de 5 lignes en V1 (cap matériel). Si > 5, on cap
  à 5 + alerte popup pour faire 2 systèmes.
- ❌ Indentation visuelle des marqueurs (`<=>` en retrait). C'est `align*`
  qui aligne sur `=`, le préfixe `\Leftrightarrow` est en début de ligne
  par convention LaTeX, ne pas chercher à le décaler.

## 8. Validation

1. `dotnet build MathCursor.sln` → 0 erreur, 0 warning.
2. `dotnet test core-csharp/tests/` → tous les tests existants verts +
   les ~10 nouveaux du §5.
3. Test manuel pipeline complet en Word :
   - Tape `{ x+y=5` Ctrl+Espace → OMath cases 1 ligne.
   - Enter, tape `2x-y=1` Ctrl+Espace → OMath cases 2 lignes (les 2 ¶
     fusionnés).
   - Enter, tape `z=0` Ctrl+Espace → OMath cases 3 lignes.
   - Enter, tape `Donc :` (texte) → fin du système.
   - Vérifie visuel : accolade qui s'étend, équations alignées.
4. Idem pour équivalences : `2x+1=5` + `<=> 2x=4` + `<=> x=2` → align*
   avec `=` alignés.
5. Test mode édition : Ctrl+E sur un système, vérifie que la source
   brute revient avec les retours-ligne correctement reconstitués.
6. ADR créé : `docs/dev/decisions/2026-04-XX-Feat-multiline-systems.md`.

## 9. Estimation

| Tâche | Durée |
|-------|-------|
| Lecture code adapter VSTO + ContextReader actuel | 1 h |
| `MultiLineBlock` AST + Lexer LineBreak token | 1 h |
| Parser `TryParseMultiLineBlock` + détection marqueurs | 3 h |
| LatexRenderer `RenderMultiLineBlock` (cases + align*) | 2 h |
| LatexToUnicodeMath : ajouter `\begin{align*}` (si absent) | 1 h |
| Adapter VSTO : lecture paragraphe précédent + merge logic | 4 h |
| Mode édition revert : split sur `\n` + recreate paragraphs | 2 h |
| Tests xUnit (~10 cas) + tests adapter (mock) | 3 h |
| Test manuel Word + fix régressions | 2 h |
| ADR + commit propre | 30 min |
| **Total V1** | **~20 h ≈ 2,5 jours** |

## 10. Phasing & dépendances

**Pré-requis** :
- Le brief `2026-04-29-merge-adjacent-omaths.md` (déjà acté ADR
  `Feat-merge-adjacent-omaths` 29-04) couvre le merge intra-paragraphe.
  Ce brief en est l'**extension cross-paragraphe**, mais le mécanisme
  d'AST merge réutilise les patterns existants.

**Phasing recommandé** (re-priorisé après validation utilisateur 30-04 :
*« Commence peut-être par les équations et les égalités ligne à ligne »*) :

- **Phase 1 (mvp)** : équivalences `<=>` / `=>` / `<=` + chaîne `=`.
  Toute la famille `\begin{align*}` (un seul environnement LaTeX). ~1.5
  jour. Cible l'usage le plus fréquent (raisonnement déductif lycée).
- **Phase 2** : système `{` (`\begin{cases}`). ~1 jour. Réutilise le
  mécanisme cross-¶ déjà en place ; n'ajoute qu'un nouveau marqueur et
  un nouveau mode de rendu.
- **Phase 3** : cascade alt désambig "garder séparés" (popup propose de
  ne pas merger pour les cas borderline). ~0.5 jour. Reportable si
  l'usage Phase 1+2 ne révèle pas le besoin.

Total étalé sur ~3 jours si on déploie progressivement. La Phase 1 seule
débloque déjà 80% de la valeur (les chaînes de raisonnement sont plus
courantes que les systèmes en cours de maths lycée).

**Pas de viz live multi-paragraphe** : le merge est purement déclenché
au commit (Ctrl+Espace de la ligne N+1). La popup montre uniquement la
ligne en cours, pas le bloc complet en train de grandir. Cohérent avec
le principe "commit régulier" demandé par l'utilisateur.

## 11. Question ouverte pour come

L'écriture manuscrite du système met l'**accolade gauche** mais souvent
**rien à droite**. LaTeX `\begin{cases}` rend exactement ça (accolade
gauche, rien à droite). C'est OK ?

Pour les équivalences, l'usage scolaire alterne : `\Leftrightarrow` en
**début** de chaque ligne (commun) ou centré entre les lignes (rare,
`\begin{aligned}` avec barre verticale invisible). V1 = en début de
ligne, simplest.

Pour la chaîne `=`, l'**indentation** du `=` (en retrait sous le `=` de
la ligne 1, alignée colonne) est obtenue gratuitement avec
`\begin{align*}`. C'est **exactement** l'effet visuel demandé "le bon
goût d'aligner les `=`".
