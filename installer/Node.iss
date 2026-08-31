#define MyAppName "Node"
#define MyAppVersion GetEnv("NODE_VERSION")
#define MySourceDir GetEnv("NODE_SOURCE_DIR")
#define MyOutputDir GetEnv("NODE_OUTPUT_DIR")

[Setup]
AppId={{0262B536-6AF6-46E8-AA3C-D1E833A5E286}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Reboot-Together
AppPublisherURL=https://github.com/Reboot-Together/node
AppSupportURL=https://github.com/Reboot-Together/node/issues
AppUpdatesURL=https://github.com/Reboot-Together/node/releases
DefaultDirName={localappdata}\Programs\Node
DefaultGroupName=Node
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#MyOutputDir}
OutputBaseFilename=Node-{#MyAppVersion}-Setup-win-x64
SetupIconFile=..\Assets\Node.ico
UninstallDisplayIcon={app}\Node.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕 화면 바로가기 만들기"; GroupDescription: "추가 바로가기:"; Flags: checkedonce

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Node"; Filename: "{app}\Node.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Node"; Filename: "{app}\Node.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Node.exe"; Description: "Node 실행"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
