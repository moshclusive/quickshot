; QuickShot Installer Script (Inno Setup 6)
#define MyAppName "QuickShot"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Mosh"
#define MyAppExeName "QuickShot.exe"

[Setup]
AppId={{8E7C142B-9A41-4E5B-9E2A-7F83D5B91C01}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppMutex=Local\QuickShot_SingleInstance_Mutex_v2
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\Setup
OutputBaseFilename=QuickShot-Setup
SetupIconFile=assets\Icon\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startupicon"; Description: "Launch QuickShot silently in system tray on Windows startup"; GroupDescription: "Startup:"

[Files]
Source: "bin\Release\net48\QuickShot.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\net48\QuickShot.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\net48\Tesseract.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\net48\x64\leptonica-1.82.0.dll"; DestDir: "{app}\x64"; Flags: ignoreversion
Source: "bin\Release\net48\x64\tesseract50.dll"; DestDir: "{app}\x64"; Flags: ignoreversion
Source: "bin\Release\net48\assets\*"; DestDir: "{app}\assets"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "bin\Release\net48\tessdata\*"; DestDir: "{app}\tessdata"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: files; Name: "{app}\tessdata\osd.traineddata"
Type: filesandordirs; Name: "{app}\assets\Icon"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "QuickShot"; ValueData: """{app}\{#MyAppExeName}"" --tray"; Flags: uninsdeletevalue; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM QuickShot.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(250);
  Result := True;
end;
