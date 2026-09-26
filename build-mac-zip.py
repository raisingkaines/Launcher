"""Packages the macOS .app as a zip that still works after crossing from Windows.

A zipped .app is a normal way to ship a Mac application: the person unzips it and drags
it to Applications. The catch is that Windows has no executable bit, so a zip made with
the usual tools arrives with Contents/MacOS/Launcher not runnable, and the app fails on
first launch having looked perfectly fine.

ZIP can carry Unix permissions - they live in the external attributes of each entry, and
macOS honours them when the entry says it came from Unix. So every mode is set here
explicitly, exactly as the .deb does.

Which files need the bit is decided by reading their magic rather than by their name: a
Mach-O binary gets 0755, everything else 0644. That way a renamed or extensionless
binary cannot be missed.

This bundle has no symlinks - checked - which is the other thing that usually does not
survive the trip. If that ever changes, they need storing as links, not as copies.
"""
import os
import stat
import sys
import zipfile

ROOT = os.path.dirname(os.path.abspath(__file__))

APP = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "bundle-osx", "FreeRealmsLegacyLauncher.app")
VERSION = sys.argv[2] if len(sys.argv) > 2 else "1.3.0"
ARCH = sys.argv[3] if len(sys.argv) > 3 else "osx-arm64"
OUT_DIR = sys.argv[4] if len(sys.argv) > 4 else os.path.join(ROOT, "releases-osx")

# Mach-O, in the byte orders and widths that actually turn up, plus a fat binary.
MACHO_MAGICS = {
    b"\xcf\xfa\xed\xfe",  # 64-bit, little endian
    b"\xce\xfa\xed\xfe",  # 32-bit, little endian
    b"\xfe\xed\xfa\xcf",  # 64-bit, big endian
    b"\xfe\xed\xfa\xce",  # 32-bit, big endian
    b"\xca\xfe\xba\xbe",  # universal / fat
}

# Unix, so macOS reads the permission bits rather than ignoring them.
UNIX_CREATOR = 3


def is_macho(path):
    try:
        with open(path, "rb") as fh:
            return fh.read(4) in MACHO_MAGICS
    except OSError:
        return False


def entry(name, mode, is_dir=False):
    info = zipfile.ZipInfo(name)
    info.create_system = UNIX_CREATOR
    info.external_attr = (mode & 0xFFFF) << 16

    if is_dir:
        info.external_attr |= 0x10          # the MS-DOS directory flag, still expected
        info.compress_type = zipfile.ZIP_STORED
    else:
        info.compress_type = zipfile.ZIP_DEFLATED

    info.date_time = (2026, 1, 1, 0, 0, 0)
    return info


READ_ME = """Free Realms Legacy - installing on a Mac

Right click Install.command and choose Open. Then click Open again when macOS asks.

That is it. It puts the launcher in your Applications folder, gets it past the download
warning, and offers to set up Wine for the game.


Why right click instead of double click

macOS marks everything downloaded in a browser as quarantined, and will not open a
quarantined app unless it was signed with a paid Apple developer certificate. This one is
not, so macOS refuses it - and the message it shows says the app is damaged, which is
misleading. The app is fine. Install.command clears that flag for you.

Right clicking and choosing Open is what lets the script itself run. Double clicking it
will not work the first time, for the same reason.


If you would rather not run a script

Drag FreeRealmsLegacyLauncher.app to your Applications folder, then open Terminal and run:

    xattr -cr /Applications/FreeRealmsLegacyLauncher.app

The app opens normally after that.


Which Macs this runs on

All of them. The launcher is Intel, and Apple Silicon Macs run it through Rosetta 2. If
your Mac has never run an Intel app, macOS offers to install Rosetta the first time and
takes a few seconds over it. Needs macOS 11 Big Sur or later.


Playing the game

The launcher runs on its own. The game client is a 32-bit Windows program, and macOS
dropped 32-bit support, so it needs Wine built to handle that - Whisky is free and works,
and CrossOver does too. Install.command offers to install Whisky if you have Homebrew.
The launcher also checks when you press Play and says what is missing.
"""

def main():
    if not os.path.isdir(APP):
        print(f"  {APP} is missing - build the .app bundle first.")
        return 1

    os.makedirs(OUT_DIR, exist_ok=True)

    app_name = os.path.basename(APP)
    out = os.path.join(OUT_DIR, f"FreeRealmsLegacyLauncher-{ARCH}-{VERSION}.zip")

    executables = []
    total = 0

    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for base, dirs, files in os.walk(APP):
            dirs.sort()

            rel = os.path.relpath(base, os.path.dirname(APP)).replace("\\", "/")
            z.writestr(entry(rel + "/", stat.S_IFDIR | 0o755, is_dir=True), b"")

            for name in sorted(files):
                path = os.path.join(base, name)
                arc = rel + "/" + name

                if is_macho(path):
                    mode = stat.S_IFREG | 0o755
                    executables.append(arc)
                else:
                    mode = stat.S_IFREG | 0o644

                with open(path, "rb") as fh:
                    z.writestr(entry(arc, mode), fh.read())

                total += 1

        # The setup script, and the one note worth reading before running it. A .command
        # file is a plain script, which Gatekeeper will open after a right click even when
        # it refuses to open an unsigned app at all - so this is the way in.
        installer = os.path.join(ROOT, "mac", "Install.command")

        if os.path.exists(installer):
            with open(installer, "rb") as fh:
                z.writestr(entry("Install.command", stat.S_IFREG | 0o755), fh.read())

            executables.append("Install.command")
            total += 1

        z.writestr(entry("Read me first.txt", stat.S_IFREG | 0o644), READ_ME.encode())
        total += 1

    print(f"  {os.path.basename(out)}")
    print(f"    {total} files, {os.path.getsize(out) / 1024 / 1024:.1f} MB")
    print(f"    {len(executables)} Mach-O binaries marked executable, including:")

    for name in executables[:3]:
        print(f"      {name.split(app_name + '/')[-1]}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
