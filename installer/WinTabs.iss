; WinTabs installer. Build with installer/build.ps1, which publishes the app and passes AppVersion and SourceDir.

#ifndef AppVersion
  #error Pass /DAppVersion=x.y.z
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=<publish folder>
#endif

[Setup]
; Never change AppId: upgrades and uninstall find the installed app through it.
AppId={{BEC95717-4EEC-4831-8DBE-1F0782937B7E}
AppName=WinTabs
AppVersion={#AppVersion}
AppVerName=WinTabs {#AppVersion}
AppPublisher=jhnmks
AppPublisherURL=https://github.com/jhnmks/WinTabs
AppSupportURL=https://github.com/jhnmks/WinTabs/issues
VersionInfoVersion={#AppVersion}
; Per-user install: no administrator prompt, same as the app's autostart and settings.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\WinTabs
DisableProgramGroupPage=yes
DisableDirPage=auto
DisableReadyPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
LicenseFile=..\LICENSE
SetupIconFile=..\wintabs\Resources\Icon.ico
UninstallDisplayIcon={app}\wintabs.exe
UninstallDisplayName=WinTabs
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
OutputDir=..\artifacts
OutputBaseFilename=WinTabs-{#AppVersion}-setup
; Closes a running WinTabs before files are replaced; the last page offers to start it again.
CloseApplications=force
CloseApplicationsFilter=wintabs.exe
RestartApplications=no

[Tasks]
Name: autostart; Description: "Start WinTabs when I sign in"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\WinTabs"; Filename: "{app}\wintabs.exe"

[Registry]
; The same value the app writes from Settings > Start at sign-in.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "wintabs"; \
    ValueData: """{app}\wintabs.exe"""; Tasks: autostart

[Run]
Filename: "{app}\wintabs.exe"; Description: "Start WinTabs now"; Flags: nowait postinstall

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im wintabs.exe"; Flags: runhidden; RunOnceId: "StopWinTabs"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // Also when autostart was switched on later from the app's Settings.
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'wintabs');
  // Settings and logs live in %AppData%\wintabs; ask before deleting them.
  if (CurUninstallStep = usPostUninstall) and DirExists(ExpandConstant('{userappdata}\wintabs')) then
    if SuppressibleMsgBox('Also delete your WinTabs settings?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
      DelTree(ExpandConstant('{userappdata}\wintabs'), True, True, True);
end;
