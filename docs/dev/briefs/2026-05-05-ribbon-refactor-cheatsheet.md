# Brief — Refonte du ruban MathCursor (ajout Cheatsheet)

**Date :** 2026-05-05
**Statut :** rédigé, en attente d'ADR

## Contexte

Le ruban MathCursor doit accueillir 2 entrées : **Cheatsheet** et **Signaler
un souci**. Aujourd'hui seul « Signaler un souci » existe. On garde la
philosophie produit : zéro friction, inline, pas de télémétrie, le prof
reste dans son flux Word.

## Objectifs

1. Découvrabilité des raccourcis steno via un panneau cheatsheet permanent
   et consultable pendant la frappe.
2. Conserver le flux de feedback déjà en place (JSONL local + bouton ruban).

## Ruban — structure cible

**Onglet dédié `MathCursor`** (= un nouvel onglet top-level dans le ruban
Word, à côté de Accueil / Insertion / etc.). On dégage du groupe inséré
dans TabHome pour migrer vers un tab complet — ça laisse de la place
pour futures features (settings, alias custom, préférences i18n
explicite, etc.).

```
[Accueil] [Insertion] ... [MathCursor]  ← nouvel onglet
                          └── Group "Outils"
                              ├── [Cheatsheet] (toggle pane)
                              └── [Signaler un souci] (existant)
```

- 2 boutons large icon dans le group `Outils`, libellés en français/anglais.
- Pas de split buttons, pas de menus déroulants : 1 clic = 1 action.
- Le bouton `Aide` (MessageBox actuel) est **supprimé** : son contenu
  (raccourcis clavier popup, logs path, comment ça marche) est migré
  comme section dédiée dans le pane Cheatsheet (étape 2-3 du dev).
- L'onglet n'est PAS activé par défaut (Word reste sur Accueil au
  chargement) — l'utilisateur clique pour l'atteindre, ou utilise Alt+Y
  (Word affecte une lettre auto à chaque tab).

---

## Cheatsheet — Task Pane Word

### Comportement

- Bouton ruban = **toggle** du task pane (ouvre/ferme).
- Task pane ancré à droite par défaut, redimensionnable, **non modal** :
  la frappe dans le doc continue normalement.
- État (ouvert/fermé, largeur) persisté entre sessions (stockage léger
  local, pas de Settings UI).
- Ouverture instantanée (contenu déjà rendu en mémoire au démarrage de
  l'add-in).

### Contenu — 8 catégories, 3 exemples chacune

Chaque entrée = **2 colonnes** : `steno tapé` → `rendu visuel` (réutiliser
le pipeline KaTeX du preview existant pour le rendu).

#### Fractions & puissances
- `1/2` → ½
- `x^2`, `x^{n+1}` → x², xⁿ⁺¹
- `racine x+1` → √(x+1)

#### Équations & systèmes
- `{ x+y=1 ; x-y=3` → système accolade
- `=` aligné multi-lignes
- `<=>`, `=>` → ⇔, ⇒

#### Géométrie
- `vec AB` → AB⃗
- `[AB]`, `(AB)`, `AB` → segment, droite, longueur
- `angle ABC` → ABĈ

#### Trigonométrie
- `cos²x`, `sin²x`
- `pi/2`, `2pi`
- `cos(a+b)`

#### Fonctions & dérivées
- `f'(x)`, `f''(x)`
- `f: x -> x^2` → f : x ↦ x²
- `f o g` → f ∘ g

#### Limites & suites
- `lim_{x->0}` → lim quand x→0
- `(u_n)` suite
- `+oo`, `-oo` → +∞, −∞

#### Probabilités & stats
- `P(A)`, `P(A|B)`
- `E(X)`, `V(X)`
- `bar x` → x̄

#### Ensembles & logique
- `R`, `N`, `Z`, `Q` → ℝ ℕ ℤ ℚ
- `appartient`, `inclus` → ∈, ⊂
- `[0;1]`, `]0;1[` intervalles

### Layout du panneau

```
┌─────────────────────────────┐
│  🔍 Rechercher              │
├─────────────────────────────┤
│  Fractions & puissances     │
│   1/2          → ½          │
│   x^2          → x²         │
│   racine x+1   → √(x+1)     │
├─────────────────────────────┤
│  Équations & systèmes       │
│   ...                       │
├─────────────────────────────┤
│  ...                        │
├─────────────────────────────┤
│  [Imprimer]  [Manque qqch?] │
└─────────────────────────────┘
```

- Recherche en haut, filtre **temps réel** sur steno + libellé catégorie
  + tags (cf. JSON ci-dessous).
- **Catégories repliables** : chaque header est cliquable (chevron
  expand/collapse). État de chaque catégorie (ouvert/fermé) persisté
  entre sessions. Par défaut, toutes ouvertes au 1er lancement.
- En recherche active : toutes les catégories qui ont des matchs
  s'ouvrent automatiquement, les autres restent fermées (peu importe
  leur état pré-recherche).
