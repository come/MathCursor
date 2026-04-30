# Brief — Détection `=>` / `<=>` et conversion en flèches math

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-29
**Branche :** `lattice-engine`
**Public cible :** agent C#/lattice autonome qui ne connaît pas le projet.

---

## 1. Le besoin

Les élèves de lycée tapent au clavier `=>` et `<=>` pour exprimer
l'implication et l'équivalence math. Aujourd'hui MathCursor laisse passer
ces séquences en texte brut — pas de conversion. Attendu :

| Saisie | Conversion attendue (OMath / LaTeX) |
|--------|--------------------------------------|
| `=>` | ⇒ (`\Rightarrow`) |
| `<=>` | ⇔ (`\Leftrightarrow`) |
| `<==`/`==>` (variantes) | idem (cf. §2.2) |

Cas typiques en classe :

```
A => B
P(x) <=> Q(x)
x ≥ 0 => x^2 ≥ 0
forall x R, x > 0 <=> x^2 > 0
soit A => B et B => C, alors A => C
```

Ces séquences doivent être :
1. **Reconnues** dans la zone math par le NER (probablement déjà OK car le
   contexte autour est math, mais à valider — voir §6).
2. **Tokenisées** correctement par le Lexer lattice (greedy : `<=>` doit
   matcher en un seul token, pas `<=` + `>`).
3. **Rendues** en LaTeX `\Rightarrow` / `\Leftrightarrow` par
   `LatexRenderer.cs`.

## 2. Patterns à détecter

### 2.1. Flèches simples
- `=>` → `\Rightarrow` (⇒)
- `=>` n'est PAS `=` + `>` mais bien un opérateur logique unique.

### 2.2. Flèches doubles (équivalence)
- `<=>` → `\Leftrightarrow` (⇔)
- `<==>` → `\Leftrightarrow` (variante, pareil)
- `<==` → `\Leftarrow` (⇐, implication réciproque, plus rare mais cohérent)

### 2.3. Variantes ASCII tolérées (à confirmer avec come)
- `==>` → `\Rightarrow` (variante avec `==`)
- `->` → optionnel : déjà utilisé pour les limites (`lim x -> 0`), à ne PAS
  toucher dans ce brief — garder le sens existant.

### 2.4. Unicode litéral (rare mais possible si l'élève copie-colle)
- `⇒`, `⇔`, `⇐` → idéalement passent en passe-plat (le Lexer les voit comme
  symbol math direct, le LatexRenderer émet `\Rightarrow` etc.)

## 3. Architecture

### 3.1. Lexer — tokenisation greedy
Fichier : `core-csharp/src/MathCursor.Core/Lattice/Lexer.cs`.

Le Lexer doit reconnaître `<=>`, `<==>`, `<==`, `==>`, `=>` **avant** de
tokeniser les caractères individuels `=`, `<`, `>`. Ordre de greedy match :

```
1. `<==>`  → token IFF (4 chars)
2. `<=>`   → token IFF (3 chars)
3. `==>`   → token IMPLIES (3 chars)
4. `<==`   → token IMPLIES_LEFT (3 chars)
5. `=>`    → token IMPLIES (2 chars)
6. `<=`    → token LEQ (existant, 2 chars)
7. `>=`    → token GEQ (existant)
8. `<`, `>`, `=` → tokens individuels (existant)
```

**Critique** : la priorité greedy est l'ordre de la liste. `<=` doit être
testé **après** `<=>` et `<==>`, sinon on tokenize `<=` + `>` à la place de
`<=>`.

Les Unicode `⇒` `⇔` `⇐` sont matchés directement comme tokens IMPLIES /
IFF / IMPLIES_LEFT.

### 3.2. Vocabulary — étiquettes des nouveaux tokens
Fichier : `core-csharp/src/MathCursor.Core/Lattice/Vocabulary.cs`.

Ajouter trois nouveaux types de token (ou trois nouvelles entrées dans le
vocab existant — selon convention du fichier) :
- `IMPLIES` (=> / ==> / ⇒)
- `IFF` (<=> / <==> / ⇔)
- `IMPLIES_LEFT` (<== / ⇐) — optionnel selon §2.3

### 3.3. Parser — pas de structure AST nouvelle
Fichier : `core-csharp/src/MathCursor.Core/Lattice/Parser.cs`.

