# Brief — Normaliser les opérateurs Unicode → ASCII en entrée du lattice

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-29
**Branche :** `lattice-engine`
**Public cible :** agent C#/lattice autonome qui ne connaît pas le projet.

---

## 1. Le bug observé

Quand l'utilisateur tape ou colle dans Word une formule contenant des
caractères math Unicode (`≥`, `≤`, `≠`, `±`, `×`, `÷`, `−`…), le lattice
engine plante ou produit un rendu incorrect. Reproduit aujourd'hui sur la
démo web : `forall x in R, x^2 ≥ 0` → erreur moteur. La même chaîne avec
`>=` ASCII rend correctement.

Cas d'usage concrets :
- Élève qui colle un énoncé tapé en Unicode par son prof.
- Élève sur clavier AZERTY étendu qui tape directement `≥` (AltGr+,).
- Texte importé d'un PDF / d'un site : `−` (minus Unicode U+2212) au lieu
  du `-` ASCII.

## 2. Cause racine

`LatticeEngine.NormalizeUnicodeSubSup` (dans
`core-csharp/src/MathCursor.Core/LatticeEngine.cs`) ne gère **que** les
superscripts (`²` `³`…) et subscripts (`₂` `₃`…). Les opérateurs Unicode
math classiques ne sont pas traités → le Lexer voit un caractère qu'il ne
sait pas tokeniser.

## 3. Ce qu'on veut

Étendre la fonction de normalisation pour mapper les caractères Unicode
math courants vers leur équivalent ASCII attendu par le Lexer.

### 3.1. Mappings prioritaires (V1)

| Unicode | Codepoint | ASCII | Justification |
|---------|-----------|-------|---------------|
| `≥` | U+2265 | `>=` | clavier étendu, copy-paste fréquent |
| `≤` | U+2264 | `<=` | idem |
| `≠` | U+2260 | `!=` | idem |
| `±` | U+00B1 | `+-` | très fréquent en physique |
| `×` | U+00D7 | `*` | clavier numérique étendu |
| `÷` | U+00F7 | `/` | idem |
| `−` | U+2212 | `-` | minus Unicode (souvent dans les PDF) |
| `–` | U+2013 | `-` | en-dash (autocorrection Word fréquente) |
| `—` | U+2014 | `-` | em-dash (idem) |
| `⋅` | U+22C5 | `*` | dot operator (multiplication math) |
| `·` | U+00B7 | `*` | middle dot (variante) |

### 3.2. Mappings secondaires (V2, à voir si pertinent)

Si on veut aller plus loin (et accepter que l'utilisateur copie-colle un
énoncé entièrement Unicode-math) :

| Unicode | ASCII | Note |
|---------|-------|------|
| `→` | `->` | flèche limite |
| `↦` | `->` | mapsto |
| `⇒` | `=>` | déjà couvert par brief implication-arrows |
| `⇔` | `<=>` | idem |
| `∈` | ` in ` | « x ∈ R » → « x in R » |
| `∉` | ` notin ` | (option) |
| `∀` | `forall ` | espace après car suivi de variable |
| `∃` | `exists ` | idem |
| `∞` | `+inf` | infini |
| `∑` | `sum ` | sigma |
| `∫` | `int ` | intégrale |
| `∏` | `prod ` | pi product |
| `√` | `sqrt ` | racine |
| `∂` | `partial ` | dérivée partielle |
| `∪` | `U` | union (interagit avec brief intervalles) |
| `∩` | `inter` | intersection |
| `π` | `pi` | déjà reconnu via Vocabulary mais utile en normalize |
| `α` `β` `γ` `δ` `θ` `λ` `μ` `σ` `φ` `ω`… | `alpha` `beta`…  | grec → token |

V2 est optionnel et peut être un brief séparé. **Ne pas l'embarquer dans
v1 si pas testé** — risque d'effets de bord (un `α` dans un mot non-math
serait remplacé).

## 4. Architecture

### 4.1. Renommer ou ajouter ?

Deux options :

1. **Étendre `NormalizeUnicodeSubSup`** : ajouter les nouveaux mappings
   dans le même `switch`. Pour chaque codepoint, on émet la séquence ASCII
   correspondante. Le test rapide `bool needs` au début doit aussi
   reconnaître les nouveaux caractères.
2. **Créer `NormalizeUnicodeOperators`** distinct, appelé après
   `NormalizeUnicodeSubSup`. Plus propre logiquement (sub/sup vs
   opérateurs binaires), un peu plus de boilerplate.

**Recommandation** : option 1 (extension du switch existant) tant que la
liste reste courte. Si V2 ajoute 50+ caractères, refactoriser en une
table de lookup `Dictionary<char, string>` + boucle.

### 4.2. Espaces autour ?

Pour un mapping comme `∈ → in`, il faut **ajouter un espace** avant et
après pour que le lexer le reconnaisse comme keyword distinct :
```
"x∈R"  →  "x in R"  (et pas "xinR")
```
Faire attention dans le mapping : émettre `" in "` avec espaces. Idem
pour `∀`, `∃`, `∑`, etc.

Pour les opérateurs binaires (`≥`, `≤`, `≠`, `±`…), pas besoin d'espace —
le lexer les reconnaît collés (`x≥0` → `x>=0` se lex correctement comme
`x` `>=` `0`).

### 4.3. Cas borderline : minus Unicode et tirets

- `−` (U+2212) est le minus mathématique propre. Doit toujours être
  remplacé par `-` (sans ambiguïté).
- `–` (en-dash U+2013) et `—` (em-dash U+2014) sont des artéfacts de
  l'autocorrection Word ("a -- b" → "a — b") et apparaissent souvent
  dans les copies. À mapper aussi vers `-`.
