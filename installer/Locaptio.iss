#ifndef AppSource
  #error AppSource must name the prepared distribution folder
#endif
#ifndef AppVersion
  #define AppVersion "1.0.39"
#endif
[Setup]
AppId={{33DCECCB-21D1-4D03-93BE-DA69E751A5A7}
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
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: desktopicon; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autoprograms}\Locaptio"; Filename: "{app}\Locaptio.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Locaptio"; Filename: "{app}\Locaptio.exe"; WorkingDir: "{app}"; Tasks: desktopicon
[Run]
Filename: "{app}\Locaptio.exe"; Description: "{cm:LaunchProgram,Locaptio}"; Flags: nowait postinstall skipifsilent
; User models, recordings and settings are deliberately retained on uninstall.
