; Akte Europa Reborn — Windows-Installer (Inno Setup 6)
;
; Was hier hineingeht, ist AUSSCHLIESSLICH die Engine: die exportierte .exe, die
; .pck und die .NET-Laufzeit. Nichts aus dem Spiel von 1997 wird mitgeliefert —
; Gelaende, Einheiten, Karten und Tabellen entstehen beim ersten Start auf dem
; Rechner des Spielers aus dessen eigenen CDs (siehe Core/ContentSources.cs).
;
; Bauen:
;   "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" packaging\AkteEuropaReborn.iss
; (Inno Setup 6 installiert sich ohne Adminrechte NICHT nach Program Files —
;  dort steht es auf diesem Rechner nicht, und die alte Zeile hat einen Lauf
;  gekostet.)
; Voraussetzung: build\windows\ ist frisch exportiert
;   Godot --path . --headless --export-release "Windows Desktop"

#define AppName        "Akte Europa Reborn"
#define AppVersion     "0.6.9"
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
; Das Symbol des Installers selbst. Es liegt neben dieser Datei und wird von
; packaging\make_icon.py erzeugt (7 Groessen, 16..256).
SetupIconFile=AkteEuropaReborn.ico
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\AkteEuropaReborn.ico

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#BuildDir}\{#AppExeName}";                  DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\AkteEuropaReborn.pck";           DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\{#DotNetDir}\*";                 DestDir: "{app}\{#DotNetDir}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Das Symbol muss MIT INSTALLIERT werden, sonst zeigt IconFilename unten ins Leere.
Source: "AkteEuropaReborn.ico";                        DestDir: "{app}"; Flags: ignoreversion

[Icons]
; ⚠ Die Verknuepfungen bekommen das Symbol AUSDRUECKLICH mitgegeben, statt es
; sich aus der .exe zu holen. Godot bettet das Symbol nur ein, wenn im Editor
; ein rcedit hinterlegt ist — ist es das nicht, traegt die .exe still das
; Godot-Symbol weiter, und niemand merkt es bis zum Blick auf den Desktop.
; Mit IconFilename stimmt das Symbol in JEDEM Fall.
Name: "{group}\{#AppName}";        Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\AkteEuropaReborn.ico"
Name: "{autodesktop}\{#AppName}";  Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\AkteEuropaReborn.ico"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Die abgeleiteten Inhalte gehoeren dem Spieler; sie werden NICHT mitgeloescht.
; Wer sie los werden will, loescht %APPDATA%\Godot\app_userdata\ von Hand.

; ---- Hinweis beim Update (16.10.2026, Spielerwunsch "Pruefer, ob Karten
; gebacken werden muessen") ----------------------------------------------------
; Der Installer KANN nicht backen: das braucht die CDs, und das tut das Spiel
; selbst beim naechsten Start (Scripts/Import/InhaltsStand.cs und
; Scripts/UI/MainMenuInhaltsstand.cs vergleichen den Stand der eingelesenen
; Inhalte mit dem, den diese Version braucht, und bieten das Neu-Einlesen nur
; der veralteten Teile an). Hier steht nur der HINWEIS darauf, und nur, wenn
; schon eine ANDERE Version installiert ist. In /SILENT- und /VERYSILENT-Laeufen
; erscheint keine Seite, also stoert er dort nicht.
; UNSERE SETZUNG: eine eigene Seite nach der Begruessung statt eines
; Meldungsfensters -- ein MsgBox wuerde auch in einem stillen Lauf anhalten,
; wenn man ihn nicht eigens abfaengt.

[CustomMessages]
de.UpdateTitel=Update einer vorhandenen Installation
de.UpdateUnter=Es ist bereits Version %1 installiert.
de.UpdateText=Beim naechsten Start prueft das Spiel, ob Karten oder andere Inhalte neu eingelesen werden muessen -- halte ggf. deine CDs bereit (CD 1, fuer Karten auch CD 2).%n%nDeine eingelesenen Inhalte und Spielstaende im Benutzerordner (AppData) bleiben dabei erhalten. Musst du gerade nicht neu einlesen, kannst du im Spiel "Spaeter" waehlen.
en.UpdateTitel=Updating an existing installation
en.UpdateUnter=Version %1 is already installed.
en.UpdateText=On its next start the game checks whether maps or other content must be re-imported -- keep your CDs at hand (CD 1, for maps also CD 2).%n%nYour imported content and saved games in your user folder (AppData) are kept. If you do not want to re-import right now, choose "Spaeter" (later) in the game.

[Code]
// AppId oben in [Setup] -- die doppelte Klammer dort ist nur die Schreibweise
// des Praeprozessors. Inno legt den Deinstallationsschluessel unter
// "<AppId>_is1" an, ohne Adminrechte unter HKCU.
const
  UninstKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{7C2B9A64-1E3D-4B7A-9C2E-AE1997CD0001}_is1';

var
  AlteVersion: String;

function VorherigeVersion(): String;
begin
  Result := '';
  if not RegQueryStringValue(HKCU, UninstKey, 'DisplayVersion', Result) then
    if not RegQueryStringValue(HKLM, UninstKey, 'DisplayVersion', Result) then
      Result := '';
end;

procedure InitializeWizard();
begin
  AlteVersion := VorherigeVersion();
  // nur bei einem echten Update ueber eine ANDERE Version; dieselbe Version
  // noch einmal zu installieren aendert am Importeur nichts
  if (AlteVersion <> '') and (CompareText(AlteVersion, '{#AppVersion}') <> 0) then
    CreateOutputMsgPage(wpWelcome,
      CustomMessage('UpdateTitel'),
      FmtMessage(CustomMessage('UpdateUnter'), [AlteVersion]),
      CustomMessage('UpdateText'));
end;
