# Feat — Ruban revient dans TabHome + pane pivote vers galerie d'exemples

**Date :** 2026-05-06
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** [`2026-05-05-Feat-ribbon-refactor-cheatsheet.md`](2026-05-05-Feat-ribbon-refactor-cheatsheet.md)

## Contexte

La 0.5.6 a livré un onglet ruban dédié `MathCursor` + un pane « Cheatsheet »
listant ~24 raccourcis (steno → symbole). Le résultat visuel est décevant :

- Onglet quasi-vide (2 boutons large icon) — ça fait pro-vide, pas pro.
- Cheatsheet de raccourcis = **contre-narratif** : la promesse produit est
  *« tape comme tu penses, MathCursor reconnaît »*. Lister `bbR → ℝ`,
  `racine x+1 → √(x+1)` suggère une syntaxe à mémoriser, alors que
  l'utilisateur peut écrire `R` (NER désambiguïse) ou `racine x` /
  `sqrt x` / `√ x` indifféremment.
- Tentation de remplir le ruban par une *galerie d'insertion rapide* (boutons
  symboles) écartée pour la même raison : un bouton « π » ramène l'utilisateur
  au modèle palette MathType, pas au modèle MathCursor (flow clavier).

## Décision

### Ruban — retour au group dans `TabHome`

```
[Accueil ▼ … MathCursor (group dans Accueil) …] [Insertion] …
                  └── [Exemples] (toggle pane)
                  └── [Signaler un souci]
```

- Le group `MathCursorGroup` revit dans l'onglet `TabHome` (pré-0.5.6).
- 2 boutons large icon : **Exemples** (renommé depuis « Cheatsheet ») +
  **Signaler**. Pas de bouton À propos / Aide (même décision que la 0.5.6).
- Pas d'onglet dédié pour l'instant : moins de chrome, plus discret. Si la
  liste de fonctionnalités grandit (réglages, alias, préférences), on rouvrira
  la question.
- Icônes laissées en `imageMso` stock (`HelpIndex`, `WarningOriginal`) : un
  passage design dédié les remplacera plus tard (hors scope de cette ADR).

### Pane — galerie d'exemples concrets, multi-syntaxes, lecture seule

Le pane n'est plus une liste de raccourcis isolés mais une **galerie
d'exemples concrets organisés par sujet** :

```
┌─ Vecteur AB ────────────────────────────────────────┐
│  Tape :   vec AB                                    │
│           AB                                        │
│           vecteur AB                                │
│  Rendu :  ⃗AB                                       │
└──────────────────────────────────────────────────────┘
```

- Une entrée = **1 titre concret** (« Vecteur AB », « Système 2×2 »,
  « Dérivée d'un produit »…) + **N syntaxes équivalentes empilées** + **1
  rendu WpfMath**.
- **Lecture seule** : pas de click-to-insert. Le user lit, retape lui-même
  dans son doc. Justification : un click-to-insert recasse le flow (pose un
  OMath au mauvais endroit) et entérine une syntaxe canonique unique, ce qui
  contredit la promesse de flexibilité.
- **Catégories pédagogiques** (taxonomie lycée, pas LaTeX) :
  Équations simples, Systèmes, Géométrie, Fonctions et dérivées,
  Limites, Probabilités, Ensembles et logique.
- **Recherche** en haut, filtrage temps réel sur titre + tous les `stenos[*]`
  + tags.
- **Pied de pane** : bouton « Il manque quelque chose ? » conservé.

### Schéma `cheatsheet.json` v2 (breaking — pas de back-compat)

```jsonc
{
  "schema_version": 2,
  "categories": [
    {
      "id": "vectors",
      "label_fr": "Géométrie",
      "label_en": "Geometry",
      "order": 3,
      "entries": [
        {
          "title_fr": "Vecteur AB",
          "title_en": "Vector AB",
          "stenos": ["vec AB", "AB", "vecteur AB"],
          "rendered_latex": "\\vec{AB}",
          "tags": ["vector", "geometry"]
        }
      ]
    }
  ]
}
```

Diffs vs v1 :
- `entry.steno` (string) → `entry.stenos` (array de string).
- Ajout de `entry.title_fr` / `entry.title_en` (titre concret affiché).
- `schema_version: 1` → `2`. Pas de migration runtime ; le JSON embarqué est
  réécrit en v2, et `CheatsheetData.Parse` valide `schema_version == 2`.

### Stack technique conservée

Reste inchangé depuis la 0.5.6 :

- Rendu math : **WpfMath** (cf. ADR 24-04 popup-revert-wpfmath).
- Persistance : **IsolatedStorage** (largeur, ouvert/fermé, collapse par
  catégorie). `CheatsheetState.SchemaVersion` reste à 1 — la persistance n'a
  pas changé de structure, seul le JSON statique a évolué.
- JSON embarqué via `EmbeddedResource`.
- Bouton « Il manque quelque chose ? » → préfill `[missing_shortcut]` dans le
  feedback existant.

### Hors scope (= pas de page web `/cheatsheet`)

L'ADR retracté prévoyait une page `/cheatsheet` côté docs site (SEO +
impression). On la **différencie** de cette ADR : la décision sera reprise à
part une fois le contenu du pane stabilisé. La logique « contenu = source de
vérité unique JSON » reste valide pour quand on s'en occupera.

