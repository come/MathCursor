# math-addon — Office Add-in pour la notation mathématique

## Contexte

Add-in Word (Office.js) destiné à des lycéens, notamment avec PAP, pour prendre des cours de maths de façon fluide au clavier. L'objectif est zéro friction : on ne quitte jamais le flux d'écriture.

Cible prioritaire : **Word for the web** (`word.cloud.microsoft`) testé en sideloading local, puis Word desktop Windows.

---

## Stack

- **Office Add-in** — Task pane add-in
- **Vue 3 + TypeScript** (Composition API, `<script setup>`)
- **Office.js** (WordApi 1.7+)
- **Vite** comme bundler
- Pas de framework CSS — variables CSS natives uniquement
- Pas de dépendances lourdes

---

## Architecture

```
math-addon/
├── manifest.xml
├── src/
│   ├── taskpane/
│   │   ├── index.tsx
│   │   ├── App.tsx           # composant racine, gère les deux modes
│   │   ├── MathEditor.tsx    # mode notation (ghost text + autocomplete)
│   │   ├── GraphEditor.tsx   # mode graphe (canvas repère)
│   │   └── patterns.ts       # table des patterns de notation
│   └── commands/
│       └── commands.ts
└── assets/
```

---

## Règles de dev

- **Pas de `localStorage`** — tout en state React
- **Pas de `position: fixed`** — la task pane est une iframe
- **Tab** toujours intercepté en `{ capture: true }` sur `document`
- Les patterns vivent dans `patterns.ts`, jamais inline dans les composants
- Chaque insertion Word dans un `Word.run(async context => { ... await context.sync() })` avec try/catch
- Le canvas GraphEditor est un `<canvas>` natif, pas WebGL
- Pas de MathJax/KaTeX — OMath natif Word uniquement

---

## Ce qu'on ne fait PAS

- Pas de VBA
- Pas de VSTO
- Pas d'extension Chrome (Word for the web first)
- Pas de backend
- Pas de MathJax / KaTeX

---

## Ordre d'implémentation

```
1. Scaffold + manifest + sideloading OK sur word.cloud.microsoft
2. MathEditor : ghost text + Tab capture + patterns vec / frac / exposant
3. Insertion OMath dans Word
4. Picker multi-choix (↑↓)
5. Tous les patterns de la table
6. GraphEditor : canvas repère + points
7. Export SVG → Word
8. Panel courbes multiples
9. Polish
```
