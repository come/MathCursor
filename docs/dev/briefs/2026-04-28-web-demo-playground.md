# Brief — Page web démo / playground MathCursor

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-28
**Branche :** `lattice-engine`
**Public cible :** agent web/.NET autonome qui ne connaît pas le projet.

---

## 1. Le besoin

Aujourd'hui, MathCursor n'est démontrable qu'en installant l'add-in VSTO
dans Word Desktop Windows : MSI à télécharger (102 Mo), certificat à
accepter, redémarrage Word. Friction énorme pour un curieux qui veut
juste *voir* à quoi ça ressemble — surtout en lycée où l'élève est sur
laptop perso, sur Mac, ou avec Word Online.

**Solution voulue :** une page statique sur `mathcursor.pages.dev/demo`
(ou `/playground`) avec un textarea + un panneau de preview en LaTeX
rendu. L'utilisateur tape une formule au clavier, voit la conversion
en live à droite. Zéro install, fonctionne sur tout browser desktop.

## 2. Périmètre — décisions tranchées

- **Pas de NER** dans la démo. Le visiteur sait qu'il "ne tape que des
  formules" → tout le contenu du textarea est traité comme une
  expression math, pas de zone-detection nécessaire.
- **Pas d'export OMath / Word** en sortie. Juste preview LaTeX rendu en
  HTML/MathML à l'écran. Si plus tard on veut "copier en OMath", ça sera
  un brief séparé.
- **Réutiliser le moteur** `core-csharp/Lattice/*` tel quel via Blazor
  WebAssembly. Pas de réécriture en TypeScript (anti-duplication).
- **Hébergement** : `docs/` (Cloudflare Pages déjà en place via ADR
  `2026-04-24-Feat-cloudflare-deployment.md`). Nouvelle page
  `docs/demo.html` (ou `docs/playground.html`).

## 3. Architecture

```
mathcursor.pages.dev/demo
    │
    ├── HTML/CSS : textarea + panneau preview, layout 2 colonnes
    │
    ├── JS : debounce input → invoke WASM
    │
    ├── Blazor WASM bundle (~3-5 Mo gzipped)
    │     └── exposes Convert(string) → ConvertResult { latex, errors }
    │             [wraps core-csharp LatticeEngine + LatexRenderer]
    │
    └── KaTeX (CDN) : render(latex) → HTML inline
```

### Stack précise

- **Moteur** : project Blazor WebAssembly minimal (`docs-demo/MathCursor.Demo.csproj`)
  qui référence `core-csharp/MathCursor.Core` (déjà `netstandard2.0`).
- **Bridge JS↔WASM** : `[JSInvokable]` sur une méthode statique
  `MathCursor.Demo.Bridge.Convert(string input)`.
- **Renderer LaTeX→HTML** : **KaTeX** (CDN `cdn.jsdelivr.net`,
  `katex.min.js` + `katex.min.css`, ~150 Ko gz). MathJax fonctionne aussi
  mais est plus gros et plus lent. KaTeX est le choix par défaut.
