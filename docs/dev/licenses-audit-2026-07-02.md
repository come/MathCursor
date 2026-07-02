# Audit des licences de dépendances — MathCursor

**Date :** 2026-07-02
**Objet :** valider la conformité GPLv3 et repérer toute dépendance
copyleft/restrictive qui fermerait l'option de relicenciement futur (auteur
unique).
**Verdict global :** ✅ **aucune incompatibilité.** Toutes les dépendances
first-party sont sous licences permissives (MIT / BSD / Apache-2.0), compatibles
GPLv3 **et** ne verrouillant pas un relicenciement ultérieur. Deux composants
système sont notés pour mémoire (WebView2, WebKitGTK) — liaison dynamique, sans
impact.

Statuts : **OK** = permissif, compatible GPLv3, n'entrave pas le relicenciement.
**NOTE** = compatible mais mérite une mention. **STOP** = incompatible / à traiter
(aucun ici).

---

## 1. NuGet (.NET)

Périmètre : `PackageReference` des `*.csproj` first-party (add-in, moteur, démo,
analyzers, outils) et projets de test.

| Paquet | Version | Usage | Licence | Statut |
|---|---|---|---|---|
| Microsoft.ML.OnnxRuntime | 1.16.3 | inférence NER (prod) | MIT | OK |
| WpfMath | 2.1.0 | rendu LaTeX popup (prod) | MIT | OK |
| System.Runtime.CompilerServices.Unsafe | 6.0.0 | dépendance ORT (prod) | MIT | OK |
| Microsoft.AspNetCore.Components.WebAssembly | 9.0.0 | démo web (prod) | MIT | OK |
| Microsoft.AspNetCore.Components.WebAssembly.DevServer | 9.0.0 | démo web (dev only) | MIT | OK |
| Microsoft.CodeAnalysis.CSharp | 4.8.0 | analyzers Roslyn | MIT | OK |
| Microsoft.CodeAnalysis.Analyzers | 3.3.4 | analyzers Roslyn | MIT | OK |
| DocumentFormat.OpenXml | 3.0.2 | générateur de tutoriel (outil) | MIT | OK |
| Microsoft.NET.Test.Sdk | 17.11.1 | tests (dev only) | MIT | OK |
| xunit | 2.9.0 / 2.9.2 | tests (dev only) | Apache-2.0 | OK |
| xunit.runner.visualstudio | 2.8.2 | tests (dev only) | Apache-2.0 | OK |
| Xunit.SkippableFact | 1.5.23 | tests (dev only) | MIT | OK |
| Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit | 1.1.2 | tests analyzers (dev only) | MIT | OK |

Seuls **OnnxRuntime**, **WpfMath** et **Unsafe** sont réellement redistribués
dans le binaire de l'add-in (déjà dans `THIRD-PARTY-NOTICES.md`). Le reste est
outillage de dev/test, non distribué.

## 2. Cargo (Rust) — cœur des hosts non-Word

Périmètre : `[dependencies]` déclarées de `mc-popup`, `mc-ner`, `mc-engine`.
Licences des crates directes (bien établies, dual MIT/Apache-2.0 pour l'essentiel
de l'écosystème) :

| Crate | Version | Usage | Licence | Statut |
|---|---|---|---|---|
| serde_json | 1 | (I/O JSON, tous les crates) | MIT OR Apache-2.0 | OK |
| wry | 0.45 | webview popup (mc-popup) | MIT OR Apache-2.0 | OK |
| tao | 0.30 | fenêtrage popup (mc-popup) | MIT OR Apache-2.0 | OK |
| windows | 0.58 | API Win32 (mc-popup, Windows) | MIT OR Apache-2.0 | OK |
| tokenizers | 0.20 | tokenizer NER (mc-ner) | Apache-2.0 | OK |
| ort | 2.0.0-rc.10 | ONNX Runtime binding (mc-ner) | MIT OR Apache-2.0 | OK |

Composants système atteints via ces crates (**non vendored**, liaison dynamique) :

| Composant | Via | Licence | Statut |
|---|---|---|---|
| WebView2 (Windows) | wry | Runtime Microsoft propriétaire, redistribuable, composant système | NOTE — pas lié statiquement, fourni par l'OS/runtime ; pas d'incidence GPL |
| WebKitGTK (Linux) | wry | LGPL-2.1 | NOTE — copyleft **faible**, lib système en liaison dynamique : compatible même avec un binaire GPL ; ne verrouille rien |

> Recommandation : câbler `cargo license` (ou `cargo-about`) dans la CI Rust pour
> figer l'inventaire transitif complet à chaque build. Aucun crate GPL/AGPL connu
> dans l'arbre.

## 3. npm (extension VS Code)

Périmètre : `package.json` de `adapter-vscode/extension`. **Uniquement des
`devDependencies`** (bundle esbuild) — rien n'est redistribué dans le `.vsix`
au-delà du JS compilé first-party.

| Paquet | Version | Licence | Statut |
|---|---|---|---|
| typescript | ^5.4.0 | Apache-2.0 | OK |
| esbuild | ^0.21.0 | MIT | OK |
| @types/node | ^20 | MIT | OK |
| @types/vscode | ^1.90.0 | MIT | OK |

## 4. Python (extension LibreOffice)

Périmètre : bibliothèques vendored dans `adapter-libreoffice/_spike_vendor/`
(présentes dans le dépôt ; embarquées dans le `.oxt` selon la configuration de
`build_oxt.py`). Toutes permissives :

| Paquet | Usage | Licence | Statut |
|---|---|---|---|
| numpy | calcul (NER) | BSD-3-Clause | OK |
| protobuf (google) | sérialisation | BSD-3-Clause | OK |
| onnxruntime (python) | inférence NER | MIT | OK |
| flatbuffers | dépendance ONNX | Apache-2.0 | OK |
| packaging | métadonnées | Apache-2.0 / BSD-2 (dual) | OK |

> Note : le préfixe `_spike_` marque du code exploratoire. Si ces libs sont
> effectivement embarquées dans le `.oxt` distribué, leurs textes de licence
> doivent accompagner l'archive (elles sont permissives → simple attribution).

## Conclusion

- **Conformité GPLv3 : validée.** Aucune dépendance sous licence incompatible
  (GPL/AGPL forte liée statiquement, « non-commercial », propriétaire non
  redistribuable).
- **Option de relicenciement futur : préservée.** Aucune dépendance copyleft
  forte n'est incorporée dans le code first-party. Les deux composants copyleft
  faible/propriétaire (WebKitGTK LGPL, WebView2) sont des libs système en liaison
  dynamique — elles n'imposent rien à la base de code.
- **Suivi recommandé (CI, non bloquant) :** `dotnet list package --include-transitive`,
  `cargo about generate`, `npm ls --prod` pour figer l'inventaire transitif à
  chaque release et détecter toute dérive.
