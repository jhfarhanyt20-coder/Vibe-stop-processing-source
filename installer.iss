; Vibe Stop Processing - Windows installer (Inno Setup 6)
; Built by release.ps1:  ISCC.exe /DAppVersion=x.y.z installer.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName "Vibe Stop Processing"
#define AppExe "StopProcessing.exe"
#define AppUrl "https://github.com/jhfarhanyt20-coder/stop-processing"

[Setup]
; Never change AppId - Windows uses it to recognise upgrades and the uninstaller.
AppId={{8F3C2A71-5B4E-4D2A-9C61-7E2B5A9D4F10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Vibe
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}/releases
AppCopyright=Copyright (c) 2026 Vibe. All rights reserved.
DefaultDirName={autopf}\Vibe\Stop Processing
DefaultGroupName=Vibe
DisableProgramGroupPage=yes
LicenseFile=LICENSE
OutputDir=dist
OutputBaseFilename=VibeStopProcessing-Setup-{#AppVersion}
SetupIconFile=app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany=Vibe
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
Type: files; Name: "{app}\{#AppExe}.old"
Type: files; Name: "{app}\{#AppExe}.download"
Type: filesandordirs; Name: "{localappdata}\StopProcessing"
