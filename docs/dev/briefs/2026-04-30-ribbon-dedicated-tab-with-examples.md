# Brief — Onglet ruban dédié "MathCursor" avec boutons d'exemples + paramètres

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-30
**Branche :** `lattice-engine`
**Public cible :** agent C#/VSTO autonome qui ne connaît pas le projet,
intervient sur la couche `adapter-vsto/`.

---

## 1. Le besoin

Aujourd'hui, MathCursor ajoute **2 boutons** (`Signaler un souci`,
`Aide`) dans un groupe au sein de **l'onglet "Accueil" de Word**. C'est
une présence discrète, mais :

1. **Pas découvrable** : un nouvel utilisateur (élève PAP, prof beta) ne
   sait pas par où commencer. Aucun "appel à l'action" pour essayer
   l'add-in.
2. **Pas extensible** : à mesure qu'on ajoute des fonctions (snippets,
   préférences de notation, plus tard partage de templates...), on ne
   peut pas continuer à entasser des boutons dans le groupe Accueil.
3. **Pas conforme aux conventions Office** : les add-ins établis
   (Grammarly, Zotero, Mendeley, Acrobat...) ont **leur propre onglet**
   dédié dans le ruban. C'est ce que les utilisateurs cherchent.

**Doctrine cible** : un onglet dédié `MathCursor` qui sert de **hub** :
- Boutons d'exemple "essaie ça" (comme la web demo) → insèrent du texte
  au curseur, déclenchent mécaniquement la popup
