#ifndef AppVersion
  #define AppVersion "0.2.2"
#endif
#ifndef PublishSource
  #define PublishSource "..\artifacts\win-x64"
#endif
#ifndef InstallerOutput
  #define InstallerOutput "..\artifacts\installer"
#endif

[Setup]
AppId={{B5758E5F-AF08-49FA-ABAC-41F740F19A85}
AppName=ScanControl
AppVersion={#AppVersion}
AppVerName=ScanControl {#AppVersion}
AppPublisher=ScanControl
AppComments=Scannersteuerung für Uniden BC780XLT
DefaultDirName={localappdata}\Programs\ScanControl
DefaultGroupName=ScanControl
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#InstallerOutput}
OutputBaseFilename=ScanControl-Setup-{#AppVersion}-win-x64
SetupIconFile=..\ScanControl.App\Assets\ScanControl.ico
UninstallDisplayIcon={app}\ScanControl.App.exe
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName=ScanControl
VersionInfoDescription=ScanControl Windows Installer
SetupLogging=yes

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "Desktopsymbol erstellen"; GroupDescription: "Verknüpfungen:"; Flags: unchecked

[Files]
Source: "{#PublishSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ScanControl"; Filename: "{app}\ScanControl.App.exe"; AppUserModelID: "ScanControl.BC780XLT"
Name: "{group}\ScanControl (Kompatibilitätsstart)"; Filename: "{app}\ScanControl.App.exe"; Parameters: "--safe-start"; AppUserModelID: "ScanControl.BC780XLT"
Name: "{group}\ScanControl deinstallieren"; Filename: "{uninstallexe}"
Name: "{autodesktop}\ScanControl"; Filename: "{app}\ScanControl.App.exe"; Tasks: desktopicon; AppUserModelID: "ScanControl.BC780XLT"

[Run]
Filename: "{app}\ScanControl.App.exe"; Description: "ScanControl starten"; Flags: nowait postinstall skipifsilent

; User projects, backups and settings are deliberately outside {app}.
; No UninstallDelete rule: uninstall preserves all user data.
