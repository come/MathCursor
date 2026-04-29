# Brief — Downgrade ONNX Runtime pour compat CPUs sans AVX (Bay Trail et antérieurs)

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-28
**Branche :** `lattice-engine`
**Public cible :** agent C#/MSI autonome qui ne connaît pas le projet.

---

## 1. Le bug observé

Une utilisatrice (prof de maths beta-testeuse, profil cible §"Validation
produit" du `CLAUDE.md`) sur **Lenovo G50-30** (Mfg 06/2016, Intel Bay Trail
SoC sans AVX/AVX2) ne peut pas démarrer MathCursor :

```
Échec du démarrage MathCursor :
Une exception a été levée par l'initialiseur de type pour
'Microsoft.ML.OnnxRuntime.NativeMethods'.

   à Microsoft.ML.OnnxRuntime.SessionOptions..ctor()
   à MathCursor.Detection.MathNerDetector..ctor(String modelDir, Double threshold)
   à MathCursor.ThisAddIn.ThisAddIn_Startup(...)
```

Word est en **64 bits** (LTSC MSO 16.0.14334.20624), pas de mismatch x86/x64.
**VC++ 2022 Redistributable a été installé**, ça ne change rien.

## 2. Diagnostic

Le crash est sur le `static cctor` de `Microsoft.ML.OnnxRuntime.NativeMethods`
— donc avant même qu'une `SessionOptions` puisse s'instancier. C'est le DLL
natif `onnxruntime.dll` qui ne se charge pas.

**Cause** : ONNX Runtime 1.17+ a progressivement durci ses requirements CPU
au niveau du **DLL natif lui-même**. La version 1.20 (qu'on utilise) a des
chemins d'initialisation MLAS qui utilisent des instructions au-delà de
SSE4.2. Sur Bay Trail (CPU 2014, SSE4.2 OK mais pas d'AVX/AVX2), ça génère
une instruction illégale au DllMain → crash immédiat.

ORT **1.16.x** est le dernier release avec baseline SSE2 dans le DLL natif,
connu compatible avec tout CPU x86_64 jusqu'à Sandy Bridge / Bay Trail.

Le hardware cible "lycéen avec PAP / prof de maths" inclut beaucoup de
machines 2014-2016 en éducation française. **C'est une contrainte
structurelle**, pas un cas marginal.

## 3. Décision à appliquer

Downgrader la dépendance `Microsoft.ML.OnnxRuntime` de `1.20.1` à `1.16.3`
dans le projet VSTO. Notre modèle ONNX actuel (XLM-R int8 AVX2-quantized) ET
le futur (distilmult v4 int8 AVX2-quantized) restent compatibles avec ORT
1.16 — l'opset utilisé par `optimum` 1.x est antérieur à ce qui change entre
1.16 et 1.20.

**Note importante** : sur Bay Trail, même avec ORT 1.16, le modèle quantizé
AVX2 tournera en fallback scalaire (~100-200 ms par phrase au lieu de ~25 ms).
Acceptable au clavier (élève tape pas plus vite que ça), donc pas besoin de
re-quantizer le modèle dans ce brief — orthogonal.

## 4. Livrables

### a) Bump version ORT

Fichier : `adapter-vsto/src/MathCursor/MathCursor.csproj`

```xml
<!-- Avant -->
<PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.20.1" />

<!-- Après -->
<PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.16.3" />
```

Vérifier qu'aucun autre `.csproj` du repo ne référence une version
incompatible (en particulier dans `core-csharp/` ou tests). Si un autre
projet pin 1.20+, l'aligner sur 1.16.3 aussi.

### b) Build + MSI

1. `dotnet restore && dotnet build MathCursor.sln -c Release` → 0 warning
   nouveau lié au downgrade.
2. Vérifier qu'aucune API utilisée dans le code C# n'a été retirée /
   renommée entre 1.20 et 1.16. Endroits sensibles à inspecter :
   - `adapter-vsto/src/MathCursor/Detection/MathNerDetector.cs` —
     instanciation de `SessionOptions`, configuration des providers, etc.
   - Tout autre fichier qui `using Microsoft.ML.OnnxRuntime`.
   APIs susceptibles d'avoir bougé entre 1.16 et 1.20 :
   - `SessionOptions.AppendExecutionProvider_*` (renommages mineurs)
   - `RunOptions` flags
   - Méthodes async (apparues plus tard, à éviter si 1.16 ne les a pas)
3. Re-construire le MSI signé via le pipeline existant (chercher le projet
   d'installer ou la doc dans `docs/dev/decisions/2026-04-24-Fix-cert-trustedpublisher-only.md`
   et `2026-04-24-UX-installer-imports-cert.md`).

### c) Test sur Bay Trail (idéalement)

Si possible, valider sur la même machine que l'utilisatrice (ou une VM avec
CPU émulé sans AVX, ex. via QEMU `-cpu Nehalem`). Sinon : envoyer le MSI
test à l'utilisatrice, attendre confirmation.

Sur une machine moderne (dev), vérifier juste que MathCursor démarre, charge
le modèle, et fait une inférence correcte. Le test CPU-spécifique ne peut
être fait que sur le hardware concerné.

### d) ADR de suivi

Fichier à créer : `docs/dev/decisions/2026-04-28-Fix-ort-version-bay-trail-compat.md`.

Squelette :
- **Kind** : Fix
- **Température** : molle (réversible — on peut re-bumper plus tard si on
  veut profiter d'optims ORT récentes, par exemple via un dual-runtime
  packaging)
- **Statut** : acté
- **Décision** : pin Microsoft.ML.OnnxRuntime à 1.16.3 pour compat CPU
  x86_64 SSE4.2 (sans AVX requis) — couvre Bay Trail et ultérieur.
- **Pourquoi** : reprendre §1-2 de ce brief.
- **Citation utilisateur** : ce thread + image stack trace de l'utilisatrice.

### e) Note utilisateur

Ajouter dans le README ou la page d'install (Cloudflare Pages, voir ADR
`2026-04-24-Feat-cloudflare-deployment.md`) :

