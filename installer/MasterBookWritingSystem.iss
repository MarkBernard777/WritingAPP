; Master Book-Writing System — Inno Setup script (unsigned development builds)
;
; Code signing is intentionally disabled. To enable later, uncomment SignTool below
; and supply a certificate outside source control (never commit passwords or .pfx files).
;
; Unsigned installers may trigger a Windows SmartScreen / reputation warning.

#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#define MyAppName "Master Book-Writing System"
#define MyAppExeName "MasterBookWritingSystem.App.exe"
#define MyAppPublisher "Master Book-Writing System"
#define MyAppId "MasterBookWritingSystem.OfflineAuthoring"

[Setup]
AppId={{A7C2E9F1-4B5D-4E8A-9C31-12F0B8D6A401}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppVerName={#MyAppName} {#AppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\MasterBookWritingSystem\App
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=MasterBookWritingSystem-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
; Do not use CloseApplications on user project folders — only the installed app directory.
CloseApplications=yes
; SignTool=signtool
; SignedUninstaller=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; Install the complete self-contained publish tree, including seed/.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
// Uninstall removes only {app} (LocalAppData\MasterBookWritingSystem\App).
// User book projects and other LocalAppData settings outside {app} are never deleted.
function InitializeUninstall(): Boolean;
begin
  Result := True;
end;