- **Pas de pagination, pas d'onglets** : tout visible/scrollable d'une
  traite, c'est le principe du cheatsheet.

### Pied de panneau

- Bouton **Il manque quelque chose ?** → ouvre directement le flux de
  signalement existant avec préfill `type: "missing_shortcut"` dans le
  JSONL, et un champ texte libre. Réutiliser l'infra « Signaler un
  souci ».
- Pas de bouton Imprimer (cf. décision plus bas) — l'utilisateur qui veut
  une version papier va sur la page web `/cheatsheet` du site et utilise
  l'impression du navigateur.

### Source de données

- Le contenu de la cheatsheet vit dans un fichier `cheatsheet.json`
  embarqué dans l'add-in (resource).
- Schéma :

```json
{
  "categories": [
    {
      "id": "fractions-powers",
      "label": "Fractions & puissances",
      "order": 1,
      "entries": [
        {
          "steno": "1/2",
          "rendered_latex": "\\frac{1}{2}",
          "tags": ["fraction", "demi"],
          "note": null
        }
      ]
    }
  ]
}
```

- Rationale : un seul fichier source de vérité, modifiable sans recompiler
  la logique métier, et exploitable pour générer aussi le PDF d'impression.

---

## Signaler un souci — existant, à harmoniser

- Conserver le comportement actuel (JSONL local rolling).
- Vérifier que le **point d'entrée depuis la cheatsheet** (« Il manque
  quelque chose ? ») arrive avec le bon `type` dans le JSONL pour pouvoir
  trier les feedbacks plus tard.

---

## Architecture

```
MathCursor.Vsto/
├── Ribbon/
│   ├── MathCursorRibbon.cs         (2 boutons : Cheatsheet, Signaler)
│   └── MathCursorRibbon.xml
├── Cheatsheet/
│   ├── CheatsheetPane.xaml         (WPF UserControl hosted in CustomTaskPane)
│   ├── CheatsheetPane.xaml.cs
│   ├── CheatsheetViewModel.cs      (filtrage, recherche)
│   ├── CheatsheetData.cs           (chargement JSON embarqué)
│   └── Resources/cheatsheet.json
└── Feedback/
    └── (existant, ajout d'un type "missing_shortcut")
```

---

## Page web `/cheatsheet`

- Page HTML statique sur le site `docs/cheatsheet.html`, générée à
  partir du même `cheatsheet.json` (script de build qui copie le JSON
  + génère les sections + intègre KaTeX pour le rendu math).
- Layout adapté à l'impression navigateur : 2 colonnes, sans header de
  nav, sans color, A4-friendly (`@media print`).
