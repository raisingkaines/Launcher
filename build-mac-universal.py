"""Merges the arm64 and x64 macOS builds into one that runs natively on any Mac.

Apple's own tool for this is lipo, which only exists on macOS. It is not doing anything
mysterious though: a universal binary is a small header listing each architecture with an
offset, followed by the ordinary thin binaries laid out at those offsets. That is what is
built here.

Most of a .NET publish is architecture independent - the managed assemblies are identical
between the two builds - so only the native pieces need combining. Files are compared
rather than assumed: identical ones are copied once, Mach-O pairs are made universal, and
anything that differs without being Mach-O is reported rather than silently picking one.
"""
import os
import shutil
import struct
import sys

ROOT = os.path.dirname(os.path.abspath(__file__))

ARM64 = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "publish-osx")
X64 = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ROOT, "publish-osx-x64")
OUT = sys.argv[3] if len(sys.argv) > 3 else os.path.join(ROOT, "publish-osx-universal")

FAT_MAGIC = 0xCAFEBABE

# Thin Mach-O magics, little endian on disk for both architectures.
THIN_MAGICS = {b"\xcf\xfa\xed\xfe", b"\xce\xfa\xed\xfe"}

# Alignment Apple uses, as a power of two: 16K pages on arm64, 4K on x86_64.
ALIGN = {0x0100000C: 14, 0x01000007: 12}

DEFAULT_ALIGN = 14


def read_header(path):
    """cputype and cpusubtype of a thin Mach-O, or None if it is not one."""
    with open(path, "rb") as fh:
        head = fh.read(12)

    if len(head) < 12 or head[:4] not in THIN_MAGICS:
        return None

    # Both of these are little endian on disk, which is what the magic tells us.
    cputype, cpusubtype = struct.unpack_from("<ii", head, 4)

    return cputype, cpusubtype


def make_universal(paths):
    """A fat binary from thin ones, in the order given."""
    slices = []

    for path in paths:
        header = read_header(path)

        if header is None:
            return None

        cputype, cpusubtype = header

        with open(path, "rb") as fh:
            slices.append((cputype, cpusubtype, fh.read()))

    # Header first: magic, count, then a 20 byte entry per architecture.
    header_size = 8 + 20 * len(slices)
    offset = header_size
    placed = []

    for cputype, cpusubtype, data in slices:
        align = ALIGN.get(cputype, DEFAULT_ALIGN)
        step = 1 << align

        # Each slice starts on its own alignment boundary.
        offset = (offset + step - 1) // step * step
        placed.append((cputype, cpusubtype, offset, len(data), align, data))
        offset += len(data)

    out = bytearray(struct.pack(">II", FAT_MAGIC, len(placed)))

    for cputype, cpusubtype, off, size, align, _ in placed:
        out += struct.pack(">iiIII", cputype, cpusubtype, off, size, align)

    for _, _, off, _, _, data in placed:
        out += b"\0" * (off - len(out))
        out += data

    return bytes(out)


def main():
    for path, name in ((ARM64, "arm64"), (X64, "x64")):
        if not os.path.isdir(path):
            print(f"  {name} build missing: {path}")

            return 1

    if os.path.isdir(OUT):
        shutil.rmtree(OUT)

    same = combined = arm_only = x64_only = 0
    mismatched = []

    arm_files = {
        os.path.relpath(os.path.join(b, f), ARM64).replace("\\", "/")
        for b, _, fs in os.walk(ARM64) for f in fs
    }
    x64_files = {
        os.path.relpath(os.path.join(b, f), X64).replace("\\", "/")
        for b, _, fs in os.walk(X64) for f in fs
    }

    for rel in sorted(arm_files | x64_files):
        a = os.path.join(ARM64, rel.replace("/", os.sep))
        x = os.path.join(X64, rel.replace("/", os.sep))
        dest = os.path.join(OUT, rel.replace("/", os.sep))

        os.makedirs(os.path.dirname(dest), exist_ok=True)

        if rel not in x64_files:
            shutil.copy2(a, dest)
            arm_only += 1
            continue

        if rel not in arm_files:
            shutil.copy2(x, dest)
            x64_only += 1
            continue

        arm_bytes = open(a, "rb").read()
        x64_bytes = open(x, "rb").read()

        if arm_bytes == x64_bytes:
            # Architecture independent, which is most of a managed publish.
            shutil.copy2(a, dest)
            same += 1
            continue

        fat = make_universal([x, a])

        if fat is None:
            # Differs but is not a pair of Mach-O binaries - worth naming rather than
            # quietly choosing one and hoping.
            mismatched.append(rel)
            shutil.copy2(a, dest)
            continue

        with open(dest, "wb") as fh:
            fh.write(fat)

        combined += 1

    print(f"  {OUT}")
    print(f"    {same} identical, {combined} made universal, "
          f"{arm_only} arm64 only, {x64_only} x64 only")

    if mismatched:
        print(f"    {len(mismatched)} differ but are not Mach-O (kept arm64):")

        for rel in mismatched[:6]:
            print(f"      {rel}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
