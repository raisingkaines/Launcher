"""Reads the Linux packages back and checks they say what they should.

Written as a separate reader rather than reusing anything from the builders, so a
misunderstanding in one is not repeated here and hidden. The point is to catch the
mistakes that leave a package looking fine until a package manager refuses it: a digest
over the wrong bytes, a region offset that does not point where it claims, a payload whose
contents do not match the file list describing them.

  validate-linux-packages.py [dir]
"""
import gzip
import hashlib
import io
import os
import struct
import sys
import tarfile

DIR = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "releases-linux")

failures = []


def check(what, ok, detail=""):
    print(f"    [{'PASS' if ok else 'FAIL'}] {what}{' - ' + detail if detail else ''}")

    if not ok:
        failures.append(what)


# --- rpm ---------------------------------------------------------------------
TYPES = {6: "STRING", 7: "BIN", 8: "STRING_ARRAY", 9: "I18NSTRING",
         3: "INT16", 4: "INT32", 5: "INT64"}


def read_header(data, offset):
    """Parses one rpm header, returning its tags and where it ends."""
    magic, version, nindex, hsize = struct.unpack_from(">3sB4xII", data, offset)

    if magic != b"\x8e\xad\xe8":
        raise ValueError(f"bad header magic {magic!r} at {offset}")

    index_at = offset + 16
    store_at = index_at + nindex * 16
    end = store_at + hsize

    entries = []

    for i in range(nindex):
        entries.append(struct.unpack_from(">iiii", data, index_at + i * 16))

    tags = {}

    for tag, type_, off, count in entries:
        raw = data[store_at + off:end]

        if type_ in (6, 9):
            tags[tag] = raw.split(b"\0", 1)[0].decode("utf-8", "replace")
        elif type_ == 8:
            parts, rest = [], raw
            for _ in range(count):
                piece, rest = rest.split(b"\0", 1)
                parts.append(piece.decode("utf-8", "replace"))
            tags[tag] = parts
        elif type_ == 7:
            tags[tag] = raw[:count]
        elif type_ in (3, 4, 5):
            width = {3: 2, 4: 4, 5: 8}[type_]
            fmt = {3: ">H", 4: ">i", 5: ">q"}[type_]
            tags[tag] = [struct.unpack_from(fmt, raw, j * width)[0] for j in range(count)]

    return tags, entries, end, store_at, nindex


def read_cpio(payload):
    """Every entry in a 'new ASCII' cpio stream as (name, mode, bytes)."""
    out, at = [], 0

    while at < len(payload):
        if payload[at:at + 6] != b"070701":
            raise ValueError(f"bad cpio magic at {at}")

        fields = [int(payload[at + 6 + i * 8:at + 14 + i * 8], 16) for i in range(13)]
        mode, size, namesize = fields[1], fields[6], fields[11]

        name_at = at + 110
        name = payload[name_at:name_at + namesize - 1].decode()

        data_at = name_at + namesize
        data_at += (-data_at) % 4

        if name == "TRAILER!!!":
            break

        out.append((name, mode, payload[data_at:data_at + size]))

        at = data_at + size
        at += (-at) % 4

    return out


