#define MyAppName "Asterism"
#define MyAppVersion GetEnv("ASTERISM_VERSION")
#define MySourceDir GetEnv("ASTERISM_SOURCE_DIR")
#define MyOutputDir GetEnv("ASTERISM_OUTPUT_DIR")

[Setup]
AppId={{0262B536-6AF6-46E8-AA3C-D1E833A5E286}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Reboot-Together
AppPublisherURL=https://github.com/Reboot-Together/node
AppSupportURL=https://github.com/Reboot-Together/node/issues
AppUpdatesURL=https://github.com/Reboot-Together/node/releases
DefaultDirName={localappdata}\Programs\Asterism
DefaultGroupName=Asterism
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#MyOutputDir}
OutputBaseFilename=Asterism-{#MyAppVersion}-Setup-win-x64
SetupIconFile=..\Assets\Asterism.ico
UninstallDisplayIcon={app}\Asterism.exe
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
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Excludes: "Asterism.exe.WebView2\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: files; Name: "{app}\Node.exe"
Type: files; Name: "{autodesktop}\Node.lnk"
Type: files; Name: "{autoprograms}\Node.lnk"

[Icons]
Name: "{autoprograms}\Asterism"; Filename: "{app}\Asterism.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Asterism"; Filename: "{app}\Asterism.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Asterism.exe"; Description: "Asterism 실행"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
