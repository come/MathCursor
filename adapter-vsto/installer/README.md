# MathCursor — Installer Inno Setup

Installer léger per-user pour distribution aux beta testeurs (PAP + profs de maths).
Pas besoin de privilèges admin, pas de MSI, pas de signature code pour démarrer.

## Structure

```
installer/
├── MathCursor.iss        # script Inno Setup (source de l'installer)
├── build.ps1             # build + copie binaires + compile l'EXE
├── after-install.txt     # texte montré après install (auto-généré)
├── payload/              # (auto) binaires copiés depuis bin/Release
│   ├── MathCursor.dll, .vsto, .manifest, .config
│   ├── MathCursor.Engine.dll, MathCursor.Serialization.dll, MathCursor.HostContract.dll
│   ├── WpfMath.dll, OnnxRuntime.dll, ...
│   └── models/           # modèle NER (à placer manuellement, ~200 Mo)
└── output/               # (auto) EXE final
    └── MathCursor-Setup-0.1.0.exe
```

## Prérequis côté dev (toi)

1. **Visual Studio 2022** avec charge **Office Development** installée (compile le projet VSTO)
2. **Inno Setup 6** — https://jrsoftware.org/isinfo.php (compile le .iss → .exe)
3. **Modèle NER** dans `D:\Software\DocMath\models\` (le script copie depuis là)

## Build en une commande

```powershell
powershell -ExecutionPolicy Bypass -File adapter-vsto\installer\build.ps1
```

Ce que fait le script :
1. MSBuild Release du projet VSTO
2. Copie les binaires dans `payload/`
3. Copie le modèle NER dans `payload/models/` (si trouvé)
4. Appelle ISCC.exe pour compiler le `.iss`
5. Produit `output/MathCursor-Setup-0.1.0.exe`

## Prérequis côté beta testeur

- Windows 10 ou 11 (inclut .NET Framework 4.8)
- Microsoft Word 2016 ou plus récent (inclut le VSTO Runtime)
- Rien d'autre — double-cliquer l'EXE, suivre l'assistant

## Ce que fait l'installer

- Copie les fichiers dans `%LocalAppData%\MathCursor\`
- Enregistre l'add-in dans `HKCU\Software\Microsoft\Office\Word\Addins\MathCursor`
  avec `LoadBehavior = 3` (chargement auto au démarrage de Word)
- Affiche un message post-install expliquant comment vérifier

Désinstallation : `Panneau de configuration → Programmes → MathCursor → Désinstaller`.
Ça retire les fichiers ET les clés registre (donc Word oublie l'add-in proprement).

## Vérifications de prérequis (intégrées au setup)

- .NET Framework 4.x dans HKLM → bloque si absent
- VSTO Runtime dans HKLM → warn si absent (Office récent l'apporte)
- Word installé (HKCR\Word.Application) → warn si absent
- Word ouvert au moment de l'install → erreur claire (clés verrouillées)

## Signature (phase ultérieure)

Pour une V2 pro, signer avec un certificat code :
1. Signer `MathCursor.dll` et `MathCursor.vsto` avec `signtool.exe` avant le build installer
2. Signer l'EXE installer final avec le même cert
3. Supprime le warning SmartScreen pour les testeurs

Pour un beta à main — pas critique, les testeurs cliquent "Exécuter quand même".

## Dépannage

**Erreur "Word est ouvert"** à l'install : fermer Word complètement, relancer.

**L'add-in n'apparaît pas dans Word** : Fichier → Options → Compléments → "Gérer : Compléments COM" → coche "MathCursor".

**Logs** :
- Installer : `%TEMP%\Setup Log *.txt`
- Add-in : `%AppData%\MathCursor\logs\mathcursor.log`

**LoadBehavior passé de 3 à 2** : Word a désactivé l'add-in après un crash.
Remet à 3 manuellement via regedit ou réinstalle.
