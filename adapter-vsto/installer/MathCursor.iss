; MathCursor — Inno Setup script (VSTO Word Add-in)
;
; Build :
;   1. msbuild adapter-vsto\src\MathCursor\MathCursor.csproj /p:Configuration=Release
;   2. placer les fichiers du modèle NER dans installer/payload/models/
;   3. ouvrir ce .iss dans Inno Setup Compiler → Build → Compile
;   4. l'exe final est dans installer/output/MathCursor-Setup-x.y.z.exe
;
; Installation : per-user (HKCU + %LocalAppData%), pas d'UAC.
;
; Prérequis pour l'utilisateur :
;   - Windows 10+ (inclut .NET Framework 4.8)
;   - Microsoft Word 2016 ou plus récent
;   - Visual Studio Tools for Office Runtime (livré avec Office 2016+)

#define MyAppName "MathCursor"
#define MyAppVersion "0.11.4"
#define MyAppPublisher "MathCursor"
#define MyAppExeName "MathCursor.dll"
#define MyAppId "{{6E4B3A1E-7F2D-4B8C-9A0E-2C5D6F7A8B90}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\{#MyAppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=output
OutputBaseFilename=MathCursor-Setup-{#MyAppVersion}
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupLogging=yes

[Languages]
; Fenêtre d'info post-install (InfoAfterFile) localisée PAR LANGUE — sinon
; l'utilisateur EN voyait le texte FR (bug remonté 2026-06-19).
Name: "french";  MessagesFile: "compiler:Languages\French.isl"; InfoAfterFile: "after-install.txt"
Name: "english"; MessagesFile: "compiler:Default.isl";          InfoAfterFile: "after-install-en.txt"

[CustomMessages]
; Tous les textes custom de l'installeur, par langue (le préfixe = Name de la
; langue). Référencés par {cm:Cle} dans les sections directives et par
; ExpandConstant('{cm:Cle}') dans [Code]. %n = saut de ligne.
french.OpenTutorial=Ouvrir le tutoriel
english.OpenTutorial=Open the tutorial
french.StatusVcRedistX86=Vérification du runtime Visual C++ (x86)...
english.StatusVcRedistX86=Checking the Visual C++ runtime (x86)...
french.StatusVcRedistX64=Vérification du runtime Visual C++ (x64)...
english.StatusVcRedistX64=Checking the Visual C++ runtime (x64)...
french.StatusCert=Importation du certificat...
english.StatusCert=Importing the certificate...
french.NeedDotNet=MathCursor nécessite .NET Framework 4.8.%nIl est préinstallé sur Windows 10 et 11 — vérifiez les mises à jour Windows.
english.NeedDotNet=MathCursor requires .NET Framework 4.8.%nIt is preinstalled on Windows 10 and 11 — check for Windows updates.
french.VstoMissing=Le runtime VSTO ne semble pas installé.%nIl est normalement livré avec Office 2016+. Continuer quand même ?
english.VstoMissing=The VSTO runtime does not appear to be installed.%nIt normally ships with Office 2016+. Continue anyway?
french.WordMissing=Microsoft Word ne semble pas installé sur ce PC.%nMathCursor est un add-in pour Word — installer quand même ?
english.WordMissing=Microsoft Word does not appear to be installed on this PC.%nMathCursor is a Word add-in — install anyway?
french.WordOpen=Microsoft Word est ouvert. Fermez Word avant de continuer, puis relancez l'installation.
english.WordOpen=Microsoft Word is open. Close Word before continuing, then restart the installation.

[Files]
; Binaires et manifest VSTO (copiés par build.ps1 depuis bin/Release/)
Source: "payload\MathCursor.dll";                         DestDir: "{app}"; Flags: ignoreversion
Source: "payload\MathCursor.dll.manifest";                DestDir: "{app}"; Flags: ignoreversion
Source: "payload\MathCursor.vsto";                        DestDir: "{app}"; Flags: ignoreversion
Source: "payload\MathCursor.Engine.dll";                  DestDir: "{app}"; Flags: ignoreversion
Source: "payload\MathCursor.Serialization.dll";           DestDir: "{app}"; Flags: ignoreversion
Source: "payload\MathCursor.HostContract.dll";            DestDir: "{app}"; Flags: ignoreversion

