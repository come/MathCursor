# Feat — Extension LibreOffice Writer (UNO/Python) sur le moteur portable

**Date :** 2026-06-16
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Lié à :** `engine-python/` (port P2), `engine-python/mc_engine/starmath.py` (P3),
`libreoffice-ext/`
**Dépend de :** ADR `2026-06-16-Feat-portable-engine-universal-vocab`

## Citation acté

> « on enchaine du coup P3 et P4 » — utilisateur, 2026-06-16 (P3 = LaTeX→StarMath,
> P4 = extension LibreOffice). Stratégie cadrée en plan mode : Python/UNO, StarMath,
> cross-OS, parité visée.

## Contexte

Le moteur est désormais portable (P1 C# data-driven, P2 port Python 434/434, P3
LaTeX→StarMath). LibreOffice n'expose pas .NET ; les extensions se font en **Python
via UNO**, et le format formule natif est **StarMath**. On consomme donc le port
Python + le convertisseur StarMath dans une extension Writer.

## Décision

Extension **Python/UNO** pour **Writer**, cross-OS (Python pur, pas de binaire par OS).

- **Pipeline** : sténo → `engine.analyze` (port Python) → candidat → `to_starmath`
  → insertion d'un objet formule (`TextEmbeddedObject`, CLSID StarMath
  `078B7ABA-54FC-457F-8551-6147E776A997`, propriété `Formula` = StarMath).
- **v1 (MVP)** : conversion de la **sélection** (l'utilisateur sélectionne la sténo,
  déclenche, la sélection est remplacée par la formule). Déclenchement par macro liée
  à un raccourci (Ctrl+Espace) via Tools ▸ Customize, ou item de menu (Addons.xcu).
- **v2+** (non fait) : auto-détection à la frappe (`XKeyHandler` + debounce), **popup**
  de candidats, **réédition**. La réédition nécessitera un équivalent du *hash-source-
  map* Word (stocker la sténo) — métadonnées RDF du doc, attribut nommé de l'objet, ou
  XML custom dans l'`.odt` — **à trancher en v2**.
- **Packaging** : `.oxt` (Python + `mc_engine/` + `data/engine/` bundlés ; `data.py`
  a un hook `set_data_dir`). Pour le test initial, la macro se dépose dans le dossier
  utilisateur `Scripts/python/` en pointant `MATHCURSOR_ENGINE` vers `engine-python/`.

## Tradeoff & alternatives écartées

- **CLI .NET bundlé** : écarté (cf. ADR portable-engine — runtime lourd cross-OS).
- **MathML au lieu de StarMath** : import MathML LibreOffice lossy ; `Formula`
  StarMath est direct et natif.
- **Popup/auto-détection en v1** : reporté — l'insertion sur sélection valide d'abord
  toute la chaîne UNO + bridge + StarMath avec un minimum de surface.

## Conséquences

- **Nouveau** : `libreoffice-ext/` (macro `mathcursor.py`, fichiers `.oxt`, README,
  build). `data.py` : hook `set_data_dir`.
- **Validation** : impossible hors LibreOffice — le StarMath (P3, best-effort) et
  l'insertion UNO se confirment **visuellement chez l'utilisateur** (Win/Mac/Linux),
  puis on itère (syntaxe StarMath, ancrage, raccourci).
- **Roadmap** : à refléter dans CLAUDE.md / ROADMAP (LibreOffice = nouveau front).

## Validation (à faire côté utilisateur)

Installer la macro, sélectionner `1/2`, `x^2+1/2`, `lim x 0 g(x)`, `sum k 1 n k2`,
`cos x`, une matrice → vérifier l'insertion d'une formule Math correcte. Lister les
écarts StarMath pour itérer sur `starmath.py`.
