; RcloneHelper Full AOT Inno Setup Script
; AOT 编译的主程序 + 捆绑 WinFsp 和 rclone，开箱即用
; AppId 与完整包（setup.iss）共用，视为同一产品，可互相升级覆盖

#define MyAppName "RcloneHelper"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "nullpoint333"
#define MyAppURL "https://github.com/nullpoint333/RcloneHelper"
#define MyAppExeName "RcloneHelper.exe"

[Setup]
; NOTE: The value of AppId uniquely identifies this application. Do not use the same AppId value in installers for other applications.
; (To generate a new GUID, click Tools | Generate GUID inside the IDE.)
AppId={{A7B8C9D0-E1F2-3A4B-5C6D-7E8F9A0B1C2D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
;AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
; Require admin privileges for WinFsp installation
PrivilegesRequired=admin
OutputDir={#SourcePath}
OutputBaseFilename=RcloneHelperv{#MyAppVersion}-Full-AOT
Compression=lzma
SolidCompression=yes
WizardStyle=modern

SetupIconFile="{#SourcePath}..\RcloneHelper\Assets\app.ico"

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; 依赖文件 - 嵌入安装包但不自动安装
Source: "{#SourcePath}winfsp-2.1.25156.msi"; DestDir: "{tmp}"; Flags: dontcopy noencryption
Source: "{#SourcePath}rclone-v1.75.1-windows-amd64.zip"; DestDir: "{tmp}"; Flags: dontcopy noencryption
; 主程序文件（AOT 发布产物）
Source: "{#SourcePath}publish-aot\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;

// 检测 WinFsp 是否已安装（与程序内检测逻辑一致：注册表 InstallDir + 目录存在）
function IsWinFspInstalled(): Boolean;
var
  InstallDir: String;
begin
  Result := False;
  // 64 位视图（WinFsp 为 64 位驱动，安装信息写在这里）
  if RegQueryStringValue(HKLM64, 'SOFTWARE\WinFsp', 'InstallDir', InstallDir) and DirExists(InstallDir) then
  begin
    Result := True;
    Exit;
  end;
  // 32 位视图兜底
  if RegQueryStringValue(HKLM32, 'SOFTWARE\WinFsp', 'InstallDir', InstallDir) and DirExists(InstallDir) then
    Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  WinFspPath: String;
  RclonePath: String;
  RcloneDest: String;
  RcloneExe: String;
  TempDir: String;
begin
  // ssInstall 阶段：在主程序文件复制之前安装 WinFsp（已安装则跳过）
  if CurStep = ssInstall then
  begin
    TempDir := ExpandConstant('{tmp}');

    if IsWinFspInstalled() then
    begin
      Log('检测到已安装 WinFsp，跳过安装');
    end
    else
    begin
      // 提取并安装 WinFsp
      ExtractTemporaryFile('winfsp-2.1.25156.msi');
      WinFspPath := TempDir + '\winfsp-2.1.25156.msi';

      if FileExists(WinFspPath) then
      begin
        // 静默安装 WinFsp MSI
        if Exec('msiexec.exe', '/i "' + WinFspPath + '" /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
        begin
          // 0=成功；1641/3010=需要重启（已用 /norestart 压住，可稍后手动重启，不算失败）
          if (ResultCode <> 0) and (ResultCode <> 1641) and (ResultCode <> 3010) then
            MsgBox('WinFsp 自动安装失败（错误码 ' + IntToStr(ResultCode) + '），挂载功能将不可用，请手动安装。', mbError, MB_OK);
        end
        else
          MsgBox('无法启动 WinFsp 安装程序，挂载功能将不可用，请手动安装。', mbError, MB_OK);
      end;
    end;
  end;

  // ssPostInstall 阶段：主程序文件复制完成后解压 rclone
  if CurStep = ssPostInstall then
  begin
    TempDir := ExpandConstant('{tmp}');

    // 提取 rclone zip
    ExtractTemporaryFile('rclone-v1.75.1-windows-amd64.zip');
    RclonePath := TempDir + '\rclone-v1.75.1-windows-amd64.zip';
    RcloneDest := ExpandConstant('{app}');

    if FileExists(RclonePath) and DirExists(RcloneDest) then
    begin
      // 使用 PowerShell 解压 zip 文件
      Exec('powershell.exe',
           '-Command "Expand-Archive -Path ''' + RclonePath + ''' -DestinationPath ''' + RcloneDest + ''' -Force"',
           '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

      // zip 内是版本子目录（rclone-v1.75.1-windows-amd64\rclone.exe），
      // 把 rclone.exe 提升到安装根目录，程序才能按“程序目录\rclone.exe”直接找到
      RcloneExe := RcloneDest + '\rclone-v1.75.1-windows-amd64\rclone.exe';
      if FileExists(RcloneExe) then
      begin
        CopyFile(RcloneExe, RcloneDest + '\rclone.exe', True);
      end;
    end;
  end;
end;
