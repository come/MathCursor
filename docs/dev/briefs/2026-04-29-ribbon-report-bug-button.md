# Brief — Icône custom "Signaler un bug" dans le bandeau Word

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-29
**Branche :** `lattice-engine`
**Public cible :** agent C#/VSTO + design autonome qui ne connaît pas le
projet, intervient sur la couche `adapter-vsto/`.

---

## 1. Le besoin (et clarification)

L'utilisateur a demandé : *"éventuellement une petite icone pour signaler
un bug ?"*

**À noter** : le bouton **existe déjà** dans le ribbon, défini dans
`Ribbon.xml:7-12` :

```xml
<button id="ReportIssueButton"
        label="Signaler un souci"
        screentip="Prépare un rapport (log + screenshot + contexte) prêt à envoyer"
        size="large"
        imageMso="ReviewComments"
        onAction="OnReportIssueClicked" />
```

L'icône utilisée (`imageMso="ReviewComments"`) est une **icône built-in
Office** (bulle de commentaire), réutilisée par défaut. Ce qui manque,
c'est une **icône dédiée** — typiquement un insecte / bug, ou un point
d'exclamation, ou une enveloppe — qui rend le bouton **immédiatement
identifiable** comme "signaler un problème" plutôt que générique.

Donc la portée de ce brief est : **remplacer l'icône built-in
`imageMso="ReviewComments"` par une icône custom embarquée dans le DLL.**

## 2. Spécifications de l'icône

### 2.1. Concept visuel

Trois directions possibles, à trancher par l'utilisateur :

| Concept | Pour | Contre |
|---------|------|--------|
| **Insecte / bug** 🐞 | Universellement compris dans tech | Trop "tech", peut paraître ludique pour un public lycée/prof |
| **Point d'exclamation dans bulle** ⚠ | Sérieux, professionnel | Confondable avec icône Word warning native |
| **Enveloppe + petit ⚠** ✉ | "Envoie-nous un message" littéral | Plus lisible mais demande une icône composée |

**Recommandation** : un **insecte stylisé** (silhouette simple, monochrome
ou 2 couleurs max), type "icone material design ladybug". Plus distinctif
et amical, en cohérence avec le ton MathCursor (outil bienveillant pour
PAP).

### 2.2. Contraintes techniques Office Ribbon

- **Tailles requises** : 32×32 px (large button) **et** 16×16 px (si on
  veut supporter le mode collapsed du ribbon). Le `size="large"` actuel
  utilise le 32×32.
- **Format** : PNG avec canal alpha (fond transparent obligatoire).
- **Couleurs** : Office ribbon a deux thèmes (clair / sombre). Une
  icône monochrome ou avec contraste suffisant fonctionne sur les deux.
  **Éviter le rouge pur** qui jure avec l'iconographie Word.
- **Style** : trait fin, pas de dégradé fort, lecture immédiate à 32 px.
  Inspiration : icônes Office natives, Lucide / Heroicons monochromes.

### 2.3. Sourcing de l'icône

Trois options :

