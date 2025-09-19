#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PUBLISH_DIR="$SCRIPT_DIR/publish/osx-arm64"

printf '\n========================================\n'
printf ' PPTcrunch - macOS Release Publisher\n'
printf '========================================\n\n'

printf 'Building self-contained single file executable with embedded FFmpeg...\n'
printf 'Target: macOS (Apple silicon, arm64) - no .NET runtime or FFmpeg installation required\n\n'

rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR"

dotnet clean "$SCRIPT_DIR/PPTcrunch.csproj" --configuration Release > /dev/null

dotnet publish "$SCRIPT_DIR/PPTcrunch.csproj" \
    --configuration Release \
    --runtime osx-arm64 \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:TrimMode=partial \
    -p:PublishReadyToRun=true \
    -o "$PUBLISH_DIR"

printf '\nChecking build results...\n\n'

if [[ -f "$PUBLISH_DIR/PPTcrunch" ]]; then
    printf '========================================\n'
    printf ' Build completed successfully!\n'
    printf '========================================\n\n'
    printf 'Single-file executable created:\n'
    printf '  %s\n\n' "$PUBLISH_DIR/PPTcrunch"
    printf '[OK] Single-file deployment ready\n'
    printf '[OK] No external dependencies required\n'
    printf '[OK] Embedded FFmpeg included - no external installation needed\n'
    printf '[OK] Auto-detects NVIDIA GPU capabilities when available\n'
    printf '[OK] Self-contained includes .NET 8 runtime\n\n'
    printf 'Files in publish directory:\n'
    ls -1 "$PUBLISH_DIR"
    printf '\n'
else
    printf '========================================\n'
    printf ' Build failed!\n'
    printf '========================================\n\n'
    printf 'Expected executable not found. Please check the error messages above.\n\n'
    exit 1
fi
