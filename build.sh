#!/usr/bin/env bash
# Build script for Akte Europa Reborn

set -e

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GODOT="${GODOT:-godot}"

echo "=== Akte Europa Reborn Build Script ==="
echo "Project: $PROJECT_DIR"
echo "Godot: $GODOT"

# Function to check command exists
check_command() {
    if ! command -v "$1" &> /dev/null; then
        echo "ERROR: $1 not found in PATH"
        exit 1
    fi
}

# Check dependencies
check_command "$GODOT"
check_command dotnet

# Restore NuGet packages
echo "--- Restoring NuGet packages ---"
cd "$PROJECT_DIR"
dotnet restore "Akte Europa Reborn.csproj"

# Build C# project
echo "--- Building C# project ---"
dotnet build "Akte Europa Reborn.csproj" -c Release

# Run Godot headless for validation
echo "--- Validating project with Godot ---"
"$GODOT" --headless --check-only --path "$PROJECT_DIR"

echo "=== Build successful! ==="

# Optional: Run tests
if [[ "$1" == "--test" ]]; then
    echo "--- Running tests ---"
    dotnet test AkteEuropaReborn.Tests/AkteEuropaReborn.Tests.csproj -c Release
fi

# Optional: Export
if [[ "$1" == "--export" ]]; then
    PLATFORM="${2:-Linux/X11}"
    echo "--- Exporting for $PLATFORM ---"
    "$GODOT" --headless --export-release "$PLATFORM" "$PROJECT_DIR/build/AkteEuropaReborn" --path "$PROJECT_DIR"
fi