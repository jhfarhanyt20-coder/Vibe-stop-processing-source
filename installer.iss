; BD All-in-One Tools - Windows installer (Inno Setup 6)
; Built by release.ps1:  ISCC.exe /DAppVersion=x.y.z installer.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName "BD All-in-One Tools"
#define AppExe "StopProcessing.exe"
#define AppUrl "https://github.com/jhfarhanyt20-coder/Vibe-stop-processing"

[Setup]
; Never change AppId - Windows uses it to recognise upgrades and the uninstaller.
AppId={{8F3C2A71-5B4E-4D2A-9C61-7E2B5A9D4F10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=BDCoder AI (Jamil Hussain)
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}/releases
AppCopyright=Copyright (c) 2026 Jamil Hussain (JH Farhan), BDCoder AI. All rights reserved.
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
VersionInfoCompany=BDCoder AI
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

[UninstallRun]
; remove the "start with Windows" task the app created
Filename: "{sys}\schtasks.exe"; Parameters: "/delete /tn ""VibeStopProcessing_{username}"" /f"; Flags: runhidden; RunOnceId: "RemoveStartupTask"
; ...and the ones made by any other user of this PC
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -Command ""Get-ScheduledTask -TaskName 'VibeStopProcessing_*' -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false"""; Flags: runhidden; RunOnceId: "RemoveAllStartupTasks"
; make sure the tray app isn't running while files are removed
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im {#AppExe}"; Flags: runhidden; RunOnceId: "StopApp"

[UninstallDelete]
Type: files; Name: "{app}\{#AppExe}.old"
Type: files; Name: "{app}\{#AppExe}.download"
Type: files; Name: "{app}\{#AppExe}.*.old"
Type: filesandordirs; Name: "{localappdata}\StopProcessing"
; shortcuts renamed by the app after the BD rename (older installs)
Type: files; Name: "{commondesktop}\Vibe Stop Processing.lnk"
Type: files; Name: "{commonprograms}\Vibe Stop Processing.lnk"