; Dépendances gérées
Source: "payload\WpfMath.dll";                            DestDir: "{app}"; Flags: ignoreversion
Source: "payload\XamlMath.Shared.dll";                    DestDir: "{app}"; Flags: ignoreversion
Source: "payload\Microsoft.ML.OnnxRuntime.dll";           DestDir: "{app}"; Flags: ignoreversion
; Native ORT DLLs : multi-arch (cf. commit cca4712, Word peut être 32 ou 64 bits).
; ThisAddIn.ConfigureOnnxRuntimeNativeDir() appelle SetDllDirectory sur
; {app}\onnxruntime-{x86|x64} selon IntPtr.Size avant le 1er SessionOptions.
Source: "payload\onnxruntime-x86\onnxruntime.dll";                  DestDir: "{app}\onnxruntime-x86"; Flags: ignoreversion
Source: "payload\onnxruntime-x86\onnxruntime_providers_shared.dll"; DestDir: "{app}\onnxruntime-x86"; Flags: ignoreversion skipifsourcedoesntexist
Source: "payload\onnxruntime-x64\onnxruntime.dll";                  DestDir: "{app}\onnxruntime-x64"; Flags: ignoreversion
Source: "payload\onnxruntime-x64\onnxruntime_providers_shared.dll"; DestDir: "{app}\onnxruntime-x64"; Flags: ignoreversion skipifsourcedoesntexist
Source: "payload\Microsoft.Office.Tools.Common.v4.0.Utilities.dll"; DestDir: "{app}"; Flags: ignoreversion

; Runtimes .NET (ne-gênent-pas si présents dans GAC aussi)
Source: "payload\System.Buffers.dll";                     DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "payload\System.Memory.dll";                      DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "payload\System.Numerics.Vectors.dll";            DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "payload\System.Runtime.CompilerServices.Unsafe.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

; Modèle NER (volumineux — copier dans payload/models/ avant de compiler)
Source: "payload\models\*";                               DestDir: "{app}\models"; Flags: ignoreversion recursesubdirs

; URL endpoint pour le bouton "Signaler une erreur" (déposée dans %AppData%
; pour que FeedbackSenderFactory la trouve, sinon fallback clipboard).
; Toujours écrasée à l'install : permet de migrer l'URL à la prochaine
; release sans intervention user. Les devs qui veulent override ponctuel
; utilisent la variable d'env `MATHCURSOR_FEEDBACK_URL` qui prime sur le
; fichier. `uninsneveruninstall` : on garde le fichier après désinstall.
Source: "feedback.url";                                   DestDir: "{userappdata}\MathCursor"; Flags: ignoreversion uninsneveruninstall

; Certificat public, extrait temporairement pour être importé dans le user
; trust store via [Run] ci-dessous. `deleteafterinstall` supprime le fichier
; une fois l'installation terminée.
Source: "payload\mathcursor.cer";                         DestDir: "{tmp}"; Flags: deleteafterinstall

