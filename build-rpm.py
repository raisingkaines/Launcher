"""Builds an RPM for the launcher, for Fedora, RHEL, openSUSE and their relatives.

Built by hand, like the .deb, because rpmbuild does not exist on Windows. An RPM is four
parts back to back: a 96 byte lead nobody reads any more, a signature header, the real
header, and a compressed cpio archive of the files.

Both headers use the same layout - a count, a run of fixed size index entries, then a data
store the entries point into. Two details are easy to get wrong and fatal if you do:
integers in the data store have to be aligned to their own width, and each header needs a
"region" entry describing the extent of its own index, which rpm checks before it will
trust what follows.

  build-rpm.py [version] [publishDir] [outDir] [arch]
"""
import gzip
import hashlib
import io
import os
import struct
import sys
import time

ROOT = os.path.dirname(os.path.abspath(__file__))

VERSION = sys.argv[1] if len(sys.argv) > 1 else "1.3.1"
PUBLISH = os.path.abspath(sys.argv[2]) if len(sys.argv) > 2 else os.path.join(ROOT, "publish-linux")
OUT_DIR = os.path.abspath(sys.argv[3]) if len(sys.argv) > 3 else os.path.join(ROOT, "releases-linux")
ARCH = sys.argv[4] if len(sys.argv) > 4 else "x86_64"

ICON = os.path.join(ROOT, "assets", "icon.png")

NAME = "freerealmslegacy"
RELEASE = "1"
INSTALL_DIR = "/opt/" + NAME

SUMMARY = "Free Realms Legacy launcher"
DESCRIPTION = """Downloads, updates and launches the Free Realms Legacy game client.

The launcher itself needs nothing else installed. The game client is a 32-bit Windows
program, so it runs through Wine, and on a 64-bit system that also needs Wine's 32-bit
support packages."""

# Tag numbers from rpm's own header definitions.
HEADERI18NTABLE = 100
HEADERSIGNATURES = 62
HEADERIMMUTABLE = 63

SIG_SIZE, SIG_MD5, SIG_SHA1, SIG_SHA256, SIG_PAYLOADSIZE = 1000, 1004, 269, 273, 1007

(NAME_T, VERSION_T, RELEASE_T, SUMMARY_T, DESCRIPTION_T, BUILDTIME, SIZE_T, LICENSE,
 GROUP, OS_T, ARCH_T, FILESIZES, FILEMODES, FILERDEVS, FILEMTIMES, FILEDIGESTS,
 FILELINKTOS, FILEFLAGS, FILEUSERNAME, FILEGROUPNAME, PROVIDENAME, REQUIREFLAGS,
 REQUIRENAME, REQUIREVERSION, DIRINDEXES, BASENAMES, DIRNAMES, PAYLOADFORMAT,
 PAYLOADCOMPRESSOR, PAYLOADFLAGS, PROVIDEFLAGS, PROVIDEVERSION, FILEDIGESTALGO) = (
    1000, 1001, 1002, 1004, 1005, 1006, 1009, 1014, 1016, 1021, 1022, 1028, 1030, 1033,
    1034, 1035, 1036, 1037, 1039, 1040, 1047, 1048, 1049, 1050, 1116, 1117, 1118, 1124,
    1125, 1126, 1112, 1113, 5011)

NULL, CHAR, INT8, INT16, INT32, INT64, STRING, BIN, STRING_ARRAY, I18NSTRING = range(10)

WIDTH = {INT16: 2, INT32: 4, INT64: 8}

# RPMSENSE_RPMLIB | RPMSENSE_EQUAL | RPMSENSE_LESS, which is what rpm itself writes.
RPMLIB_SENSE = (1 << 24) | 8 | 2

