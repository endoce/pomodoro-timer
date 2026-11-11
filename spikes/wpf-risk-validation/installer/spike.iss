#ifndef PublishDirectory
  #error PublishDirectory is required
#endif
#ifndef ArtifactDirectory
  #error ArtifactDirectory is required
#endif

[Setup]
AppId={{208B99D9-1FA7-46AD-961A-AE98A8F3207A}
AppName=Pomodoro Timer WPF 技术预览
AppVersion=0.1.0
AppPublisher=endoce
DefaultDirName={localappdata}\Programs\PomodoroTimerSpike
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableDirPage=yes
DisableProgramGroupPage=yes
OutputDir={#ArtifactDirectory}
OutputBaseFilename=PomodoroTimer-WPF-Spike-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\PomodoroTimerSpike.exe

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Default.isl,ChineseSimplified.isl"

[Files]
Source: "{#PublishDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Pomodoro Timer WPF 技术预览"; Filename: "{app}\PomodoroTimerSpike.exe"

[Run]
Filename: "{app}\PomodoroTimerSpike.exe"; Description: "打开技术预览"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeUninstall(): Boolean;
var ExitCode: Integer;
begin
  Result := False;
  if MsgBox('请先从托盘退出技术预览。卸载将调用 Windows App SDK 清理通知注册。继续？', mbConfirmation, MB_YESNO) <> IDYES then
    Exit;
  if not Exec(ExpandConstant('{app}\PomodoroTimerSpike.exe'), '--unregister-notifications', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then begin
    MsgBox('无法执行通知清理。未继续卸载，请记录错误。', mbError, MB_OK);
    Exit;
  end;
  if ExitCode <> 0 then begin
    MsgBox('通知清理失败。未继续卸载，请检查技术预览日志。', mbError, MB_OK);
    Exit;
  end;
  Result := True;
end;