**A. Icône libre de droits** (recommandé pour V1) — chercher sur
[Lucide](https://lucide.dev/icons/) ("bug", "alert-triangle", "send"),
[Tabler Icons](https://tabler.io/icons), ou icônes Material. Licence MIT
ou équivalent. **Pas de paiement, pas de friction.**

**B. Création custom** — Figma / Inkscape, demande ~30 min de design.

**C. Réutiliser un emoji** rendu en PNG via une lib comme Twemoji.
Marche mais qualité variable selon les rendus.

**Décision déléguée à l'agent** : si Lucide a "bug" en vector libre,
l'utiliser tel quel. Sinon, basculer sur création simple Inkscape.

## 3. Plan d'implémentation

### 3.1. Créer le dossier d'assets

Le projet n'a pas de dossier `Resources/` ou `Images/` aujourd'hui (cf.
inventaire couche VSTO). En créer un :

```
adapter-vsto/src/MathCursor/Resources/Images/
    bug-32.png        (icône 32×32 PNG transparent)
    bug-16.png        (icône 16×16 PNG transparent)
```

### 3.2. Embedder les images dans le DLL

Modifier `MathCursor.csproj` :

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Images\bug-32.png">
    <LogicalName>MathCursor.Resources.Images.bug-32.png</LogicalName>
  </EmbeddedResource>
  <EmbeddedResource Include="Resources\Images\bug-16.png">
    <LogicalName>MathCursor.Resources.Images.bug-16.png</LogicalName>
  </EmbeddedResource>
</ItemGroup>
```

Le `LogicalName` permet de retrouver l'image au runtime via
`Assembly.GetManifestResourceStream("MathCursor.Resources.Images.bug-32.png")`.

### 3.3. Modifier `Ribbon.xml`

Remplacer `imageMso="ReviewComments"` par un callback `getImage` :

```xml
<button id="ReportIssueButton"
        label="Signaler un souci"
        screentip="…"
        size="large"
        getImage="OnGetImage"
        onAction="OnReportIssueClicked" />
```

Si le brief i18n (`2026-04-29-ribbon-i18n-menu-help.md`) est implémenté
en parallèle, le bouton aura aussi `getLabel` et `getScreentip` —
plusieurs callbacks coexistent sans souci.

### 3.4. Implémenter `OnGetImage` dans `RibbonCallback.cs`

```csharp
public System.Drawing.Bitmap OnGetImage(IRibbonControl control)
{
    var resource = control.Id switch
    {
        "ReportIssueButton" => "MathCursor.Resources.Images.bug-32.png",
        // À l'avenir, d'autres boutons custom mappent ici
        _ => null,
    };
    if (resource == null) return null;
    using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
    {
        if (stream == null) return null;
        return new System.Drawing.Bitmap(stream);
    }
}
```

**Note** : VSTO accepte `Bitmap` ou `IPictureDisp` comme retour de
`getImage`. `Bitmap` est plus simple. Référence MS :
[Office.IRibbonControl.GetImage](https://learn.microsoft.com/en-us/dotnet/api/microsoft.office.core.iribbonextensibility).

### 3.5. Garder `imageMso` pour `AboutButton`

Le bouton "Aide" (`imageMso="Help"`) reste avec l'icône built-in Office
— pas de raison de le custom, l'icône Help universelle suffit.

## 4. Tests manuels

1. Build l'add-in, lancer Word.
2. Onglet Home → groupe MathCursor : le bouton "Signaler un souci"
   affiche l'icône custom (bug 32×32).
3. Réduire la fenêtre Word jusqu'à ce que le ribbon collapse → le bouton
   affiche correctement le 16×16 (si la version 16 existe). Sinon Office
   redimensionne le 32×32 (acceptable mais moins net).
4. Hover : le screentip s'affiche normalement.
5. Click : `OnReportIssueClicked` se déclenche comme avant (pas de
   régression sur le flow feedback).

## 5. Hors scope

- ❌ Refonte complète de l'identité visuelle de l'add-in (logo, splash,
  thème).
- ❌ Icônes pour les autres boutons (`AboutButton` reste sur `imageMso`).
- ❌ Animation / état hover custom — Office gère le hover natif.
- ❌ Mode sombre / clair adaptatif — utiliser une icône qui marche dans
  les deux (monochrome ou contraste suffisant).
- ❌ Icône au format SVG / vectoriel — Office ribbon ne supporte pas
  SVG natif en VSTO ; rester en PNG.
- ❌ Localisation de l'icône — une icône par culture, inutile (l'icône
  est universelle).
- ❌ Création d'un set complet d'icônes pour de futurs boutons — au
  cas par cas, pas en avance.

## 6. Pointers utiles

| Fichier | Rôle |
|---------|------|
| `adapter-vsto/src/MathCursor/Ribbon.xml:7-12` | Bouton à modifier (passer de `imageMso` à `getImage`). |
| `adapter-vsto/src/MathCursor/RibbonCallback.cs:64+` | Ajouter `OnGetImage`. |
| `adapter-vsto/src/MathCursor/MathCursor.csproj` | Ajouter `EmbeddedResource` pour les PNG. |
| `adapter-vsto/src/MathCursor/Resources/Images/` | À créer. |
| [Lucide bug icon](https://lucide.dev/icons/bug) | Source recommandée pour l'icône (MIT). |
| [MS guideline ribbon image](https://learn.microsoft.com/en-us/office/vba/api/overview/library-reference/imagemso-images-gallery) | Référence iconographie ribbon Office. |

## 7. Ce qu'il NE faut PAS faire

- ❌ Garder `imageMso` ET ajouter une icône custom — un seul mécanisme,
  remplacer.
- ❌ Charger l'image depuis le système de fichiers (`File.ReadAllBytes`).
  Embedded resource = portable, pas de chemin à gérer.
- ❌ Utiliser une icône en violation de licence (Font Awesome Pro,
  iconmonstr commercial, etc.). Rester sur du libre de droits MIT/CC0.
- ❌ Choisir une icône rouge vif — trop alarmiste pour le contexte
  "signaler un souci" (qui doit rester engageant, pas anxiogène).
- ❌ Oublier le `LogicalName` dans `csproj` — sinon le nom complet de
  ressource inclura la structure de dossier de manière imprédictible
  (`adapter_vsto.src…` etc.).
- ❌ Bloquer l'app si l'image manque — `OnGetImage` retourne `null`,
  Office tombera sur l'icône par défaut sans crasher.

## 8. Validation finale

1. Build → 0 erreur, le DLL contient les 2 PNG embedded (vérifier avec
   `ildasm` ou `dotPeek` si doute).
2. Word démarre, ribbon affiche l'icône custom.
3. Test à différentes tailles de fenêtre (large / collapsed).
4. Click toujours fonctionnel → flow feedback intact.

## 9. Estimation

- Sourcing icône (Lucide ou création) : 15-30 min
- Export PNG 32+16 + alpha : 15 min
- Modif csproj + Ribbon.xml + callback : 30 min
- Tests visuels : 15 min
- **Total** : ~1h-1h30

## 10. Coordination avec les autres briefs ribbon

- `2026-04-29-ribbon-version-display.md` : modifie `<group>`, pas le
  bouton. Pas de conflit.
- `2026-04-29-ribbon-i18n-menu-help.md` : modifie `getLabel` /
  `getScreentip` du même bouton. Cohabitent : `Ribbon.xml` gagne plusieurs
  callbacks (`getLabel`, `getScreentip`, `getImage`, `onAction`) sans
  problème.

Si les 3 briefs sont implémentés ensemble, faire un seul commit
multi-fichier ; sinon ordre recommandé : version (1h) → icon (1h) →
i18n (4h).
