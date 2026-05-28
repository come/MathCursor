# Guide d'Archivage Legacy Engine - Phase D-6

**Date** : 2026-05-28  
**Auteur** : Mistral Vibe  
**Commit source** : `4c70adc` (Refactor Ch4 Phase D-6 : BASCULE FRANCHE)  
**Statut** : ✅ Prêt pour exécution

---

## 📌 Résumé

Ce guide documente la **bascule complète** du moteur legacy MathCursor vers le **RewriteEngine** (Phase D-6).

### Ce qui a été fait

1. ✅ **MathEngine.cs nettoyé** : Suppression de tout le code legacy (≈350 lignes supprimées)
2. ✅ **Dossier d'archive créé** : `archive/legacy-engine-2026-05-28/`
3. ✅ **Script PowerShell créé** : `archive-legacy-engine.ps1`
4. ✅ **Documentation** : Ce fichier + README dans l'archive

### Ce qui reste à faire

1. ⏳ **Exécuter le script PowerShell** (1 commande)
2. ⏳ **Vérifier la compilation** (`dotnet build`)
3. ⏳ **Corriger les régressions** (si nécessaire)

---

## 🎯 Modifications Déjà Effectuées

### Fichier: `core-csharp/src/MathCursor.Engine/MathEngine.cs`

**Avant** (523 lignes) :
- Constructeur legacy : `MathEngine(vocab, rules)`
- Champ `_rewriteEngine?` (nullable)
- Propriété `UsesRewriteEngine`
- Méthode `Resolve()` avec **double branche** (legacy + rewriting)
- 8 champs legacy (`_tokenizer`, `_matcher`, `_parser`, etc.)
- 5 méthodes helpers legacy
- `BuildDefaultLegacy()`

**Après** (≈120 lignes) :
- Constructeur unique : `MathEngine(vocab, rewriteEngine)` (non-nullable)
- 2 champs seulement : `_vocab`, `_rewriteEngine`
- `Resolve()` simplifié : délégation directe au RewriteEngine
- `BuildDefault()` → `BuildDefaultWithRewriteEngine()`
- **Plus de code legacy**

**Diff** : -350+ lignes de complexité supprimées

---

## 📁 Fichiers à Archiver

### Dossiers complets (≈40-50 fichiers)

| Dossier | Sous-dossiers | Fichiers estimés | Statut |
|--------|---------------|------------------|--------|
| `Collision/` | `Detectors/` | 10 | ⏳ À archiver |
| `Emit/` | - | 3-5 | ⏳ À archiver |
| `Parsing/` | `List/` | 5-8 | ⏳ À archiver |
| `Resolution/` | `Signals/` | 10-15 | ⏳ À archiver |

### Liste complète des fichiers

#### Collision/
```
Collision/
├── CollisionContext.cs
├── CollisionScores.cs
├── EngineCandidate.cs
├── ICollisionDetector.cs
└── Detectors/
    ├── SlurpFractionDetector.cs
    ├── SlurpSupSubDetector.cs
    ├── LetterSupSubDetector.cs
    ├── VecLetterDetector.cs
    ├── DotVecDetector.cs
    ├── TripleUpperDetector.cs
    └── VectorCoordsDetector.cs
```

#### Emit/
```
Emit/
├── LatexEmitter.cs
├── TemplateEmitter.cs
└── IEmitter.cs (si existe)
```

#### Parsing/
```
Parsing/
├── StackParser.cs
└── List/
    ├── ListCombinator.cs
    └── (autres fichiers)
```

#### Resolution/
```
Resolution/
├── MultiLineBlockResolver.cs
├── PrefixMatchResolver.cs
├── IPreResolver.cs
├── GlobalContext.cs
├── ContextScorer.cs
├── ContextSnapshot.cs
├── MatchSignature.cs
├── ResolutionSidecar.cs
├── SidecarMerger.cs
├── SidecarSerializer.cs
├── SpanPin.cs
├── SpanOverride.cs
├── RulePin.cs
├── ZoomLevel.cs
└── Signals/
    ├── SidecarSignal.cs
    └── ParagraphResolutionsSignal.cs
```

---

