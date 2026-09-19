"""Builds a Debian package for the launcher, for Linux and ChromeOS.

ChromeOS's Linux container is Debian, so a .deb is the closest thing to a double-click
install there: Files opens it, and apt pulls in what it depends on.

Built by hand rather than with dpkg-deb, which does not exist on Windows. A .deb is an ar
archive of exactly three members in order: debian-binary, control.tar.gz, data.tar.gz.

The permissions matter more than usual here. Windows has no executable bit, so every mode
is set explicitly - without that the installed launcher is not runnable and the package
looks fine until someone tries to start it.
"""
import gzip
import io
import os
import struct
import sys
import tarfile
import time

ROOT = os.path.dirname(os.path.abspath(__file__))
PUBLISH = os.path.join(ROOT, "publish-linux")
ICON = os.path.join(ROOT, "assets", "logo.png")
OUT_DIR = os.path.join(ROOT, "releases-linux")

PACKAGE = "freerealmslegacy"
VERSION = sys.argv[1] if len(sys.argv) > 1 else "1.3.0"
INSTALL_DIR = "/opt/" + PACKAGE

CONTROL = f"""Package: {PACKAGE}
Version: {VERSION}
Section: games
Priority: optional
Architecture: amd64
Maintainer: Raising Kaines <noreply@raisingkaines.com>
Depends: wine
Recommends: wine32
Installed-Size: {{installed_size}}
Description: Free Realms Legacy launcher
 Downloads, updates and launches the Free Realms Legacy game client.
 .
 The game client is a 32-bit Windows program, so it runs through Wine. On a 64-bit
 system that also needs the 32-bit Wine packages, which require the i386 architecture
 to be enabled first - see /usr/share/doc/{PACKAGE}/README.Debian.
"""

# Deliberately does no apt work of its own: apt holds its lock while this runs, so
# anything clever here fails at exactly the wrong moment. It checks and tells instead.
POSTINST = """#!/bin/sh
set -e

if [ "$1" = "configure" ]; then
    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database -q /usr/share/applications || true
    fi

    if ! dpkg --print-foreign-architectures | grep -qx i386; then
        echo ""
        echo "Free Realms Legacy: the game client is 32-bit, so Wine needs its 32-bit"
        echo "packages. To finish setting that up, run:"
        echo ""
        echo "    sudo dpkg --add-architecture i386"
        echo "    sudo apt update"
        echo "    sudo apt install wine32"
        echo ""
    fi
fi

exit 0
"""

LAUNCHER_SH = f"""#!/bin/sh
exec {INSTALL_DIR}/Launcher "$@"
"""

DESKTOP = f"""[Desktop Entry]
Type=Application
Name=Free Realms Legacy
Comment=Play Free Realms Legacy
Exec=/usr/bin/{PACKAGE} %U
Icon={PACKAGE}
Terminal=false
Categories=Game;
StartupWMClass=Launcher
"""

README_DEBIAN = f"""Free Realms Legacy for Debian, Ubuntu and ChromeOS
==================================================

The launcher itself is self-contained and needs nothing else.

The game client is a 32-bit Windows program and runs through Wine. On a 64-bit system
Wine's 32-bit support lives in separate packages, and those need the i386 architecture
enabled first:

    sudo dpkg --add-architecture i386
    sudo apt update
    sudo apt install wine32

On a Chromebook, run those inside the Linux container.

The launcher checks for Wine when you press Play and says what is missing, so nothing
here has to be done in advance to install the package.
"""


