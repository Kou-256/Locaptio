#ifndef AppSource
  #error AppSource must name the prepared distribution folder
#endif
#ifndef AppVersion
  #define AppVersion "1.0.40"
#endif
[Setup]
#ifdef SetupUiTest
AppId=Locaptio.Setup.UiTest
Uninstallable=no
CreateUninstallRegKey=no
UsePreviousAppDir=no
CloseApplications=no
#else
AppId={{33DCECCB-21D1-4D03-93BE-DA69E751A5A7}
CloseApplications=yes
#endif
AppName=Locaptio
AppVersion={#AppVersion}
AppPublisher=Kou Hashizume
AppPublisherURL=https://locaptio.vercel.app/
DefaultDirName={localappdata}\Programs\Locaptio
DefaultGroupName=Locaptio
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.26100
OutputBaseFilename=Locaptio-Setup-x64
SetupIconFile={#AppSource}\Assets\Locaptio.ico
UninstallDisplayIcon={app}\Locaptio.exe
Compression=lzma2/fast
SolidCompression=yes
#if Ver >= EncodeVer(6, 6, 0)
WizardStyle=modern light excludelightcontrols
#else
WizardStyle=modern
#endif
DisableProgramGroupPage=yes
RestartApplications=no
[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: startmenuicon; Description: "{cm:StartMenuShortcut}"; GroupDescription: "{cm:AdditionalIcons}"
Name: desktopicon; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
[CustomMessages]
japanese.StartMenuShortcut=スタートメニューに追加する
english.StartMenuShortcut=Add to the Start menu
[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
#ifndef SetupUiTest
Name: "{autoprograms}\Locaptio"; Filename: "{app}\Locaptio.exe"; WorkingDir: "{app}"; Tasks: startmenuicon
Name: "{autodesktop}\Locaptio"; Filename: "{app}\Locaptio.exe"; WorkingDir: "{app}"; Tasks: desktopicon
#endif
[Run]
Filename: "{app}\Locaptio.exe"; Description: "{cm:LaunchProgram,Locaptio}"; Flags: nowait postinstall skipifsilent
; User models, recordings and settings are deliberately retained on uninstall.

[Code]
var
  StartMenuCheck, DesktopCheck, LaunchCheck: TNewCheckBox;
  UpdatingChecks: Boolean;

procedure ShortcutChanged(Sender: TObject);
var Tasks: String;
begin
  if UpdatingChecks then Exit;
  Tasks := '';
  if StartMenuCheck.Checked then Tasks := 'startmenuicon';
  if DesktopCheck.Checked then begin
    if Tasks <> '' then Tasks := Tasks + ',';
    Tasks := Tasks + 'desktopicon';
  end;
  WizardSelectTasks(Tasks);
end;

procedure LaunchChanged(Sender: TObject);
begin
  if (not UpdatingChecks) and (WizardForm.RunList.Items.Count > 0) then
    WizardForm.RunList.Checked[0] := LaunchCheck.Checked;
end;

procedure InitializeWizard();
begin
  { TNewCheckListBox's owner-drawn glyph can clip on mixed/high DPI displays.
    Real checkbox controls have their own padded, native layout instead. }
  StartMenuCheck := TNewCheckBox.Create(WizardForm);
  StartMenuCheck.Parent := WizardForm.TasksList.Parent;
  StartMenuCheck.SetBounds(WizardForm.TasksList.Left + ScaleX(8), WizardForm.TasksList.Top + ScaleY(8), WizardForm.TasksList.Width - ScaleX(16), ScaleY(28));
  StartMenuCheck.Caption := CustomMessage('StartMenuShortcut');
  StartMenuCheck.OnClick := @ShortcutChanged;
  DesktopCheck := TNewCheckBox.Create(WizardForm);
  DesktopCheck.Parent := WizardForm.TasksList.Parent;
  DesktopCheck.SetBounds(StartMenuCheck.Left, StartMenuCheck.Top + ScaleY(36), StartMenuCheck.Width, ScaleY(28));
  DesktopCheck.Caption := ExpandConstant('{cm:CreateDesktopIcon}');
  DesktopCheck.OnClick := @ShortcutChanged;
  LaunchCheck := TNewCheckBox.Create(WizardForm);
  LaunchCheck.Parent := WizardForm.RunList.Parent;
  LaunchCheck.SetBounds(WizardForm.RunList.Left + ScaleX(8), WizardForm.RunList.Top + ScaleY(8), WizardForm.RunList.Width - ScaleX(16), ScaleY(28));
  LaunchCheck.Caption := ExpandConstant('{cm:LaunchProgram,Locaptio}');
  LaunchCheck.OnClick := @LaunchChanged;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  UpdatingChecks := True;
  if CurPageID = wpSelectTasks then begin
    StartMenuCheck.Checked := WizardIsTaskSelected('startmenuicon');
    DesktopCheck.Checked := WizardIsTaskSelected('desktopicon');
    WizardForm.TasksList.Visible := False;
  end;
  if (CurPageID = wpFinished) and (WizardForm.RunList.Items.Count > 0) then begin
    LaunchCheck.Checked := WizardForm.RunList.Checked[0];
    WizardForm.RunList.Visible := False;
  end;
  UpdatingChecks := False;
end;
