# Fix — Hygiène de suppression des anchors CC (caret piégé, orphelines)

**Date :** 2026-06-10
**Kind :** Fix
**Température :** molle (mécanismes locaux, ajustables)
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-05-19-Feat-anchor-cc-pattern.md](2026-05-19-Feat-anchor-cc-pattern.md) (le pattern anchor) ; [2026-06-10-Refactor-phase2-adapter-orchestration-rewrite.md](2026-06-10-Refactor-phase2-adapter-orchestration-rewrite.md) (qui a élagué `TrySelectOMathOnLeft/Right` sans les remplacer)

## Citation acté

> « oui fait le chantier hygiene, l'effet est carret bloqué » — utilisateur,
> 2026-06-10 (après « déjà le cc merdouille pas mal à la suppression »).

## Contexte

Le pattern anchor (CC minuscule sur ZWSP caché À CÔTÉ de l'OMath) n'a
aucune défense à la SUPPRESSION manuelle depuis la Phase 2 : DocMath avait
`TrySelectOMathOnLeft/Right` (flèches sélectionnant l'équation pour que
Suppr emporte le tout), élagués avec SuggestionService. Modes de
défaillance observés/connus :

1. **Caret piégé** (symptôme rapporté) : Backspace ou clic fait entrer le
   caret DANS la CC cachée → coincé, et la frappe hérite de `Font.Hidden`
   (texte invisible).
2. **Anchor orpheline** : l'OMath supprimée, la CC+ZWSP reste dans le flux.
3. Placeholder fantôme si le contenu de la CC est vidé.

## Décision — trois défenses LOCALES (jamais de scan du document)

- **H1 — Suppression atomique** : Backspace avec caret réduit juste APRÈS
  une de nos OMaths (resp. Suppr juste AVANT son anchor) → la frappe
  SÉLECTIONNE anchor + OMath comme une unité (consommée, sélection
  visible) ; la frappe suivante supprime le tout. Probe O(1) via
  `CcMetaResolver`. Une équation étrangère n'est pas touchée.
- **H2 — Balayeur d'orphelines** : sur `WindowSelectionChange` (event déjà
  branché), probe ±4 positions autour du caret : CC `MathCursor` sans
  OMath adjacente → supprimée avec son ZWSP. O(1).
- **H3 — Anti-piège** : caret détecté DANS une de nos CC → éjection
  immédiate juste avant la CC (plus jamais de frappe en police cachée).

Hook clavier : nouveaux handlers `OnBackspacePressed`/`OnDeletePressed`
(non-consommants par défaut) ; le flux du hook devient « handler peut
laisser passer ET la touche texte réarme quand même le debounce NER ».
Garde anti-réentrance (`_busy`) : les `SetRange` d'hygiène ne re-déclenchent
pas l'hygiène ; gardes existantes (commit en cours) respectées.

## Tradeoff & alternatives écartées

- **Abandonner l'anchor CC** (store doc-level par hash) : sans ancre dans
  le flux, impossible de suivre l'équation à travers édition/déplacement/
  copie ; déjà écarté dans l'ADR anchor (option ii). Le CC reste le moins
  pire — il lui fallait juste ses défenses.
- **Verrouiller la CC** (`LockContents`) : historique DocMath = soft-locks
  et revert cassé (règle dure : jamais sans tester `cc.Delete`).
- **Scan périodique du doc** : contraire à « pas de polling » ; le balayage
  local au caret suffit (une orpheline ne gêne que là où on tape).

## Conséquences

- Nouveau `Host/AnchorHygiene.cs` (Word-coupled) ; `KeyboardInterceptor`
  (+2 handlers, flux consumed/passthrough revu) ; câblage `ThisAddIn`.
- Tests : logique Word-coupled → validation manuelle (ci-dessous).

## Validation post-fix

1. Convertir une équation, Backspace : 1ʳᵉ frappe = équation sélectionnée,
   2ᵉ = tout disparaît (OMath + anchor), Ctrl+Z restaure.
2. Cliquer/flécher pour entrer dans la zone de l'anchor → caret éjecté,
   la frappe reste visible.
3. Supprimer une équation à la sélection souris (sans l'anchor) → au
   prochain passage du caret à proximité, l'orpheline disparaît.
4. Équation OMath étrangère (insérée via Word) : Backspace = comportement
   Word natif inchangé.
