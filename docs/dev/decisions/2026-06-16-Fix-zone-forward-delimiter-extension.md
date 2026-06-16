# Fix — La zone NER avale le délimiteur de fermeture tapé (`[0;1[`, `[a]`)

**Date :** 2026-06-16
**Kind :** Fix
**Température :** molle
**Statut :** retracté
**Superseded by :** [2026-06-16-Fix-keyboard-hook-altgr-debounce.md](2026-06-16-Fix-keyboard-hook-altgr-debounce.md)

> **Rétracté le 2026-06-16** : ce fix traitait un SYMPTÔME. La vraie cause racine
> était le hook clavier qui n'armait pas le debounce d'auto-détection sur les
> frappes AltGr (= Ctrl+Alt) — donc sur AZERTY `[`/`]` (AltGr+5 / AltGr+°) ne
> re-déclenchaient jamais la détection, le `[` final n'atteignait jamais le
> texte analysé. `TryExtendForwardDelimiters` (extension de zone) a été révoqué :
> méthode, câblage, tests et entrée README retirés. Voir l'ADR qui supersede.

## Citation acté

> « [0;1[ ne marche plus […] et ça marchait avant » puis « possible que ce soit l'auto fermeture (la feature qui auto complete la fin) qui fasse merder » puis « yes » — utilisateur, 2026-06-16 (aperçu : source `« [0;1 »`, candidat `[0;1]` fermé).

## Contexte

En **auto-détection**, taper `[0;1[` donne `[0;1]` (intervalle fermé), et taper `[a]`/`[0;1]` laisse un `]` en **texte résidu** après l'OMath (`[a]]`). Diagnostic (logs) :

1. La **zone NER exclut le délimiteur final tapé** : `[0;1[` → zone `[0;1` ; `[a]` → zone `[a`.
2. L'**auto-fermeture du moteur** (lexer : `if (brk % 2 == 1)` ajoute un `]` virtuel) ferme la source amputée → `[0;1]`.
3. Le commit supprime seulement la zone amputée → le délimiteur tapé reste en texte (résidu `]`).

Le moteur est correct : donné `[0;1[` complet, il rend `[0;1[` (fixture verte). Le bug est la **frontière de zone** (auto-détection), pas le moteur. Pré-existant ; sans rapport avec la feature « parenthèses conservées » (non committée).

## Décision

Nouvelle méthode pure `ZoneRefiner.TryExtendForwardDelimiters(paragraph, zone, caret)` : si le gap entre `zone.End` et le **caret** (≤ 3 chars) est **uniquement** des délimiteurs collés (`( ) [ ] { }`), étendre la zone jusqu'au caret. L'utilisateur a tapé ces délimiteurs comme partie de l'expression, juste avant son caret — signal d'intention sûr (même esprit que `TryExtendForwardWhitespace`). Câblée dans `AutoDetectController` juste après l'extension blancs.

Résultat : `[0;1[` → zone `[0;1[` → moteur `[0;1[` (plus d'auto-fermeture parasite) ; `[a]` → zone `[a]` → commit supprime tout → plus de résidu. **Un seul fix, les deux symptômes.**

## Conséquences

- **Code** : `Host/Detection/ZoneRefiner.cs` (méthode pure), `Host/AutoDetectController.cs` (1 ligne de câblage).
- **Tests** : `ZoneRefinerTests` (extension crochet ouvrant/fermant, blocage si non-délimiteur, no-op si caret au bord). `[0;1[` reste une fixture moteur verte (le trou de couverture était la zone, pas le moteur).
- **Périmètre** : auto-détection seulement (le Ctrl+Espace manuel prend déjà le texte complet jusqu'au caret — d'où « ça marchait avant »).
- **Garde** : gap ≤ 3 et caractères strictement dans `()[]{}` → pas de sur-extension sur du texte.

## Validation post-fix

Tests adapter verts (`ZoneRefinerTests`). Test Word : taper `[0;1[` → `[0;1[` ; `[a]` → `[a]` sans résidu.
