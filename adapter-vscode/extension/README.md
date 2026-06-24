# MathCursor (VSCode)

Écris les maths comme elles te viennent : notation au clavier → **LaTeX propre
inline**, avec un **aperçu de la formule au caret** (popup « façon Word »).

Au-dessus de LaTeX Workshop (coexistence, zéro couplage). Tape une expression
(`1/x+1`, `vec AB . vec BC`, `lim x 0 1/x`, `R*`…) : MathCursor détecte la zone
math, propose le LaTeX rendu, tu valides → insertion avec délimiteurs et packages
auto.

## Comment ça marche

- **Détection** de zone math au fil de la frappe (modèle NER) + `Ctrl+Espace` pour
  forcer / agrandir la zone.
- **Moteur** texte → candidats LaTeX classés (popup multi-candidats au caret).
- **100 % natif** : moteur, NER et popup sont des binaires **Rust** embarqués
  (aucun runtime .NET). Windows-x64 pour l'instant.

## Réglages

`mathcursor.culture` (fr/us), `delimiters` (auto/inline/display/paren/none),
`maxCandidates`, `autoDetect`, `autoPackages`, `inlineDisplaystyle`.

## Raccourcis

- `Ctrl+Espace` — popup au caret / forcer / agrandir la zone.