Les flèches d'implication / équivalence se comportent comme des
**relations binaires**, exactement comme `=`, `<`, `≤`, `>`, `≥`. Donc
elles s'insèrent dans la même grammaire que ces tokens existants — pas de
nouveau nœud AST nécessaire, juste un type de relation supplémentaire dans
le nœud `Relation` (ou équivalent).

### 3.4. LatexRenderer — émission des macros
Fichier : `core-csharp/src/MathCursor.Core/Lattice/LatexRenderer.cs`.

Pour chaque type de relation, émettre la macro LaTeX correspondante :
- `IMPLIES` → `\Rightarrow`
- `IFF` → `\Leftrightarrow`
- `IMPLIES_LEFT` → `\Leftarrow`

Les espaces autour suivent la convention déjà utilisée pour `=`, `<`, etc.
(typiquement `a \Rightarrow b` avec espaces simples).

### 3.5. WpfMath — vérification du rendu popup
Fichier (référence) : `adapter-vsto/src/MathCursor/UI/WpfMathAdapter.cs`.

WPF-Math (la lib utilisée pour le rendu popup) supporte nativement
`\Rightarrow`, `\Leftrightarrow`, `\Leftarrow`. Pas de substitution
nécessaire en principe. **À valider** par un test manuel : ouvrir la popup
sur `A => B` et `A <=> B` et vérifier que le rendu n'affiche pas un
fallback "."

## 4. Livrables

1. **Lexer** : ajouter les patterns greedy `<==>`, `<=>`, `==>`, `=>`,
   `<==` + Unicode `⇒` `⇔` `⇐`.
2. **Vocabulary** : trois nouveaux tokens `IMPLIES`, `IFF`,
   `IMPLIES_LEFT` (ou ajout dans la table existante selon convention du
   fichier).
3. **Parser** : intégrer ces tokens comme relations binaires.
4. **LatexRenderer** : émettre `\Rightarrow`, `\Leftrightarrow`,
   `\Leftarrow`.
5. **Tests xUnit** dans `core-csharp/tests/MathCursor.Core.Tests/Lattice/` :
   - `LexerTests` : tokenisation greedy correcte (cf. §5)
   - `LatexRendererTests` : sortie attendue pour chaque pattern
   - `LatticeEngineTests` : end-to-end sur les phrases du §5
6. **Optionnel — corpus NER v6** : si en pratique la NER ne tag pas
   `A => B` ou `A <=> B` comme zone math, ajouter une trentaine
   d'exemples dans une `extension_v6_arrows.jsonl`. Sinon (cas le plus
   probable, le contexte math autour suffit) — pas nécessaire.
7. **ADR** : `docs/dev/decisions/2026-04-XX-Feat-implication-equivalence-arrows.md`
   - Kind = Feat, Température = molle, Statut = acté
   - Citation utilisateur = ce brief

## 5. Cas de test obligatoires

### 5.1. Lexer (tokenisation)

| Input | Token attendu |
|-------|----------------|
| `=>` | `IMPLIES` |
| `<=>` | `IFF` |
| `<==>` | `IFF` |
| `==>` | `IMPLIES` |
| `<==` | `IMPLIES_LEFT` |
| `<=` | `LEQ` (existant, intact) |
| `>=` | `GEQ` (existant, intact) |
| `<` | `LT` (existant, intact) |
| `=` | `EQ` (existant, intact) |
| `⇒` | `IMPLIES` |
| `⇔` | `IFF` |
| `⇐` | `IMPLIES_LEFT` |

### 5.2. Conversion end-to-end (LatticeEngine.Convert)

| Input | Sortie LaTeX attendue (au moins ces macros présentes) |
|-------|-------------------------------------------------------|
| `A => B` | `A \Rightarrow B` |
| `P(x) <=> Q(x)` | `P(x) \Leftrightarrow Q(x)` |
| `x >= 0 => x^2 >= 0` | `x \geq 0 \Rightarrow x^2 \geq 0` |
| `forall x R, x > 0 <=> x^2 > 0` | `\forall x \in \mathbb{R}, x > 0 \Leftrightarrow x^2 > 0` |
| `A <==> B` | `A \Leftrightarrow B` |
| `A ==> B` | `A \Rightarrow B` |
| `A <== B` | `A \Leftarrow B` (si IMPLIES_LEFT implémenté) |

### 5.3. Anti-régression

Ces inputs doivent **continuer** à fonctionner comme avant :

