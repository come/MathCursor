# Icônes ruban — set sobre monochrome

Set d'icônes SVG sobres pour le ruban MathCursor (ADR
`2026-05-11-Feat-ribbon-home-duo-plus-dedicated-tab`).

## Style

- **ViewBox** : 24×24
- **Stroke** : `#333` (gris foncé), `stroke-width="1.5"`
- **Fill** : `none` sauf petits glyphes texte (qui héritent du `#333` en
  `fill` avec `stroke="none"`)
- **Linecap/Linejoin** : `round` (rendu doux)
- **Cohérence** : tous les éléments sont stroke-based, pas de gros aplats

## Inventaire

| Fichier | Bouton ruban | Concept visuel |
|---|---|---|
| `convert.svg` | Convertir | « fx » + flèche → |
| `columns-1.svg` … `columns-4.svg` | Colonnes 1-4 | Cadre + N-1 séparatrices verticales |
| `cheatsheet.svg` | Exemples (paused) | Page avec lignes |
| `sign-table.svg` | Tableau de signe (roadmap) | Grille 2×3 avec x / + / − |
| `variation-table.svg` | Tableau de variation (roadmap) | Cadre + courbe ↗ ↘ |
| `curve.svg` | Courbe (roadmap) | Repère + parabole |
| `figure.svg` | Figure géométrique (roadmap) | Triangle inscrit + cercle |
| `settings.svg` | Paramètres | Engrenage 8 dents |
| `report-bug.svg` | Signaler un bug | Bug ovale + pattes |
| `inspector.svg` | Inspecteur debug | Loupe |
| `about.svg` | À propos | « i » dans un cercle |

## Rasterisation pour VSTO

Le ruban VSTO (`Ribbon.xml`) consomme du **PNG**, pas du SVG. Pour
chaque icône, exporter en `Resources/<name>{16,32}.png` (16×16 pour
les boutons normaux, 32×32 pour les boutons large) puis référencer
via `getImage` callback dans `RibbonCallback.cs`.

Script de conversion à venir (cf. `build-cheatsheet-icon.ps1` pour
le pattern existant — Inkscape CLI ou ImageMagick).

Note : pour respecter la densité DPI, prévoir aussi 48×48 et 64×64
si on cible le ruban "Office hi-DPI".
