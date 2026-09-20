; UACToolBox standard setup script. Build with scripts/build-setup.ps1 after publish.
; File is ASCII-only where Inno parsing matters; Chinese strings are UTF-8 with BOM-safe usage.

#define MyAppName "UACToolBox"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "XianTrace Studio"
#define MyAppExeName "Config.exe"

[Setup]
AppId={{7C1D9C4A-5E2B-4A3F-8D6E-9F0B1C2D3E4A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppComments=UAC ToolBox - shortcut manager with background elevation
DefaultDirName={autopf}\XianTrace\UACToolBox
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
WizardStyle=modern
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
OutputDir=..\artifacts
OutputBaseFilename=UACToolBox-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\artifacts\win-x64\Config.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\win-x64\Launcher.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--admin-operation-self register"; Flags: runhidden waituntilterminated; StatusMsg: "正在注册计划任务、环境变量与右键菜单…"
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--admin-operation-self uninstall"; RunOnceId: "UninstallTask"; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Parameters: "--admin-operation-self environment-remove"; RunOnceId: "UninstallEnvironment"; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Parameters: "--admin-operation-self menus-remove"; RunOnceId: "UninstallMenus"; Flags: runhidden waituntilterminated

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\XianTrace\UACToolBox"