- **Build** : `dotnet publish -c Release` produit un dossier `wwwroot/`
  → copier les artefacts dans `docs/demo/_framework/` (Blazor sait
  émettre du static qu'on bundle dans Cloudflare Pages).

## 4. UX

### Layout
```
┌─────────────────────────────────────────────────────────┐
│   MathCursor — Démo                          [GitHub]   │
├──────────────────────────────┬──────────────────────────┤
│                              │                          │
│   Tape une formule           │   Aperçu                 │
│   ┌──────────────────────┐   │                          │
│   │ f(x) = 2x + 1        │   │      f(x) = 2x + 1       │
│   │                      │   │                          │
│   │                      │   │                          │
│   └──────────────────────┘   │                          │
│                              │                          │
│   Exemples : "lim x 0 sin    │                          │
│   x / x", "somme k 1 n k^2"  │                          │
└──────────────────────────────┴──────────────────────────┘
```

### Comportement
- **Live preview** : debounce 300 ms après dernière frappe → `Convert(input)`
  → render KaTeX. Pas de bouton "Convertir" — simplifie l'UX.
- **Empty state** : preview affiche un placeholder gris "Le rendu
  apparaîtra ici…".
- **Erreur de parse** : afficher le LaTeX brut en code style + message
  inline "Pas reconnu" (gris discret, pas rouge agressif). Ne pas casser
  la session.
- **Exemples cliquables** sous le textarea : 4-6 phrases qui injectent
  l'exemple dans le textarea quand on clique (pour montrer ce qui marche).
  Suggestions : `f(x) = 2x + 1`, `lim x 0 sin x / x`, `somme k 1 n k^2`,
  `int 0 1 x^2 dx`, `racine x+1`, `(a+b)^2 = a^2 + 2ab + b^2`.
- **Mobile** : layout responsive, textarea passe en pleine largeur,
  preview en dessous. Démo principalement desktop mais ne pas casser sur
  mobile.

### Lien depuis le site existant
Ajouter un bouton "Essayer en ligne" dans `docs/index.html` (à côté du
CTA "Télécharger") qui pointe vers `/demo`.

## 5. Livrables

1. **Projet Blazor WASM minimal** dans `docs-demo/` (à la racine du repo)
   ou dans `tools/web-demo/` (à l'agent de juger ce qui colle le mieux
   avec la convention du repo) :
   - `MathCursor.Demo.csproj` qui référence `core-csharp/MathCursor.Core`
   - `Program.cs` (host minimal Blazor WASM)
   - `Bridge.cs` avec `[JSInvokable] public static string Convert(string input)`
   - Fichier `wwwroot/index.html` (template Blazor) — peut être omis si
     on bundle les artefacts dans `docs/demo/`

2. **Page web démo** : `docs/demo.html` (ou `docs/playground.html`)
   - Layout 2 colonnes responsive
   - Textarea + preview KaTeX
   - Exemples cliquables
   - Style cohérent avec le reste du site (réutiliser `docs/style.css`)
   - i18n FR/EN via le pattern existant (`data-i18n` + `I18N` dict, voir
     `docs/releases.html` ligne 130-208 pour le pattern)

3. **Bundle WASM** copié dans `docs/demo/_framework/` (ou équivalent
   selon le mode publish Blazor).

4. **Lien depuis index.html** : ajouter `<a href="/demo">Essayer en ligne</a>`
   à côté du CTA download.

5. **Script de build** dans `tools/web-demo/build.sh` :
   - `dotnet publish docs-demo/MathCursor.Demo.csproj -c Release -o tmp/`
   - Copie ciblée des fichiers utiles dans `docs/demo/`
   - Exécuté en pré-déploiement (à intégrer ou laisser manuel pour
     l'instant)

6. **ADR** : `docs/dev/decisions/2026-04-XX-Feat-web-demo-playground.md`
   - Kind = Feat, Température = molle (réversible), Statut = acté
   - Citation utilisateur = ce brief
   - Mentionner Supersedes partiel de `archive/officejs-prototype/` ?
     Non — l'archive reste figée comme référence historique, la démo web
     est un produit séparé qui réutilise le core C#, pas le proto TS.

## 6. Cas de test obligatoires

Phrases qui doivent rendre correctement (à valider manuellement) :

```
f(x) = 2x + 1
lim x 0 sin x / x
somme k 1 n k^2
int 0 1 x^2 dx
racine x+1
(a+b)^2 = a^2 + 2ab + b^2
sin^2 x + cos^2 x = 1
frac a b
vec u + vec v
```

Cas de bord :
- Textarea vide → preview vide, pas d'erreur
- Texte non-math (`bonjour`) → preview affiche le texte tel quel ou
  message "Pas reconnu". Selon ce que `LatticeEngine.Convert` retourne
  pour une entrée non-math (à vérifier dans le code).
- Multi-line : si l'utilisateur tape sur plusieurs lignes, comportement
  à définir avec le code core. Probablement : on traite chaque ligne
  comme une expression et on rend le tout. Ou on concatène avec espace.
  Trancher en lisant `LatticeEngine.Convert` : que fait-il avec `\n` ?
  Si ça crashe → trim/split/loop côté Bridge.
- Caractère bizarre (emoji, char Unicode rare) : ne doit pas crasher,
  fallback affichage texte brut.

## 7. Pointers utiles

| Fichier | Rôle |
|---------|------|
| `core-csharp/src/MathCursor.Core/Lattice/Lexer.cs` | Lexer (à utiliser) |
| `core-csharp/src/MathCursor.Core/Lattice/Parser.cs` | Parser AST |
| `core-csharp/src/MathCursor.Core/Lattice/LatexRenderer.cs` | AST → LaTeX (sortie attendue par KaTeX) |
| `core-csharp/src/MathCursor.Core/Lattice/Vocabulary.cs` | Vocab keywords |
| `core-csharp/src/MathCursor.Core/MathCursor.Core.csproj` | netstandard2.0 confirmé compilable WASM |
| `docs/index.html` | Style et i18n du site (pattern à réutiliser) |
| `docs/style.css` | CSS global du site |
| `docs/wrangler.toml` | Config Pages (juste référence — la démo est statique) |
| `archive/officejs-prototype/` | **Référence visuelle UX uniquement**, NE PAS importer le code TS |

## 8. Ce qu'il NE faut PAS faire

- ❌ Réécrire le LatticeEngine en TypeScript / JS. Tout l'intérêt = zéro
  duplication. Si l'agent est tenté, c'est qu'il a mal lu §3.
- ❌ Embarquer le NER (DistilBERT 129 Mo). Décision tranchée § 2.
- ❌ Embarquer WPF-Math en JS. Pas portable. KaTeX est le rendu web.
  Si KaTeX ne supporte pas certaines commandes LaTeX émises par
  `LatexRenderer.cs`, lister les écarts et les remonter — adaptation
  côté Renderer ou côté Bridge selon le cas.
- ❌ Faire un build serveur. C'est une page statique 100% client-side.
- ❌ Stocker l'input utilisateur (analytics, telemetry, anything).
  Démo = 100% local. Aucune fuite réseau.
- ❌ Ajouter une auth, une session, ou un compte. C'est une page démo,
  on tape, on voit, on referme.
- ❌ Toucher à `archive/officejs-prototype/` (figé par règle CLAUDE.md).

## 9. Validation

1. `dotnet publish docs-demo/MathCursor.Demo.csproj -c Release` → succès,
   bundle généré.
2. Bundle taille raisonnable (<10 Mo gzipped, idéalement <5).
3. Local : `npx http-server docs/` → ouvrir `http://localhost:8080/demo.html`
   → page s'ouvre, WASM se charge, conversion live OK.
4. Tester les 9 cas du §6.
5. Tester depuis Chrome, Firefox, Safari (au moins 2 sur 3).
6. Mobile (DevTools simulation) : layout pas cassé.
7. Deploy via `tools/cloudflare/deploy.sh site` → vérifier
   `https://mathcursor.pages.dev/demo` (ou URL de preview).
8. Lien "Essayer en ligne" depuis `index.html` fonctionne.
9. ADR créé.

## 10. Estimations

| Tâche | Durée |
|-------|-------|
| Setup projet Blazor WASM + ref core-csharp | 0.5-1 jour |
| Bridge JSInvokable + premier "Convert" qui marche | 0.5 jour |
| Page HTML/CSS textarea + layout responsive | 1 jour |
| Intégration KaTeX + tests cas du §6 | 0.5 jour |
| Polish UX + exemples cliquables + i18n | 0.5 jour |
| Build script + intégration deploy.sh | 0.5 jour |
| **Total MVP** | **3-4 jours** |

Si KaTeX ne supporte pas certaines commandes LaTeX du `LatexRenderer`
(ex. `\widehat{}` custom, `\square` pour les holes…), prévoir +0.5 jour
pour adapter — soit côté Bridge (post-process LaTeX → KaTeX-compatible),
soit en remontant l'écart au renderer C# pour qu'il émette du LaTeX
"safe".
