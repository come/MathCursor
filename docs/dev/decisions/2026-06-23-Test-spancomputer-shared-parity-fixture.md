# Test — Fixture partagée pour la parité SpanComputer C#↔JS

**Date :** 2026-06-23
**Kind :** Test
**Température :** molle
**Statut :** acté
**Lié à :** [2026-06-19-Feat-web-demo-real-mode-editor.md](2026-06-19-Feat-web-demo-real-mode-editor.md)

## Citation acté

> [audit 2026-06-22 — #5] parité par copie, pas par fixture partagée. « 5 go rapide » — utilisateur, 2026-06-23

## Contexte

`SpanComputer` (détection de la zone Ctrl+Espace) vit en double : `SpanComputer.cs`
(C#, autorité) et `spancomputer.js` (port JS de la démo web). Leurs tests étaient **deux
listes de cas recopiées à la main** (`SpanComputerTests.cs` 12 `[Fact]` ; `spancomputer.test.js`
12 cas inline). Rien ne forçait la synchro : un 13ᵉ cas ajouté d'un côté restait vert de
l'autre ; la doc avait déjà dérivé (« 13 cas » annoncés, 12 réels). Drift silencieux.

## Décision

Une **source unique** : `adapter-vsto/tests/MathCursor.Tests/Host/spancomputer-fixtures.txt`
(format `name|text|caret|expected`, `#`=commentaire), lue par les **deux** suites :
- C# : `SpanComputerTests` → `[Theory]` + `MemberData` (lecture depuis `AppContext.BaseDirectory`, copie via csproj).
- JS : `spancomputer.test.js` → `fs.readFileSync` du même fichier (chemin relatif depuis wwwroot).

Ajouter un cas = éditer **un** fichier → verrouillé des deux côtés.

Format **pipe-délimité** (pas JSON) : zéro dépendance des deux côtés (le projet hand-roll
déjà son parsing JSON ; net48 n'a pas `System.Text.Json` sans package). Contrainte : aucun
champ ne contient `|` (vrai pour ces cas ; signalé en tête de fichier).

Hors périmètre (pas « rapide ») : la parité **moteur natif == WASM** reste non testée —
vrai harnais à part.

## Tradeoff & alternatives écartées

- **Fixture JSON + `JavaScriptSerializer`/`System.Text.Json`** : JSON cohérent avec `fixtures.json`, mais System.Web.Extensions/package = friction net48 (binding redirects). Pipe-délimité = plus léger pour 4 champs plats.
- **Garder les listes recopiées** : le drift silencieux est précisément le problème.
- Les cas **démo** (set réduit `DEMO_DELIMITERS`) restent inline côté JS : spécifiques à la démo, pas une parité C#.

## Conséquences

- **Code** : `Host/spancomputer-fixtures.txt` (nouveau, 12 cas), `SpanComputerTests.cs` (12 `[Fact]`→`[Theory]`), `MathCursor.Tests.csproj` (copie fixture), `spancomputer.test.js` (lecture fixture au lieu de la liste inline).
- Parité désormais structurelle, plus par discipline.
