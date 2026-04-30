# Brief — Localisation FR/EN du ribbon, menu et dialogue d'aide

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-29
**Branche :** `lattice-engine`
**Public cible :** agent C#/VSTO autonome qui ne connaît pas le projet,
intervient sur la couche `adapter-vsto/`.

---

## 1. Le besoin

Aujourd'hui, **toute l'UI de l'add-in est hardcodée en français** : labels
ribbon, dialogue d'aide, dialogue de feedback, messages de
`FeedbackBundle`. Les profs beta-testeurs hors France (ou en école
bilingue, ou utilisant un Word EN-US) voient une UI étrangère sur leur
poste.

**Objectif** : permettre une UI localisée FR/EN, qui suit la langue de
Word (= `Globals.ThisAddIn.Application.LanguageSettings.LanguageID`) ou
fallback sur la `CultureInfo.CurrentUICulture` du poste.

## 2. Stratégie : ResX + détection de langue

L'infrastructure est **déjà préparée** mais inutilisée :

- `Properties/Resources.resx` existe (vide) avec son `Resources.Designer.cs`
  auto-généré.
- `MathCursor.csproj` déclare déjà `Resources.resx` comme `EmbeddedResource`.

Plan : **populer `Resources.resx` (FR par défaut) + créer
`Resources.en.resx` (EN), puis remplacer chaque string hardcodée par
`Properties.Resources.<Key>`.** .NET résout automatiquement la bonne
langue selon `CultureInfo.CurrentUICulture`.

### 2.1. Pourquoi ResX et pas un autre format

- Standard .NET, support natif Visual Studio (éditeur graphique de resx).
- Embedded dans le DLL → pas de fichier extra à déployer.
- Fallback hiérarchique automatique (`fr-FR` → `fr` → invariant = FR par
  défaut). Pas besoin de wiring custom.
- Pas de dépendance externe (vs. JSON/YAML qui demanderaient un
  `IStringLocalizer`).

### 2.2. Détection de langue Word

Au démarrage de `ThisAddIn` (`ThisAddIn_Startup`), forcer la culture
selon Word :

```csharp
var lang = Application.LanguageSettings.LanguageID[
    Microsoft.Office.Core.MsoAppLanguageID.msoLanguageIDUI];
// LCID 1036 = fr-FR, 1033 = en-US, etc.
var culture = lang == 1036 ? new CultureInfo("fr") : new CultureInfo("en");
Thread.CurrentThread.CurrentUICulture = culture;
```

Si Word est en autre chose que FR ou EN → fallback `en` (langue
internationale par défaut).

**Note** : la détection doit avoir lieu **avant** que `RibbonCallback`
charge `Ribbon.xml` (cf. §3.1 sur les labels dynamiques).

## 3. Strings à localiser

Inventaire exhaustif basé sur le code actuel.

### 3.1. `Ribbon.xml` (4 strings)

| Source actuelle (FR hardcodé) | Clé proposée | EN |
|-------------------------------|--------------|-----|
| `label="Signaler un souci"` | `Ribbon_ReportIssue_Label` | `Report an issue` |
| `screentip="Prépare un rapport…"` | `Ribbon_ReportIssue_Screentip` | `Prepares a report (log + screenshot + context) ready to send` |
| `label="Aide"` | `Ribbon_About_Label` | `Help` |
| `screentip="Guide rapide MathCursor"` | `Ribbon_About_Screentip` | `Quick MathCursor guide` |

**Implémentation Ribbon.xml** : remplacer les `label="..."` /
`screentip="..."` par des callbacks `getLabel="OnGetLabel"` /
`getScreentip="OnGetScreentip"`. Le callback dispatche selon
`control.Id` :

```csharp
public string OnGetLabel(IRibbonControl control)
{
    return control.Id switch
    {
        "ReportIssueButton" => Properties.Resources.Ribbon_ReportIssue_Label,
        "AboutButton"       => Properties.Resources.Ribbon_About_Label,
        _ => "",
    };
}
```

Pareil pour `OnGetScreentip`.

### 3.2. Dialogue d'aide — `RibbonCallback.cs:69-94` (1 gros bloc)

Le contenu actuel fait ~25 lignes de texte avec sections (`COMMENT ÇA
MARCHE`, `RACCOURCIS`, `REVENIR SUR UNE ÉQUATION`, `UN SOUCI ?`).

**Stratégie recommandée : un seul string ResX avec template `{0}` pour
la version.**

```csharp
// FR :
"MathCursor — Notation math au clavier pour Word\nVersion {0} — beta\n\n" +
"COMMENT ÇA MARCHE\n…"

// EN :
"MathCursor — Math notation by keyboard for Word\nVersion {0} — beta\n\n" +
"HOW IT WORKS\n…"
```

Et dans le code :

