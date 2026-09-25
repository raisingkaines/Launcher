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
dotnet publish ./src/Launcher.slnx -c Release --self-contained -r linux-x64 --property:PublishDir="$PUBLISH_DIR"

echo ""
echo "Building Velopack Release v$BUILD_VERSION"
vpk pack --packTitle "Free Realms Legacy" --packAuthors "Raising Kaines" -u FreeRealmsLegacyLauncher -e Launcher -o "$RELEASE_DIR" -p "$PUBLISH_DIR" -i ./assets/icon.png -v $BUILD_VERSION

echo ""
echo "Building Debian package (easy install for Linux and ChromeOS)"
python3 "$SCRIPT_DIR/build-deb.py" "$BUILD_VERSION" "$PUBLISH_DIR" "$RELEASE_DIR" amd64

echo ""
echo "Building RPM (Fedora, RHEL, openSUSE)"
python3 "$SCRIPT_DIR/build-rpm.py" "$BUILD_VERSION" "$PUBLISH_DIR" "$RELEASE_DIR" x86_64

echo ""
echo "Building pacman package (Arch, Manjaro)"
python3 "$SCRIPT_DIR/build-arch.py" "$BUILD_VERSION" "$PUBLISH_DIR" "$RELEASE_DIR" x86_64

echo ""
echo "Building the generic tarball (every other distribution)"
python3 "$SCRIPT_DIR/build-tarball.py" "$BUILD_VERSION" "$PUBLISH_DIR" "$RELEASE_DIR" x86_64

echo ""
echo "Checking the packages read back correctly"
python3 "$SCRIPT_DIR/validate-linux-packages.py" "$RELEASE_DIR"
