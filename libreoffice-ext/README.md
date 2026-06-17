# MathCursor — extension LibreOffice Writer (P4, MVP)

Convertit une sténo math sélectionnée en **formule LibreOffice Math** native, via le
moteur portable Python (P2) + le convertisseur StarMath (P3). Cross-OS (Python pur).

> ⚠️ **Statut** : MVP non encore testé en conditions réelles (écrit hors LibreOffice).
> La syntaxe StarMath (`mc_engine/starmath.py`) est **best-effort** — c'est ici, en
> rendu Writer, qu'elle se valide et s'itère. Voir « Retours utiles » en bas.

## A. Test rapide (recommandé pour démarrer — sans packaging)

1. **Copier** `mathcursor.py` dans le dossier scripts Python utilisateur :
   - Linux : `~/.config/libreoffice/4/user/Scripts/python/`
   - Windows : `%APPDATA%\LibreOffice\4\user\Scripts\python\`
   - macOS : `~/Library/Application Support/LibreOffice/4/user/Scripts/python/`
2. **Pointer le moteur** : définir la variable d'environnement `MATHCURSOR_ENGINE`
   sur le chemin de `engine-python/` du repo (ou éditer `ENGINE_PATH` en tête du
   script). LibreOffice doit voir cette variable (la lancer depuis un shell qui
   l'exporte).
3. **Redémarrer** LibreOffice. Ouvrir Writer.
4. **Tester** : taper `1/2`, le **sélectionner**, puis
   `Outils ▸ Macros ▸ Exécuter la macro… ▸ Mes macros ▸ mathcursor ▸ convert_selection`.
   → la sélection doit devenir une formule.
5. **Raccourci** : `Outils ▸ Personnaliser ▸ Clavier`, lier `Ctrl+Espace` à
   `convert_selection`.

## B. Construire l'extension `.oxt` (packaging, à valider)

```bash
bash libreoffice-ext/build.sh        # -> libreoffice-ext/MathCursor.oxt
```
Le script bundle `mathcursor.py` + `mc_engine/` + `data/engine/` dans l'`.oxt`. Pour
ce mode, **décommenter** dans `mathcursor.py` les 2 lignes `set_data_dir(...)` (data
embarquée) — sinon le moteur cherche la data via le chemin repo. Installer ensuite via
`Outils ▸ Gestionnaire des extensions`. (Le câblage Addons.xcu / Script Provider de
l'`.oxt` est un point à valider — la méthode A reste la plus sûre pour les 1ers tests.)

## Périmètre

- **v1 (ici)** : conversion de la **sélection** → formule StarMath insérée (ancrage
  « comme caractère »).
- **v2 (à venir)** : auto-détection à la frappe (`XKeyHandler` + debounce), **popup**
  de candidats (le moteur en renvoie plusieurs via `analyze().ranked`), **réédition**
  (nécessite de stocker la sténo — équivalent du hash-source-map Word, à concevoir).

## Retours utiles (pour itérer)

Pour chaque cas qui rend mal, noter : la **sténo** saisie, le **StarMath** obtenu
(visible via double-clic sur la formule → éditeur Math), et le **rendu attendu**. Ça
permet de corriger `mc_engine/starmath.py` (et d'ajouter un cas au corpus
`engine-python/selftest_starmath.py`). Points connus à surveiller : majuscules grecques
(`%PI` ?), `lfloor/rfloor`, `nroot`, matrices `matrix{…#…##…}`, intervalles ouverts.
