# MathCursor — extension LibreOffice Writer (P4, MVP)

Convertit une sténo math sélectionnée en **formule LibreOffice Math** native, via le
moteur portable Python (P2) + le convertisseur StarMath (P3). **Autonome** (moteur +
data bundlés dans l'`.oxt`) — cross-OS, aucune dépendance au repo une fois installé.

> ⚠️ **Statut** : MVP. Le moteur bundlé est vérifié hors LibreOffice, mais la chaîne
> UNO (insertion de la formule) et la **syntaxe StarMath** (best-effort) se valident en
> rendu Writer — voir « Retours utiles ». L'`.oxt` EST l'installeur LibreOffice : pas
> de `.exe` à part, le Gestionnaire d'extensions gère l'install nativement.

## Installation (l'`.oxt` = l'installeur)

1. **Construire** l'extension (Python pur, aucun outil externe) :
   ```
   python libreoffice-ext/build_oxt.py        # -> libreoffice-ext/MathCursor.oxt
   ```
2. **Installer** : double-cliquer sur `MathCursor.oxt` (ou
   `Outils ▸ Gestionnaire des extensions ▸ Ajouter…`). Redémarrer LibreOffice.
   - En ligne de commande : `unopkg add MathCursor.oxt` (Windows :
     `"C:\Program Files\LibreOffice\program\unopkg.exe" add MathCursor.oxt`).
3. **Lier le raccourci** : `Outils ▸ Personnaliser ▸ Clavier`, choisir `Ctrl+Espace`,
   catégorie **Macros LibreOffice ▸ … ▸ mathcursor**, fonction `convert_selection`,
   *Modifier*. (Un item de menu est aussi tenté via Addons.xcu — best-effort.)
4. **Utiliser** : dans Writer, taper une sténo (`1/2`, `x^2+1/2`, `lim x 0 g(x)`,
   `sum k 1 n k2`, `cos x`…), la **sélectionner**, `Ctrl+Espace` → formule insérée.

## Périmètre

- **v1 (ici)** : conversion de la **sélection** → formule StarMath (ancrage « comme
  caractère »). Rien reconnu (prose) → le document n'est pas modifié.
- **v2 (à venir)** : auto-détection à la frappe (`XKeyHandler` + debounce), **popup**
  de candidats (`analyze().ranked` en renvoie plusieurs), **réédition** (stocker la
  sténo — équivalent du hash-source-map Word, à concevoir).

## Dev (sans rebuild de l'`.oxt`)

Déposer `mathcursor.py` dans `Scripts/python/` utilisateur (Linux :
`~/.config/libreoffice/4/user/Scripts/python/` ; Windows :
`%APPDATA%\LibreOffice\4\user\Scripts\python\`) et exporter `MATHCURSOR_ENGINE` vers
`engine-python/` du repo. Le script auto-détecte ce mode (sinon mode bundlé `.oxt`).

## Retours utiles (pour itérer)

Pour chaque cas qui rend mal : la **sténo**, le **StarMath** obtenu (double-clic sur la
formule → éditeur Math), le **rendu attendu**. → je corrige `mc_engine/starmath.py` +
ajoute un cas au corpus `engine-python/selftest_starmath.py`. À surveiller : majuscules
grecques (`%PI` ?), `lfloor/rfloor`, `nroot`, matrices `matrix{…#…##…}`, intervalles
ouverts, sur-parenthésage (`left ( … right )`).
