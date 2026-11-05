; XNY SCADA 安装脚本（Inno Setup 6）
; 由 build/publish.ps1 -Installer 调用，也可手工编译：
;   ISCC.exe XNYScada.iss /DAppVariant=Wpf /DAppVersion=1.0.0 /DSourceDir=..\..\artifacts\publish\Wpf

#ifndef AppVariant
  #define AppVariant "Wpf"
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\artifacts\publish\" + AppVariant
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts"
#endif
#ifndef SelfContained
  #define SelfContained "0"
#endif

#define AppName "XNY SCADA (" + AppVariant + ")"
#define AppExe "XNY.Scada." + AppVariant + ".exe"

[Setup]
AppId=XNYScada.{#AppVariant}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=XNY
DefaultDirName={autopf}\XNYScada\{#AppVariant}
DefaultGroupName=XNY SCADA
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=XNYScada-{#AppVariant}-{#AppVersion}-Setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#AppExe}
CloseApplications=yes
WizardStyle=modern

[Languages]
#if FileExists(CompilerPath + "Languages\ChineseSimplified.isl")
Name: "chs"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
#endif
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "开机自动启动"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Dirs]
; 数据库、日志、插件目录需要普通用户可写（工控机常以受限账户运行）
Name: "{app}\data"; Permissions: users-modify
Name: "{app}\logs"; Permissions: users-modify
Name: "{app}\plugins"; Permissions: users-modify
Name: "{app}\configs"; Permissions: users-modify

[Files]
; 升级时保留现场修改过的配置
Source: "{#SourceDir}\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "appsettings.json,data\*,logs\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\卸载 {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
Name: "{commonstartup}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\logs"

[Code]
function DesktopRuntimeInstalled: Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if ('{#SelfContained}' = '0') and not DesktopRuntimeInstalled then
  begin
    if MsgBox('未检测到 .NET 8 桌面运行时 (Microsoft.WindowsDesktop.App 8.x)。' + #13#10 +
              '请先安装后再运行程序：https://dotnet.microsoft.com/download/dotnet/8.0' + #13#10#13#10 +
              '是否仍继续安装？', mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;
