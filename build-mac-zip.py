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

    print(f"  {os.path.basename(out)}")
    print(f"    {total} files, {os.path.getsize(out) / 1024 / 1024:.1f} MB")
    print(f"    {len(executables)} Mach-O binaries marked executable, including:")

    for name in executables[:3]:
        print(f"      {name.split(app_name + '/')[-1]}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
