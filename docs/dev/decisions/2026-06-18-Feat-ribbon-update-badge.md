# Feat — Indicateur « MAJ dispo » sur l'onglet ruban MathCursor

**Date :** 2026-06-18
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-18-Feat-usage-counter-telemetry.md](2026-06-18-Feat-usage-counter-telemetry.md) (réutilise l'infra HTTP : `FeedbackSenderFactory.Resolve*Url`, HttpClient TLS 1.2)

## Citation acté

> « remplacer le nom de l'onglet math cursor par "Math Cur (* maj)" ce serait
> jouabel ? » — utilisateur, 2026-06-18 (plan approuvé en plan mode)

## Contexte

Les utilisateurs installent un `.exe` et n'ont **aucun signal** d'une nouvelle
version — ils restent sur une vieille build sans le savoir. On veut un
indicateur **passif, non-intrusif** dans Word.

Découverte pendant le cadrage : `AssemblyInfo.cs` (`AssemblyVersion` /
`AssemblyFileVersion`) est bumpé **manuellement** à chaque release, mais ni
`build-iss` ni `/deploy-prod` ne le documentent → **oubli sur la 0.11.0** (elle
se déclare `0.10.3` en interne). Comme l'indicateur compare la version interne à
la dernière en ligne, cette source DOIT être fiable.

## Décision

**Indicateur = label de l'onglet ruban décoré** (ex. « MathCursor ● MAJ »)
quand une version plus récente est dispo. Pas de pastille GDI ni de bouton
dédié (écartés en plan mode).

- **Backend** : endpoint `GET /api/v1/version` → `{ "latest": "x.y.z" }`
  (`docs/functions/api/v1/version.js`), source partagée `functions/_latest.js`
  (importée aussi par `download/[[filename]].js`). Pas de log analytics.
- **Adapter (L3)** : `Host/Update/UpdateChecker.cs` (statique, best-effort, même
  pattern que `UsageStatsClient`) : `GET` au démarrage (fire-and-forget),
  compare en **semver normalisé Major.Minor.Build** la version courante
  (`Strings.FormatVersion(Assembly…Version)`, = celle d'« À propos ») à `latest`.
  Si supérieure → `UpdateAvailable=true` + callback d'invalidation ruban.
  Hors-ligne/erreur → no-op (pas de marqueur, pas de gel).
- **Ruban — marqueur onglet** : `OnGetTabLabel` renvoie le label décoré si
  `UpdateAvailable` ; `RibbonCallback.InvalidateUpdateBadge()` invalide l'onglet
  **et** le bouton ci-dessous.
- **Ruban — groupe dédié « Mise à jour »** (raffinement validé 2026-06-18 ; un
  bouton seul dans le groupe Conversion était trop discret — il passait pour un
  bouton de conversion de plus) : un `<group id="MathCursorUpdateGroup">`
  **tout à droite** de l'onglet, `getVisible="OnGetUpdateGroupVisible"`
  (= `UpdateAvailable` → **groupe entier caché par défaut**), contenant un bouton
  large « Mise à jour disponible » (icône native). Clic → ouvre la page releases.
  Le marqueur onglet attire l'œil ; ce groupe rend la chose visible + actionnable.
- **Action** : « À propos » enrichi — si MAJ dispo, affiche la version dispo +
  ouvre la page releases (`Process.Start(...releases.html)`).
- **Correctif de version** : `AssemblyVersion`/`FileVersion` bumpés à **0.11.1.0**
  (fix-forward, décision utilisateur). La 0.11.0 déjà déployée reste telle quelle
  (cosmétique : affiche 0.10.3). **`/deploy-prod` doit désormais bumper
  `AssemblyInfo.cs`** (ajout à l'Étape 2 du skill) pour ne plus oublier.

## Tradeoff & alternatives écartées

- **Vraie pastille GDI** (point rouge composé sur une icône via `GetImageMso` +
  dessin) : écartée — fragile (System.Drawing, DPI/thème), point discret.
- **Bouton « Mettre à jour » conditionnel** (`getVisible`) : écarté par
  l'utilisateur au profit du marqueur sur l'onglet (plus simple, plus visible).
- **Marqueur texte sur un bouton** (« À propos ● ») : moins visible que l'onglet.
- **Parser le `Content-Disposition` de `/download/latest.exe`** au lieu d'un
  endpoint : écarté — polluerait les stats de téléchargement (Analytics Engine).
- **Re-release 0.11.0 corrigée** : écartée (risque updater VSTO sur même numéro)
  → fix-forward en 0.11.1.

## Conséquences

- **Backend** : `functions/_latest.js` (nouveau) + `functions/api/v1/version.js`
  (nouveau) ; `download/[[filename]].js` importe `_latest.js`.
- **Adapter (L3)** : nouveau `Host/Update/UpdateChecker.cs` ;
  `FeedbackSenderFactory` (+ `ResolveVersionUrl`) ; `RibbonCallback`
  (`OnGetTabLabel` conditionnel, `InvalidateUpdateBadge`, About enrichi) ;
  `ThisAddIn` (check au Startup) ; `Strings` (label MAJ + textes À-propos, FR/EN) ;
  `MathCursor.csproj` (déclarer `UpdateChecker.cs`) ; `AssemblyInfo.cs` (0.11.1.0).
- **Process** : `/deploy-prod` Étape 2 doit bumper `AssemblyInfo.cs` (sinon
  « À propos » + l'indicateur se basent sur une version figée).
- **Confidentialité** : un `GET` au démarrage, **aucune donnée envoyée**. Ajouter
  1 ligne à `privacy.html` (« vérification de mise à jour, aucune donnée
  personnelle »). Pas d'opt-out séparé (comportement standard de logiciel).
- **API publique** : inchangée.

## Validation post-fix

1. `curl https://mathcursor.pages.dev/api/v1/version` → `{"latest":"x.y.z"}`.
2. Unitaire : comparateur semver (`0.10.3<0.11.1` → MAJ ; égal/supérieur → pas
   de MAJ ; parsing robuste) dans `adapter-vsto/tests/` (pure-compute).
3. Manuel Word : endpoint « latest » > courante → onglet « MathCursor ● MAJ » au
   démarrage + « À propos » propose Télécharger ; égal → pas de marqueur ;
   réseau coupé → pas de marqueur, pas de gel.
