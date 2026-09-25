"""Builds a pacman package for the launcher, for Arch, Manjaro and EndeavourOS.

The simplest of the three package formats: a tar archive holding the files at the paths
they install to, plus two metadata members. .PKGINFO has to come first and .MTREE second,
because pacman reads them from the front of the stream rather than seeking.

Compressed with xz rather than zstd. Arch's own packages use zstd now, and pacman still
installs .pkg.tar.xz perfectly well - xz is in Python's standard library and zstd is not
until 3.14, so this keeps the script runnable with no dependencies.

  build-arch.py [version] [publishDir] [outDir] [arch]
"""
import gzip
import hashlib
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
RELEASE = "1"
INSTALL_DIR = "opt/" + NAME

NOW = int(time.time())

LAUNCHER_SH = f"""#!/bin/sh
exec /{INSTALL_DIR}/Launcher "$@"
"""

DESKTOP = f"""[Desktop Entry]
Type=Application
Name=Free Realms Legacy
Comment=Play Free Realms Legacy
Exec=/usr/bin/{NAME} %U
Icon={NAME}
Terminal=false
Categories=Game;
StartupWMClass=Launcher
"""

# Arch keeps multilib Wine in one package, so unlike Debian there is no separate 32-bit
# piece to ask for - but multilib has to be enabled in pacman.conf for wine to exist.
INSTALL_SCRIPT = """post_install() {
    cat <<'EOF'

Free Realms Legacy: the game client is a 32-bit Windows program and runs through Wine.
If pacman could not find wine, enable the multilib repository in /etc/pacman.conf and
install it with: pacman -S wine

EOF
}

post_upgrade() {
    :
}
"""


def collect():
    """Every installed file as (path inside the package, bytes, mode)."""
    files = []

    for base, dirs, names in os.walk(PUBLISH):
        dirs.sort()

        rel = os.path.relpath(base, PUBLISH).replace("\\", "/")
        prefix = INSTALL_DIR if rel == "." else f"{INSTALL_DIR}/{rel}"

        for name in sorted(names):
            with open(os.path.join(base, name), "rb") as fh:
                files.append((f"{prefix}/{name}", fh.read(), 0o755 if name == "Launcher" else 0o644))

    files.append((f"usr/bin/{NAME}", LAUNCHER_SH.encode(), 0o755))
    files.append((f"usr/share/applications/{NAME}.desktop", DESKTOP.encode(), 0o644))

    if os.path.exists(ICON):
        with open(ICON, "rb") as fh:
            files.append((f"usr/share/icons/hicolor/256x256/apps/{NAME}.png", fh.read(), 0o644))

    files.sort()

    return files


def pkginfo(installed_size):
    lines = [
        f"pkgname = {NAME}",
        f"pkgbase = {NAME}",
        f"pkgver = {VERSION}-{RELEASE}",
        "pkgdesc = Free Realms Legacy launcher",
        "url = https://github.com/raisingkaines/Launcher",
        f"builddate = {NOW}",
        "packager = Raising Kaines <noreply@raisingkaines.com>",
        f"size = {installed_size}",
        f"arch = {ARCH}",
        "license = MIT",
        "depend = wine",
    ]

    return ("\n".join(lines) + "\n").encode()


def mtree(members):
    """The listing pacman verifies an installed package against.

    Written uncompressed then gzipped, which is how the real .MTREE is stored.
    """
    out = io.StringIO()
    out.write("#mtree\n")
    out.write("/set type=file uid=0 gid=0 mode=644\n")

    for path, data, mode in members:
        digest = hashlib.sha256(data).hexdigest()
        md5 = hashlib.md5(data).hexdigest()

        # mtree escapes anything awkward as octal; these paths have no spaces, but the
        # leading ./ is required.
        out.write(f"./{path} mode={mode:o} time={NOW}.0 size={len(data)} "
                  f"md5digest={md5} sha256digest={digest}\n")

    return gzip.compress(out.getvalue().encode(), 9, mtime=0)


def add(tar, name, data, mode):
    info = tarfile.TarInfo(name)
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

    files = collect()
    installed = sum(len(d) for _, d, _ in files)

    # The metadata members are listed in .MTREE too, so build their bytes first.
    info_bytes = pkginfo(installed)
    install_bytes = INSTALL_SCRIPT.encode()

    listing = mtree([(".PKGINFO", info_bytes, 0o644),
                     (".INSTALL", install_bytes, 0o644)] + files)

    out = os.path.join(OUT_DIR, f"{NAME}-{VERSION}-{RELEASE}-{ARCH}.pkg.tar.xz")

    with tarfile.open(out, "w:xz", preset=6) as tar:
        # Order matters: pacman reads these from the front of the stream.
        add(tar, ".PKGINFO", info_bytes, 0o644)
        add(tar, ".MTREE", listing, 0o644)
        add(tar, ".INSTALL", install_bytes, 0o644)

        for path, data, mode in files:
            add(tar, path, data, mode)

    print(f"  {os.path.basename(out)}")
    print(f"    {len(files)} files, {installed / 1024 / 1024:.0f} MB installed")
    print(f"    package {os.path.getsize(out) / 1024 / 1024:.1f} MB")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