LAUNCHER_SH = f"""#!/bin/sh
exec {INSTALL_DIR}/Launcher "$@"
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


class Header:
    """One rpm header: index entries plus the data store they point into."""

    def __init__(self, region_tag):
        self.region_tag = region_tag
        self.entries = []
        self.store = bytearray()

    def add(self, tag, type_, value):
        if type_ in (STRING, I18NSTRING):
            data, count = value.encode() + b"\0", 1
        elif type_ is BIN:
            data, count = bytes(value), len(value)
        elif type_ is STRING_ARRAY:
            data = b"".join(v.encode() + b"\0" for v in value)
            count = len(value)
        elif type_ in WIDTH:
            # Integers are read in place, so each has to start on its own boundary.
            pad = (-len(self.store)) % WIDTH[type_]
            self.store += b"\0" * pad

            # rpm reads these as unsigned, which matters for file modes.
            fmt = {INT16: ">H", INT32: ">i", INT64: ">q"}[type_]
            data = b"".join(struct.pack(fmt, v) for v in value)
            count = len(value)
        else:
            raise ValueError(f"unsupported type {type_}")

        self.entries.append((tag, type_, len(self.store), count))
        self.store += data

    def build(self):
        """The header bytes, with its region entry written first as rpm expects."""
        # The region covers every entry including itself.
        count = len(self.entries) + 1
        trailer_offset = len(self.store)

        # The trailer is an index entry pointing backwards over the whole index.
        trailer = struct.pack(">iiii", self.region_tag, BIN, -(16 * count), 16)

        store = bytes(self.store) + trailer
        index = struct.pack(">iiii", self.region_tag, BIN, trailer_offset, 16)

        for tag, type_, offset, n in sorted(self.entries):
            index += struct.pack(">iiii", tag, type_, offset, n)

        return (struct.pack(">3sB4xII", b"\x8e\xad\xe8", 1, count, len(store))
                + index + store)


def cpio_entry(name, data, mode, ino, link=b""):
    """One 'new ASCII' cpio record, which is the format rpm payloads use."""
    payload = link if link else data

    fields = [ino, mode, 0, 0, 1, int(time.time()), len(payload),
              0, 0, 0, 0, len(name) + 1, 0]

    out = b"070701" + b"".join(b"%08X" % f for f in fields) + name.encode() + b"\0"
    out += b"\0" * ((-len(out)) % 4)
    out += payload
    out += b"\0" * ((-len(payload)) % 4)

    return out


def main():
    if not os.path.isdir(PUBLISH):
        print(f"  {PUBLISH} is missing - publish the linux build first.")

        return 1

    os.makedirs(OUT_DIR, exist_ok=True)

    # Gather every installed file as (absolute install path, bytes, mode).
    files = []

    for base, dirs, names in os.walk(PUBLISH):
        dirs.sort()

        rel = os.path.relpath(base, PUBLISH).replace("\\", "/")
        prefix = INSTALL_DIR if rel == "." else f"{INSTALL_DIR}/{rel}"

        for name in sorted(names):
            with open(os.path.join(base, name), "rb") as fh:
                files.append((f"{prefix}/{name}", fh.read(), 0o100755 if name == "Launcher" else 0o100644))

    files.append((f"/usr/bin/{NAME}", LAUNCHER_SH.encode(), 0o100755))
    files.append((f"/usr/share/applications/{NAME}.desktop", DESKTOP.encode(), 0o100644))

    if os.path.exists(ICON):
        with open(ICON, "rb") as fh:
            files.append((f"/usr/share/icons/hicolor/256x256/apps/{NAME}.png", fh.read(), 0o100644))

    files.sort()

    # rpm stores paths split into a directory table and per-file basenames.
    dirnames, dirindexes, basenames = [], [], []

    for path, _, _ in files:
        directory = path.rsplit("/", 1)[0] + "/"

        if directory not in dirnames:
            dirnames.append(directory)

        dirindexes.append(dirnames.index(directory))
        basenames.append(path.rsplit("/", 1)[1])

    payload = bytearray()

    for i, (path, data, mode) in enumerate(files):
        payload += cpio_entry("." + path, data, mode, i + 1)

    payload += cpio_entry("TRAILER!!!", b"", 0, 0)

    raw_payload_size = len(payload)
    compressed = gzip.compress(bytes(payload), 9, mtime=0)

    total = sum(len(d) for _, d, _ in files)

    h = Header(HEADERIMMUTABLE)
    h.add(HEADERI18NTABLE, STRING_ARRAY, ["C"])
    h.add(NAME_T, STRING, NAME)
    h.add(VERSION_T, STRING, VERSION)
    h.add(RELEASE_T, STRING, RELEASE)
    h.add(SUMMARY_T, I18NSTRING, SUMMARY)
    h.add(DESCRIPTION_T, I18NSTRING, DESCRIPTION)
    h.add(BUILDTIME, INT32, [int(time.time())])
    h.add(SIZE_T, INT32, [total])
    h.add(LICENSE, STRING, "MIT")
    h.add(GROUP, I18NSTRING, "Amusements/Games")
    h.add(OS_T, STRING, "linux")
    h.add(ARCH_T, STRING, ARCH)

    h.add(FILESIZES, INT32, [len(d) for _, d, _ in files])
    h.add(FILEMODES, INT16, [m for _, _, m in files])
    h.add(FILERDEVS, INT16, [0] * len(files))
    h.add(FILEMTIMES, INT32, [int(time.time())] * len(files))
    h.add(FILEDIGESTS, STRING_ARRAY, [hashlib.sha256(d).hexdigest() for _, d, _ in files])
    h.add(FILEDIGESTALGO, INT32, [8])                       # 8 is sha256
    h.add(FILELINKTOS, STRING_ARRAY, [""] * len(files))
    h.add(FILEFLAGS, INT32, [0] * len(files))
    h.add(FILEUSERNAME, STRING_ARRAY, ["root"] * len(files))
    h.add(FILEGROUPNAME, STRING_ARRAY, ["root"] * len(files))

    h.add(PROVIDENAME, STRING_ARRAY, [NAME])
    h.add(PROVIDEFLAGS, INT32, [8])                          # equal to this version
    h.add(PROVIDEVERSION, STRING_ARRAY, [f"{VERSION}-{RELEASE}"])

    # Wine, plus the note that this package uses the split path tables above.
    h.add(REQUIRENAME, STRING_ARRAY, ["wine", "rpmlib(CompressedFileNames)"])
    h.add(REQUIREFLAGS, INT32, [0, RPMLIB_SENSE])
    h.add(REQUIREVERSION, STRING_ARRAY, ["", "3.0.4-1"])

    h.add(DIRINDEXES, INT32, dirindexes)
    h.add(BASENAMES, STRING_ARRAY, basenames)
    h.add(DIRNAMES, STRING_ARRAY, dirnames)

    h.add(PAYLOADFORMAT, STRING, "cpio")
    h.add(PAYLOADCOMPRESSOR, STRING, "gzip")
    h.add(PAYLOADFLAGS, STRING, "9")

    header = h.build()

    # The signature header describes the bytes that follow it.
    body = header + compressed

    sig = Header(HEADERSIGNATURES)
    sig.add(SIG_SHA1, STRING, hashlib.sha1(header).hexdigest())
    sig.add(SIG_SHA256, STRING, hashlib.sha256(header).hexdigest())
    sig.add(SIG_SIZE, INT32, [len(body)])
    sig.add(SIG_PAYLOADSIZE, INT32, [raw_payload_size])
    sig.add(SIG_MD5, BIN, hashlib.md5(body).digest())

    signature = sig.build()

    # The signature header is padded to an 8 byte boundary; the real one is not.
    signature += b"\0" * ((-len(signature)) % 8)

    label = f"{NAME}-{VERSION}-{RELEASE}"
    lead = struct.pack(">4sBBhh66shh16x", b"\xed\xab\xee\xdb", 3, 0, 0, 1,
                       label.encode()[:66], 1, 5)

    out = os.path.join(OUT_DIR, f"{NAME}-{VERSION}-{RELEASE}.{ARCH}.rpm")

    with open(out, "wb") as fh:
        fh.write(lead + signature + body)

    print(f"  {os.path.basename(out)}")
    print(f"    {len(files)} files, {total / 1024 / 1024:.0f} MB installed")
    print(f"    package {os.path.getsize(out) / 1024 / 1024:.1f} MB")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
