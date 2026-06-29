# UX — Page Téléchargements = hub, guides d'installation sur une page dédiée à onglets

**Date :** 2026-06-29
**Kind :** UX
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-29-Feat-alpha-distribution-vsix-oxt](2026-06-29-Feat-alpha-distribution-vsix-oxt.md) (qui avait mis les procédures inline dans releases.html), [2026-06-29-Feat-vscode-vsix-multiplatform-site-distribution](2026-06-29-Feat-vscode-vsix-multiplatform-site-distribution.md) (les 3 boutons VSIX vivent maintenant dans le guide), `docs/releases.html`, `docs/install.html`, `docs/index.html`

## Citation acté

> « alors juste la page de download est devenue illisible, met en haut Word => telecharger
> la derniere version / guide d'installation et pareil pour VSCode et libre office. enleve
> beta en cours et met les procedures d'installation dans les guides pas directement dans la
> page de release » — utilisateur, 2026-06-29

> (placement des guides) « page dediée, avec des onglets par produit + un lien de download en
> haut de chaque guide (EN/FR) » — utilisateur, 2026-06-29

## Contexte

À force d'empiler Word (stable) + VS Code (3 boutons plateforme + pas-à-pas) + LibreOffice
(pas-à-pas) **inline** dans `releases.html`, la page est devenue un mur illisible : trois
sections longues avant même d'atteindre l'historique des versions. La bannière « Beta en
cours » alourdissait encore le haut de page.

## Décision

Séparer **téléchargement** (hub) et **installation** (guides).

### 1. `releases.html` = hub de téléchargement

Haut de page : **3 cartes** (Word / VS Code / LibreOffice), chacune avec deux boutons
uniformes — `[Télécharger la dernière version]` + `[Guide d'installation]`. Bannière
« Beta en cours » **retirée**. Les pas-à-pas inline sont **supprimés** (déplacés vers les
guides). L'historique des versions (changelog) reste dessous. Les ancres `#word` / `#vscode`
/ `#libreoffice` sont conservées (id sur les cartes).

### 2. `install.html` = guides à onglets (page dédiée)

Nouvelle page avec **onglets par produit** (Word / VS Code / LibreOffice), deep-link via
`#word` / `#vscode` / `#libreoffice` (les boutons « Guide d'installation » du hub y mènent
directement). Chaque onglet commence par un **bloc de téléchargement** (Word `.exe`,
VS Code = les 3 `.vsix` plateforme, LibreOffice `.oxt`), puis le pas-à-pas + prérequis +
note alpha. Bilingue **FR/EN** (même mécanisme `data-i18n` + `lang-toggle` que les autres
pages : snapshot FR du markup, dict `I18N.en`).

### 3. `index.html` : section installation slimmée

Le pas-à-pas Word de la home (dupliqué avec le guide) est remplacé par un **résumé + deux
boutons** (`Télécharger pour Word` + `Guide d'installation` → `install.html#word`). Le
pointeur « autres éditeurs » renvoie vers `install.html`.

Couche cible : **site statique** uniquement. Aucun impact binaire/moteur/contrats.

## Tradeoff & alternatives écartées

- **Garder les procédures dans `index.html` (ancres par éditeur)** : la home mélangerait
  vitrine et procédures techniques et s'allongerait. Écarté au profit d'une page dédiée.
- **Bouton de download auto-détecté par OS** : déjà écarté (ADR VSIX multiplateforme) ;
  les 3 liens explicites vivent en haut du guide VS Code.
- **Laisser la bannière beta** : l'info « signaler une erreur » est déjà dans la popup ;
  en haut de la page de téléchargement elle ajoutait du bruit sans valeur d'action.

## Conséquences

- **Code touché** : `docs/install.html` *(nouveau)*, `docs/releases.html` (hub + retrait des
  pas-à-pas + nettoyage des clés i18n mortes), `docs/index.html` (section install slimmée +
  liens).
- **Liens** : `releases.html#vscode` / `#libreoffice` restent valides (id sur les cartes).
  Les « Guide d'installation » pointent vers `install.html#<éditeur>`. Le lien **« Installation »
  du header pointe désormais vers `install.html`** sur **toutes** les pages (index, releases,
  contact, privacy, demo) — avant il visait `index.html#installation`, devenu un simple résumé
  (incohérent). Les `dl_note` de `releases.html` (FR + EN) aussi.
- **i18n** : nouvelles clés `dl_card_*` (hub), `ig_*` (guides), `inst_dl`/`inst_guide` (home),
  FR + EN. Clés EN devenues mortes retirées de `releases.html`.
- **Tests** : pas de tests auto. Validé : syntaxe JS des 3 scripts inline OK, complétude
  i18n (toute clé markup a sa traduction EN).

## Validation post-fix

1. `releases.html` : 3 cartes en haut, plus de bannière beta, changelog conservé.
2. `install.html` : onglets cliquables, deep-link `#vscode` ouvre le bon onglet, download en
   haut, bascule FR/EN sur toute la page.
3. Les boutons « Guide d'installation » du hub ouvrent le bon onglet.
</content>
