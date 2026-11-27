; Windows installer for VRoom. See RELEASING.md.
; Build the game first (Builds/Windows/VRoom.exe), then from the repo root:
;   ISCC.exe /DAppVersion=0.1.0 Tools/installer/VRoom.iss
; The setup file is written to Builds/Installer/VRoom-Setup.exe. Never rename it:
; the website downloads releases/latest/download/VRoom-Setup.exe.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#define AppName "VRoom"
#define AppPublisher "IT Kamianets"
#define AppUrl "https://vroom.itkamianets.com"
#define AppExe "VRoom.exe"

[Setup]
AppId={{7C1B6E44-3D5A-4F0B-9B7D-2A8E5C4F91D3}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\Builds\Installer
OutputBaseFilename=VRoom-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
LicenseFile=..\..\LICENSE
; An update runs this file again: the same AppId replaces the old install in place.
CloseApplications=yes
RestartApplications=no
UsePreviousTasks=yes
UsePreviousLanguage=yes

[Languages]
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\..\Builds\Windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; \
    Excludes: "*_BurstDebugInformation_DoNotShip*,*_BackUpThisFolder_ButDontShipItWithYourGame*"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; A silent update has no finish page, so start the game again here.
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: WizardSilent
