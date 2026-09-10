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
echo "Building Velopack Release v$BUILD_VERSION"
vpk pack --packTitle "FreeRealms Legacy Launcher" --packAuthors "FreeRealms Legacy Team" -u FreeRealmsLegacyLauncher -e Launcher -o "$RELEASE_DIR" -p "$PUBLISH_DIR" -i ./assets/icon.icns -v $BUILD_VERSION