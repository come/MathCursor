# Fix — Titres d'encadrés : accents conservés (« THÉORÈME »)

**Date :** 2026-10-21
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-10-20-UX-site-visual-redesign](2026-10-20-UX-site-visual-redesign.md) (constat fait en rendant les visuels du site)

## Citation acté

> « ok traite ca » — utilisateur, 2026-10-21, en réponse à la liste des points
> relevés, dont « Encadrés : leurs titres perdent les accents selon une option
> de Word ».

## Contexte

Le titre d'un encadré (`CalloutInserter`) était écrit « Théorème » puis mis en
**petites capitales** (`Font.SmallCaps`). Word affiche alors des majuscules
sans accent (« THEOREME ») quand l'option *Majuscules accentuées en français*
(`Options.AllowAccentedUppercase`) est décochée, ce qui est le cas par défaut
sur certaines installations. Constaté en générant les captures du site dans un
Word piloté par script.

## Décision

- Le titre est inséré **déjà en majuscules** (`Label.ToUpper(fr-FR)` →
  « THÉORÈME », « DÉFINITION », « PROPRIÉTÉ ») : les accents sont des
  caractères, plus une transformation d'affichage.
- L'aspect petites capitales est reproduit à la main : initiale à la taille du
  corps, reste à 80 % (arrondi au demi-point), gras, couleur d'accent.
  `SmallCaps` n'est plus utilisé.

## Tradeoff & alternatives écartées

- **Forcer `AllowAccentedUppercase = true`** à l'insertion : modifie un réglage
  global de Word de l'utilisateur, hors de notre périmètre.
- **Majuscules d'une seule taille** : plus simple mais change l'allure des
  encadrés déjà en place dans les documents.
- **Titre en casse normale (« Théorème »)** : perd l'identité visuelle des
  encadrés.

## Conséquences

- **Code touché** : `adapter-vsto/src/MathCursor/Host/CalloutInserter.cs`
  (texte du titre, `StyleTitle`, constante `SmallCapsRatio`).
- Le texte du titre dans le document est désormais « THÉORÈME » (et non
  « Théorème » affiché en petites capitales). Aucun code ne relit ce titre.
  Les encadrés déjà insérés ne changent pas.
- **Tests** : adapter 438/438 verts (pas de test COM sur l'encadré).

## Validation post-fix

Rendu Word (script de capture, code compilé) avec
`AllowAccentedUppercase = false` : « DÉFINITION », « PROPRIÉTÉ », « EXEMPLE »,
« THÉORÈME » accentués, allure identique aux petites capitales. À confirmer
dans Word au quotidien après la prochaine version de l'add-in.
