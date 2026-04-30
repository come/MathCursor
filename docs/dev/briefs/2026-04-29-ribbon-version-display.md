# Brief — Afficher la version MathCursor dans le bandeau Word

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-29
**Branche :** `lattice-engine`
**Public cible :** agent C#/VSTO autonome qui ne connaît pas le projet,
intervient sur la couche `adapter-vsto/`.

---

## 1. Le besoin

Aujourd'hui, **la version de l'add-in n'est visible nulle part dans le
ribbon Word**. Elle est :

- déclarée dans `AssemblyInfo.cs` (`AssemblyVersion("0.3.2.0")`),
- duplicate manuellement dans le dialogue "Aide" (`RibbonCallback.cs:73` :
  `"Version 0.3.2 — beta\n\n"`),
- lue dynamiquement par `FeedbackBundle.cs` pour les rapports de bug.

Pour qu'un utilisateur (élève PAP, prof beta, ou toi en debug) sache d'un
coup d'œil **quelle version tourne**, il faut afficher la version
directement dans le bandeau.

## 2. UX visée

### 2.1. Emplacement

Dans le groupe `MathCursorGroup` du ribbon (onglet Home), à côté des deux
boutons existants (`ReportIssueButton`, `AboutButton`).

### 2.2. Forme

**Option A — `<labelControl>` discret** (recommandé) : un libellé en gris,
non-cliquable, format `v0.3.2` ou `v0.3.2-beta`. Petit, lisible, présent
sans encombrer.

```xml
<labelControl id="VersionLabel" getLabel="OnGetVersionLabel" />
```

**Option B — séparateur + label dans le screentip d'un bouton** : ajouter
la version dans le screentip de `AboutButton`. Plus discret, mais
nécessite de hover pour la voir → moins utile pour un debug rapide.

**Option C — titre du groupe contient la version** : changer
`<group label="MathCursor">` en `<group getLabel="OnGetGroupLabel">` qui
retourne `"MathCursor v0.3.2"`. Très visible, prend zéro place ribbon.

**Recommandation** : **Option C** (titre du groupe). Zéro coût visuel,
visible immédiatement, et c'est exactement le pattern Office (regarder
"Modifications" / "Compare" qui adaptent leurs titres). Si après essai le
visuel ne plaît pas, fallback Option A.

### 2.3. Format de la version

Lecture dynamique via `Assembly.GetExecutingAssembly().GetName().Version` :

- AssemblyVersion `0.3.2.0` → afficher `v0.3.2` (les 3 premiers chiffres,
  drop le 4ème qui est toujours 0).
- Pas de `-beta` automatique pour l'instant ; si besoin, ajouter une
  constante `BuildChannel` (release / beta) dans une classe C# et
  l'afficher en suffixe (`v0.3.2-beta`). Hors scope V1.

## 3. Plan d'implémentation

### 3.1. Modifications

**Fichier 1 : `Ribbon.xml`**

Remplacer :
```xml
<group id="MathCursorGroup" label="MathCursor">
```

Par :
```xml
<group id="MathCursorGroup" getLabel="OnGetGroupLabel">
```

**Fichier 2 : `RibbonCallback.cs`**

Ajouter une méthode :

```csharp
public string OnGetGroupLabel(IRibbonControl control)
{
    var v = Assembly.GetExecutingAssembly().GetName().Version;
    // Format "Major.Minor.Patch" — drop le Build (toujours 0)
    return $"MathCursor v{v.Major}.{v.Minor}.{v.Build}";
}
```

S'assurer que `using System.Reflection;` est en tête de fichier (à vérifier,
probablement déjà présent vu que `FeedbackBundle.cs` l'utilise).

### 3.2. Suppression du hardcoding

Dans `RibbonCallback.cs:73` (dialogue "Aide"), remplacer le string
`"Version 0.3.2 — beta\n\n"` par une lecture dynamique :

```csharp
var v = Assembly.GetExecutingAssembly().GetName().Version;
var versionLine = $"Version {v.Major}.{v.Minor}.{v.Build} — beta\n\n";
```

Comme ça **un seul endroit** détient la version (`AssemblyInfo.cs`) et
toute l'UI s'adapte au build.

### 3.3. Tests manuels

1. Build l'add-in en mode debug, lancer Word.
2. Vérifier que le groupe ribbon affiche `MathCursor v0.3.2`.
3. Vérifier que le dialogue "Aide" affiche la même version.
4. Bumper `AssemblyInfo.cs` à `0.3.3.0`, rebuild, vérifier que les deux
   endroits passent à `v0.3.3` sans modification de code.

Pas de test unitaire VSTO requis (l'UI VSTO n'est pas testable hors Word).

## 4. Hors scope

- ❌ Suffixe de canal (`-beta`, `-rc`, `-stable`) automatique selon build
  config — à traiter séparément si le besoin émerge.
- ❌ Lien cliquable "vérifier mises à jour" — phase release pipeline,
  hors V1.
- ❌ Numéro de build / commit hash dans le label — plus pertinent dans
  feedback bundle, déjà couvert par `FeedbackBundle.cs`.
- ❌ Localisation du préfixe "v" (FR/EN) — sera traité dans le brief
  localisation `2026-04-29-ribbon-i18n-menu-help.md`. Pour V1, hardcoder
  `v` (universel).

## 5. Pointers utiles

| Fichier | Rôle |
|---------|------|
| `adapter-vsto/src/MathCursor/Ribbon.xml` | Définition XML du ribbon. Modifier `<group>`. |
| `adapter-vsto/src/MathCursor/RibbonCallback.cs:69+` | Callbacks ribbon. Ajouter `OnGetGroupLabel`. |
| `adapter-vsto/src/MathCursor/RibbonCallback.cs:73` | Hardcoding version dans Help dialog, à dégager. |
| `adapter-vsto/src/MathCursor/Properties/AssemblyInfo.cs:36-37` | Source unique de vérité de la version. |
| `adapter-vsto/src/MathCursor/Host/FeedbackBundle.cs:106` | Référence pour la lecture runtime de la version. |

## 6. Ce qu'il NE faut PAS faire

- ❌ Dupliquer la version dans le ribbon en string littérale. Toujours
  lire `Assembly.GetExecutingAssembly().GetName().Version`.
- ❌ Créer une nouvelle constante `Version` dans le code C#. La source
  est `AssemblyInfo.cs`, point.
- ❌ Stocker la version dans un fichier séparé (`version.json`, etc.).
  Inutile — VSTO embarque déjà la version dans le DLL.
- ❌ Afficher les 4 segments (`v0.3.2.0`) — le 4ème est toujours 0 et
  fait du bruit visuel.

## 7. Validation finale

1. `MSBuild MathCursor.sln` → 0 erreur.
2. Word démarré : groupe ribbon affiche `MathCursor v0.3.2`.
3. Dialogue Aide affiche la même version.
4. Bump `AssemblyVersion` à `0.3.3.0`, rebuild → les deux endroits
   passent à `v0.3.3` automatiquement.

## 8. Estimation

~30 min — c'est un three-liner avec 2 fichiers touchés.
