#!/bin/bash
# Builds the drag-to-Applications disk image, from the .app inside the packed zip.
#
# A DMG is what people expect a Mac app to arrive as: open it, drag the icon onto the
# Applications shortcut next to it, done. The shortcut is a real symlink to /Applications,
# which is why the window makes sense without any explaining.
#
# Runs on macOS only - hdiutil is Apple's.
set -euo pipefail

RELEASE_DIR="$1"
VERSION="$2"
APP_NAME="FreeRealmsLegacyLauncher"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

ZIP="$(find "$RELEASE_DIR" -name '*Portable.zip' -print -quit)"

if [ -z "$ZIP" ]; then
    echo "  No portable zip in $RELEASE_DIR - nothing to build a DMG from."
    exit 1
fi

mkdir -p "$WORK/stage"
ditto -x -k "$ZIP" "$WORK/extract"

APP="$(find "$WORK/extract" -maxdepth 2 -name '*.app' -print -quit)"

if [ -z "$APP" ]; then
    echo "  No .app inside $ZIP."
    exit 1
fi

# ditto rather than cp, so signatures and extended attributes survive the copy.
ditto "$APP" "$WORK/stage/$(basename "$APP")"
ln -s /Applications "$WORK/stage/Applications"

DMG="$RELEASE_DIR/$APP_NAME-$VERSION.dmg"
rm -f "$DMG"

hdiutil create \
    -volname "Free Realms Legacy" \
    -srcfolder "$WORK/stage" \
    -ov -format UDZO \
    "$DMG" >/dev/null

# A signed DMG keeps its own signature intact through the download, so Gatekeeper
# assesses the image as a whole rather than only what comes out of it.
if [ -n "${MACOS_SIGN_IDENTITY:-}" ]; then
    codesign --force --sign "$MACOS_SIGN_IDENTITY" --timestamp "$DMG"
    echo "  Signed the disk image."
fi

echo "  $(basename "$DMG") - $(du -h "$DMG" | cut -f1)"