def validate_rpm(path):
    print(f"\n  {os.path.basename(path)}")

    data = open(path, "rb").read()

    magic, major, minor, type_, archnum, name, osnum, sigtype = struct.unpack_from(
        ">4sBBhh66shh", data, 0)

    check("lead magic", magic == b"\xed\xab\xee\xdb")
    check("lead says binary package, rpm 3.0", (major, type_) == (3, 0))
    check("lead signature type is header-style", sigtype == 5, f"got {sigtype}")
    check("lead names the package", name.rstrip(b"\0").decode().startswith("freerealmslegacy"))

    sig, sig_entries, sig_end, _, _ = read_header(data, 96)

    # rpm pads the signature header out to an 8 byte boundary; the next one starts there.
    header_at = sig_end + ((-(sig_end - 96)) % 8)

    hdr, entries, hdr_end, store_at, nindex = read_header(data, header_at)

    header_bytes = data[header_at:hdr_end]
    payload_bytes = data[hdr_end:]

    # The signature header's whole job is to describe these bytes.
    check("SHA256 in the signature matches the header",
          sig.get(273) == hashlib.sha256(header_bytes).hexdigest())
    check("SHA1 in the signature matches the header",
          sig.get(269) == hashlib.sha1(header_bytes).hexdigest())
    check("MD5 in the signature matches header plus payload",
          sig.get(1004) == hashlib.md5(header_bytes + payload_bytes).digest())
    check("size in the signature matches header plus payload",
          sig.get(1000, [None])[0] == len(header_bytes + payload_bytes),
          f"says {sig.get(1000)}, actual {len(header_bytes + payload_bytes)}")

    # Both headers must carry a region entry whose backwards offset covers the index.
    for label, ents, tag, n in (("signature", sig_entries, 62, len(sig_entries)),
                                ("header", entries, 63, nindex)):
        region = [e for e in ents if e[0] == tag]
        check(f"{label} header has its region entry", len(region) == 1)

        if region:
            trailer_off = region[0][2]
            trailer = (data[96 + 16 + len(sig_entries) * 16 + trailer_off:][:16] if label == "signature"
                       else data[store_at + trailer_off:][:16])
            rtag, rtype, roff, rcount = struct.unpack(">iiii", trailer)

            check(f"{label} region trailer points back over its index",
                  rtag == tag and roff == -(16 * n), f"tag {rtag}, offset {roff}, expected {-(16 * n)}")

    for tag, label in ((1000, "NAME"), (1001, "VERSION"), (1002, "RELEASE"), (1004, "SUMMARY"),
                       (1022, "ARCH"), (1117, "BASENAMES"), (1118, "DIRNAMES"),
                       (1116, "DIRINDEXES"), (1124, "PAYLOADFORMAT")):
        check(f"{label} present", tag in hdr)

    check("payload format is cpio", hdr.get(1124) == "cpio")
    check("payload compressor is gzip", hdr.get(1125) == "gzip")
    check("requires wine", "wine" in hdr.get(1049, []))

    raw = gzip.decompress(payload_bytes)
    check("payload size in the signature matches the decompressed payload",
          sig.get(1007, [None])[0] is None or abs(sig[1007][0] - len(raw)) <= 128,
          f"says {sig.get(1007)}, actual {len(raw)}")

    files = read_cpio(raw)

    basenames, dirnames, dirindexes = hdr.get(1117, []), hdr.get(1118, []), hdr.get(1116, [])

    check("payload holds as many files as the file list", len(files) == len(basenames),
          f"{len(files)} in payload, {len(basenames)} listed")

    expected = {"." + dirnames[d] + b for b, d in zip(basenames, dirindexes)}
    actual = {name for name, _, _ in files}

    check("payload paths match the file list", expected == actual,
          "" if expected == actual else f"{len(expected ^ actual)} differ")

    sizes, digests, modes = hdr.get(1028, []), hdr.get(1035, []), hdr.get(1030, [])
    by_name = {name: (mode, body) for name, mode, body in files}

    bad_size = bad_digest = bad_mode = 0

    for i, (b, d) in enumerate(zip(basenames, dirindexes)):
        entry = by_name.get("." + dirnames[d] + b)

        if entry is None:
            bad_size += 1
            bad_digest += 1
            bad_mode += 1
            continue

        mode, body = entry

        if sizes[i] != len(body):
            bad_size += 1
        if digests[i] != hashlib.sha256(body).hexdigest():
            bad_digest += 1
        if modes[i] != mode:
            bad_mode += 1

    check("every listed size matches the payload", bad_size == 0, f"{bad_size} wrong")
    check("every listed sha256 matches the payload", bad_digest == 0, f"{bad_digest} wrong")
    check("every listed mode matches the payload", bad_mode == 0, f"{bad_mode} wrong")

    launcher = [(n, m) for n, m, _ in files if n.endswith("/Launcher")]
    check("the launcher is executable", bool(launcher) and launcher[0][1] & 0o111 == 0o111,
          f"mode {oct(launcher[0][1])}" if launcher else "not found")


