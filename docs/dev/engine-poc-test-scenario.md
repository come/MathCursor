# P11 — Scénario de tests humain pour `MathCursor.Engine` v2

> Cf. ADR [`2026-05-22-Feat-engine-poc-isolation.md`](decisions/2026-05-22-Feat-engine-poc-isolation.md)

POC livré : **15 jalons** P11.0-P11.15 verts. Suite à exécuter pour valider
**en conditions réelles Word** que le drop-in fonctionne et que l'inspecteur
affiche la trace.

## Couverture tests automatisée déjà acquise

| Niveau | Verts | Cible |
|---|---|---|
| Engine v2 xUnit (P11) | **72/72** | smoke + tokenizer + parser + combinator + emitter + golden + collision + validator + shadow + perf |
| Engine v2 — shadow golden cases | **6/6** (100 %) | parité brief §5 sur limites + sommes |
| Engine v2 — perf re-parse 50 tokens | **0,13 ms** avg (cible < 5 ms) | brief §1.5 |
| Core legacy | **1266/1273** (= 6 rouges préexistants) | aucune régression P11 |
| Adapter VSTO | **393/393** | aucune régression P11 |

## Pré-requis humain

1. Build VSTO : `/build-iss` ou via Visual Studio.
2. Word ouvert avec l'add-in `MathCursor` chargé.
3. **Activer Engine v2** avant de lancer Word :

   ```powershell
   # PowerShell (avant de démarrer Word)
   [System.Environment]::SetEnvironmentVariable("MATHCURSOR_ENGINE_V2", "1", "User")
   ```

   Ou pour la session courante seulement :
   ```powershell
   $env:MATHCURSOR_ENGINE_V2 = "1"
   ```

   Pour **désactiver** : supprimer la variable ou `unset MATHCURSOR_ENGINE_V2`.

4. Activer l'inspecteur : ruban **MathCursor → Outils → Inspecteur de contexte**
   (= `ToggleContextInspectorPane`).

## Scénarios à dérouler dans Word

### 1. Sanity — Engine v2 actif

| Action | Attendu (logs inspector) |
|---|---|
| Taper `lim x 0 f(x)`, Ctrl+Espace | `engine-v2 source="lim x 0 f(x)"` puis `top="\lim_{x \to 0} f(x)"` puis `rule=limite-x-to-bound complete=True collisions=0` |
| Popup affiche | `\lim_{x \to 0} f(x)` rendu |
| Logs `LogDiag` | `engine-v2 enabled` au démarrage |

### 2. Source mutation préservée

| Action | Attendu |
|---|---|
| Taper `lim x->0 f(x)` | Inspecteur : `engine-v2 rule=limite-x-to-bound` + popup correcte |
| Vérifier que la frappe `->` est convertie en `\to` dans le rendu | Le LaTeX émis a `\to`, pas `->` |

### 3. Sommes / produits — concepts apparentés

| Source | Attendu top |
|---|---|
| `sum k 1 n (1/k)` | `\sum_{k=1}^{n} \frac{1}{k}` |
| `sum i 0 N (a_i)` | `\sum_{i=0}^{N} a_i` |
| `prod k 1 n k` | `\prod_{k=1}^{n} k` |

### 4. Fallback legacy quand l'Engine v2 ne sait pas

| Action | Attendu |
|---|---|
| Taper `V x R` (= forall belongs, hors POC v2) | Inspecteur : `engine-v2 empty → fallback legacy` puis le rendu vient du **pipeline legacy** (= templates patterns P10). Popup affiche `∀x ∈ ℝ` comme avant P11. |
| Taper `(a b ; c d)` | Engine v2 émet `\begin{pmatrix} a & b \\ c & d \end{pmatrix}` (= règle Engine v2 ? non, fallback legacy matrix template). Vérifier dans l'inspecteur d'où ça vient. |

### 5. Collision (= autocomplete IDE-style §2.4)

Le moteur v2 émet plusieurs candidats si plusieurs règles matchent. En POC,
seules `limites` et `sommes` sont actives, peu probable d'avoir des collisions
naturelles. Pour forcer une collision : ajouter une 2ᵉ règle YAML temporaire
qui shadowe `lim`.

### 6. Désactivation propre

| Action | Attendu |
|---|---|
| Quitter Word, supprimer env var `MATHCURSOR_ENGINE_V2` | Au redémarrage : pas de log `engine-v2 enabled`. Comportement strictement identique à avant P11. |

### 7. Inspecteur affichage Engine v2

Le pane debug doit afficher la trace **à chaque résolution** quand engine v2
est actif et le pane ouvert. Format attendu :

```
⟳ ENGINE V2  |  HH:mm:ss.fff  actif

engine-v2 source="lim x 0 f(x)"
engine-v2 top="\lim_{x \to 0} f(x)"
engine-v2 rule=limite-x-to-bound complete=True collisions=0
```

Si engine v2 n'a pas tourné sur la dernière résolution (= fallback ou OFF) :

```
⟳ ENGINE V2  |  HH:mm:ss.fff  inactif

(engine v2 inactive — pas branché ou pas tourné sur la dernière résolution)
```

## Critères de réussite POC

| # | Critère | Mesure |
|---|---|---|
| 1 | Golden cases brief sur limites + sommes | 100 % automatisé (6/6) ✓ |
| 2 | Perf re-parse 50 tokens | < 5 ms (0,13 ms acquis) ✓ |
| 3 | LOC engine pur (sans tests, sans data) | < 2 000 LOC — à compter post-livraison |
| 4 | Couplage code↔Core legacy | 0 dépendance ✓ (vérifiable par `dotnet list reference`) |
| 5 | Aucune régression Core/adapter | 1266+393 verts ✓ |
| 6 | Drop-in propre via feature flag | Fallback testé manuellement scénario §4 |
| 7 | Trace visible dans inspecteur | Scénario §7 |

## Sortie attendue

Tu peux choisir :
- **POC concluant** sur les 7 critères → ADR `Feat-engine-v2-promotion` + étendre
  concept par concept (analyse.yml, geometrie.yml, logique.yml, ensembles.yml).
- **POC non concluant** → ADR `Retracted-engine-poc` documentant les écarts. Le
  legacy n'a jamais bougé, suppression triviale (3 projets + 1 dossier
  `data-v2/` + 1 alias renommé + 1 ctor étendu).