- Bénéfices vs PDF généré côté add-in :
  - Zéro dépendance NuGet (pas de PdfSharp).
  - Indexable par Google → SEO bonus (« raccourci word racine carrée » →
    cheatsheet MathCursor).
  - Accessible aux non-utilisateurs Windows / non-Word.
  - Maintenance unique : 1 source `cheatsheet.json` → pane Word + page web.
- Pas de lien dans le pane Word vers la page web (pas indispensable,
  l'utilisateur peut chercher s'il veut imprimer).

---

## Tests à ajouter

- **CheatsheetData** : chargement JSON, intégrité (chaque entrée a steno
  + rendered_latex non vides).
- **CheatsheetViewModel** : recherche filtrante (steno, label, tags) ;
  case-insensitive.
- **Page web `/cheatsheet`** : test visuel manuel post-deploy
  (responsive, impression A4 OK).

---

## Hors scope

- Settings utilisateur (alias, comportement) — différé à une itération
  ultérieure.
- Cloud sync, édition utilisateur de la cheatsheet, catégorie « Mes
  raccourcis ».
- Génération PDF côté add-in — remplacée par la page web `/cheatsheet`
  printable.

---

## Ordre de dev suggéré

1. **Refacto du ruban** : passer de 1 à 2 boutons, stub vide pour
   Cheatsheet.
2. **Cheatsheet pane statique** : JSON + rendu WpfMath + scroll, sans
   recherche, sans collapse, persistance largeur via `IsolatedStorage`.
3. **Catégories repliables** : header cliquable, état expand/collapse
   persisté.
4. **Recherche + filtrage** dans le pane (auto-expand des catégories
   avec matchs).
5. **Bouton « Il manque quelque chose ? »** (réutilise infra Signaler).
6. **Page web `/cheatsheet`** : génération à partir du même
   `cheatsheet.json`, layout 2 colonnes, `@media print`.

Étapes 1-4 = MVP fonctionnel utilisable au quotidien. 5 = polish
feedback. 6 = SEO / impression user.

---

## Décisions validées par l'utilisateur

1. **Pas de PDF côté add-in** : « pas de pdf en effet ». Remplacé par
   une page web statique `/cheatsheet` printable via le navigateur.
2. **Rendu math : WpfMath** : « rendu math dans le pane avec wpf math
   evidement ». Cohérence avec la popup (cf. ADR 24-04
   popup-revert-wpfmath). Fallback texte plain si certains rendus
   complexes pètent.
3. **Densité 1080p : catégories repliables** : « 2 c pour moi ». Header
   cliquable avec chevron, état expand/collapse persisté entre sessions.
   Auto-expand sur match de recherche.
4. **Persistance préférences : IsolatedStorage** : « 3 persistance
   isolated ». Largeur du pane + état ouvert/fermé + état des catégories
   stockés en `IsolatedStorage` (transverse à tous les docs).

## Validation utilisateur attendue

- L'utilisateur teste dans un cours réel : ouvre le pane, cherche
  « racine », trouve le raccourci, le tape dans Word. Pane reste ouvert,
  pas de friction.
- Bouton « Il manque quelque chose ? » : un beta-testeur l'utilise sur
  une notation manquante, le JSONL contient bien le `type:
  "missing_shortcut"` + texte libre.
- Bouton Imprimer : une page A4 s'imprime avec les 24 entrées lisibles,
  sans la barre de recherche ni les boutons.

## Liens

- ADR feedback report (existant) :
  [`2026-04-30-Feat-feedback-form-cloudflare-backend.md`](../decisions/2026-04-30-Feat-feedback-form-cloudflare-backend.md)
- ADR popup KaTeX → WpfMath (raison du choix WpfMath ici aussi) :
  [`2026-04-24-Feat-popup-revert-wpfmath.md`](../decisions/2026-04-24-Feat-popup-revert-wpfmath.md)
- Ribbon callback existant : `adapter-vsto/src/MathCursor/RibbonCallback.cs`
  + `adapter-vsto/src/MathCursor/Ribbon.xml`
