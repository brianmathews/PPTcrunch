#!/usr/bin/env bash
set -euo pipefail

echo "Signing macOS builds..."

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PUBLISH_SCRIPT="$SCRIPT_DIR/publish.sh"
PUBLISH_DIR="$SCRIPT_DIR/publish/osx-arm64"
BINARY_NAME="pptcrunch"
BINARY_PATH="$PUBLISH_DIR/$BINARY_NAME"
ENTITLEMENTS_FILE="$SCRIPT_DIR/${BINARY_NAME}.entitlements"
ENV_FILE="$SCRIPT_DIR/sign-macos-builds.env"
DISTRIBUTION_DIR="$SCRIPT_DIR/publish/distribution"
ZIP_FOR_NOTARIZATION="$DISTRIBUTION_DIR/${BINARY_NAME}-macos.zip"
FINAL_ZIP="$DISTRIBUTION_DIR/${BINARY_NAME}-macos-final.zip"
README_FILE="$PUBLISH_DIR/README.txt"

if [ -f "$ENV_FILE" ]; then
    echo "Sourcing environment variables from $ENV_FILE"
    # shellcheck source=/dev/null
    source "$ENV_FILE"
else
    echo "Warning: Environment file $ENV_FILE not found"
    echo "Will rely on environment variables already set"
fi

# Check for required environment variables
if [ -z "${DEVELOPER_CERTIFICATE_ID:-}" ]; then
    echo "Error: DEVELOPER_CERTIFICATE_ID environment variable is not set"
    echo "Please set it with: export DEVELOPER_CERTIFICATE_ID='Developer ID Application: Your Name (XXXXXXXXXX)'"
    exit 1
fi

if [ -z "${APPLE_ID:-}" ]; then
    echo "Error: APPLE_ID environment variable is not set"
    echo "Please set it with: export APPLE_ID='your.apple.id@example.com'"
    exit 1
fi

if [ -z "${APPLE_ID_PASSWORD:-}" ]; then
    echo "Error: APPLE_ID_PASSWORD environment variable is not set"
    echo "Please set it with: export APPLE_ID_PASSWORD='your-app-specific-password'"
    exit 1
fi

if [ -z "${APPLE_TEAM_ID:-}" ]; then
    echo "Error: APPLE_TEAM_ID environment variable is not set"
    echo "Please set it with: export APPLE_TEAM_ID='your-team-id'"
    exit 1
fi

if [ ! -f "$BINARY_PATH" ]; then
    if [ -x "$PUBLISH_SCRIPT" ]; then
        echo "Published binary not found. Running publish script..."
        "$PUBLISH_SCRIPT"
    else
        echo "Error: Published binary not found at $BINARY_PATH and publish script $PUBLISH_SCRIPT is not executable"
        exit 1
    fi
fi

if [ ! -f "$BINARY_PATH" ]; then
    echo "Error: Published binary not found at $BINARY_PATH after running publish script"
    exit 1
fi

echo "Preparing to sign $BINARY_PATH"
ls -la "$BINARY_PATH"
file "$BINARY_PATH"

chmod +x "$BINARY_PATH"

echo "Cleaning extended attributes..."
xattr -cr "$BINARY_PATH"

echo "Checking entitlements file..."
if [ -f "$ENTITLEMENTS_FILE" ]; then
    echo "Entitlements file found: $ENTITLEMENTS_FILE"
    cat "$ENTITLEMENTS_FILE"
else
    echo "No custom entitlements file found - CLI programs typically don't require special entitlements"
fi

echo "Checking certificate availability..."
if ! security find-identity -v -p codesigning | grep -F "$DEVELOPER_CERTIFICATE_ID"; then
    echo "ERROR: Certificate $DEVELOPER_CERTIFICATE_ID not found in keychain!"
    echo "Available certificates:"
    security find-identity -v -p codesigning
    exit 1
fi

