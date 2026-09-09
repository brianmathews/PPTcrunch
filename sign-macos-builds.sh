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
PKG_IDENTIFIER="${PKG_IDENTIFIER:-com.ab6d.pptcrunch}"
PKG_INSTALL_LOCATION="/usr/local/bin"

if [ -f "$ENV_FILE" ]; then
    echo "Sourcing environment variables from $ENV_FILE"
    # shellcheck source=/dev/null
    source "$ENV_FILE"
else
    echo "Warning: Environment file $ENV_FILE not found"
    echo "Will rely on environment variables already set"
fi

if [ -n "${PKG_VERSION:-}" ]; then
    echo "ERROR: PKG_VERSION is now derived from the executable. Set the release version in Version.props instead."
    exit 1
fi

require_env() {
    local name="$1"
    local hint="$2"
    if [ -z "${!name:-}" ]; then
        echo "Error: $name environment variable is not set"
        echo "Please set it with: export $name='$hint'"
        exit 1
    fi
}

require_env DEVELOPER_CERTIFICATE_ID "Developer ID Application: Your Name (XXXXXXXXXX)"
require_env DEVELOPER_INSTALLER_ID "Developer ID Installer: Your Name (XXXXXXXXXX)"
require_env APPLE_ID "your.apple.id@example.com"
require_env APPLE_ID_PASSWORD "your-app-specific-password"
require_env APPLE_TEAM_ID "your-team-id"

# Always build from current source instead of silently signing a stale artifact.
"$PUBLISH_SCRIPT"

# Read the version from this exact artifact, keeping installer and app in sync.
APP_VERSION_OUTPUT="$("$BINARY_PATH" --version)"
PKG_VERSION="${APP_VERSION_OUTPUT#pptcrunch }"
if [[ ! "$PKG_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "ERROR: Unexpected executable version: $APP_VERSION_OUTPUT"
    exit 1
fi
PKG_PATH="$DISTRIBUTION_DIR/PPTcrunchInstaller-${PKG_VERSION}-ARM.pkg"
LATEST_PKG_PATH="$DISTRIBUTION_DIR/${BINARY_NAME}-macos.pkg"

if [ ! -f "$BINARY_PATH" ]; then
    echo "Error: Published binary not found at $BINARY_PATH after running publish script"
    exit 1
fi

echo "Preparing to sign $BINARY_PATH"
bash "$SCRIPT_DIR/verify-macos-dependencies.sh" "$BINARY_PATH"
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
    echo "ERROR: Missing $ENTITLEMENTS_FILE; the .NET runtime requires the allow-jit entitlement."
    exit 1
fi

echo "Checking application certificate availability..."
if ! security find-identity -v -p codesigning | grep -F "$DEVELOPER_CERTIFICATE_ID"; then
    echo "ERROR: Certificate $DEVELOPER_CERTIFICATE_ID not found in keychain!"
    echo "Available certificates:"
    security find-identity -v -p codesigning
    exit 1
fi

echo "Checking installer certificate availability..."
if ! security find-identity -v | grep -F "$DEVELOPER_INSTALLER_ID"; then
    echo "ERROR: Certificate $DEVELOPER_INSTALLER_ID not found in keychain!"
    echo "Installer packages must be signed with a Developer ID Installer certificate."
    echo "Available identities:"
    security find-identity -v
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
    echo "ERROR: Hardened runtime is required for notarization. Fix codesign and retry."
    exit 1
fi

echo "Verifying signature..."
codesign -v --deep --strict --verbose=2 "$BINARY_PATH"

echo "Testing launch with the signed hardened runtime..."
"$BINARY_PATH" --help > /dev/null

if ! codesign -d --entitlements - "$BINARY_PATH" 2>/dev/null; then
    echo "No entitlements embedded in binary"
fi

echo "Building installer package (installs to $PKG_INSTALL_LOCATION)..."
mkdir -p "$DISTRIBUTION_DIR"
PKG_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/pptcrunch-pkg.XXXXXX")"
cleanup() {
    rm -rf "$PKG_ROOT"
}
trap cleanup EXIT

cp "$BINARY_PATH" "$PKG_ROOT/$BINARY_NAME"
chmod 755 "$PKG_ROOT/$BINARY_NAME"
xattr -cr "$PKG_ROOT/$BINARY_NAME"

rm -f "$PKG_PATH"

pkgbuild \
    --root "$PKG_ROOT" \
    --install-location "$PKG_INSTALL_LOCATION" \
    --identifier "$PKG_IDENTIFIER" \
    --version "$PKG_VERSION" \
    --sign "$DEVELOPER_INSTALLER_ID" \
    --timestamp \
    "$PKG_PATH"

echo "Verifying installer signature..."
pkgutil --check-signature "$PKG_PATH"

echo "Submitting installer for notarization..."
SUBMISSION_OUTPUT="$(xcrun notarytool submit "$PKG_PATH" \
    --apple-id "$APPLE_ID" \
    --password "$APPLE_ID_PASSWORD" \
    --team-id "$APPLE_TEAM_ID" \
    --wait \
    --output-format json)"

echo "Notarization result:"
echo "$SUBMISSION_OUTPUT"

STATUS="$(python3 -c 'import json,sys; print(json.load(sys.stdin).get("status",""))' <<<"$SUBMISSION_OUTPUT" 2>/dev/null || true)"
SUBMISSION_ID="$(python3 -c 'import json,sys; print(json.load(sys.stdin).get("id",""))' <<<"$SUBMISSION_OUTPUT" 2>/dev/null || true)"

if [ -z "$STATUS" ]; then
    STATUS="$(echo "$SUBMISSION_OUTPUT" | awk '/"status"/ {gsub(/[",]/, "", $2); print $2; exit}')"
fi
if [ -z "$SUBMISSION_ID" ]; then
    SUBMISSION_ID="$(echo "$SUBMISSION_OUTPUT" | awk '/"id"/ {gsub(/[",]/, "", $2); print $2; exit}')"
fi

if [ -n "$SUBMISSION_ID" ]; then
    echo "Retrieving notarization log for submission $SUBMISSION_ID..."
    xcrun notarytool log "$SUBMISSION_ID" \
        --apple-id "$APPLE_ID" \
        --password "$APPLE_ID_PASSWORD" \
        --team-id "$APPLE_TEAM_ID" || echo "Could not retrieve notarization log"
fi

if [ "$STATUS" != "Accepted" ]; then
    echo "ERROR: Notarization did not succeed (status: ${STATUS:-unknown})"
    exit 1
fi

echo "Stapling notarization ticket to installer..."
xcrun stapler staple "$PKG_PATH"
xcrun stapler validate "$PKG_PATH"

cp "$PKG_PATH" "$LATEST_PKG_PATH"

echo "Signing and notarization complete!"
echo "Distribution packages are available in: $DISTRIBUTION_DIR"
echo "- Installer (upload this): $PKG_PATH"
echo "- Latest copy: $LATEST_PKG_PATH"
echo "Installs $BINARY_NAME to $PKG_INSTALL_LOCATION (on the default macOS PATH)."
echo "Users can run: $BINARY_NAME --help"