## 🚀 Instructions d'Exécution

### Étape 1 : Exécuter le script PowerShell

Ouvrir **PowerShell en tant qu'administrateur** et exécuter :

```powershell
cd D:\Software\DocMath
.\archive-legacy-engine.ps1
```

**Ce que fait le script** :
1. Compte les fichiers à archiver
2. Copie chaque fichier `.cs` dans `archive/legacy-engine-2026-05-28/`
3. Remplace chaque fichier source par un **STUB** qui throw une exception si utilisé
4. Affiche un résumé

**Sortie attendue** :
```
Fichiers à archiver: 42

[Dossier] Collision
  ✓ CollisionContext.cs
  ✓ CollisionScores.cs
  ✓ EngineCandidate.cs
  ✓ ICollisionDetector.cs
  ✓ Detectors/SlurpFractionDetector.cs
  ...
  → 10 fichiers archivés

[Dossier] Emit
  ✓ LatexEmitter.cs
  ✓ TemplateEmitter.cs
  → 3 fichiers archivés

...

✅ ARCHIVAGE TERMINÉ!
Fichiers archivés dans: D:\Software\DocMath\archive\legacy-engine-2026-05-28
```

### Étape 2 : Vérifier la compilation

```bash
dotnet build core-csharp/src/MathCursor.Engine
```

**Attendu** : Build réussi (0 erreur)

Si des erreurs apparaissent, elles seront dues à :
1. Des fichiers qui utilisent encore les namespaces archivés
2. Des tests qui dépendent du legacy

### Étape 3 : Corriger les erreurs (si nécessaire)

#### Cas 1 : Erreur "The type or namespace 'X' could not be found"

**Solution** : Le fichier qui produce l'erreur utilise encore un namespace archivé.

