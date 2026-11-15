; Inno Setup script for the EarthWatch3D installer (EarthWatch3D-Setup.exe).
; Built by .github/workflows/release.yml:  iscc /DAppVersion=1.0.1 /DPublishDir=<publish folder> /DOutputDir=<out> EarthWatch3D.iss
; Installs to C:\Program Files\Qapow\EarthWatch3D by default (every QAPOW program goes under C:\Program Files\Qapow),
; and the user can pick another folder. Settings, caches and logs stay in %LOCALAPPDATA%\EarthWatch3D.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts\release"
#endif

#define AppName "EarthWatch3D"
#define AppExe "EarthWatch3D.App.exe"

[Setup]
; AppId identifies this program to Windows across versions. Never change it.
AppId={{F5ED128E-15B1-403B-9C11-F81F253842FE}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=John Apolloni
AppPublisherURL=https://qapow.com/p/earthwatch3d/
AppSupportURL=https://github.com/johnovanni/EarthWatch3D
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\Qapow\{#AppName}
DisableDirPage=no
DefaultGroupName=Qapow
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
SetupIconFile=..\..\src\EarthWatch3D.App\app.ico
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
OutputDir={#OutputDir}
OutputBaseFilename=EarthWatch3D-Setup

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Qapow\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
