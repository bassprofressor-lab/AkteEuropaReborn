@echo off
rem ============================================================================
rem  spielen.cmd — bauen und SOFORT starten, ohne Export und ohne Installer.
rem
rem  Der Weg ueber `--export-release` und ISCC kostet rund eine Minute; Godot
rem  kann das Projekt aber direkt laufen lassen. Fuer einen Pruefdurchgang, bei
rem  dem nach jeder Behebung neu gestartet wird, ist das der Unterschied
rem  zwischen einer Minute und fuenf Sekunden.
rem
rem  Aufruf:
rem     spielen                 Hauptmenue
rem     spielen 18              Kampagnenmission 18 direkt
rem     spielen 18 --no-briefing --erwartung=18
rem                             Mission 18 ohne Vorspann, Erwartungsblatt zuerst
rem     spielen 0 --map=map_NET02
rem                             Gefecht/Karte statt Kampagne
rem
rem  ⚠ Alles ab dem zweiten Wort geht unveraendert ans Spiel.
rem ============================================================================
setlocal
set GODOT=C:\Users\chrizzo\Downloads\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe
set PROJ=%~dp0

echo [1/2] bauen ...
dotnet build "%PROJ%Akte Europa Reborn.sln" -v q --nologo
if errorlevel 1 (
  echo.
  echo   BAU FEHLGESCHLAGEN — es wird nicht gestartet.
  exit /b 1
)

set ARGS=
if not "%~1"=="" if not "%~1"=="0" set ARGS=--campaign=%~1
:weiter
shift
if "%~1"=="" goto starten
set ARGS=%ARGS% %~1
goto weiter

:starten
echo [2/2] starten: %ARGS%
"%GODOT%" --path "%PROJ%" -- %ARGS%
endlocal