- `‐` (hyphen U+2010) idem.

Tous mappent vers `-` ASCII.

## 5. Livrables

1. **Code** : `core-csharp/src/MathCursor.Core/LatticeEngine.cs`
   - Étendre `NormalizeUnicodeSubSup` (V1) avec les mappings §3.1
   - Renommer en `NormalizeUnicode` si on prévoit V2
2. **Tests** : `core-csharp/tests/MathCursor.Core.Tests/Lattice/LatticeEngineTests.cs`
   ou un nouveau fichier `LatticeEngineNormalizeTests.cs` :
   - 1 test par mapping (`≥` → `>=` rendu correct)
   - 1 test combiné : `forall x in R, x² ≥ 0` rend `\forall x \in \mathbb{R}, x^2 \geq 0`
   - 1 test anti-régression : `²` continue de fonctionner
   - 1 test sur un mot non-math (`pavé` ne doit pas être altéré par le
     mapping de `é` — qui n'est PAS dans la liste, mais juste pour vérifier
     qu'on touche pas aux caractères non listés)
3. **ADR** : `docs/dev/decisions/2026-04-XX-Feat-unicode-operators-normalization.md`
   - Kind = Feat, Température = molle, Statut = acté
   - Citation utilisateur = ce brief

## 6. Cas de test obligatoires

### 6.1. Conversion (xUnit)

| Input | LaTeX rendu attendu (extrait) |
|-------|-------------------------------|
| `x ≥ 0` | `x \geq 0` |
| `x ≤ 5` | `x \leq 5` |
| `x ≠ 0` | `x \neq 0` |
| `x = 2 ± 1` | `x = 2 \pm 1` |
| `2 × 3 = 6` | `2 \times 3 = 6` ou `2 \cdot 3 = 6` |
| `12 ÷ 4 = 3` | `12 / 4 = 3` (ou `\frac{12}{4} = 3` selon parser) |
| `−x + 1 = 0` | `-x + 1 = 0` (minus Unicode → ASCII) |
| `forall x in R, x² ≥ 0` | `\forall x \in \mathbb{R}, x^2 \geq 0` |
| `f(x) ≤ g(x) ≠ h(x)` | `f(x) \leq g(x) \neq h(x)` |

### 6.2. Anti-régression

| Input | Sortie attendue |
|-------|-----------------|
| `x²` | `x^2` (sub/sup intacte) |
| `n₃` | `n_3` |
| `f(x) = 2x + 1` | inchangé (pas d'Unicode dedans) |
| `pavé est un mot` | inchangé (le `é` n'est PAS dans la liste de mappings) |

## 7. Pointers utiles

| Fichier | Rôle |
|---------|------|
| `core-csharp/src/MathCursor.Core/LatticeEngine.cs` | À modifier (méthode `NormalizeUnicodeSubSup`) |
| `core-csharp/src/MathCursor.Core/Lattice/Lexer.cs` | Lit la chaîne post-normalize ; doit déjà reconnaître `>=`, `<=`, `!=` (à vérifier) |
| `core-csharp/tests/MathCursor.Core.Tests/Lattice/` | Suite de tests existante (ajouter là) |
| `docs/dev/briefs/2026-04-29-implication-equivalence-arrows.md` | Brief lié — `=>`/`<=>` côté ASCII, complémentaire à ce brief côté Unicode |

## 8. Ce qu'il NE faut PAS faire

- ❌ Mapper des caractères qui changeraient le sens dans des mots non-math
  (ex : ne pas mapper les lettres accentuées `é`, `à`, `ç`, `è`…). Le
  lattice ne les voit pas comme math, mais une fois substituées, ça
  pourrait créer du bruit dans les tokens.
- ❌ Faire la normalisation **avant** que la string atteigne le moteur
  côté C#. Le NER voit le texte original avec ses Unicode, c'est OK —
  il sait reconnaître la zone math même avec des `≥`. Le mapping doit
  rester côté `LatticeEngine.Convert` après détection de zone.
- ❌ Embarquer V2 (lettres grecques, ∈, ∀, ∑…) dans la même PR que V1.
  V2 a beaucoup plus de surface d'effets de bord — séparer pour pouvoir
  ship V1 vite.
- ❌ Toucher au Lexer ou au Parser. Le mapping doit produire de l'ASCII
  conforme à ce que ces composants attendent déjà.

## 9. Validation

1. `dotnet build MathCursor.sln` → 0 erreur, 0 warning nouveau.
2. `dotnet test core-csharp/tests/MathCursor.Core.Tests/` → tous les tests
   du §6 passent.
3. Test manuel sur la démo web (`mathcursor.pages.dev/demo/`) :
   - Coller `forall x in R, x^2 ≥ 0` → conversion OK
   - Coller `−x ± 1 = 0` → conversion OK
   - Coller `x ≤ 5 ≠ 7` → conversion OK
4. Test manuel dans Word avec le MSI rebuilt :
   - Taper `x ≥ 0` (AltGr+, sur AZERTY) → Ctrl+Espace → conversion OK
5. ADR créé.

## 10. Estimation

| Tâche | Durée |
|-------|-------|
| Lecture `NormalizeUnicodeSubSup` + structure des tests | 30 min |
| Étendre la méthode + ajouter les 11 mappings V1 | 1 h |
| Tests xUnit (1 par mapping + combinés + anti-régression) | 1-2 h |
| Test manuel démo web + Word | 30 min |
| ADR + commit | 30 min |
| **Total estimé** | **~3-4 h** |

V2 (lettres grecques + symboles math complets) : prévoir ~1 jour
supplémentaire si décidé, mais éviter de mélanger les deux.
