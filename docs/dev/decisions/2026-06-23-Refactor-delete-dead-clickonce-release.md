# Refactor — Suppression du ClickOnce mort `docs/release/` + FAQ MAJ corrigée

**Date :** 2026-06-23
**Kind :** Refactor
**Température :** molle
**Statut :** acté
**Lié à :** [2026-06-23-Refactor-cleanup-docs-and-dead-yaml-vestiges.md](2026-06-23-Refactor-cleanup-docs-and-dead-yaml-vestiges.md) (qui l'avait laissé en « différé, décision user »), [2026-06-18-Feat-ribbon-update-badge](2026-06-18-Feat-ribbon-update-badge.md)

## Citation acté

> « push deja et on investigue la suite » puis « oui go » — utilisateur, 2026-06-23

## Contexte

`docs/release/` (8 fichiers ClickOnce v0.1.0.0 trackés, dont `MathCursor.Core.dll.deploy`
d'un projet supprimé + `setup.exe` 946 Ko) avait été laissé en différé : risque qu'il
soit l'endpoint d'auto-update ClickOnce des bêtas installées. Investigation :

- **L'add-in installé n'auto-update PAS via ClickOnce.** L'installeur Inno l'enregistre
  en `file:///{app}\MathCursor.vsto|vstolocal` (`MathCursor.iss:199`) — flag `vstolocal`
  = add-in **local pur**, jamais mis à jour depuis une URL.
- **La MAJ réelle = `UpdateChecker` custom** : GET d'un endpoint version (R2/Pages) qui
  allume un **badge ruban « Mise à jour disponible »** (ADR 2026-06-18-ribbon-update-badge) ;
  le téléchargement de l'installeur reste **manuel**. Aucun ClickOnce.
- `docs/release/` n'est produit que par `release.yml` (CI GitHub **non utilisée**) ;
  aucune page ne le lie (téléchargement = `/download/latest.exe` → R2).

→ `docs/release/` est un résidu d'un mode de distribution ClickOnce abandonné. Mort.

Trouvé en passant : la **FAQ du site** (`docs/index.html` faq_2_a, FR+EN) affirmait à
tort que la bêta s'auto-met à jour « via ClickOnce » — faux.

## Décision

1. `git rm -r docs/release/` (résidu ClickOnce mort).
2. Corriger la FAQ FR/EN : décrire le vrai mécanisme (vérification de version en ligne →
   badge « MAJ dispo » dans le ruban, retéléchargement manuel ; hors-ligne OK).

## Conséquences

- **Non touché** : `.github/workflows/release.yml` (la CI ClickOnce qui régénérait
  `docs/release/`) — dormante (CI GitHub non utilisée). À supprimer plus tard si on
  acte qu'on n'y reviendra pas. La distribution vit dans la skill `/deploy-prod`
  (Inno Setup → R2 + Cloudflare Pages).
- `git` garde l'historique des binaires supprimés si besoin.
