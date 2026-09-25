"""Builds the generic Linux tarball, for every distribution without a package of its own.

Gentoo, Void, Slackware, Solus, NixOS, anything running a package manager this project
does not build for - and anyone who would rather not install a package at all. The tarball
runs from wherever it is unpacked; install.sh is there for people who want a menu entry.

The executable bit is set explicitly on the launcher rather than copied from disk, because
Windows has no such bit and the tarball is built there.

  build-tarball.py [version] [publishDir] [outDir] [arch]
"""
import io
import os
import sys
import tarfile
import time

ROOT = os.path.dirname(os.path.abspath(__file__))

VERSION = sys.argv[1] if len(sys.argv) > 1 else "1.3.1"
PUBLISH = os.path.abspath(sys.argv[2]) if len(sys.argv) > 2 else os.path.join(ROOT, "publish-linux")
OUT_DIR = os.path.abspath(sys.argv[3]) if len(sys.argv) > 3 else os.path.join(ROOT, "releases-linux")
ARCH = sys.argv[4] if len(sys.argv) > 4 else "x86_64"

ICON = os.path.join(ROOT, "assets", "icon.png")

NAME = "freerealmslegacy"
NOW = int(time.time())

TOP = f"FreeRealmsLegacyLauncher-{VERSION}"

INSTALL_SH = """#!/bin/sh
# Installs the launcher for the current user - no root, nothing outside your home.
set -e

here="$(cd "$(dirname "$0")" && pwd)"
target="$HOME/.local/share/freerealmslegacy"
bindir="$HOME/.local/bin"
apps="$HOME/.local/share/applications"
icons="$HOME/.local/share/icons/hicolor/256x256/apps"

echo "Installing to $target"

mkdir -p "$target" "$bindir" "$apps" "$icons"

# Everything except this script and the readme.
for entry in "$here"/*; do
    name="$(basename "$entry")"

    case "$name" in
        install.sh | uninstall.sh | README) continue ;;
    esac

    cp -R "$entry" "$target/"
done

chmod +x "$target/Launcher"

cat > "$bindir/freerealmslegacy" <<EOF
#!/bin/sh
exec "$target/Launcher" "\\$@"
EOF

chmod +x "$bindir/freerealmslegacy"

if [ -f "$here/icon.png" ]; then
    cp "$here/icon.png" "$icons/freerealmslegacy.png"
fi

cat > "$apps/freerealmslegacy.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Free Realms Legacy
Comment=Play Free Realms Legacy
Exec=$bindir/freerealmslegacy %U
Icon=freerealmslegacy
Terminal=false
Categories=Game;
StartupWMClass=Launcher
EOF

if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database -q "$apps" || true
fi

echo "Done."
echo ""

case ":$PATH:" in
    *":$bindir:"*) echo "Run it with: freerealmslegacy" ;;
    *) echo "Run it with: $bindir/freerealmslegacy"
       echo "($bindir is not on your PATH, so the short name will not work yet.)" ;;
esac

if ! command -v wine >/dev/null 2>&1; then
    echo ""
    echo "Wine was not found. The launcher runs without it, but the game itself needs"
    echo "Wine and its 32-bit support - install them with your package manager."
fi
"""

UNINSTALL_SH = """#!/bin/sh
# Removes what install.sh created, and nothing else.
set -e

rm -rf "$HOME/.local/share/freerealmslegacy"
rm -f "$HOME/.local/bin/freerealmslegacy"
rm -f "$HOME/.local/share/applications/freerealmslegacy.desktop"
rm -f "$HOME/.local/share/icons/hicolor/256x256/apps/freerealmslegacy.png"

echo "Removed. Your settings and downloaded game files were left alone."
"""

README = f"""Free Realms Legacy Launcher {VERSION}
{'=' * (28 + len(VERSION))}

For any Linux distribution. If yours has one of the packages built alongside this
tarball - .deb, .rpm, or .pkg.tar.xz - prefer that one; it will keep itself updated
through your package manager.

Running it without installing
-----------------------------

    chmod +x Launcher
    ./Launcher

That is all it needs. The launcher is self-contained.

Installing it properly
----------------------

    ./install.sh

Installs into ~/.local, so it needs no root and touches nothing outside your home
directory. It adds a menu entry and a 'freerealmslegacy' command. Undo it with
./uninstall.sh, which leaves your settings and downloaded game files alone.

Playing the game
----------------

The launcher runs natively on Linux. The game client is a 32-bit Windows program and
runs through Wine, which needs its 32-bit support installed:

    Debian, Ubuntu, ChromeOS   sudo dpkg --add-architecture i386
                               sudo apt update && sudo apt install wine wine32
    Fedora                     sudo dnf install wine wine-core.i686
    Arch                       sudo pacman -S wine          (needs multilib enabled)
    openSUSE                   sudo zypper install wine

The launcher checks when you press Play and tells you what is missing, so there is
nothing to do in advance.
"""


def add(tar, name, data, mode):
    info = tarfile.TarInfo(f"{TOP}/{name}")
    info.size = len(data)
    info.mode = mode
    info.mtime = NOW
    info.uname = info.gname = "root"

    tar.addfile(info, io.BytesIO(data))


def main():
    if not os.path.isdir(PUBLISH):
        print(f"  {PUBLISH} is missing - publish the linux build first.")

        return 1

    os.makedirs(OUT_DIR, exist_ok=True)

    out = os.path.join(OUT_DIR, f"FreeRealmsLegacyLauncher-{VERSION}-linux-{ARCH}.tar.gz")
    count = 0

    with tarfile.open(out, "w:gz", compresslevel=6) as tar:
        for base, dirs, names in os.walk(PUBLISH):
            dirs.sort()

            rel = os.path.relpath(base, PUBLISH).replace("\\", "/")

            for name in sorted(names):
                with open(os.path.join(base, name), "rb") as fh:
                    data = fh.read()

                arc = name if rel == "." else f"{rel}/{name}"

                add(tar, arc, data, 0o755 if name == "Launcher" else 0o644)
                count += 1

        add(tar, "install.sh", INSTALL_SH.encode(), 0o755)
        add(tar, "uninstall.sh", UNINSTALL_SH.encode(), 0o755)
        add(tar, "README", README.encode(), 0o644)

        if os.path.exists(ICON):
            with open(ICON, "rb") as fh:
                add(tar, "icon.png", fh.read(), 0o644)

    print(f"  {os.path.basename(out)}")
    print(f"    {count} app files plus install.sh, uninstall.sh and README")
    print(f"    archive {os.path.getsize(out) / 1024 / 1024:.1f} MB")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
