; UACToolBox standard setup script. Build with scripts/build-setup.ps1 after publish.
; File is ASCII-only where Inno parsing matters; Chinese strings are UTF-8 with BOM-safe usage.

#define MyAppName "UACToolBox"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "XianTrace Studio"
#define MyAppExeName "Config.exe"
; Overridden by scripts\build-setup.ps1 for the self-contained variant.
#ifndef PayloadDir
#define PayloadDir "..\artifacts\win-x64"
#endif
#ifndef OutputName
#define OutputName "UACToolBox-Setup"
#endif

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
OutputBaseFilename={#OutputName}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[CustomMessages]
en.GroupSystem=System registration:
en.TaskRegTask=Register scheduled task (elevated-launch core, required)
en.TaskRegEnv=Set the launcher environment variable %XianTrace_UAC_ToolBox% (required)
en.TaskRegMenus=Register Explorer context menus (optional)
en.MandatoryMsg=The scheduled task and the environment variable are required and cannot be cleared.
en.StatusTask=Registering the scheduled task...
en.StatusEnv=Setting the launcher environment variable...
en.StatusMenus=Registering the context menus...
chinesesimplified.GroupSystem=系统注册：
chinesesimplified.TaskRegTask=注册计划任务（免 UAC 启动核心，必选）
chinesesimplified.TaskRegEnv=设置启动器环境变量 %XianTrace_UAC_ToolBox%（必选）
chinesesimplified.TaskRegMenus=注册资源管理器右键菜单（可选）
chinesesimplified.MandatoryMsg=计划任务与环境变量是必选项，不能取消。
chinesesimplified.StatusTask=正在注册计划任务…
chinesesimplified.StatusEnv=正在设置启动器环境变量…
chinesesimplified.StatusMenus=正在注册右键菜单…

[Tasks]
Name: "reg_task"; Description: "{cm:TaskRegTask}"; GroupDescription: "{cm:GroupSystem}"
Name: "reg_env"; Description: "{cm:TaskRegEnv}"; GroupDescription: "{cm:GroupSystem}"
Name: "reg_menus"; Description: "{cm:TaskRegMenus}"; GroupDescription: "{cm:GroupSystem}"; Flags: unchecked
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PayloadDir}\Config.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\Launcher.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--admin-operation-self install"; Tasks: reg_task; Flags: runhidden waituntilterminated; StatusMsg: "{cm:StatusTask}"
Filename: "{app}\{#MyAppExeName}"; Parameters: "--admin-operation-self environment-add"; Tasks: reg_env; Flags: runhidden waituntilterminated; StatusMsg: "{cm:StatusEnv}"
Filename: "{app}\{#MyAppExeName}"; Parameters: "--admin-operation-self menus-add"; Tasks: reg_menus; Flags: runhidden waituntilterminated; StatusMsg: "{cm:StatusMenus}"
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpSelectTasks then
  begin
    if (not WizardIsTaskSelected('reg_task')) or (not WizardIsTaskSelected('reg_env')) then
    begin
      MsgBox(CustomMessage('MandatoryMsg'), mbError, MB_OK);
      Result := False;
    end;
  end;
end;

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--admin-operation-self uninstall"; RunOnceId: "UninstallTask"; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Parameters: "--admin-operation-self environment-remove"; RunOnceId: "UninstallEnvironment"; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Parameters: "--admin-operation-self menus-remove"; RunOnceId: "UninstallMenus"; Flags: runhidden waituntilterminated

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\XianTrace\UACToolBox"
