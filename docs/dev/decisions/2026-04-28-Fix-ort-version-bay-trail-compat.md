---
date: 2026-04-28
kind: Fix
température: molle
statut: acté
---

# Fix — Pin Microsoft.ML.OnnxRuntime à 1.16.3 pour compat CPU sans AVX

## Contexte

Une beta-testeuse (prof de maths, profil cible §"Validation produit" du
`CLAUDE.md`) sur **Lenovo G50-30** (Mfg 06/2016, Intel Bay Trail SoC sans
AVX/AVX2) ne peut pas démarrer MathCursor :

```
Échec du démarrage MathCursor :
Une exception a été levée par l'initialiseur de type pour
'Microsoft.ML.OnnxRuntime.NativeMethods'.
   à Microsoft.ML.OnnxRuntime.SessionOptions..ctor()
   à MathCursor.Detection.MathNerDetector..ctor(...)
   à MathCursor.ThisAddIn.ThisAddIn_Startup(...)
```

Word x64, VC++ 2022 Redistributable installé : aucun effet. Le crash est
sur le `static cctor` de `NativeMethods` — donc avant qu'une `SessionOptions`
puisse s'instancier. C'est le DLL natif `onnxruntime.dll` qui ne se charge
pas.

**Cause racine** : ONNX Runtime 1.17+ a progressivement durci ses
requirements CPU au niveau du DLL natif. ORT 1.20 (qu'on utilisait) a des
chemins d'init MLAS au-delà de SSE4.2. Sur Bay Trail (CPU 2014, SSE4.2 OK
mais pas d'AVX/AVX2), ça génère une instruction illégale au DllMain → crash
immédiat. ORT **1.16.x** est le dernier release avec baseline SSE2, connu
compatible avec tout CPU x86_64 jusqu'à Sandy Bridge / Bay Trail.

Le hardware cible (lycéen avec PAP / prof de maths) inclut beaucoup de
machines 2014-2016 en éducation FR. C'est une contrainte structurelle, pas
un cas marginal.

## Décision

Downgrade `Microsoft.ML.OnnxRuntime` de `1.20.1` à `1.16.3` dans
`adapter-vsto/src/MathCursor/MathCursor.csproj`.

Notre modèle ONNX (XLM-R int8 hier, distilmult v4 int8 aujourd'hui) reste
compatible avec ORT 1.16 — l'opset utilisé par `optimum` 1.x est antérieur
aux changements 1.16→1.20.

Sur Bay Trail, même avec ORT 1.16, le modèle quantizé AVX2 tournera en
fallback scalaire (~100-200 ms par phrase au lieu de ~25 ms). Acceptable
au clavier (l'élève tape pas plus vite), donc pas besoin de re-quantizer.

## Implémentation

- `adapter-vsto/src/MathCursor/MathCursor.csproj` : `<PackageReference
  Include="Microsoft.ML.OnnxRuntime" Version="1.16.3" />`. Commentaire
  explicite dans le csproj pour empêcher un re-bump accidentel.
- Aucun changement d'API requis : le code utilise `SessionOptions`,
  `InferenceSession`, `NamedOnnxValue.CreateFromTensor`, `DenseTensor<T>` —
  tout est stable entre 1.16 et 1.20.
- Build VSTO Release vert au premier essai, tests core 136/136 verts.
- Installer rebuilt en version `0.4.0` (cf. `MathCursor.iss`).

## Bénéfices

- Compat retro pour CPUs 2014-2016 (Bay Trail, Apollo Lake, etc.)
- Aucun changement dans l'API publique du détecteur NER
- Réversible : on peut re-bumper plus tard si on packagine en dual-runtime

## Ce qui n'est PAS modifié

- API publique `MathNerDetector.Detect(string) → IReadOnlyList<DetectedZone>`
- Modèle ONNX, notebook d'entraînement, code de conversion
- Code consommateur (`SuggestionService`, `ThisAddIn`)

## Citation utilisateur

Thread du 2026-04-28 :

> « docs/dev/briefs/2026-04-28-ort-downgrade-bay-trail-compat.md lis ca et
>   genere moi un installer stp »

Brief intégral suivi tel quel : downgrade pin 1.16.3, sans toucher au
modèle ni au code.

## Plan de repli

Si le crash persiste sur Bay Trail malgré 1.16.3 :
1. Tester ORT 1.15.1 (encore plus ancien, baseline SSE2 garantie).
2. Examiner Event Viewer pour identifier l'instruction illégale exacte.
3. Si toujours pas : packaging dual-runtime ou désactivation NER
   conditionnelle (brief séparé à écrire).
