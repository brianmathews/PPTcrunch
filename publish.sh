#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PUBLISH_DIR="$SCRIPT_DIR/publish/osx-arm64"

printf '\n========================================\n'
printf ' PPTcrunch - macOS Release Publisher\n'
printf '========================================\n\n'

printf 'Building self-contained single file executable with automatic FFmpeg download...\n'
printf 'Target: macOS (Apple silicon, arm64) - no .NET runtime or FFmpeg installation required\n\n'

if ! command -v git >/dev/null 2>&1; then
    printf 'ERROR: Git is required to derive the build number from the current commit.\n' >&2
    exit 1
fi

if [[ "$(git -C "$SCRIPT_DIR" rev-parse --is-shallow-repository)" == "true" ]]; then
    printf 'ERROR: A full Git clone is required because shallow clones do not have a stable total commit count.\n' >&2
    exit 1
fi

BUILD_NUMBER="$(git -C "$SCRIPT_DIR" rev-list --count HEAD)"
if [[ ! "$BUILD_NUMBER" =~ ^[0-9]+$ ]]; then
    printf 'ERROR: Could not derive a numeric build number from Git.\n' >&2
    exit 1
fi
printf 'Build number: %s (Git commit count)\n\n' "$BUILD_NUMBER"

rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR"

dotnet clean "$SCRIPT_DIR/PPTcrunch.csproj" --configuration Release -p:BuildNumber="$BUILD_NUMBER" > /dev/null

dotnet publish "$SCRIPT_DIR/PPTcrunch.csproj" \
    --configuration Release \
    --runtime osx-arm64 \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=false \
    -p:TrimMode=partial \
    -p:PublishReadyToRun=true \
    -p:BuildNumber="$BUILD_NUMBER" \
    -o "$PUBLISH_DIR"

printf '\nChecking build results...\n\n'

if [[ -f "$PUBLISH_DIR/pptcrunch" ]]; then
    bash "$SCRIPT_DIR/verify-macos-dependencies.sh" "$PUBLISH_DIR/pptcrunch"
    "$PUBLISH_DIR/pptcrunch" --help > /dev/null
    "$PUBLISH_DIR/pptcrunch" --version
    printf '========================================\n'
    printf ' Build completed successfully!\n'
    printf '========================================\n\n'
    printf 'Single-file executable created:\n'
    printf '  %s\n\n' "$PUBLISH_DIR/pptcrunch"
    printf '[OK] Single-file deployment ready\n'
    printf '[OK] No external dependencies required\n'
    printf '[OK] FFmpeg is downloaded automatically on first use\n'
    printf '[OK] Auto-detects NVIDIA NVENC and Apple VideoToolbox hardware when available\n'
    printf '[OK] Self-contained includes .NET 10 runtime\n\n'
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
