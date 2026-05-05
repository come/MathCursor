# Feat — Refonte du ruban : ajout d'un panneau Cheatsheet

**Date :** 2026-05-05
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Contexte

Le ruban MathCursor n'expose qu'un seul bouton aujourd'hui (« Signaler un
souci »). Les raccourcis steno ne sont **pas découvrables** sans lire la
doc en ligne — friction pour les nouveaux utilisateurs (élèves PAP,
profs beta-testeurs).

On veut ajouter un panneau **Cheatsheet** consultable pendant la frappe,
sans casser le flux Word, et conserver le bouton de feedback existant.

Cf. brief : [`docs/dev/briefs/2026-05-05-ribbon-refactor-cheatsheet.md`](../briefs/2026-05-05-ribbon-refactor-cheatsheet.md)

## Décision

### Ruban — onglet dédié `MathCursor`

```
[Accueil] [Insertion] ... [MathCursor]  ← nouvel onglet
                          └── Group "Outils"
                              ├── [Cheatsheet] (toggle pane)
                              └── [Signaler un souci] (existant)
```

- Onglet top-level dédié (vs ancien group inséré dans TabHome) : laisse
  de la place pour futures features (settings, alias, préférences).
- 2 boutons large icon dans le group `Outils`, i18n FR/EN.
- Le bouton `Aide` (MessageBox actuel) est supprimé : son contenu migre
  dans le pane Cheatsheet (section dédiée raccourcis clavier).
- Pas de menus déroulants : 1 clic = 1 action.

### Cheatsheet — Task Pane WPF (CustomTaskPane)

- **Toggle** au clic sur le bouton ruban (ouvre/ferme).
- **Non modal** : la frappe dans le doc continue.
- **Catégories repliables** (header cliquable + chevron expand/collapse).
  Auto-expand sur match de recherche.
- **Recherche en haut**, filtrage temps réel sur steno + libellé +
  tags.
- **Pied de panneau** : bouton « Il manque quelque chose ? » (préfill
  `type: "missing_shortcut"` dans le JSONL feedback existant).
- **Pas de bouton Imprimer** : remplacé par une page web `/cheatsheet`
  côté docs site, printable via le navigateur.

### Rendu math : WpfMath

- Cohérence avec la popup de suggestion (cf. ADR 24-04
  popup-revert-wpfmath).
- Fallback texte plain si certains rendus complexes pètent (matrices,
  intégrales doubles).

### Persistance : IsolatedStorage

- Largeur du pane, état ouvert/fermé, état expand/collapse de chaque
  catégorie → stockés en `IsolatedStorage` (transverse à tous les docs).
- Pas de `CustomXMLPart` (lié au doc, perdrait la persistance entre
  docs).

### Source de données : `cheatsheet.json` embarqué

- Un seul fichier JSON, source de vérité unique.
- Schéma `categories[].entries[]` avec `steno`, `rendered_latex`, `tags`,
  `note`.
- Exploitable côté add-in (pane Word) **et** côté docs (page web statique).

### Page web `/cheatsheet` (bonus SEO + impression)

- HTML statique généré à partir du même `cheatsheet.json`.
- Layout 2 colonnes, `@media print` pour impression A4 propre.
- Indexable Google → SEO bonus (« raccourci word racine carrée » →
  cheatsheet MathCursor).

## Tradeoff

- **Pro** : découvrabilité majeure pour les nouveaux utilisateurs ;
  l'ergo « pane à côté du doc, je tape, je regarde » correspond au flux
  réel d'un cours de maths.
- **Pro** : page web `/cheatsheet` apporte du trafic organique sans
  effort additionnel (même source JSON).
- **Pro** : zéro dépendance NuGet ajoutée (WpfMath déjà présent,
  IsolatedStorage en .NET Framework standard).
- **Con** : maintenance du JSON dans la durée — chaque nouveau raccourci
  produit demande une mise à jour de la cheatsheet pour rester
  synchrone. Mitigation : le bouton « Il manque quelque chose ? »
  collecte les manques user, on bumpe le JSON à intervalle régulier.

## Validé par l'utilisateur

> « pas de pdf en effet ! »
>
> « rendu math dans le pane avec wpf math evidement »
>
> « 2 c pour moi » (= catégories repliables pour la densité 1080p)
>
> « 3 persistance isolated » (= IsolatedStorage pour la persistance)
>
> « pour moi y'a un onglet complet MathCursor qui arrive et on met tout
> dedans non ? » (= onglet ribbon dédié au lieu d'un group dans TabHome,
> et migration du contenu Aide dans le pane Cheatsheet)

## Plan d'implémentation (6 étapes)

1. **Refacto du ruban** : `Ribbon.xml` passe d'un group dans `TabHome`
   à un **onglet dédié `MathCursor`** avec un group `Outils` contenant
   2 boutons (Cheatsheet, Signaler). `RibbonCallback.cs` ajoute les
   callbacks Cheatsheet ; le code Aide (MessageBox) est supprimé.
   `Strings.cs` étendu avec les nouveaux libellés FR/EN. Stub
   Cheatsheet = MessageBox « Coming soon » pour l'instant.
2. **Cheatsheet pane statique** : JSON + rendu WpfMath par entrée +
   scroll. Persistance largeur via `IsolatedStorage`.
3. **Catégories repliables** : header cliquable avec chevron, état
   persisté par catégorie.
4. **Recherche + filtrage** dans le pane. Auto-expand des catégories
   qui ont des matchs.
5. **Bouton « Il manque quelque chose ? »** : réutilise infra Signaler,
   préfill `type: "missing_shortcut"`.
6. **Page web `/cheatsheet`** : génération à partir du JSON, layout 2
   colonnes, `@media print`.

Étapes 1-4 = MVP fonctionnel. 5 = polish feedback. 6 = SEO / impression.

## Hors scope

- Settings utilisateur (alias custom, comportement) — itération
  ultérieure.
- Cloud sync, édition utilisateur de la cheatsheet, catégorie « Mes
  raccourcis ».
- Génération PDF côté add-in (remplacée par page web).

## Liens

- Brief : [`2026-05-05-ribbon-refactor-cheatsheet.md`](../briefs/2026-05-05-ribbon-refactor-cheatsheet.md)
- ADR popup WpfMath (rendu math du pane) : [`2026-04-24-Feat-popup-revert-wpfmath.md`](2026-04-24-Feat-popup-revert-wpfmath.md)
- ADR feedback Cloudflare (réutilisé par « Il manque quelque chose ? ») :
  [`2026-04-30-Feat-feedback-form-cloudflare-backend.md`](2026-04-30-Feat-feedback-form-cloudflare-backend.md)
- Code existant : `adapter-vsto/src/MathCursor/Ribbon.xml`,
  `adapter-vsto/src/MathCursor/RibbonCallback.cs`
