#ifndef AppPublishDir
  #define AppPublishDir "..\artifacts\publish\win-x64"
#endif
#define AppName "ArcTrellis"
#define AppVersion "1.3.22"
#define AppPublisher "ArcTrellis"
#define AppExeName "ArcTrellis.exe"

[Setup]
AppId={{D8B9500E-3A55-4A73-9B9D-81BFB641109B}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousAppDir=no
UsePreviousTasks=no
DirExistsWarning=no
OutputDir=..\artifacts\installer
OutputBaseFilename=ArcTrellis-Setup-{#AppVersion}-win-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoDescription=ArcTrellis Story Planner installer
VersionInfoProductName=ArcTrellis

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
english.LaunchApp=Launch ArcTrellis
russian.LaunchApp=Запустить ArcTrellis

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"

[Files]
Source: "{#AppPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ArcTrellis"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\ArcTrellis"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon; Check: not FileExists(ExpandConstant('{autodesktop}\ArcTrellis.lnk'))

[Registry]
Root: HKA; Subkey: "Software\Classes\.arctrellis"; ValueType: string; ValueName: ""; ValueData: "ArcTrellis.Project"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\ArcTrellis.Project"; ValueType: string; ValueName: ""; ValueData: "ArcTrellis Story Project"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\ArcTrellis.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"
Root: HKA; Subkey: "Software\Classes\ArcTrellis.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent
