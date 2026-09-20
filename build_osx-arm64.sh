#!/bin/bash
set -e

# Find the absolute path of the script
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Check if version parameter is provided
if [ "$#" -ne 1 ]; then
    echo "Version number is required."
    echo "Usage: ./build.sh [version]"
    exit 1
fi

BUILD_VERSION="$1"
RELEASE_DIR="$SCRIPT_DIR/releases"
PUBLISH_DIR="$SCRIPT_DIR/publish"

echo ""
echo "Compiling Launcher with dotnet..."
dotnet publish ./src/Launcher.slnx -c Release --self-contained -r osx-arm64 --property:PublishDir="$PUBLISH_DIR"

echo ""
source "$SCRIPT_DIR/build-mac-sign.sh"

echo ""
echo "Building Velopack Release v$BUILD_VERSION"
vpk pack --packTitle "Free Realms Legacy" --packAuthors "Raising Kaines" -u FreeRealmsLegacyLauncher -e Launcher -o "$RELEASE_DIR" -p "$PUBLISH_DIR" -i ./assets/icon.icns --channel osx-arm64 -v $BUILD_VERSION "${SIGN_ARGS[@]}"

echo ""
echo "Building the disk image..."
chmod +x "$SCRIPT_DIR/build-mac-dmg.sh"
"$SCRIPT_DIR/build-mac-dmg.sh" "$RELEASE_DIR" "$BUILD_VERSION"