```csharp
var v = Assembly.GetExecutingAssembly().GetName().Version;
var text = string.Format(Properties.Resources.Help_Dialog_Body,
    $"{v.Major}.{v.Minor}.{v.Build}");
MessageBox.Show(text, Properties.Resources.Help_Dialog_Title, …);
```

Clés :
- `Help_Dialog_Title` (FR : `MathCursor — Aide`, EN : `MathCursor — Help`)
- `Help_Dialog_Body` (le gros multilignes avec `{0}` pour la version)

### 3.3. Dialogue feedback "Rapport prêt" — `RibbonCallback.cs:119-141`

Plusieurs strings :

| FR | Clé | EN |
|----|-----|-----|
| `Impossible de créer le rapport.\nEnvoie-nous un message à {0}…` | `Feedback_CreateError_Body` | `Could not create the report.\nPlease email us at {0}…` |
| `MathCursor — Signaler un souci` | `Feedback_Title` | `MathCursor — Report an issue` |
| `Le rapport est prêt !\n\nFichier copié dans le presse-papier…` | `Feedback_Ready_Body` | `Report is ready!\n\nFile copied to clipboard…` |
| `MathCursor — Rapport prêt` | `Feedback_Ready_Title` | `MathCursor — Report ready` |
| `Ajoute un petit mot…` (etc.) | dans `Feedback_Ready_Body` | idem |

### 3.4. `FeedbackBundle.cs` et `FeedbackDialog.cs`

À auditer dans cette même passe — strings probablement similaires
(messages succès / échec, labels du formulaire WPF). **Lister
exhaustivement avant le commit, en faisant `Grep` sur les strings entre
guillemets dans ces 2 fichiers.**

### 3.5. `HttpFeedbackSender.cs`

Strings de succès / échec réseau (`Merci ! Ton retour a été envoyé.` etc.)
à localiser aussi.

## 4. Plan d'implémentation

### 4.1. Étape 1 — populer Resources.resx (FR)

Ouvrir `Properties/Resources.resx` dans Visual Studio (éditeur graphique
intégré). Ajouter toutes les clés du §3 avec valeur FR. Vérifier que
`Resources.Designer.cs` se régénère avec les propriétés correspondantes.

### 4.2. Étape 2 — créer Resources.en.resx

Copier `Resources.resx` → `Resources.en.resx`, traduire les valeurs.
Visual Studio doit auto-générer les variantes anglaises.

S'assurer que **les deux fichiers ont les mêmes clés** (un script de
validation Python au build serait sain, mais hors scope V1 — checker à la
main pour cette première passe).

### 4.3. Étape 3 — détection de langue dans `ThisAddIn_Startup`

Ajouter au début de `ThisAddIn_Startup` (avant tout autre init) :

```csharp
private void ThisAddIn_Startup(object sender, EventArgs e)
{
    SetupCulture();
    // … reste du startup
}

private void SetupCulture()
{
    try
    {
        int lcid = Application.LanguageSettings.LanguageID[
            Microsoft.Office.Core.MsoAppLanguageID.msoLanguageIDUI];
        // LCID 1036 = fr-FR, 12=fr, 1033 = en-US, 9 = en
        bool isFrench = lcid == 1036 || (lcid & 0xFF) == 12;
        var culture = isFrench ? new CultureInfo("fr") : new CultureInfo("en");
        Thread.CurrentThread.CurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
    }
    catch { /* fallback : laisser CurrentUICulture du système */ }
}
```

### 4.4. Étape 4 — remplacer Ribbon.xml par des callbacks

Modifier `Ribbon.xml` :

```xml
<group id="MathCursorGroup" label="MathCursor">
    <button id="ReportIssueButton"
            getLabel="OnGetLabel"
            getScreentip="OnGetScreentip"
            size="large"
            imageMso="ReviewComments"
            onAction="OnReportIssueClicked" />
    <button id="AboutButton"
            getLabel="OnGetLabel"
            getScreentip="OnGetScreentip"
            size="large"
            imageMso="Help"
            onAction="OnAboutClicked" />
</group>
```

Ajouter dans `RibbonCallback.cs` les méthodes `OnGetLabel` et
`OnGetScreentip` (cf. §3.1).

**Note coordination** : si le brief
`2026-04-29-ribbon-version-display.md` est implémenté en parallèle, le
`<group>` utilise déjà `getLabel="OnGetGroupLabel"`. Ne pas casser cette
liaison — `OnGetGroupLabel` retourne `MathCursor v{version}` et n'a pas
besoin de localisation (terme universel).

### 4.5. Étape 5 — remplacer les strings inline

Pour chaque appel `MessageBox.Show("...", "...", ...)` ou string littéral
dans le code, remplacer par `Properties.Resources.<Cle>`.

### 4.6. Étape 6 — tests manuels

1. Démarrer Word en FR : tout est en français. Vérifier les 2 boutons
   ribbon, l'About, le Feedback flow complet.
