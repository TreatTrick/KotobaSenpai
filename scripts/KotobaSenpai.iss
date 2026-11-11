; Build-Installer.ps1 supplies PublishDir, NoticesFile, AppIcon, AppVersion and OutputDir.

[Setup]
AppId={{A32009BB-E6D3-49E5-A793-E0B1F2F51C11}
AppName=KotobaSenpai
AppVersion={#AppVersion}
AppPublisher=TreatTrick
AppPublisherURL=https://github.com/TreatTrick/KotobaSenpai
AppSupportURL=https://github.com/TreatTrick/KotobaSenpai/issues
DefaultDirName={localappdata}\Programs\KotobaSenpai
DefaultGroupName=KotobaSenpai
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=KotobaSenpai-{#AppVersion}-win-x64-Setup
SetupIconFile={#AppIcon}
UninstallDisplayIcon={app}\KotobaSenpai.App.exe
UninstallDisplayName=KotobaSenpai {#AppVersion}
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription=KotobaSenpai installer
VersionInfoProductName=KotobaSenpai
VersionInfoProductVersion={#AppVersion}.0
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#NoticesFile}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\KotobaSenpai"; Filename: "{app}\KotobaSenpai.App.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\KotobaSenpai"; Filename: "{app}\KotobaSenpai.App.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\KotobaSenpai.App.exe"; Description: "{cm:LaunchProgram,KotobaSenpai}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent; Check: IsRegularInstall
Filename: "{app}\KotobaSenpai.App.exe"; WorkingDir: "{app}"; Flags: nowait; Check: IsAppUpdate

[Code]
function OpenProcess(DesiredAccess: LongWord; InheritHandle: Boolean; ProcessId: LongWord): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function IsAppUpdate: Boolean;
begin
  Result := ExpandConstant('{param:APPUPDATE|0}') = '1';
end;

function IsRegularInstall: Boolean;
begin
  Result := not IsAppUpdate;
end;

function InitializeSetup: Boolean;
var
  ParentId: Integer;
  ParentHandle: THandle;
begin
  Result := True;
  if not IsAppUpdate then exit;
  ParentId := StrToIntDef(ExpandConstant('{param:UPDATEPID|0}'), 0);
  Result := False;
  if ParentId > 0 then
  begin
    ParentHandle := OpenProcess($00100000, False, ParentId);
    if ParentHandle = 0 then
      Result := DLLGetLastError = 87 // ERROR_INVALID_PARAMETER: the process has already exited.
    else
    begin
      try
        Result := WaitForSingleObject(ParentHandle, 60000) = 0;
      finally
        CloseHandle(ParentHandle);
      end;
    end;
  end;
  if not Result then
    SuppressibleMsgBox('KotobaSenpai has not exited. The update was stopped without replacing files. Close the application and run the installer again.', mbError, MB_OK, IDOK);
end;
