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
#define MyAppVersion "0.8.1"
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
; Fenêtre d'info post-install : rappel ouvrir Word
InfoAfterFile=after-install.txt
SetupLogging=yes

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

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

; Visual C++ Redistributables x86 + x64 — requis par les DLLs natives ONNX
; (1 par arch). Word 32-bit a besoin du x86 ; Word 64-bit a besoin du x64.
; `skipifsourcedoesntexist` : si build.ps1 n'a pas pu télécharger, on skippe
; sans casser le build (l'utilisateur devra avoir VC++ Redist installé).
Source: "payload\vc_redist.x86.exe";                      DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist
Source: "payload\vc_redist.x64.exe";                      DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist

[Tasks]
; Checkbox optionnelle proposée à l'utilisateur en fin d'installation.
; "postinstall" dans le [Run] correspondant fait apparaître la checkbox sur
; la dernière page du wizard — l'utilisateur décide.
Name: "opentutorial"; Description: "Ouvrir le tutoriel maintenant"; GroupDescription: "Pour bien démarrer :"

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
    StatusMsg: "Vérification du runtime Visual C++ (x86)..."
Filename: "{tmp}\vc_redist.x64.exe"; \
    Parameters: "/install /quiet /norestart"; \
    Flags: waituntilterminated skipifdoesntexist; \
    StatusMsg: "Vérification du runtime Visual C++ (x64)..."

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
    StatusMsg: "Importation du certificat..."

; Ouverture optionnelle du tutoriel à la fin de l'install. La checkbox liée
; à Tasks: opentutorial apparaît sur la dernière page du wizard.
; - shellexec : laisse Word gérer l'ouverture (équivalent double-clic explorateur)
; - postinstall : déplace l'action à la fin, après la fermeture des autres jobs
; - nowait : l'installer rend la main sans attendre Word
; - skipifsilent : pas d'ouverture en mode /SILENT (déploiement scripté)
Filename: "{userdocs}\MathCursor\MathCursor-Tutorial.docx"; \
    Description: "Ouvrir le tutoriel"; \
    Flags: shellexec postinstall nowait skipifsilent; \
    Tasks: opentutorial

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
        MsgBox('MathCursor nécessite .NET Framework 4.8.'#13#10'Il est préinstallé sur Windows 10 et 11 — vérifiez les mises à jour Windows.',
               mbError, MB_OK);
        Result := False;
        Exit;
    end;

    // VSTO Runtime : clé HKLM\SOFTWARE\Microsoft\vsto runtime Setup\v4
    HasVstoRuntime := RegKeyExists(HKLM, 'SOFTWARE\Microsoft\vsto runtime Setup\v4') or
                      RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\vsto runtime Setup\v4');
    if not HasVstoRuntime then
    begin
        if MsgBox('Le runtime VSTO ne semble pas installé.'#13#10 +
                  'Il est normalement livré avec Office 2016+. Continuer quand même ?',
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
        if MsgBox('Microsoft Word ne semble pas installé sur ce PC.'#13#10 +
                  'MathCursor est un add-in pour Word — installer quand même ?',
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
        Result := 'Microsoft Word est ouvert. Fermez Word avant de continuer, puis relancez l''installation.';
    end;
end;
