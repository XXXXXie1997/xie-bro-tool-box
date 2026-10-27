#define Payload "..\..\artifacts\release\installer-payload"
[Setup]
AppId={{AE006223-8CC9-49D5-A806-887CDFBFC7B5}
AppName=蟹小箱
AppVersion=1.0.0
AppPublisher=xie-bro
DefaultDirName={autopf}\xieBroToolBox
DisableDirPage=no
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
AppendDefaultDirName=no
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
OutputDir=..\..\artifacts\release
OutputBaseFilename=XieBox-InstallEngine
SetupIconFile=..\..\Resources\app.ico
UninstallDisplayIcon={app}\XieBox.exe
UninstallFilesDir={app}\uninstall
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
WizardImageFile=wizard.bmp
WizardSmallImageFile=header.bmp
WizardImageStretch=yes
WizardSizePercent=110
DisableWelcomePage=yes
DisableReadyPage=yes
ShowLanguageDialog=no
CloseApplications=yes
RestartApplications=no
MinVersion=10.0.19045

[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"

[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{commonprograms}\蟹小箱"; Filename: "{app}\XieBox.exe"; WorkingDir: "{app}"

Name: "{commonprograms}\卸载蟹小箱"; Filename: "{app}\app\Uninstall.exe"

[Run]
Filename: "{app}\app\SetupActions.exe"; Parameters: "--shortcut ""{commondesktop}\蟹小箱.lnk"" ""{app}\XieBox.exe"""; Description: "创建桌面快捷方式"; Flags: postinstall skipifsilent runascurrentuser
Filename: "{app}\XieBox.exe"; Description: "打开蟹小箱"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallDelete]
Type: files; Name: "{commondesktop}\蟹小箱.lnk"
Type: files; Name: "{userdesktop}\蟹小箱.lnk"

[Code]
function InitializeUninstall: Boolean;
var Locator, Services, Processes, Item: Variant; I: Integer; FileName, Root: String;
begin
  Result := False;
  Root := Lowercase(AddBackslash(ExpandConstant('{app}')));
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Services := Locator.ConnectServer('', 'root\CIMV2');
    Processes := Services.ExecQuery('SELECT ExecutablePath FROM Win32_Process');
    for I := 0 to Processes.Count - 1 do begin
      Item := Processes.ItemIndex(I);
      if not VarIsNull(Item.ExecutablePath) then begin
        FileName := Lowercase(Item.ExecutablePath);
        if (Pos(Root, FileName) = 1) and
           (CompareText(ExtractFileName(FileName), 'Uninstall.exe') <> 0) and
           (CompareText(ExtractFileName(FileName), 'unins000.exe') <> 0) then begin
          Log('Uninstall blocked by running process: ' + FileName);
          if not UninstallSilent then MsgBox('蟹小箱仍在运行。请右键托盘图标选择退出，等待任务结束后重新卸载。', mbError, MB_OK);
          Exit;
        end;
      end;
    end;
    Result := True;
  except
    Log('Unable to verify running processes; uninstall cancelled.');
  end;
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
var ProgressFile: String;
begin
  ProgressFile := ExpandConstant('{param:PROGRESSFILE|}');
  if (ProgressFile <> '') and (MaxProgress > 0) then
    SaveStringToFile(ProgressFile, IntToStr(Round(CurProgress * 100.0 / MaxProgress)), False);
end;

procedure NormalizeDirectory;
var Dir: String;
begin
  Dir := RemoveBackslash(Trim(WizardForm.DirEdit.Text));
  if (Length(Dir) = 2) and (Dir[2] = ':') then
    WizardForm.DirEdit.Text := Dir + '\xieBroToolBox';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  if CurPageID = wpSelectDir then NormalizeDirectory;
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  NormalizeDirectory;
  Result := '';
end;


procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    RegWriteStringValue(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{AE006223-8CC9-49D5-A806-887CDFBFC7B5}_is1', 'UninstallString', '"' + ExpandConstant('{app}\app\Uninstall.exe') + '"');
end;
