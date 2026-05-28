# Legacy Engine Archive - 2026-05-28

**Date d'archivage** : 2026-05-28  
**Commit source** : `4c70adc` (Refactor Ch4 Phase D-6 : BASCULE FRANCHE BuildDefault → RewriteEngine)  
**Remplacé par** : `RewriteEngine` (Phase D-6)

---

## Contexte

Ce dossier contient le **moteur legacy** de MathCursor, remplacé par le nouveau système de **Rewriting** (Phase D-6).

### Décision d'archivage
- **Bascule franche** : Le `RewriteEngine` est désormais le moteur par défaut via `MathEngine.BuildDefault()`
- **Simplification** : Suppression de la dualité legacy/rewriting pour réduire la complexité
- **Maintenabilité** : Le code legacy n'est plus maintenu et bloque l'évolution de l'architecture

### Composants archivés

| Dossier | Contenu | Remplacé par |
|--------|---------|---------------|
| `Collision/` | Détecteurs de collisions (SlurpFraction, DotVec, etc.) | Règles Rewriting + PrimitiveRules |
| `Emit/` | LatexEmitter, TemplateEmitter | RewriteMatcher + EmitTemplate |
| `Parsing/` | StackParser, ListCombinator | RewriteEngine (parsing intégré) |
| `Resolution/` | PreResolvers, ContextScorer, etc. | RewriteEngine (résolution intégrée) |

### Fichiers modifiés dans le core

- **MathEngine.cs** : 
  - Suppression du constructeur legacy `MathEngine(vocab, rules)`
  - Suppression de `BuildDefaultLegacy()`
  - Suppression du main loop legacy dans `Resolve()`
  - Suppression des champs : `_tokenizer`, `_matcher`, `_parser`, `_flatEmitter`, `_detectors`, `_preResolvers`
  - Conservation unique de `_rewriteEngine` et `_vocab`

---

## Restore (si nécessaire)

Pour restaurer le legacy :

1. Copier les fichiers de ce dossier vers `core-csharp/src/MathCursor.Engine/`
2. Restaurer `MathEngine.cs` depuis le commit `4c70adc`
3. Recompiler

```bash
git checkout 4c70adc -- core-csharp/src/MathCursor.Engine/MathEngine.cs
cp -r archive/legacy-engine-2026-05-28/* core-csharp/src/MathCursor.Engine/
```

---

## Statut des fonctionnalités

| Fonctionnalité | Legacy | RewriteEngine | Statut |
|---------------|--------|---------------|--------|
| Tokenization | ✅ | ✅ | Identique |
| Parsing | ✅ | ✅ | Rewriting-based |
| Fraction slurp | ✅ | ❌ | À implémenter en règles |
| DotVec | ✅ | ❌ | À implémenter en règles |
| LetterSupSub | ✅ | ⚠️ Partiel | `prim-letter-num-superscript` OK |
| Multi-line | ✅ | ❌ | À implémenter |

---

## Tests associés

Les tests legacy dans :
- `core-csharp/tests/MathCursor.Engine.Tests/` (certains)
- `adapter-vsto/tests/` (certains)

Doivent être migrés vers le nouveau système ou archivés.

---

## Notes techniques

### Points bloquants connus (2026-05-28)

1. **Detectors non portés** : Les détecteurs de collision (SlurpFraction, DotVec, etc.) ne sont pas encore implémentés en règles Rewriting. Cela peut causer des régressions sur :
   - `1/x+1` → devrait donner `rac{1}{x+1}` (slurp)
   - `u.v` → devrait donner `\vec{u} \cdot \vec{v}` (DotVec)
   - `u(1;2)` → devrait donner vecteur avec coordonnées

2. **Précédence des opérateurs** : Le legacy utilisait un système de tiers (`PrecedenceTier`) pour gérer la précédence. Le RewriteEngine utilise des phases de priorité.

3. **Compatibilité** : Certains cas edge (multi-line, align*) ne sont pas encore couverts.

### Migration recommandée

Pour les fonctionnalités manquantes, implémenter des règles dans :
- `PrimitiveRules.cs` (pour les primitives comme SlurpFraction)
- `data-v2/concepts/*.yml` (pour les règles sémantiques)

---

## Historique

| Date | Événement |
|------|----------|
| 2026-05-22 | Phase D-5 : RewriteEngine POC validé à 100% sur les tests YAML |
| 2026-05-26 | Phase D-6 : Bascule franche - `BuildDefault()` utilise RewriteEngine |
| 2026-05-28 | Archivage complet du legacy engine |