## Pourquoi

- L'utilisateur a explicitement rejeté à la fois (i) la galerie d'insertion
  rapide *« le problème de l'insertion rapide c'est qu'on contraint
  l'utilisateur dans un schéma de pensée »* et (ii) le rendu visuel de
  l'onglet dédié *« je trouve ça bien vilain visuellement, le ruban est
  vide »*.
- Les exemples concrets multi-syntaxes répondent à *« comment montrer la
  richesse ? »* sans imposer une syntaxe canonique unique — ils
  **démontrent** la flexibilité au lieu de la **prescrire**.
- Lecture seule = cohérence avec le flow clavier. L'utilisateur lit, comprend
  qu'il peut écrire de plusieurs façons, retape lui-même dans le doc.

## Conséquences

- `Ribbon.xml` : group dans `TabHome` au lieu d'onglet dédié. Bouton
  `CheatsheetButton` → `ExamplesButton` (label « Exemples »).
- `RibbonCallback.cs` : `OnGetTabLabel` / `OnGetToolsGroupLabel` supprimés ;
  retour à `OnGetGroupLabel`. `OnGetCheatsheetButton*` → `OnGetExamplesButton*`.
- `Strings.cs` : libellés FR/EN renommés.
- `cheatsheet.json` réécrit en v2.
- `CheatsheetEntry.Steno` (string) → `Stenos` (string[]) + `TitleFr`/`TitleEn`.
- `CheatsheetViewModel.MatchesQuery` filtre sur `Title*` + tous `Stenos[i]` +
  `Tags`.
- `CheatsheetPane` : layout d'une entrée passe de 2 colonnes (steno + rendu)
  à un bloc vertical (titre + bloc multi-stenos + rendu).
- `ToggleCheatsheetPane` reste, juste renommé en `ToggleExamplesPane` pour
  cohérence interne.
- Tests xUnit adaptés (CheatsheetData, CheatsheetViewModel).
- Persistance utilisateur : pas de migration nécessaire (le state ne dépend
  pas du JSON contenu).

## Validé par l'utilisateur

> « le problème de l'insertion rapide c'est qu'on contraint l'utilisateur
> dans un schéma de pensée.. l'idée de mathcursor est d'être dans le flow…
> et s'adapter.. vec AB ou AB + désambiguisation vecteur produisent le
> même résultat »

> « j'aime bien l'idée d'avoir des exemples visibles.. je pense que ce
> serait bien de remettre le menu math cursor dans l'accueil comme avant
> du coup.. et remettre l'icone qui avait sauté. le pane sur le coté
> deviendrait des listes d'exemples.. concrets : une équation / de la
> géométrie / des systèmes etc le tout avec plusieurs syntaxes visibles
> si possible ? »

> Q1 (catégories pédagogiques) : « oui »
> Q2 (click sur une card = ?) : « A » (lecture seule)
> Q3 (icône custom) : « on s'en fout, on va remettre des icônes après »

## Liens

- Brief retracté : [`docs/dev/briefs/2026-05-05-ribbon-refactor-cheatsheet.md`](../briefs/2026-05-05-ribbon-refactor-cheatsheet.md)
  (à mettre à jour quand on rouvre le sujet page web).
- ADR retracté : [`2026-05-05-Feat-ribbon-refactor-cheatsheet.md`](2026-05-05-Feat-ribbon-refactor-cheatsheet.md)
- ADR popup WpfMath : [`2026-04-24-Feat-popup-revert-wpfmath.md`](2026-04-24-Feat-popup-revert-wpfmath.md)
- ADR feedback Cloudflare : [`2026-04-30-Feat-feedback-form-cloudflare-backend.md`](2026-04-30-Feat-feedback-form-cloudflare-backend.md)