| Input | Sortie attendue |
|-------|-----------------|
| `x <= 5` | `x \leq 5` (LEQ pas cassé par l'ajout `<=>`) |
| `x >= 5` | `x \geq 5` |
| `lim x -> 0` | `\lim_{x \to 0}` (`->` pas réinterprété comme implication) |
| `x = 5` | `x = 5` |

### 5.4. Test manuel dans Word (post-déploiement)

Taper dans Word puis Ctrl+Espace :
```
A => B
A <=> B
forall x R, x ≥ 0 => x^2 ≥ 0
P <=> Q et Q <=> R donc P <=> R
```

Vérifier :
- Popup s'ouvre, affiche le rendu OMath avec les bonnes flèches ⇒ et ⇔
- Pas de fallback `.` dans la popup
- L'OMath inséré dans Word affiche correctement les flèches

## 6. Pointers utiles

| Fichier | Rôle |
|---------|------|
| `core-csharp/src/MathCursor.Core/Lattice/Lexer.cs` | Tokenisation (à modifier) |
| `core-csharp/src/MathCursor.Core/Lattice/Vocabulary.cs` | Définition des tokens (à étendre) |
| `core-csharp/src/MathCursor.Core/Lattice/Parser.cs` | Construction AST (probablement modif minime) |
| `core-csharp/src/MathCursor.Core/Lattice/LatexRenderer.cs` | Émission LaTeX (à étendre) |
| `core-csharp/tests/MathCursor.Core.Tests/Lattice/LexerTests.cs` | Tests à compléter |
| `core-csharp/tests/MathCursor.Core.Tests/Lattice/LatexRendererTests.cs` | Tests à compléter |
| `core-csharp/tests/MathCursor.Core.Tests/Lattice/LatticeEngineTests.cs` | Tests end-to-end |
| `adapter-vsto/src/MathCursor/UI/WpfMathAdapter.cs` | Rendu popup (vérif uniquement) |
| `data/ner-corpus/extension_v5_quant_letters.jsonl` | Référence du format JSONL si v6 ajouté |

## 7. Ce qu'il NE faut PAS faire

- ❌ Réinterpréter `->` (déjà utilisé dans `lim x -> 0`). Le brief
  ne touche QUE `=>` et `<=>` et leurs variantes.
- ❌ Tokeniser `<=` ou `>=` autrement — ces opérateurs existent déjà,
  l'ajout `<=>` ne doit pas les casser. Bien tester l'ordre greedy §3.1.
- ❌ Modifier le NER pour tagger explicitement `=>` / `<=>` côté zone-detection
  hors d'un contexte math. Le NER détecte la zone math entière, ces
  séquences à l'intérieur ne demandent pas un tag dédié — elles sont juste
  des caractères dans la zone.
- ❌ Renommer ou retirer les tokens `LEQ`, `GEQ`, `LT`, `GT`, `EQ`
  existants. Juste ajouter les nouveaux à côté.
- ❌ Embarquer `\implies` / `\iff` (LaTeX wide arrows). On utilise les
  versions standard `\Rightarrow` / `\Leftrightarrow` qui sont supportées
  partout y compris dans WPF-Math.
- ❌ Toucher à l'algorithme de conversion (LatticePathFinder, Dijkstra…).
  L'ajout est purement vocabulaire/lexer/renderer.

## 8. Validation

1. `dotnet build MathCursor.sln` → 0 erreur, 0 warning nouveau.
2. `dotnet test core-csharp/tests/MathCursor.Core.Tests/` → tous les tests
   passent, incluant les nouveaux du §5.
3. Test manuel dans Word des 4 phrases du §5.4.
4. ADR créé dans `docs/dev/decisions/`.
5. (Optionnel) Si corpus NER v6 ajouté, retrain notebook en suivant le
   pattern brief v5.

## 9. Estimation

| Tâche | Durée |
|-------|-------|
| Lexer (3 patterns greedy + Unicode) | 1-2 h |
| Vocabulary (3 tokens) | 30 min |
| Parser (intégration relations) | 1 h |
| LatexRenderer (3 macros) | 30 min |
| Tests xUnit (lexer + renderer + e2e) | 2-3 h |
| Test manuel Word + ADR | 1 h |
| **Total estimé** | **~1 jour** |

Si NER v6 jugé nécessaire : +0.5 jour pour le script + retrain Colab.
