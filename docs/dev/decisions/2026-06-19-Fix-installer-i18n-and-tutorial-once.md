# Fix — Installeur : textes custom localisés (FR/EN) + tutoriel proposé une seule fois (fin)

**Date :** 2026-06-19
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** `adapter-vsto/installer/MathCursor.iss`, [2026-05-22-Feat-tutorial-docx-generated-onboarding.md](2026-05-22-Feat-tutorial-docx-generated-onboarding.md)

## Citation acté

> « j'ai choisi langue anglais dans l'installeur, mais la moitié des textes sont
> en FR, par ailleurs, ouvrir le tutoriel est present deux fois dans le process
> d'install, il ne faudrait le mettre qu'a la fin » — utilisateur, 2026-06-19

## Contexte

Deux bugs sur l'installeur Inno Setup, langue EN choisie :

1. **Textes mi-FR mi-EN** : seuls les messages *intégrés* d'Inno (chrome du wizard)
   étaient traduits. Tous les textes **custom** étaient codés en dur en français —
   la tâche « Pour bien démarrer / Ouvrir le tutoriel maintenant », les `StatusMsg`
   (VC++ redist, certificat), les `MsgBox` de prérequis ([Code]), et la page
   `InfoAfterFile` (after-install.txt, FR uniquement).
2. **Tutoriel proposé deux fois** : une checkbox `[Tasks] opentutorial` sur la
   page « Select Additional Tasks » (AVANT install) **et** la checkbox
   `[Run] postinstall` sur la page finale → double proposition.

## Décision

1. **Tout texte custom → `[CustomMessages]`** avec préfixes `french.` / `english.`,
   référencés par `{cm:Cle}` (directives) et `ExpandConstant('{cm:Cle}')` ([Code]) :
   `OpenTutorial`, `StatusVcRedistX86/X64`, `StatusCert`, `NeedDotNet`,
   `VstoMissing`, `WordMissing`, `WordOpen`.
2. **`InfoAfterFile` par langue** via le paramètre des entrées `[Languages]`
   (`after-install.txt` FR / `after-install-en.txt` EN) — pas la directive
   globale `[Setup]` (qui est mono-fichier). Création de `after-install-en.txt`.
3. **Tutoriel : une seule fois, à la fin.** Suppression de la section `[Tasks]
   opentutorial` (et du `Tasks: opentutorial` sur l'entrée `[Run]`). La checkbox
   « Ouvrir le tutoriel » reste portée par le `[Run] postinstall` (page finale,
   cochée par défaut).

## Tradeoff & alternatives écartées

- **Garder le `[Tasks]` et retirer le `[Run] postinstall`** : écarté — le
  postinstall (page finale, après que tout est installé) est le bon moment pour
  « ouvrir le tutoriel maintenant » ; la page « Additional Tasks » est trop tôt.
- **Laisser `after-install.txt` global** : impossible à localiser (directive
  mono-fichier) → le paramètre `[Languages].InfoAfterFile` est le mécanisme natif.

## Conséquences

- **Installeur** : `MathCursor.iss` (CustomMessages + Languages + Run + Code),
  nouveau `after-install-en.txt`. ISCC compile sans erreur (warnings
  `WORDVERSION`/`WORDPID` préexistants, hors sujet).
- **Aucun code applicatif touché** (add-in inchangé).
- **À embarquer dans la prochaine release** (le 0.11.1 déployé a l'ancien
  installeur ; ce fix part au prochain build/deploy).

## Validation post-fix

Recompiler avec ISCC, lancer l'installeur en **English** : la page des tâches et
l'info post-install sont en EN, le tutoriel n'est proposé qu'**une fois** (page
finale). Idem en **Français** sans régression.