; Tutoriels FR / EN de prise en main — copiés dans Documents\MathCursor\ (lieu
; où l'utilisateur garde ses essais ; survit aux désinstall via uninsneveruninstall).
; La langue est filtrée par Languages: french / english (= choix wizard ISS).
; Le DestName est universel pour que [Run] ouvre le bon fichier sans condition.
; Cf. ADR 2026-05-22-Feat-tutorial-docx-generated-onboarding.
Source: "payload\MathCursor-Tutoriel-fr.docx"; DestDir: "{userdocs}\MathCursor"; DestName: "MathCursor-Tutorial.docx"; Flags: ignoreversion uninsneveruninstall; Languages: french
Source: "payload\MathCursor-Tutoriel-en.docx"; DestDir: "{userdocs}\MathCursor"; DestName: "MathCursor-Tutorial.docx"; Flags: ignoreversion uninsneveruninstall skipifsourcedoesntexist; Languages: english

; Polices math embarquées (ADR 2026-06-22-Feat-math-font-selector). Installées
; PAR UTILISATEUR (pas d'UAC, cohérent avec PrivilegesRequired=lowest) via
; {autofonts} → {localappdata}\Microsoft\Windows\Fonts + enregistrement HKCU.
; Requiert Windows 10 1809+ (police per-user) ; sur un système plus ancien le
; menu « Police math » garde son repli « ouvrir la page de téléchargement ».
; Licences redistribuables fournies à côté (GUST Font License / SIL OFL).
; - onlyifdoesntexist : ne pas écraser une installation manuelle de la police.
; - uninsneveruninstall : conservées après désinstallation (d'autres documents
;   ou applications peuvent s'en servir).
Source: "fonts\latinmodern-math.otf";   DestDir: "{autofonts}"; FontInstall: "Latin Modern Math"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "fonts\STIXTwoMath-Regular.otf"; DestDir: "{autofonts}"; FontInstall: "STIX Two Math";     Flags: onlyifdoesntexist uninsneveruninstall
; Licences des polices embarquées, déposées dans le dossier d'install (traçabilité).
Source: "fonts\GUST-FONT-LICENSE.txt";   DestDir: "{app}\fonts-licenses"; Flags: ignoreversion
Source: "fonts\STIX-OFL.txt";            DestDir: "{app}\fonts-licenses"; Flags: ignoreversion

; Licence du logiciel (GNU GPL v3) + notices tierces + texte Apache 2.0 (base
; du modèle NER). MathCursor est distribué sous GPL v3 : la « source
; correspondante » exigée par la §6 est le dépôt public
; https://github.com/come/MathCursor (cf. README « Compiler depuis les sources »).
; Chemins relatifs au dossier de l'installeur → racine du dépôt.
Source: "..\..\LICENSE";                 DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\THIRD-PARTY-NOTICES.md";  DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\licenses\Apache-2.0.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion

; Visual C++ Redistributables x86 + x64 — requis par les DLLs natives ONNX
; (1 par arch). Word 32-bit a besoin du x86 ; Word 64-bit a besoin du x64.
; `skipifsourcedoesntexist` : si build.ps1 n'a pas pu télécharger, on skippe
; sans casser le build (l'utilisateur devra avoir VC++ Redist installé).
Source: "payload\vc_redist.x86.exe";                      DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist
Source: "payload\vc_redist.x64.exe";                      DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist

[Run]
; Installation conditionnelle du VC++ Redistributable. /install /quiet
; /norestart : install silencieuse + pas de redémarrage forcé. Le redist
; détecte une version >= déjà installée et skippe ; sinon UAC apparaîtra
; UNE FOIS pour autoriser l'install machine-wide.
; skipifdoesntexist : si le fichier n'a pas été inclus (DL échoué), on
; n'essaie pas de l'exécuter.
Filename: "{tmp}\vc_redist.x86.exe"; \
    Parameters: "/install /quiet /norestart"; \
    Flags: waituntilterminated skipifdoesntexist; \
    StatusMsg: "{cm:StatusVcRedistX86}"
Filename: "{tmp}\vc_redist.x64.exe"; \
    Parameters: "/install /quiet /norestart"; \
    Flags: waituntilterminated skipifdoesntexist; \
    StatusMsg: "{cm:StatusVcRedistX64}"

; Import du certificat auto-signé UNIQUEMENT dans TrustedPublisher.
; - Pas de Root : l'import dans Cert:\CurrentUser\Root déclenche un popup UAC-like
;   forcé par Windows ("Voulez-vous installer ce certificat ?"), non-skippable
;   même avec certutil -f. Casse l'expérience "un click".
; - TrustedPublisher seul suffit à VSTO pour charger l'add-in : le trust y est
;   direct (on trust explicitement ce cert-là comme publisher légitime),
;   pas besoin de remonter une chaîne jusqu'à une CA racine.
; `runhidden` = pas de fenêtre noire qui flashe. `-user` = store courant, pas d'admin.
Filename: "{sys}\certutil.exe"; \
    Parameters: "-user -addstore TrustedPublisher ""{tmp}\mathcursor.cer"""; \
    Flags: runhidden; \
    StatusMsg: "{cm:StatusCert}"

; Ouverture optionnelle du tutoriel — UNIQUEMENT en fin d'install. Le flag
; "postinstall" + "Description" fait apparaître la checkbox sur la DERNIÈRE page
; du wizard (cochée par défaut). Plus de Tasks: opentutorial (qui doublonnait la
; proposition sur la page "Select Additional Tasks" — bug remonté 2026-06-19).
; - shellexec : laisse Word gérer l'ouverture (équivalent double-clic explorateur)
; - postinstall : action à la fin, après la fermeture des autres jobs
; - nowait : l'installer rend la main sans attendre Word
; - skipifsilent : pas d'ouverture en mode /SILENT (déploiement scripté)
Filename: "{userdocs}\MathCursor\MathCursor-Tutorial.docx"; \
    Description: "{cm:OpenTutorial}"; \
    Flags: shellexec postinstall nowait skipifsilent

[UninstallRun]
; Nettoyage du certificat à la désinstallation. Non-bloquant si le cert n'est plus là.
Filename: "{sys}\certutil.exe"; \
    Parameters: "-user -delstore TrustedPublisher ""MathCursor"""; \
    Flags: runhidden; RunOnceId: "DelCertTrustedPub"

[Registry]
; Enregistrement du VSTO Add-in pour Word (clé per-user, pas de UAC).
; Cf. MSDN "Registry Entries for VSTO Add-ins" :
;   - LoadBehavior = 3 : charger au démarrage + rester chargé
;   - Manifest pointe sur le .vsto via file:/// + flag |vstolocal
Root: HKCU; Subkey: "Software\Microsoft\Office\Word\Addins\{#MyAppName}"; \
    ValueType: string; ValueName: "FriendlyName"; ValueData: "MathCursor"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Office\Word\Addins\{#MyAppName}"; \
    ValueType: string; ValueName: "Description"; ValueData: "Notation math au clavier pour Word"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Office\Word\Addins\{#MyAppName}"; \
    ValueType: dword;  ValueName: "LoadBehavior";  ValueData: "3"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Office\Word\Addins\{#MyAppName}"; \
    ValueType: string; ValueName: "Manifest"; ValueData: "file:///{app}\MathCursor.vsto|vstolocal"; Flags: uninsdeletevalue
; Nettoyage : supprime la sous-clé entière à la désinstallation (au cas où Word ajoute des valeurs)
Root: HKCU; Subkey: "Software\Microsoft\Office\Word\Addins\{#MyAppName}"; Flags: uninsdeletekey

[Code]
// Vérification des prérequis avant installation
function InitializeSetup(): Boolean;
var
    HasDotNet48, HasVstoRuntime, HasWord: Boolean;
    WordVersion: string;
begin
    Result := True;

    // .NET Framework 4.8 : clé Release >= 528040 sous
    // HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full
    HasDotNet48 := False;
    if RegKeyExists(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full') then
    begin
        HasDotNet48 := True; // simplification : on suppose 4.x présent = OK
    end;
    if not HasDotNet48 then
    begin
        MsgBox(ExpandConstant('{cm:NeedDotNet}'), mbError, MB_OK);
        Result := False;
        Exit;
    end;

    // VSTO Runtime : clé HKLM\SOFTWARE\Microsoft\vsto runtime Setup\v4
    HasVstoRuntime := RegKeyExists(HKLM, 'SOFTWARE\Microsoft\vsto runtime Setup\v4') or
                      RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\vsto runtime Setup\v4');
    if not HasVstoRuntime then
    begin
        if MsgBox(ExpandConstant('{cm:VstoMissing}'),
                  mbConfirmation, MB_YESNO) = IDNO then
        begin
            Result := False;
            Exit;
        end;
    end;

    // Word installé : clé HKCR\Word.Application
    HasWord := RegKeyExists(HKCR, 'Word.Application');
    if not HasWord then
    begin
        if MsgBox(ExpandConstant('{cm:WordMissing}'),
                  mbConfirmation, MB_YESNO) = IDNO then
        begin
            Result := False;
            Exit;
        end;
    end;
end;

// Avertir si Word est ouvert — l'install ne peut pas enregistrer l'add-in
// si Word est en cours (clé verrouillée)
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
    WordPid: Cardinal;
    ResultCode: Integer;
begin
    Result := '';
    if Exec('cmd.exe', '/c tasklist /FI "IMAGENAME eq WINWORD.EXE" 2>NUL | find /I "WINWORD.EXE" >NUL',
            '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
    begin
        Result := ExpandConstant('{cm:WordOpen}');
    end;
end;
