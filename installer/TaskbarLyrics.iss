#ifndef AppVersion
  #error AppVersion must be supplied by publish-release.ps1
#endif
#ifndef PayloadDir
  #error PayloadDir must be supplied by publish-release.ps1
#endif

[Setup]
AppId={{356B8D81-F24F-470D-8D8D-51EE0736CF7A}
AppName=TaskbarLyrics
AppVersion={#AppVersion}
AppPublisher=TaskbarLyrics
AppPublisherURL=https://github.com/ANYNC/TaskbarLyrics
DefaultDirName={code:GetDefaultInstallDir}
UsePreviousAppDir=yes
AppendDefaultDirName=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0
DisableDirPage=auto
AlwaysShowDirOnReadyPage=yes
DisableProgramGroupPage=yes
CloseApplications=no
UninstallDisplayIcon={app}\TaskbarLyrics.exe
VersionInfoVersion={#AppVersion}
SetupIconFile={#PayloadDir}\Assets\AppIcon\TaskbarLyrics.ico
WizardStyle=modern dynamic windows11
OutputBaseFilename=TaskbarLyrics-{#AppVersion}-Setup
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "zhcn"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "zhtw"; MessagesFile: "compiler:Languages\ChineseTraditional.isl"

[CustomMessages]
en.CloseTaskbarLyrics=Quit TaskbarLyrics from the notification area, or let its update finish, then try again.
zhcn.CloseTaskbarLyrics=请从托盘退出 TaskbarLyrics，或等待应用内更新完成，然后重试。
zhtw.CloseTaskbarLyrics=請從系統匣結束 TaskbarLyrics，或等待應用程式內更新完成，然後重試。
en.ForceCloseTaskbarLyrics=Force TaskbarLyrics to close and continue installation?
zhcn.ForceCloseTaskbarLyrics=是否强制关闭 TaskbarLyrics 并继续安装？
zhtw.ForceCloseTaskbarLyrics=是否強制結束 TaskbarLyrics 並繼續安裝？
en.NewerVersion=This folder already contains a newer version of TaskbarLyrics. Run the installer for that version or a later one.
zhcn.NewerVersion=此目录中已有更新版本的 TaskbarLyrics。请使用该版本或更高版本的安装器。
zhtw.NewerVersion=此資料夾中已有較新版本的 TaskbarLyrics。請使用該版本或更新版本的安裝程式。
en.GetWebView2=Open the Microsoft download page for the required WebView2 Runtime
zhcn.GetWebView2=打开微软下载页，获取运行所需的 WebView2 Runtime
zhtw.GetWebView2=開啟 Microsoft 下載頁面，取得執行所需的 WebView2 Runtime

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{userprograms}\TaskbarLyrics"; Filename: "{app}\TaskbarLyrics.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\TaskbarLyrics.exe"; Description: "{cm:LaunchProgram,TaskbarLyrics}"; WorkingDir: "{app}"; Flags: postinstall nowait skipifsilent; Check: WebView2Installed
Filename: "https://developer.microsoft.com/microsoft-edge/webview2/#download-section"; Description: "{cm:GetWebView2}"; Flags: postinstall shellexec nowait skipifsilent; Check: not WebView2Installed

[Code]
const
  InvalidHandle = -1;
  ReadWriteAccess = $C0000000;
  OpenAlways = 4;
  NormalFile = $80;
  InstallFolderName = 'TaskbarLyrics';
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  WebView2RuntimeKey = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

var
  InstanceLock: THandle;
  OriginalDirBrowseClick: TNotifyEvent;

function CreateFileForLock(FileName: String; Access, ShareMode, Security, CreationMode, Attributes: Cardinal;
  Template: THandle): THandle;
  external 'CreateFileW@kernel32.dll stdcall';

function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function GetCurrentProcessId: Cardinal;
  external 'GetCurrentProcessId@kernel32.dll stdcall';

function ProcessIdToSessionId(ProcessId: Cardinal; var SessionId: Cardinal): Boolean;
  external 'ProcessIdToSessionId@kernel32.dll stdcall';

function GetDefaultInstallDir(Param: String): String;
begin
  if DirExists('D:\') then
    Result := 'D:\Program Files\' + InstallFolderName
  else
    Result := ExpandConstant('{localappdata}\Programs\') + InstallFolderName;
end;

procedure BrowseInstallDir(Sender: TObject);
var
  PreviousDir, SelectedDir, ParentDir: String;
begin
  PreviousDir := WizardForm.DirEdit.Text;
  OriginalDirBrowseClick(Sender);
  if WizardForm.DirEdit.Text = PreviousDir then
    Exit;

  // The native dialog also appends the name when the selected folder already has it.
  SelectedDir := RemoveBackslashUnlessRoot(WizardForm.DirEdit.Text);
  ParentDir := ExtractFileDir(SelectedDir);
  if (CompareText(ExtractFileName(SelectedDir), InstallFolderName) = 0) and
      (CompareText(ExtractFileName(ParentDir), InstallFolderName) = 0) then
    WizardForm.DirEdit.Text := ParentDir;
end;

procedure InitializeWizard;
begin
  OriginalDirBrowseClick := WizardForm.DirBrowseButton.OnClick;
  WizardForm.DirBrowseButton.OnClick := @BrowseInstallDir;
end;

function AcquireInstanceLock: Boolean;
var
  Directory: String;
begin
  Result := True;
  if InstanceLock <> 0 then
    Exit;

  // Share the application and updater's per-user lock while replacing files.
  Directory := ExpandConstant('{localappdata}\TaskbarLyrics');
  if ForceDirectories(Directory) then
    InstanceLock := CreateFileForLock(AddBackslash(Directory) + 'TaskbarLyrics.lock',
      ReadWriteAccess, 0, 0, OpenAlways, NormalFile, 0);

  if InstanceLock = InvalidHandle then
    InstanceLock := 0;
  Result := InstanceLock <> 0;
end;

function AcquireInstallLock: Boolean;
var
  SessionId: Cardinal;
  Arguments: String;
  Attempt, ExitCode: Integer;
begin
  Result := AcquireInstanceLock;
  if Result or WizardSilent then
    Exit;

  if SuppressibleMsgBox(CustomMessage('ForceCloseTaskbarLyrics'), mbConfirmation,
      MB_YESNO or MB_DEFBUTTON2, IDNO) <> IDYES then
    Exit;

  if not ProcessIdToSessionId(GetCurrentProcessId, SessionId) then
    Exit;

  Arguments := '/F /FI "SESSION eq ' + IntToStr(SessionId) + '" /FI "USERNAME eq ' +
    GetUserNameString + '" /IM TaskbarLyrics.exe /IM TaskbarLyrics.App.exe';
  if Exec(ExpandConstant('{sys}\taskkill.exe'), Arguments, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    Log('TaskbarLyrics termination exit code: ' + IntToStr(ExitCode));

  // Check the lock even if taskkill did not find one of the executable names.
  for Attempt := 1 to 20 do
  begin
    Result := AcquireInstanceLock;
    if Result then
      Exit;
    Sleep(100);
  end;
end;

procedure ReleaseInstanceLock;
begin
  if InstanceLock <> 0 then
  begin
    CloseHandle(InstanceLock);
    InstanceLock := 0;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  InstalledVersion, SetupVersion: Int64;
begin
  Result := '';
  if not AcquireInstallLock then
    Result := CustomMessage('CloseTaskbarLyrics')
  else if GetPackedVersion(ExpandConstant('{app}\TaskbarLyrics.exe'), InstalledVersion) and
      GetPackedVersion(ExpandConstant('{srcexe}'), SetupVersion) and
      (ComparePackedVersion(InstalledVersion, SetupVersion) > 0) then
  begin
    ReleaseInstanceLock;
    Result := CustomMessage('NewerVersion');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    ReleaseInstanceLock;
end;

procedure DeinitializeSetup;
begin
  ReleaseInstanceLock;
end;

function InitializeUninstall: Boolean;
begin
  Result := AcquireInstanceLock;
  if not Result then
    SuppressibleMsgBox(CustomMessage('CloseTaskbarLyrics'), mbError, MB_OK, IDOK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand, InstalledCommand: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    InstalledCommand := '"' + ExpandConstant('{app}\TaskbarLyrics.exe') + '"';
    if RegQueryStringValue(HKCU, RunKey, 'TaskbarLyrics', StartupCommand) and
        (CompareText(StartupCommand, InstalledCommand) = 0) then
      RegDeleteValue(HKCU, RunKey, 'TaskbarLyrics');
  end;
end;

procedure DeinitializeUninstall;
begin
  ReleaseInstanceLock;
end;

function WebView2Installed: Boolean;
var
  Version: String;
begin
  Result := (RegQueryStringValue(HKLM32, WebView2RuntimeKey, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU, WebView2RuntimeKey, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;
