# Build script for Akte Europa Reborn (Windows PowerShell)

param(
    [switch]$Test,
    [switch]$Export,
    [string]$Platform = "Windows Desktop",
    [string]$GodotPath = "godot"
)

$ErrorActionPreference = "Stop"
$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

Write-Host "=== Akte Europa Reborn Build Script ===" -ForegroundColor Cyan
Write-Host "Project: $ProjectDir"
Write-Host "Godot: $GodotPath"

# Check dependencies
function Check-Command($name) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
        Write-Error "ERROR: $name not found in PATH"
        exit 1
    }
}

Check-Command $GodotPath
Check-Command dotnet

# Restore NuGet packages
Write-Host "--- Restoring NuGet packages ---" -ForegroundColor Yellow
Set-Location $ProjectDir
dotnet restore "Akte Europa Reborn.csproj"

# Build C# project
Write-Host "--- Building C# project ---" -ForegroundColor Yellow
dotnet build "Akte Europa Reborn.csproj" -c Release

# Validate with Godot
Write-Host "--- Validating project with Godot ---" -ForegroundColor Yellow
& $GodotPath --headless --check-only --path "$ProjectDir"

Write-Host "=== Build successful! ===" -ForegroundColor Green

# Run tests
if ($Test) {
    Write-Host "--- Running tests ---" -ForegroundColor Yellow
    dotnet test AkteEuropaReborn.Tests/AkteEuropaReborn.Tests.csproj -c Release
}

# Export
if ($Export) {
    Write-Host "--- Exporting for $Platform ---" -ForegroundColor Yellow
    $exportDir = Join-Path $ProjectDir "build"
    if (-not (Test-Path $exportDir)) { New-Item -ItemType Directory -Path $exportDir }
    & $GodotPath --headless --export-release "$Platform" "$exportDir/AkteEuropaReborn.exe" --path "$ProjectDir"
}