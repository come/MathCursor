# MathCursor VSTO — Installation et test local

> **⚠️ Guide partiellement daté (MVP phase C1).** Depuis, le produit a : popup au
> caret, mode édition (revenir à la saisie), opérateurs n-aires (lim/sum/int…),
> déclencheur **Ctrl+Espace**, installeur **Inno Setup**. Les sections « Limites
> actuelles » et les mentions « Alt+M » plus bas sont périmées. Pour l'état réel :
> [`CLAUDE.md`](../CLAUDE.md) + `git log`. Gate de test : `scripts/run-tests.ps1`.

## Étape 1 — Build complet de la solution

1. Ouvre `MathCursor.sln` dans Visual Studio 2022.
2. Dans l'**Explorateur de solutions**, fais clic droit sur la solution → **Régénérer la solution** (`Ctrl+Shift+B`).
3. Vérifie la **Sortie** : tous les projets réussissent — le moteur pur
   (`MathCursor.Engine`, `MathCursor.Serialization`), `MathCursor.HostContract`,
   l'adapter `MathCursor` (VSTO) et leurs projets de tests. *(Le build/test complet
   en ligne de commande passe par `scripts/run-tests.ps1`.)*

Si erreur sur le VSTO : vérifie que **.NET Framework 4.8 Developer Pack** est installé (Visual Studio Installer → Modifier → onglet "Composants individuels").

## Étape 2 — Lancer en mode debug

1. Dans l'Explorateur de solutions, clic droit sur le projet **MathCursor** (celui en `adapter-vsto/`) → **Définir comme projet de démarrage**.
2. Ferme Word s'il est ouvert (sinon le debugger ne pourra pas charger l'add-in).
3. Appuie sur **F5** (ou menu Débogage → Démarrer le débogage).
4. Word se lance automatiquement avec MathCursor chargé.

**Signe que ça marche** : dans l'onglet **Accueil** du ruban Word, tout à droite, tu dois voir un nouveau groupe **MathCursor** avec deux boutons :
- **Convertir** (icône équation, keytip `M`)
- **À propos**

Dans la barre d'état de Word (en bas) : "MathCursor prêt. Tapez une expression puis Alt+M."

## Étape 3 — Test rapide des fonctionnalités

Dans le document Word vide :

### Test 1 — Conversion simple
1. Tape : `f(x)=1/x`
2. Clique **Convertir** (ou `Alt+M`)
3. Attendu : `f(x)=1/x` est remplacé par une équation Word formatée en Cambria Math, avec la fraction visible.

### Test 2 — Conversion en contexte
1. Tape : `On a f(x) = 2x + 1` (avec le "On a " devant)
2. Place le curseur en fin de ligne
3. Clique **Convertir**
4. Attendu : `On a ` reste intact, `f(x) = 2x + 1` devient une équation.

### Test 3 — Exposant
1. Tape : `Soit g(x) = x^2`
2. Clique **Convertir**
3. Attendu : `Soit ` reste, `g(x) = x²` devient équation.

### Test 4 — Pas de math
1. Tape : `Bonjour tout le monde`
2. Clique **Convertir**
3. Attendu : rien ne change, barre d'état affiche "Aucune expression math détectée près du curseur."

## Dépannage

### Le groupe MathCursor n'apparaît pas dans le ruban
- Regarde **Fichier → Options → Compléments** : MathCursor doit être dans "Compléments COM actifs". Si il est dans "désactivés", active-le.
- Sinon, dans la barre d'adresses Windows : `%AppData%\MathCursor\logs\mathcursor.log` — ouvre le fichier pour voir les erreurs.

### Le build échoue avec "The reference assemblies for .NETFramework,Version=v4.8 were not found"
- Installe le **.NET Framework 4.8 Developer Pack** depuis microsoft.com ou via VS Installer (composant individuel).

### Word ne se lance pas au F5
- Vérifie que tu as fermé TOUTES les instances de Word avant F5
- Vérifie que **MathCursor** est bien défini comme projet de démarrage (pas un projet de test ou autre)

### Erreur de certificat au premier lancement
- Normal : clique **Installer** dans la boîte de dialogue. VS installe un certificat auto-signé valable en dev uniquement.
- Alternative : ouvre PowerShell admin et exécute `docs\install-cert.ps1` si ce script existe.

## Limites actuelles (phase C1 MVP)

- ✅ Conversion simple via bouton ribbon ou Alt+M
- ✅ Expressions : fractions, exposants, fonctions f(x), opérateurs
- ✅ Multilingue (FR/EN/DE/ES) — stopwords détectés
- ❌ **Pas** de popup au caret — à venir phase C2
- ❌ **Pas** de raccourci `Ctrl+Espace` — conflit Word, on reste sur Alt+M
- ❌ **Pas** de mode édition (cliquer dans une équation pour revenir au texte source) — phase C2
- ❌ **Pas** d'opérateurs nommés (lim, sum, int, derivatives) — phase B3

## Logs

Tous les events (conversions, erreurs, capabilities) sont loggés dans :
```
%AppData%\MathCursor\logs\mathcursor.log
```

Utile pour diagnostiquer un échec de conversion.

## Packaging pour distribution (plus tard)

Pour installer sur un poste d'élève ou de prof sans VS :
1. **ClickOnce** : Visual Studio → Publication → Site web ou dossier partagé (simple mais limite per-user).
2. **MSI signé via WiX Toolset** : plus lourd mais plus propre, installation machine-wide.

Voir roadmap phase D (packaging/distribution).
