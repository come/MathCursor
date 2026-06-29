# Feat — i18n EN/FR complet de l'extension VS Code (anglais par défaut, zéro FR en dur)

**Date :** 2026-06-25
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-25-Feat-vscode-marketplace-publishing-model](2026-06-25-Feat-vscode-marketplace-publishing-model.md) (publication mondiale), [2026-06-25-Feat-popup-enter-passthrough](2026-06-25-Feat-popup-enter-passthrough.md) (protocole `show` de la coquille partagée), [2026-06-24-Feat-vscode-host-popup-ner](2026-06-24-Feat-vscode-host-popup-ner.md)

## Citation acté

> « pour l'extension vs code le readme / description qu'on voit à l'install, elle
> est i18n en/fr ? » — utilisateur, 2026-06-25

> « non c'est vscode, c'est mondial.. je veux tout en i18n en/fr pas de fr en dur
> nulle part dans vscode » — utilisateur, 2026-06-25

Arbitrages (questions posées) :
- README → **anglais uniquement** (la marketplace n'affiche qu'un seul README, pas
  de bascule par locale).
- Popup « voir plus » partagée avec LibreOffice → **LibreOffice reste en français**
  via un label passé par son propre hôte (défaut HTML = EN).

## Contexte

L'extension VS Code (`adapter-vscode/extension`) vise la marketplace **mondiale**
(cf. modèle de publication N VSIX par plateforme) mais **tout son texte visible
est en français en dur** : `description` marketplace, titres de commandes,
descriptions des réglages (`package.json`), un message status-bar
(`extension.ts:109`), le « ▾ voir plus (N) » de la popup
(`mc-popup/web/index.html:118`), et le README. Le mécanisme i18n natif de VS Code
(anglais = langue de base, locales en surcouche) n'est pas utilisé.

## Décision

**Anglais par défaut** (fallback mondial) + **français** appliqué automatiquement
selon la locale de l'éditeur, **aucune chaîne FR en dur visible** dans VS Code.

### 1. `package.json` → placeholders `%clé%` + `package.nls(.fr).json`

Chaque chaîne FR de `package.json` devient une clé `%...%`. Deux fichiers à la
racine de l'extension : `package.nls.json` (valeurs **EN**, défaut/fallback) et
`package.nls.fr.json` (valeurs **FR**). VS Code choisit selon la locale. `vsce`
les embarque automatiquement (pas de modif `build.mjs`). `displayName` et
`configuration.title` (`MathCursor`) restent littéraux (marque).

### 2. Chaînes runtime → `vscode.l10n` + bundle FR

`"l10n": "./l10n"` dans `package.json`. La **source** dans le code est l'anglais :
`extension.ts:109` → `vscode.l10n.t('MathCursor: nothing to convert')`. Le bundle
`l10n/bundle.l10n.fr.json` mappe les sources EN → FR. `vsce` embarque `l10n/`.

### 3. Popup « voir plus » → label localisé passé par l'hôte

La coquille Rust `mc-popup` est **partagée** (VS Code + LibreOffice) et génère le
libellé dans le HTML. On thread une **phrase localisée** (`moreLabel`) dans le
payload `show`, **défaut EN** dans le HTML ; le « ▾ » et le « (N) » restent du
décor neutre (N calculé en HTML). VS Code passe `vscode.l10n.t('show more')` (→
EN/FR), LibreOffice passe `"voir plus"` (UX FR conservée, pas d'i18n côté LO).

### 4. README → anglais

`adapter-vscode/extension/README.md` réécrit intégralement en anglais. Pas de
version FR (la marketplace n'a pas de README par locale).

## Tradeoff & alternatives écartées

- **README bilingue ou EN+`README.fr.md`** : écarté par l'utilisateur (EN seul ;
  la marketplace ne dispatche pas par locale de toute façon).
- **Tout laisser en FR** : statu quo, incompatible avec une audience mondiale.
- **Hardcoder l'anglais dans le HTML de la popup et laisser LibreOffice basculer
  en EN** : écarté — l'UX LibreOffice est FR ; on passe donc un label depuis
  l'hôte LO (coût : un champ optionnel de plus, rétro-compatible).
- **Dupliquer le HTML popup (un par hôte)** : casse la source unique partagée
  (ADR coquille Rust). Le label passé par l'hôte est moins coûteux.

## Conséquences

- **Code touché** :
  - `adapter-vscode/extension/package.json` — chaînes → `%clés%` + `"l10n": "./l10n"`.
  - `adapter-vscode/extension/package.nls.json` *(nouveau, EN)*,
    `package.nls.fr.json` *(nouveau, FR)*.
  - `adapter-vscode/extension/l10n/bundle.l10n.json` + `bundle.l10n.fr.json` *(nouveaux)*.
  - `adapter-vscode/extension/src/extension.ts` — `vscode.l10n.t(...)` (status bar).
  - `adapter-vscode/extension/src/popup.ts` — `moreLabel` dans `sendShow`.
  - `adapter-vscode/extension/README.md` — réécriture EN.
  - `rust/mc-popup/web/index.html` — param `moreLabel`, défaut EN au lieu du FR l.118.
  - `rust/mc-popup/src/main.rs` — champ `more_label` dans `UserEvent::Show` + `mcRender`.
  - `libreoffice-ext/popup_client.py` — param `more_label="voir plus"` (garde LO FR).
- **API publique** : protocole `show` de la coquille étendu d'un champ **optionnel**
  `moreLabel` → **rétro-compatible** (défaut EN si absent).
- **Tests** : `mc-popup` n'a pas de tests Rust (validation manuelle). Gate moteur
  (`fixtures.json` 456/456) non concernée. `run-tests.ps1` si le build Rust est touché.
- **Règles MC impactées** : aucune.

## Validation post-fix

- **Build** : `node build.mjs` (rebuild cargo `mc-popup` + recopie HTML) sans erreur.
- **Locale EN** : desc/commandes/settings EN, status bar « nothing to convert »,
  popup « ▾ show more (N) ».
- **Locale FR** : tout en français, popup « ▾ voir plus (N) ».
- **LibreOffice** : popup « ▾ voir plus (N) » conservée (label FR passé par l'hôte).
