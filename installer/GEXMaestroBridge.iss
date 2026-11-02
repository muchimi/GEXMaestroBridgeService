#define AppName "GEX Maestro Bridge"
#ifndef AppVersion
#define AppVersion "0.0.0"
#endif
[Setup]
AppId={{D26DA10A-73A7-482A-A711-6B7E04C8D8A0}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={autopf}\GEXMaestroBridge
DefaultGroupName=GEX Maestro Bridge
OutputDir=..\dist
OutputBaseFilename=GEXMaestroBridgeSetup-{#AppVersion}-x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\licenses\GPL-3.0.txt
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
[Files]
Source: "..\payload\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "configure.ps1"; DestDir: "{app}\installer"; Flags: ignoreversion
Source: "upgrade.ps1"; DestDir: "{app}\installer"; Flags: ignoreversion
[Icons]
Name: "{autoprograms}\GEX Maestro Bridge Console"; Filename: "{app}\GEXMaestroBridgeService.exe"; Check: IsConsole
Name: "{autoprograms}\Uninstall GEX Maestro Bridge"; Filename: "{uninstallexe}"
[Code]
var
  NetworkPage, WarningPage: TWizardPage;
  ListenPort, SendPort, HostIP: TNewEdit;
  RemoteBox, ServiceBox, DevicesBox, FirewallBox, AnyBox: TNewCheckBox;
  WarningText: TNewStaticText;
  UpgradePending: Boolean;
  ExistingService: Boolean;

function IsConsole: Boolean;
begin
  Result := not ServiceBox.Checked;
end;

function ValidPort(S: String): Boolean;
var N: Integer;
begin
  Result := TryStrToInt(S, N) and (N >= 1) and (N <= 65535);
end;

procedure InitializeWizard;
begin
  WarningPage := CreateCustomPage(wpWelcome, 'Administrator access and security', 'Review privileged operations before proceeding');
  WarningText := TNewStaticText.Create(WarningPage);
  WarningText.Parent := WarningPage.Surface;
  WarningText.SetBounds(0, 8, WarningPage.SurfaceWidth, 180);
  WarningText.AutoSize := False;
  WarningText.WordWrap := True;
  WarningText.Caption := 'Administrator rights are required to install HIDMaestro driver components, register a Windows Service, and change Windows Firewall rules.' + #13#10#13#10 + 'Console mode does not register a service, but driver and virtual-device operations may still require elevation.' + #13#10#13#10 + 'Service mode runs as LocalSystem. HIDMaestro is a third-party open-source dependency. Cancel if you do not approve these changes.';
  NetworkPage := CreateCustomPage(WarningPage.ID, 'Operating mode and OSC network', 'Choose ports, remote host, and firewall access');
  ServiceBox := TNewCheckBox.Create(NetworkPage);
  ServiceBox.Parent := NetworkPage.Surface;
  ServiceBox.SetBounds(0, 0, 380, 22);
  ServiceBox.Caption := 'Install as Windows Service (otherwise console)';
  ExistingService := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\GEXMaestroBridgeService');
  ServiceBox.Checked := ExistingService or not FileExists(ExpandConstant('{autopf}\GEXMaestroBridge\GEXMaestroBridgeService.exe'));
  ServiceBox.Enabled := not FileExists(ExpandConstant('{autopf}\GEXMaestroBridge\GEXMaestroBridgeService.exe'));
  DevicesBox := TNewCheckBox.Create(NetworkPage);
  DevicesBox.Parent := NetworkPage.Surface;
  DevicesBox.SetBounds(0, 26, 380, 22);
  DevicesBox.Caption := 'Request default GEX 1-4 devices (not yet implemented)';
  DevicesBox.Checked := False;
  DevicesBox.Enabled := ServiceBox.Enabled;
  RemoteBox := TNewCheckBox.Create(NetworkPage);
  RemoteBox.Parent := NetworkPage.Surface;
  RemoteBox.SetBounds(0, 52, 380, 22);
  RemoteBox.Caption := 'GremlinEx runs on another computer';
  ListenPort := TNewEdit.Create(NetworkPage);
  ListenPort.Parent := NetworkPage.Surface;
  ListenPort.SetBounds(0, 86, 90, 23);
  ListenPort.Text := '8010';
  SendPort := TNewEdit.Create(NetworkPage);
  SendPort.Parent := NetworkPage.Surface;
  SendPort.SetBounds(110, 86, 90, 23);
  SendPort.Text := '8011';
  HostIP := TNewEdit.Create(NetworkPage);
  HostIP.Parent := NetworkPage.Surface;
  HostIP.SetBounds(0, 118, 260, 23);
  HostIP.Text := '127.0.0.1';
  FirewallBox := TNewCheckBox.Create(NetworkPage);
  FirewallBox.Parent := NetworkPage.Surface;
  FirewallBox.SetBounds(0, 151, 380, 22);
  FirewallBox.Caption := 'Create inbound UDP firewall rule (remote only)';
  FirewallBox.Checked := True;
  AnyBox := TNewCheckBox.Create(NetworkPage);
  AnyBox.Parent := NetworkPage.Surface;
  AnyBox.SetBounds(0, 178, 380, 22);
  AnyBox.Caption := 'Allow ANY remote IP (less secure)';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = NetworkPage.ID then begin
    if not ValidPort(ListenPort.Text) or not ValidPort(SendPort.Text) then begin
      MsgBox('UDP ports must be between 1 and 65535.', mbError, MB_OK);
      Result := False;
    end else if RemoteBox.Checked and (Trim(HostIP.Text) = '') then begin
      MsgBox('Enter the GremlinEx host IPv4 address.', mbError, MB_OK);
      Result := False;
    end else if RemoteBox.Checked and AnyBox.Checked and FirewallBox.Checked then
      Result := MsgBox('Allow UDP from ANY remote address?', mbConfirmation, MB_YESNO) = IDYES;
  end;
end;

function PsQuote(S: String): String;
begin
  Result := '"' + S + '"';
end;

function RunUpgrade(Action: String): Boolean;
var Params: String; ResultCode: Integer;
begin
  Params := '-NoProfile -ExecutionPolicy Bypass -File ' +
    PsQuote(ExpandConstant('{app}\installer\upgrade.ps1')) +
    ' -InstallDir ' + PsQuote(ExpandConstant('{app}')) +
    ' -Action ' + Action + ' -Version {#AppVersion}';
  Result := Exec('powershell.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
begin
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Params, Mode, Remote, Scope, MakeDevices: String; ResultCode: Integer;
begin
  if CurStep = ssInstall then begin
    UpgradePending := FileExists(ExpandConstant('{app}\GEXMaestroBridgeService.exe'));
    if UpgradePending and not RunUpgrade('prepare') then
      RaiseException('Could not prepare the existing bridge for upgrade. Close the console or check the service.');
  end;
  if CurStep = ssPostInstall then begin
    if UpgradePending then begin
      if not RunUpgrade('complete') then RaiseException('Upgrade cleanup failed.');
      exit;
    end;
    if ServiceBox.Checked then Mode := 'service' else Mode := 'console';
    if RemoteBox.Checked then Remote := 'true' else Remote := 'false';
    if DevicesBox.Checked then MakeDevices := 'true' else MakeDevices := 'false';
    if not RemoteBox.Checked or not FirewallBox.Checked then Scope := 'none'
    else if AnyBox.Checked then Scope := 'any' else Scope := 'host';
    Params := '-NoProfile -ExecutionPolicy Bypass -File ' + PsQuote(ExpandConstant('{app}\installer\configure.ps1')) +
      ' -InstallDir ' + PsQuote(ExpandConstant('{app}')) + ' -Mode ' + Mode +
      ' -ListenPort ' + ListenPort.Text + ' -SendPort ' + SendPort.Text +
      ' -Remote ' + Remote + ' -GremlinHost ' + PsQuote(HostIP.Text) +
      ' -FirewallScope ' + Scope + ' -CreateDevices ' + MakeDevices;
    if not Exec('powershell.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
      RaiseException('Bridge configuration failed. Exit code: ' + IntToStr(ResultCode));
  end;
end;

procedure DeinitializeSetup();
begin
  if UpgradePending and not WizardForm.Finished then RunUpgrade('rollback');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var R: Integer;
begin
  if CurUninstallStep = usUninstall then begin
    Exec('sc.exe', 'stop GEXMaestroBridgeService', '', SW_HIDE, ewWaitUntilTerminated, R);
    Exec('sc.exe', 'delete GEXMaestroBridge', '', SW_HIDE, ewWaitUntilTerminated, R);
    Exec('powershell.exe', '-NoProfile -Command "Get-NetFirewallRule -Name ''GEXMaestroBridge-OSC'' -ErrorAction SilentlyContinue | Remove-NetFirewallRule"', '', SW_HIDE, ewWaitUntilTerminated, R);
  end;
end;