> **Prérequis matériel** : CPU x86_64 avec SSE4.2 (Intel 2008+ ou AMD
> équivalent). Tous les processeurs depuis Intel Core 2 / AMD Bulldozer
> sont supportés, y compris les Celeron / Pentium d'entrée de gamme
> (Bay Trail 2014, Apollo Lake, etc.).

## 5. Cas de test obligatoires

| Cas | Attendu |
|-----|---------|
| Build local avec ORT 1.16.3 | Compile sans erreur ni warning nouveau |
| Lancer Word + add-in sur machine dev (Haswell+) | Démarrage OK, NER détecte normalement |
| Lancer Word + add-in sur Bay Trail | Démarrage OK (plus de TypeInitializationException) |
| Inférence sur Bay Trail | Latence p95 < 250 ms, résultats cohérents |
| Tests unitaires existants | Tous passent (pas de régression) |

## 6. Pointers utiles

| Fichier | Rôle |
|---------|------|
| `adapter-vsto/src/MathCursor/MathCursor.csproj` | PackageReference à modifier |
| `adapter-vsto/src/MathCursor/Detection/MathNerDetector.cs` | Code qui instancie `SessionOptions`, à inspecter pour API breakage |
| `adapter-vsto/src/MathCursor/ThisAddIn.cs` | Point d'entrée, où le crash apparaît |
| `docs/dev/briefs/2026-04-27-ner-distilmult-adoption.md` | Brief en cours sur le futur modèle (à coordonner — la nouvelle version d'ORT doit aussi être 1.16.3) |
| `docs/dev/decisions/2026-04-24-Fix-cert-trustedpublisher-only.md` | Process MSI signé |
| `docs/dev/decisions/2026-04-24-UX-installer-imports-cert.md` | Installer + cert |

## 7. Ce qu'il NE faut PAS faire

- ❌ Re-quantizer le modèle en mode generic dans CE brief — orthogonal au
  problème, à traiter dans un brief séparé si besoin (cf. §3 note).
- ❌ Build ONNX Runtime depuis source avec flags spéciaux — trop d'effort,
  ORT 1.16 prébuilt fait le job.
- ❌ Implémenter un fallback "désactiver le NER si pas d'AVX" — perte de
  qualité de détection, pas une vraie solution. Garder pour l'option 4 si
  un jour on doit re-bumper ORT.
- ❌ Bumper à 1.17 ou 1.18 "au cas où" — la frontière SSE2/SSE4.2 baseline
  côté DLL natif n'est pas documentée précisément, on prend la version
  la plus safe connue (1.16.3).
- ❌ Toucher au modèle ONNX, au notebook d'entraînement, ou au code de
  conversion — rien à voir.
- ❌ Modifier l'API publique `MathNerDetector.Detect(string)` — seule la
  dépendance ORT bouge.

## 8. Validation finale

1. `dotnet build MathCursor.sln -c Release` → succès.
2. `dotnet test adapter-vsto/tests/MathCursor.Tests/` → tous les tests
   passent (smoke `MathNerDetector.Detect("On a f(x) = 2x + 1")` doit
   renvoyer un span MATH).
3. MSI signé reconstruit, taille pas anormalement différente (~même
   poids qu'avant — ORT 1.16 vs 1.20 diffère de quelques Mo seulement).
4. Test chez l'utilisatrice Bay Trail :
   - Désinstaller la version actuelle.
   - Installer le nouveau MSI.
   - Lancer Word → MathCursor démarre sans dialog d'erreur.
   - Taper `On a f(x) = 2x + 1` + Ctrl+Espace → conversion OMath OK.
   - Vérifier `%APPDATA%\MathCursor\logs\mathcursor.log` — pas d'erreur,
     latences acceptables (<300 ms par inférence sur ce CPU).
5. ADR créé.
6. Commit unique : "fix: pin Microsoft.ML.OnnxRuntime à 1.16.3 pour compat
   CPU sans AVX (Bay Trail et antérieurs)".

## 9. Estimations

- Bump version + build local : 15 min
- Inspection API breakage 1.16 vs 1.20 : 30-60 min (probablement aucun
  changement breaking pour notre usage simple)
- Test Bay Trail (ou attente retour utilisatrice) : variable
- ADR + doc utilisateur : 30 min
- **Total estimé** : ~2 h dev + temps de feedback utilisateur

## 10. Plan de repli si downgrade ORT 1.16 ne suffit pas

Si après downgrade le crash persiste sur Bay Trail (peu probable mais
possible) :

1. Examiner le log Windows Event Viewer pour identifier l'instruction
   illégale exacte (Application → Windows Error Reporting / .NET Runtime).
2. Tester ORT 1.15.1 (encore plus ancien, baseline garantie SSE2).
3. Si toujours pas : c'est probablement un autre problème (DLL manquante,
   path Unicode, antivirus), à diagnostiquer au cas par cas.
4. Dernier recours : option 4 du diagnostic initial — packaging dual-runtime
   ou désactivation NER conditionnelle. Brief séparé à écrire à ce
   moment-là.
