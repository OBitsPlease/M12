#define MyAppName "BitsPleaseYT M12"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\windows-publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\release-assets"
#endif

[Setup]
AppId={{C1863C89-02A8-49D5-A85A-A061748C7BE8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=BitsPleaseYT
DefaultDirName={autopf}\BitsPleaseYT M12
DefaultGroupName=BitsPleaseYT M12
OutputDir={#OutputDir}
OutputBaseFilename=BitsPleaseYT-M12-Windows-x64-Setup
SetupIconFile=..\Assets\AppIcon.ico
UninstallDisplayIcon={app}\BitsPleaseYT-M12.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\BitsPleaseYT M12"; Filename: "{app}\BitsPleaseYT-M12.exe"
Name: "{autodesktop}\BitsPleaseYT M12"; Filename: "{app}\BitsPleaseYT-M12.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Run]
Filename: "{app}\BitsPleaseYT-M12.exe"; Description: "Launch BitsPleaseYT M12"; Flags: nowait postinstall skipifsilent