echo "Checking keychain access..."
security list-keychains
security default-keychain

sign_binary() {
    local apply_runtime="$1"
    local runtime_flag=()

    if [ "$apply_runtime" = "true" ]; then
        runtime_flag=(-o runtime)
        echo "Applying hardened runtime..."
    else
        echo "Attempting code signing without hardened runtime..."
    fi

    local sign_args=(
        codesign
        -s "$DEVELOPER_CERTIFICATE_ID"
        -f
        -v
        --timestamp
    )
    
    # Add runtime flag only if it's not empty
    if [ ${#runtime_flag[@]} -gt 0 ]; then
        sign_args+=("${runtime_flag[@]}")
    fi

    if [ -f "$ENTITLEMENTS_FILE" ]; then
        sign_args+=(--entitlements "$ENTITLEMENTS_FILE")
    fi

    sign_args+=("$BINARY_PATH")

    if ! "${sign_args[@]}" 2>&1; then
        if [ "$apply_runtime" = "true" ]; then
            echo "WARNING: Could not apply hardened runtime"
            return 1
        fi

        echo "ERROR: Code signing failed"
        echo "This usually indicates certificate or keychain issues"
        exit 1
    fi

    if [ "$apply_runtime" = "true" ]; then
        echo "Hardened runtime applied successfully"
    else
        echo "Code signing without hardened runtime succeeded"
    fi

    return 0
}

sign_binary "false"
if ! sign_binary "true"; then
    echo "Continuing without hardened runtime. Notarization may fail without it."
fi

echo "Verifying signature..."
codesign -v --deep --strict --verbose=2 "$BINARY_PATH"

if ! codesign -d --entitlements - "$BINARY_PATH" 2>/dev/null; then
    echo "No entitlements embedded in binary"
fi

mkdir -p "$DISTRIBUTION_DIR"

cat > "$README_FILE" <<EOL
pptcrunch for macOS

Installation:
1. Copy $BINARY_NAME to any directory (e.g., ~/bin or /usr/local/bin)
2. Make sure the directory is in your PATH
3. Run the program by typing: $BINARY_NAME [options]

For help with command-line options:
   $BINARY_NAME --help

Note: You may need to run 'chmod +x $BINARY_NAME' after copying to a new location.
EOL

echo "Creating ZIP archive for notarization..."
ditto -c -k --keepParent "$BINARY_PATH" "$ZIP_FOR_NOTARIZATION"

echo "Submitting build for notarization..."
SUBMISSION_OUTPUT=$(xcrun notarytool submit "$ZIP_FOR_NOTARIZATION" \
    --apple-id "$APPLE_ID" \
    --password "$APPLE_ID_PASSWORD" \
    --team-id "$APPLE_TEAM_ID" \
    --wait)

echo "Notarization result:"
echo "$SUBMISSION_OUTPUT"

SUBMISSION_ID=$(echo "$SUBMISSION_OUTPUT" | grep "id:" | head -1 | awk '{print $2}')
if [ -n "$SUBMISSION_ID" ]; then
    echo "Retrieving notarization log for submission $SUBMISSION_ID..."
    xcrun notarytool log "$SUBMISSION_ID" \
        --apple-id "$APPLE_ID" \
        --password "$APPLE_ID_PASSWORD" \
        --team-id "$APPLE_TEAM_ID" || echo "Could not retrieve notarization log"
else
    echo "Could not extract notarization submission ID"
fi

echo "Creating final distribution package..."
rm -f "$FINAL_ZIP"
(
    cd "$PUBLISH_DIR"
    zip -r "$FINAL_ZIP" "$BINARY_NAME" "$(basename "$README_FILE")"
)

echo "Signing and notarization complete!"
echo "Distribution packages are available in: $DISTRIBUTION_DIR"
echo "- Notarization upload: $ZIP_FOR_NOTARIZATION"
echo "- Final user package: $FINAL_ZIP"
