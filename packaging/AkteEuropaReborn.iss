; Akte Europa Reborn — Windows-Installer (Inno Setup 6)
;
; Was hier hineingeht, ist AUSSCHLIESSLICH die Engine: die exportierte .exe, die
; .pck und die .NET-Laufzeit. Nichts aus dem Spiel von 1997 wird mitgeliefert —
; Gelaende, Einheiten, Karten und Tabellen entstehen beim ersten Start auf dem
; Rechner des Spielers aus dessen eigenen CDs (siehe Core/ContentSources.cs).
;
; Bauen:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" packaging\AkteEuropaReborn.iss
; Voraussetzung: build\windows\ ist frisch exportiert
;   Godot --path . --headless --export-release "Windows Desktop"

#define AppName        "Akte Europa Reborn"
#define AppVersion     "0.3.0"
#define AppPublisher   "chr1zZo"
#define AppExeName     "AkteEuropaReborn.exe"
#define BuildDir       "..\build\windows"
; Godot legt die .NET-Laufzeit in einen Ordner NEBEN die .exe und sucht sie
; genau dort — der Name kommt aus dem Projektnamen und muss beim Ziel erhalten
; bleiben, sonst startet die Anwendung nicht.
#define DotNetDir      "data_Akte Europa Reborn_windows_x86_64"

[Setup]
AppId={{7C2B9A64-1E3D-4B7A-9C2E-AE1997CD0001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright=Copyright (C) 2026 chr1zZo — GPL-3.0
AppPublisherURL=https://github.com/bassprofressor-lab/AkteEuropaReborn
; Ohne Adminrechte: das Spiel schreibt nichts in den Programmordner, die
; abgeleiteten Inhalte liegen unter %APPDATA%. Eine UAC-Abfrage waere also nur
; eine Huerde ohne Gegenwert — und in einer stillen Installation ein Haenger.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=..\build\installer
OutputBaseFilename=AkteEuropaReborn-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#BuildDir}\{#AppExeName}";                  DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\AkteEuropaReborn.pck";           DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\{#DotNetDir}\*";                 DestDir: "{app}\{#DotNetDir}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}";        Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}";  Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Die abgeleiteten Inhalte gehoeren dem Spieler; sie werden NICHT mitgeloescht.
; Wer sie los werden will, loescht %APPDATA%\Godot\app_userdata\ von Hand.