# --- deb ---------------------------------------------------------------------
def validate_deb(path):
    print(f"\n  {os.path.basename(path)}")

    data = open(path, "rb").read()
    check("ar magic", data[:8] == b"!<arch>\n")

    at, members = 8, []

    while at < len(data) - 1:
        header = data[at:at + 60]

        if len(header) < 60:
            break

        name = header[:16].decode().strip()
        size = int(header[48:58].decode().strip())

        members.append((name, data[at + 60:at + 60 + size]))

        at += 60 + size + (size % 2)

    check("three members in the required order",
          [m[0] for m in members] == ["debian-binary", "control.tar.gz", "data.tar.gz"],
          str([m[0] for m in members]))

    found = dict(members)
    check("debian-binary says 2.0", found.get("debian-binary") == b"2.0\n")

    with tarfile.open(fileobj=io.BytesIO(found["control.tar.gz"]), mode="r:gz") as tar:
        control = tar.extractfile("./control").read().decode()

    for field in ("Package:", "Version:", "Architecture:", "Maintainer:", "Description:", "Depends:"):
        check(f"control has {field.rstrip(':')}", field in control)

    with tarfile.open(fileobj=io.BytesIO(found["data.tar.gz"]), mode="r:gz") as tar:
        names = tar.getnames()
        launcher = [n for n in names if n.endswith("/Launcher")]

        check("the launcher is in the package", bool(launcher))

        if launcher:
            check("the launcher is executable", tar.getmember(launcher[0]).mode & 0o111 == 0o111,
                  oct(tar.getmember(launcher[0]).mode))

        check("a desktop entry is installed", any("applications/" in n for n in names))
        check("a wrapper is on the path", any(n.endswith("usr/bin/freerealmslegacy") for n in names))


# --- pacman ------------------------------------------------------------------
def validate_arch(path):
    print(f"\n  {os.path.basename(path)}")

    with tarfile.open(path, "r:xz") as tar:
        names = tar.getnames()

        check(".PKGINFO comes first", names[0] == ".PKGINFO", names[0])
        check(".MTREE comes second", names[1] == ".MTREE", names[1])

        info = tar.extractfile(".PKGINFO").read().decode()

        for field in ("pkgname", "pkgver", "pkgdesc", "arch", "size", "depend"):
            check(f".PKGINFO has {field}", f"{field} = " in info)

        check("depends on wine", "depend = wine" in info)

        listing = gzip.decompress(tar.extractfile(".MTREE").read()).decode()
        check(".MTREE is a gzipped mtree", listing.startswith("#mtree"))

        rows = {}

        for line in listing.splitlines():
            if not line.startswith("./"):
                continue

            parts = line.split()
            fields = dict(p.split("=", 1) for p in parts[1:] if "=" in p)
            rows[parts[0][2:]] = fields

        bad = missing = 0

        for name in names:
            if name in (".PKGINFO", ".MTREE"):
                continue

            row = rows.get(name)

            if row is None:
                missing += 1
                continue

            body = tar.extractfile(name).read()

            if row.get("sha256digest") != hashlib.sha256(body).hexdigest():
                bad += 1

        check("every member is listed in .MTREE", missing == 0, f"{missing} absent")
        check("every .MTREE digest matches its file", bad == 0, f"{bad} wrong")

        launcher = [n for n in names if n.endswith("/Launcher")]
        check("the launcher is executable",
              bool(launcher) and tar.getmember(launcher[0]).mode & 0o111 == 0o111)


# --- generic tarball ---------------------------------------------------------
def validate_tarball(path):
    print(f"\n  {os.path.basename(path)}")

    with tarfile.open(path, "r:gz") as tar:
        names = tar.getnames()
        top = {n.split("/")[0] for n in names}

        check("everything sits under one directory", len(top) == 1, str(top))

        for want in ("install.sh", "uninstall.sh", "README", "Launcher"):
            hit = [n for n in names if n.endswith("/" + want)]
            check(f"{want} is present", bool(hit))

            if hit and want.endswith(".sh") or want == "Launcher":
                if hit:
                    check(f"{want} is executable", tar.getmember(hit[0]).mode & 0o111 == 0o111,
                          oct(tar.getmember(hit[0]).mode))

        script = [n for n in names if n.endswith("/install.sh")]

        if script:
            body = tar.extractfile(script[0]).read().decode()
            check("install.sh stays inside the home directory", "$HOME" in body and "sudo" not in body)


def main():
    if not os.path.isdir(DIR):
        print(f"{DIR} does not exist.")

        return 1

    print(f"Validating packages in {DIR}")

    handlers = ((".rpm", validate_rpm), (".deb", validate_deb),
                (".pkg.tar.xz", validate_arch), (".tar.gz", validate_tarball))

    seen = 0

    for name in sorted(os.listdir(DIR)):
        for suffix, handler in handlers:
            if name.endswith(suffix):
                try:
                    handler(os.path.join(DIR, name))
                except Exception as ex:
                    print(f"\n  {name}")
                    check("parses at all", False, f"{type(ex).__name__}: {ex}")

                seen += 1
                break

    print(f"\n{seen} package(s) checked, {len(failures) or 'no'} failure(s)")

    for f in failures:
        print(f"  - {f}")

    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