- Bouton de signalement de bug (déplacé depuis l'onglet Accueil)
- Bouton "Paramètres" pour configurer snippets + notations

## 2. UX visée

### 2.1. Maquette ruban

```
[Accueil]  [Insertion]  [...]  [MathCursor]  ← nouvel onglet
                                  │
                                  ▼
─────────────────────────────────────────────────────────────────
│ Exemples                    │ Outils         │ Configuration  │
│  [f(x)=2x+1] [lim x→0]      │  [Signaler]    │  [Paramètres]  │
│  [Σ k²]      [∫ x²]         │  [Aide]        │                │
│  [√(x+1)]    [(a+b)²]       │                │                │
│  [∀x∈ℝ]      [Uₙ₊₁=...]     │                │                │
─────────────────────────────────────────────────────────────────
   Groupe "Exemples"           "Outils"         "Configuration"
```

**3 groupes** dans l'onglet :
1. `Exemples` — ~10-12 boutons "essaie ça" (cf. §2.2)
2. `Outils` — boutons existants déplacés (Signaler, Aide)
3. `Configuration` — bouton Paramètres (cf. §2.4)

### 2.2. Boutons d'exemples (le contenu du groupe `Exemples`)

**Comportement** : clic → insère le texte source au curseur dans Word.
Comme l'utilisateur tape dans un OMath, le NER détecte la zone et la
popup s'ouvre avec la suggestion. Effet pédagogique : l'utilisateur voit
en direct ce que l'add-in fait avec le texte.

**Liste calquée sur `docs/demo/index.html` lignes 50-63** (web demo) :

| Texte inséré | Libellé bouton | Catégorie |
|---|---|---|
| `f(x) = 2x + 1` | `f(x)=2x+1` | Fonction |
| `lim x 0 sin x / x` | `lim x→0 sin x/x` | Limite |
| `somme k 1 n k^2` | `Σ k²` | Somme |
| `int 0 1 x^2 dx` | `∫ x² dx` | Intégrale |
| `racine x+1` | `√(x+1)` | Racine |
| `(a+b)^2 = a^2 + 2ab + b^2` | `(a+b)²` | Identité |
| `frac a b` | `a/b` | Fraction |
| `sin2(x) + cos2(x) = 1` | `sin²+cos²=1` | Trigo |
| `forall x in R, x^2 >= 0` | `∀ x ∈ ℝ` | Quantificateur |
| `U_n+1 = U_n-1 + 3q^2` | `Uₙ₊₁=...` | Suite |
| `f : x -> x+1` | `f:x→x+1` | Function def |
| `[0,1] union [2,3]` | `[0,1]∪[2,3]` | Intervalles |

**Implémentation** : 1 callback unique `OnExampleClicked(IRibbonControl
control)` qui lit `control.Tag` (le texte à insérer), via
`Application.Selection.TypeText(text)`. Le `Tag` est défini dans le XML :

```xml
<button id="ExFunc" tag="f(x) = 2x + 1" label="f(x)=2x+1"
        onAction="OnExampleClicked" size="normal" />
```

→ aucune logique métier nouvelle, juste de la plomberie ribbon + insertion.

### 2.3. Outils (groupe `Outils`)

**Boutons déplacés** depuis le groupe `MathCursorGroup` actuel (onglet
Accueil) :
- `Signaler un souci` (`OnReportIssueClicked` existant, INCHANGÉ)
- `Aide` (`OnAboutClicked` existant, INCHANGÉ)

**Décision Accueil** : laisser ou retirer le groupe `MathCursorGroup` de
l'onglet Accueil ?

**Recommandation** : **retirer**. Avec un onglet dédié, garder une copie
en Accueil = redondance + bruit. Décision finale à valider avec l'auteur.

### 2.4. Paramètres (groupe `Configuration`)

**Bouton Paramètres** : clic → ouvre une fenêtre WPF modale (au minimum
2 sections pour le MVP) :

#### Section "Snippets personnels"

Liste éditable de raccourcis user-defined :

| Trigger | Texte inséré | Description |
|---|---|---|
| `mat3` | `(■(a&b&c@d&e&f@g&h&i))` | Matrice 3×3 vide |
| `vec3` | `\vec{u} = (1,2,3)` | Vecteur 3D |
| ... | ... | ... |

Implémentation MVP : stockage simple dans
`%AppData%\MathCursor\snippets.json`. **Pas de raccourci clavier
custom** au MVP (trop complexe avec VSTO). Insertion via le bouton
Paramètres → onglet Snippets → clic sur la ligne.

#### Section "Préférences de notation"

Pour les choix doctrine configurables. Au MVP :

- **Multiplication explicite** : `× (times)` [✓] / `· (cdot)` [ ]
  - Default : `×` (cf. brief
    `2026-04-30-explicit-mult-times-vs-cdot.md`)
- *Plus tard* : virgule décimale `,` ou point `.`, notation matricielle,
  etc.

Implémentation MVP : stockage dans `%AppData%\MathCursor\prefs.json`,
relu au démarrage de `MathCursor.Core.Lattice.LatexRenderer` pour
modifier le rendu de `*`. (Question d'archi : passer un objet `Prefs`
au constructeur de `Engine`. À détailler à l'implémentation.)

## 3. Fichiers à toucher

| Fichier | Modification |
|---|---|
| `adapter-vsto/src/MathCursor/Ribbon.xml` | Réécriture quasi-totale : ajouter `<tab id="MathCursorTab">` avec 3 groupes. Retirer ou réduire `MathCursorGroup` de TabHome. |
| `adapter-vsto/src/MathCursor/RibbonCallback.cs` | Ajouter `OnExampleClicked` + `OnSettingsClicked`. Garder `OnReportIssueClicked`, `OnAboutClicked` (inchangés). |
| `adapter-vsto/src/MathCursor/Strings.cs` | Ajouter labels FR/EN pour les nouveaux boutons (groupe Exemples, groupe Outils, bouton Paramètres). |
| `adapter-vsto/src/MathCursor/UI/SettingsWindow.xaml` | NOUVEAU : fenêtre WPF modale avec 2 onglets (Snippets, Préférences). |
| `adapter-vsto/src/MathCursor/UI/SettingsWindow.xaml.cs` | NOUVEAU : code-behind, lit/écrit `prefs.json` et `snippets.json`. |
| `adapter-vsto/src/MathCursor/Settings/UserPrefs.cs` | NOUVEAU : modèle des préférences (POCO sérialisable JSON). |

## 4. Phasage proposé

Pour ne pas tout faire d'un coup :

**Phase A — Onglet + exemples + déplacement Signaler/Aide** (~2-3h)
- Ribbon.xml + callbacks + insertion `TypeText`
- Garder l'onglet Accueil avec les 2 boutons en doublon temporaire

**Phase B — Bouton Paramètres avec section Notation** (~3-4h)
- Fenêtre WPF minimale, juste la pref `× / ·`
- Brancher `LatexRenderer` sur `UserPrefs`
- Fait après le brief `times-vs-cdot` qui aura hardcodé `×`

**Phase C — Section Snippets** (~3-4h)
- Onglet Snippets de la fenêtre, CRUD simple
- Pas de raccourci clavier, juste insertion par clic

**Phase D — Décision Accueil** (~15min)
- Retirer définitivement le groupe `MathCursorGroup` de TabHome

## 5. Risques / points d'attention

### 5.1. Ribbon XML — `<tab id>` vs `<tab idMso>`

Pour créer un nouvel onglet (vs. modifier un existant), il faut `id` (et
non `idMso`) + `label` :

```xml
<tab id="MathCursorTab" label="MathCursor" insertAfterMso="TabHome">
```

`insertAfterMso="TabHome"` positionne notre onglet juste après Accueil
(à valider visuellement).

### 5.2. Insertion au curseur — édge case OMath existant

`Selection.TypeText("f(x) = 2x+1")` insère le texte en cours, **incluant
si le curseur est dans un OMath**. Comportement souhaité ? À tester :
quand on clique sur un exemple alors que le curseur est dans un OMath,
le texte s'insère DANS l'OMath, ce qui peut produire un résultat
incohérent.

**Mitigation** : avant `TypeText`, vérifier si on est dans un OMath
(`Selection.OMaths.Count > 0`). Si oui, sortir du OMath
(`Selection.MoveEnd Word.WdUnits.wdParagraph, 1`) puis insérer.

### 5.3. Fenêtre Paramètres — modale ou non-modale ?

**Recommandation** : modale (`ShowDialog`). Plus simple à coder,
empêche les utilisateurs de modifier les snippets en parallèle de la
saisie.

### 5.4. Stockage `prefs.json` / `snippets.json`

`%AppData%\MathCursor\` (déjà utilisé pour les logs). Format JSON simple
avec `System.Text.Json` (déjà dans .NET Framework 4.8 via
`System.Text.Json` package) ou `Newtonsoft.Json` si déjà référencé. Au
chargement, si fichier absent → defaults.

**Migration** : pas de migration nécessaire (nouveau fichier, version 1).
Prévoir un champ `version` dans le JSON pour le futur.

### 5.5. Rechargement live des préférences

Quand l'utilisateur change une pref dans la fenêtre Paramètres et clique
"OK", il faut que le moteur l'utilise immédiatement (pas redémarrer
Word). → Au "OK" de la fenêtre, déclencher
`Globals.ThisAddIn.ReloadPrefs()` qui relit le JSON et le pousse dans
`Engine`. À implémenter en Phase B.

## 6. Hors scope (à NE PAS faire dans ce brief)

- Raccourcis clavier custom pour les snippets (complexe en VSTO,
  conflits potentiels avec Word).
- Synchronisation des prefs entre machines (cloud, etc.) — phase 2.
- Internationalisation des libellés des exemples — pour MVP les libellés
  sont neutres (`f(x)=2x+1` est lisible en toutes langues).
- Édition visuelle des snippets avec preview en temps réel — au MVP,
  juste un éditeur texte pour le `Trigger` et le `Texte inséré`.

## 7. Effort estimé global

| Phase | Effort |
|---|---|
| A (Onglet + exemples + déplacement) | ~2-3h |
| B (Paramètres + notation) | ~3-4h |
| C (Snippets) | ~3-4h |
| D (Cleanup Accueil) | ~15min |
| **Total** | **~9-11h** |

À phaser sur 2-3 sessions.