1. Identifier le fichier et la ligne
2. **Option A** : Archiver aussi ce fichier (s'il est legacy)
3. **Option B** : Migrer le code pour utiliser RewriteEngine

#### Cas 2 : Erreur dans les tests

Les tests dans ces dossiers peuvent échouer :
```
core-csharp/tests/MathCursor.Engine.Tests/Emit/
core-csharp/tests/MathCursor.Engine.Tests/Parsing/
core-csharp/tests/MathCursor.Engine.Tests/Collision/
```

**Solution** : 
1. **Archiver les tests legacy** : `archive-legacy-engine.ps1` peut être modifié pour inclure les tests
2. **Migrer les tests** vers le nouveau système
3. **Supprimer les tests** si obsolètes

---

## 📊 Statut des Fonctionnalités

### ✅ Fonctionnelles avec RewriteEngine

| Fonctionnalité | Statut | Implémentation |
|---------------|--------|----------------|
| Parsing de base | ✅ | PrimitiveRules (Phase 0-1) |
| Functions (`sin x`) | ✅ | Règles YAML |
| Fractions (`a/b`) | ✅ | `prim-frac-implicit` |
| Superscript (`x^2`) | ✅ | `prim-superscript` |
| Subscript (`x_2`) | ✅ | `prim-subscript` |
| Parens (`(a+b)`) | ✅ | `prim-paren-group` |
| Addition/Soustraction | ✅ | `prim-add`, `prim-sub` |
| Implicit product (`2x`) | ✅ | `prim-implicit-product` |
| Letter+Number (`x2`→`x^2`) | ✅ | `prim-letter-num-superscript` |
| Signed infinity (`+∞`) | ✅ | `prim-signed-infinity` |

### ⚠️ Fonctionnalités non encore portées

| Fonctionnalité | Legacy | RewriteEngine | Priorité |
|---------------|--------|---------------|----------|
| **Slurp Fraction** (`1/x+1` → `\frac{1}{x+1}`) | ✅ | ❌ | ⭐⭐⭐ |
| **DotVec** (`u.v` → `\vec{u} \cdot \vec{v}`) | ✅ | ❌ | ⭐⭐⭐ |
| **Vector Coords** (`u(1;2)`) | ✅ | ❌ | ⭐⭐ |
| **Triple Upper** (`^ABC` → `\overrightarrow{ABC}`) | ✅ | ❌ | ⭐⭐ |
| **Multi-line** (`align*`, `cases`) | ✅ | ❌ | ⭐ |

**Solution recommandée** : Implémenter ces règles dans `PrimitiveRules.cs` ou créer des règles YAML dédiées.

---

## 🔄 Rollback (si nécessaire)

Pour restaurer le système legacy :

### Restauration complète
```bash
git checkout 4c70adc -- core-csharp/src/MathCursor.Engine/MathEngine.cs
cp -r archive/legacy-engine-2026-05-28/* core-csharp/src/MathCursor.Engine/
```

### Restauration d'un fichier spécifique
```bash
git checkout 4c70adc -- core-csharp/src/MathCursor.Engine/Collision/SlurpFractionDetector.cs
```

---

## 📋 Checklist de Validation

- [ ] Script PowerShell exécuté avec succès
- [ ] `dotnet build core-csharp/src/MathCursor.Engine` → SUCCESS
- [ ] `dotnet build adapter-vsto` → SUCCESS
- [ ] `dotnet test core-csharp/tests/MathCursor.Engine.Tests` → Tous les tests Rewriting passent
- [ ] Vérifier que `MathEngine.BuildDefault().Resolve("1+2")` fonctionne
- [ ] Vérifier que `MathEngine.BuildDefault().Resolve("frac 1 2")` fonctionne
- [ ] Vérifier que `MathEngine.BuildDefault().Resolve("lim x 0 f(x)")` fonctionne

---

## 📝 Notes Techniques

### Architecture Post-Archivage

```
core-csharp/src/MathCursor.Engine/
├── MathEngine.cs              # ✅ Nettoyé (RewriteEngine only)
├── EngineResult.cs            # ✅ Conservé (DTO autonome)
├── RewriteEngine.cs           # ✅ Nouveau moteur
├── PrimitiveRules.cs          # ✅ Règles de base
├── Rewriting/                 # ✅ Nouveau système
├── Rules/                     # ✅ Utilisé par Rewriting
├── Tokenization/              # ✅ Utilisé par Rewriting
├── Vocabulary/                # ✅ Utilisé par tout
├── Yaml/                      # ✅ Utilisé par Rewriting
└── [ARCHIVED] Collision/, Emit/, Parsing/, Resolution/
```

### Dépendances conservées

Ces composants sont **toujours utilisés** et ne doivent **pas** être archivés :

- `Tokenization/Tokenizer.cs` → Utilisé par `RewriteEngine`
- `Rules/RuleSpec.cs` → Utilisé par `RewriteRuleLoader`
- `Rules/RuleLoader.cs` → Utilisé par `RewriteRuleLoader`
- `Vocabulary/LocaleVocabulary.cs` → Utilisé partout
- `Rewriting/` → **Nouveau moteur**

---

## 🆘 Dépannage

### Erreur: "The type 'Token' could not be found"

**Cause** : `Token` est défini dans `Tokenization/Token.cs` qui doit être conservé.

**Solution** : Vérifier que `Tokenization/` n'a pas été archivé par erreur.

### Erreur: "The type 'RuleSpec' could not be found"

**Cause** : `RuleSpec` est dans `Rules/RuleSpec.cs` qui doit être conservé.

**Solution** : Vérifier que `Rules/` n'a pas été archivé.

### Erreur de compilation dans adapter-vsto

**Cause** : L'adapter utilise peut-être encore des classes legacy.

**Solution** : 
1. Chercher dans `adapter-vsto/` les imports de `Collision`, `Emit`, `Parsing`, `Resolution`
2. Migrer le code pour utiliser le nouveau système

---

## 📞 Support

Si tu rencontres des problèmes :

1. **Vérifie les logs de compilation** : `dotnet build --verbosity detailed`
2. **Consulte le README** dans `archive/legacy-engine-2026-05-28/`
3. **Restaure un fichier** avec `git checkout 4c70adc -- [fichier]`
4. **Demande à Mistral Vibe** : Fournis l'erreur exacte

---

**Dernière mise à jour** : 2026-05-28  
**Version** : 1.0  
**Phase** : D-6 (Bascule Franche)