2. Changer la langue de Word en EN-US, redémarrer Word : tout passe en
   anglais.
3. Vérifier qu'une langue non supportée (ex: ES) tombe sur EN par défaut.

## 5. Hors scope

- ❌ Localisation du **moteur core** (messages d'erreur du Lattice
  parser, etc.) — pour l'instant le core ne produit pas de strings UI,
  donc pas concerné.
- ❌ Plus de 2 langues (DE, ES, IT) — V1 fait FR/EN, on étendra plus
  tard si besoin.
- ❌ UI de configuration de langue manuelle (override de la détection
  Word) — pas demandé, complexité inutile.
- ❌ Localisation des popup de suggestion (popup au caret) — à
  vérifier : si elle contient des strings, à inclure dans cette passe ;
  sinon hors scope.
- ❌ Auto-traduction par script (DeepL etc.) — traduction humaine,
  petit volume.
- ❌ Pluralisation complexe — les strings actuelles n'en ont pas besoin
  (pas de `{n} elements`). Si jamais : utiliser `string.Format` avec
  ressource dédiée par cas.

## 6. Pointers utiles

| Fichier | Rôle |
|---------|------|
| `adapter-vsto/src/MathCursor/Ribbon.xml` | Labels / screentips ribbon. Passer en `getLabel`/`getScreentip`. |
| `adapter-vsto/src/MathCursor/RibbonCallback.cs:69-94` | Dialogue "Aide" complet. |
| `adapter-vsto/src/MathCursor/RibbonCallback.cs:103-159` | Dialogue feedback "Rapport prêt". |
| `adapter-vsto/src/MathCursor/Properties/Resources.resx` | Resx FR à populer. |
| `adapter-vsto/src/MathCursor/Properties/Resources.en.resx` | Resx EN à créer. |
| `adapter-vsto/src/MathCursor/Properties/Resources.Designer.cs` | Auto-généré, ne pas éditer à la main. |
| `adapter-vsto/src/MathCursor/ThisAddIn.cs` | `ThisAddIn_Startup` — y ajouter `SetupCulture()`. |
| `adapter-vsto/src/MathCursor/Host/FeedbackBundle.cs` | Strings de feedback à localiser. |
| `adapter-vsto/src/MathCursor/UI/FeedbackDialog.cs` | Labels du formulaire WPF feedback. |
| `adapter-vsto/src/MathCursor/Host/Feedback/HttpFeedbackSender.cs` | Messages succès/échec HTTP. |

## 7. Ce qu'il NE faut PAS faire

- ❌ Détection de langue à chaque appel (`Thread.CurrentThread.CurrentUICulture =
  …` dans chaque callback). Une fois au startup suffit.
- ❌ Stocker les strings dans des fichiers JSON/YAML ad-hoc — ResX est
  le pattern .NET standard, restons cohérents.
- ❌ Concaténer des strings localisées (`"Bonjour " + name + " bienvenue"`).
  Toujours `string.Format(Resources.Greeting, name)` pour permettre
  l'inversion mot/grammaire en EN.
- ❌ Mélanger FR et EN dans le même string ResX. Une langue par fichier.
- ❌ Modifier `Resources.Designer.cs` à la main — auto-généré.
- ❌ Toucher à la version-display dans le ribbon (cf. brief séparé) ; les
  deux briefs cohabitent sans conflit.

## 8. Validation finale

1. `MSBuild MathCursor.sln` → 0 erreur, 0 warning. Vérifier que les
   resx sont bien embedded (ouvrir le DLL avec `ildasm` ou regarder
   `bin/Debug/MathCursor.dll` taille augmentée).
2. Word en FR : 100% des strings testées en français.
3. Word en EN : 100% des strings testées en anglais.
4. Word en ES (langue non supportée) : tout en anglais (fallback).
5. Aucun string FR ne traîne dans le code C# en dehors des ResX.

## 9. Estimation

- ResX FR + EN : ~2h (le gros morceau = traduire le help dialog
  proprement)
- Détection langue + callbacks ribbon : ~30 min
- Migration des strings inline : ~1h30
- Tests manuels FR/EN : ~30 min
- **Total** : ~4h30

## 10. Coordination avec les autres briefs ribbon

Cette feature **partage** `Ribbon.xml` et `RibbonCallback.cs` avec les
deux autres briefs ribbon du même jour :

- `2026-04-29-ribbon-version-display.md` : ajoute `getLabel` sur le
  `<group>`. Pas de conflit, le label group n'a pas besoin de
  localisation (terme universel "MathCursor v0.3.2").
- `2026-04-29-ribbon-report-bug-button.md` : remplace l'icône `imageMso`
  du bouton existant. Pas de conflit.

**Recommandation d'ordre d'implémentation** : version-display d'abord
(plus simple), puis i18n (gros volume), puis icône custom (cosmétique).
Ou tout en parallèle si plusieurs branches.