def add_tree(tar, src_dir, dest_prefix):
    """Adds a directory, forcing root ownership and sane modes."""
    count = size = 0

    for base, dirs, files in os.walk(src_dir):
        dirs.sort()

        rel = os.path.relpath(base, src_dir).replace("\\", "/")
        dest = dest_prefix if rel == "." else dest_prefix + "/" + rel

        info = tarfile.TarInfo(dest)
        info.type = tarfile.DIRTYPE
        info.mode = 0o755
        info.mtime = int(time.time())
        info.uname = info.gname = "root"
        tar.addfile(info)

        for name in sorted(files):
            path = os.path.join(base, name)
            data = open(path, "rb").read()

            info = tarfile.TarInfo(dest + "/" + name)
            info.size = len(data)
            info.mtime = int(time.time())
            info.uname = info.gname = "root"

            # The app's own entry point has to be runnable; nothing else needs to be.
            info.mode = 0o755 if name == "Launcher" else 0o644

            tar.addfile(info, io.BytesIO(data))

            count += 1
            size += len(data)

    return count, size


def add_file(tar, dest, text, mode):
    data = text.encode()

    info = tarfile.TarInfo(dest)
    info.size = len(data)
    info.mode = mode
    info.mtime = int(time.time())
    info.uname = info.gname = "root"

    tar.addfile(info, io.BytesIO(data))


def add_binary(tar, dest, data, mode=0o644):
    info = tarfile.TarInfo(dest)
    info.size = len(data)
    info.mode = mode
    info.mtime = int(time.time())
    info.uname = info.gname = "root"

    tar.addfile(info, io.BytesIO(data))


def targz(build):
    raw = io.BytesIO()

    with tarfile.open(fileobj=raw, mode="w") as tar:
        build(tar)

    packed = io.BytesIO()

    # mtime=0 so the same input produces the same bytes.
    with gzip.GzipFile(fileobj=packed, mode="wb", mtime=0) as gz:
        gz.write(raw.getvalue())

    return packed.getvalue()


def ar_member(name, data):
    header = (
        name.ljust(16)[:16]
        + str(int(time.time())).ljust(12)[:12]
        + "0".ljust(6)
        + "0".ljust(6)
        + "100644".ljust(8)
        + str(len(data)).ljust(10)[:10]
        + "`\n"
    ).encode()

    # Members are padded to an even length.
    return header + data + (b"\n" if len(data) % 2 else b"")


def main():
    if not os.path.isdir(PUBLISH):
        print(f"  {PUBLISH} is missing - publish the linux-x64 build first.")

        return 1

    os.makedirs(OUT_DIR, exist_ok=True)

    icon = open(ICON, "rb").read() if os.path.exists(ICON) else None
    stats = {}

    def build_data(tar):
        count, size = add_tree(tar, PUBLISH, "." + INSTALL_DIR)
        stats["count"], stats["size"] = count, size

        add_file(tar, "./usr/bin/" + PACKAGE, LAUNCHER_SH, 0o755)
        add_file(tar, "./usr/share/applications/" + PACKAGE + ".desktop", DESKTOP, 0o644)
        add_file(tar, f"./usr/share/doc/{PACKAGE}/README.Debian", README_DEBIAN, 0o644)

        if icon:
            add_binary(tar, f"./usr/share/icons/hicolor/256x256/apps/{PACKAGE}.png", icon)

    data_tar = targz(build_data)

    installed_kb = max(1, stats.get("size", 0) // 1024)

    def build_control(tar):
        add_file(tar, "./control", CONTROL.format(installed_size=installed_kb), 0o644)
        add_file(tar, "./postinst", POSTINST, 0o755)

    control_tar = targz(build_control)

    deb = b"!<arch>\n"
    deb += ar_member("debian-binary", b"2.0\n")
    deb += ar_member("control.tar.gz", control_tar)
    deb += ar_member("data.tar.gz", data_tar)

    out = os.path.join(OUT_DIR, f"{PACKAGE}_{VERSION}_amd64.deb")
    open(out, "wb").write(deb)

    print(f"  {os.path.basename(out)}")
    print(f"    {stats.get('count', 0)} app files, {installed_kb / 1024:.0f} MB installed")
    print(f"    package {len(deb) / 1024 / 1024:.1f} MB")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
