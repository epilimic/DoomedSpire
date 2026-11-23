#!/usr/bin/env python3
"""Scrub check: looks at everything that would go to the Workshop and stops on anything unnecessary or personal.

    python3 workshop/scrub.py               check (read-only); exit 1 if anything must be fixed first
    python3 workshop/scrub.py --fix-image   also rewrite workshop/image.png without metadata chunks (pixels untouched)

upload.sh runs this before every upload. Identical in every mod repo (the mod id is the name of the .csproj in the repo root).

What goes to the Workshop: workshop/content/*, workshop/image.png, workshop/image.gif (if any), workshop/previews/*, and the text fields of workshop/workshop.json.
Your own details (home folder, user name, computer name, this repo's path, the game folder) are looked up when it runs, never stored in the repo.
"""
import getpass
import glob
import hashlib
import json
import os
import re
import socket
import struct
import sys
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
FIX_IMAGE = "--fix-image" in sys.argv

stops, warns = [], []


def stop(msg):
    stops.append(msg)


def warn(msg):
    warns.append(msg)


# ---------------------------------------------------------------- what is looked for

def personal_values():
    """(label, regex) for things that identify this machine or person, found at run time."""
    out = []
    home = os.path.expanduser("~")
    if len(home) > 6 and home.rstrip("/") not in ("/root",):
        out.append(("your home folder", re.escape(home)))
    user = getpass.getuser()
    if len(user) >= 5 and user.lower() not in {"admin", "ubuntu", "vagrant", "runner", "docker"}:
        out.append(("your user name", r"(?<![A-Za-z0-9])" + re.escape(user) + r"(?![A-Za-z0-9])"))
    host = socket.gethostname().split(".")[0]
    if len(host) >= 5 and host.lower() not in {"localhost", "ubuntu", "runner"}:
        out.append(("your computer's name", r"(?<![A-Za-z0-9])" + re.escape(host) + r"(?![A-Za-z0-9])"))
    out.append(("this repo's folder", re.escape(ROOT)))
    sts2 = os.environ.get("STS2_PATH", "")
    props = os.path.join(ROOT, "Directory.Build.props")
    if not sts2 and os.path.exists(props):
        # XML comments are removed first: a commented-out <Sts2Path> line must never be taken for the real one.
        text = re.sub(r"<!--.*?-->", "", open(props, encoding="utf-8-sig", errors="replace").read(), flags=re.S)
        m = re.search(r"<Sts2Path>(.*?)</Sts2Path>", text)
        sts2 = m.group(1).strip() if m else ""
    if sts2:
        out.append(("the game folder", re.escape(sts2.rstrip("/"))))
        steamapps = os.path.dirname(os.path.dirname(sts2.rstrip("/")))
        if len(steamapps) > 6:
            out.append(("your Steam library folder", re.escape(steamapps)))
    return out


GENERIC = [  # (label, regex): worth a look, but may be innocent, so these only warn
    ("an absolute path", r"(?<![\w.:])/(?:home|mnt|Users|media|run/media)/[^\s\"'<>]{2,}"),
    ("a Windows path", r"(?:\b[A-Za-z]:\\[^\s\"'<>]{2,}|\\Users\\[^\s\"'<>]+)"),
    ("an email address", r"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+\.[A-Za-z]{2,}"),
    ("a Steam account id", r"\b7656119\d{10}\b"),
]
SECRETS = [  # these stop the upload
    ("a GitHub token", r"gh[pousr]_[A-Za-z0-9]{36,}"),
    ("a GitHub token", r"github_pat_[A-Za-z0-9_]{50,}"),
    ("an AWS key", r"AKIA[0-9A-Z]{16}"),
    ("a private key", r"-----BEGIN [A-Z ]*PRIVATE KEY-----"),
    ("a Slack token", r"xox[baprs]-[A-Za-z0-9-]{10,}"),
    ("an API key", r"\bsk-[A-Za-z0-9]{32,}"),
    ("a Google API key", r"AIza[0-9A-Za-z_-]{35}"),
]

# ---------------------------------------------------------------- reading files


def strings_in(data):
    """Printable runs of 6+ characters, as ASCII/UTF-8 and as UTF-16 (.NET keeps its string literals as UTF-16)."""
    runs = [m.group(0).decode("latin-1") for m in re.finditer(rb"[\x20-\x7e]{6,}", data)]
    runs += [m.group(0).decode("utf-16le") for m in re.finditer(rb"(?:[\x20-\x7e]\x00){6,}", data)]
    return runs


def png_chunks(data):
    """[(type, body)] or None if it is not a well-formed PNG."""
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        return None
    i, out = 8, []
    while i + 12 <= len(data):
        n = struct.unpack(">I", data[i:i + 4])[0]
        t = data[i + 4:i + 8].decode("latin-1")
        out.append((t, data[i + 8:i + 8 + n]))
        i += 12 + n
    return out


PNG_OK = {"IHDR", "PLTE", "tRNS", "IDAT", "IEND", "gAMA", "cHRM", "sRGB", "sBIT", "bKGD", "pHYs", "iCCP"}


def metadata_of(path, data):
    """(bytes to scan for personal strings, [problems]) for a picture: pixel data is skipped, metadata is not."""
    ext = os.path.splitext(path)[1].lower()
    scan, problems = b"", []
    if ext == ".png":
        chunks = png_chunks(data)
        if chunks is None:
            return b"", ["is not a valid PNG"]
        for t, body in chunks:
            if t not in PNG_OK:
                problems.append(f"has a '{t}' metadata chunk" + (" (text, may name the program or your files)" if t in ("tEXt", "zTXt", "iTXt") else ""))
            if t != "IDAT":
                scan += body + b"\n"
    elif ext in (".jpg", ".jpeg"):
        i = 2
        while i + 4 <= len(data) and data[i] == 0xFF:
            marker = data[i + 1]
            if marker == 0xDA:  # start of image data
                break
            n = struct.unpack(">H", data[i + 2:i + 4])[0]
            body = data[i + 4:i + 2 + n]
            if marker == 0xFE or (0xE1 <= marker <= 0xEF and marker != 0xE2):
                problems.append(f"has a metadata segment (0x{marker:02X}: EXIF, XMP, comment or editor data)")
                scan += body + b"\n"
            i += 2 + n
    elif ext == ".gif":
        i = 13
        if data[10] & 0x80:
            i += 3 * (2 ** ((data[10] & 7) + 1))
        while i < len(data):
            b = data[i]
            if b == 0x3B:
                break
            if b == 0x21:  # extension
                label = data[i + 1]
                j = i + 2
                body = b""
                while j < len(data) and data[j] != 0:
                    body += data[j + 1:j + 1 + data[j]]
                    j += 1 + data[j]
                if label == 0xFE or (label == 0xFF and not body.startswith((b"NETSCAPE2.0", b"ANIMEXTS1.0"))) or label == 0x01:
                    problems.append(f"has a metadata block (extension 0x{label:02X}: comment, text or editor data)")
                    scan += body + b"\n"
                i = j + 1
            elif b == 0x2C:  # image: skip its pixel data
                flags = data[i + 9]
                i += 10
                if flags & 0x80:
                    i += 3 * (2 ** ((flags & 7) + 1))
                i += 1
                while i < len(data) and data[i] != 0:
                    i += 1 + data[i]
                i += 1
            else:
                break
    return scan, problems


def clean_png(data):
    out, chunks = b"\x89PNG\r\n\x1a\n", png_chunks(data)
    for t, body in chunks:
        if t in PNG_OK:
            out += struct.pack(">I", len(body)) + t.encode("latin-1") + body + struct.pack(">I", zlib.crc32(t.encode("latin-1") + body) & 0xFFFFFFFF)
    return out


# ---------------------------------------------------------------- the checks


def scan_text(where, data_or_strings):
    strs = data_or_strings if isinstance(data_or_strings, list) else strings_in(data_or_strings)
    personal = personal_values()
    seen = set()
    for s in strs:
        for label, rx in personal:  # one finding per string: the first personal thing in it
            m = re.search(rx, s)
            if m:
                if (label, m.group(0)) not in seen:
                    seen.add((label, m.group(0)))
                    stop(f"{where} contains {label}: ...{s[max(0, m.start() - 20):m.end() + 30]}...")
                break
    for label, rx in SECRETS:
        for s in strs:
            m = re.search(rx, s)
            if m and ("secret", m.group(0)) not in seen:
                seen.add(("secret", m.group(0)))
                stop(f"{where} contains {label} ({m.group(0)[:6]}...)")
    for label, rx in GENERIC:
        hits = []
        for s in strs:
            for m in re.finditer(rx, s):
                h = m.group(0)
                if h in hits or ("g", h) in seen or any(re.search(prx, h) for _, prx in personal):
                    continue  # already reported above as something personal
                hits.append(h)
        for h in hits[:3]:
            seen.add(("g", h))
            warn(f"{where} contains {label}: {h[:100]}")
        if len(hits) > 3:
            warn(f"{where}: {len(hits) - 3} more of {label}")


def check_dll(rel, data):
    for s in strings_in(data):
        for m in re.finditer(r"[^\s\"']*\.pdb\b", s):
            p = m.group(0)
            if re.match(r"(/|[A-Za-z]:\\|\\\\)", p):
                stop(f"{rel} embeds the full path of your build folder ({p}); the .csproj needs the PathMap lines (see the template), then rebuild")
            else:
                print(f"  note: {rel} embeds the debug-file path '{p}' (relative, fine)")
            if re.search(r"[/\\]obj[/\\]Debug[/\\]", p):
                stop(f"{rel} was built in the Debug configuration ({p}), not Release. For a Godot-export mod check that the .sln maps Release|Any CPU to Release, then rebuild")
            return


PCK_NEVER = (".md", ".py", ".sh", ".csproj", ".sln", ".log", ".bak", ".psd", ".kra", ".xcf", ".blend", ".aseprite", ".ase", ".txt", ".json", ".props", ".yml", ".yaml")
PCK_DIRS = ("tests/", "test/", "docs/", "tools/", "workshop/", "dist/", "bin/", "obj/", ".git/", ".github/", "scripts/")


def check_pck(rel, data):
    rx = rb"res://[\w./ \-]+?\.(?:csproj|cs|png|ogg|wav|mp3|tscn|tres|scn|res|import|gd|json|md|txt|py|sh|sln|cfg|log|bak|psd|kra|xcf|blend|aseprite|ase|gif|jpe?g|webp|svg|ttf|otf|glb|gltf|gdshader|shader|material|uid|props|ya?ml)(?![A-Za-z0-9])"
    paths = sorted({m.group(0)[len(b"res://"):].decode("latin-1") for m in re.finditer(rx, data)})
    # A Godot C# project lists EVERY .cs file of the project in the .pck (the engine needs them to attach scripts), whatever exclude_filter says, as empty placeholders: the code is in the dll.
    # They are not reported by folder; instead the .pck is searched for what real C# source looks like.
    scripts = [p for p in paths if p.lower().endswith(".cs")]
    paths = [p for p in paths if p not in scripts]
    print(f"  note: {rel} lists {len(paths)} files" + (f" and {len(scripts)} C# script placeholders" if scripts else ""))
    if scripts and re.search(rb"(?m)^\s*(namespace\s+[A-Za-z_][\w.]*|using\s+[A-Z][\w.]*;)", data):
        warn(f"{rel} contains what looks like C# source text, not just placeholders for the scripts")
    bad_dirs = [p for p in paths if p.startswith(PCK_DIRS)]
    bad_ext = [p for p in paths if p.lower().endswith(PCK_NEVER) and p not in bad_dirs]
    if bad_dirs:
        warn(f"{rel} packs files from folders that should not ship: {', '.join(bad_dirs[:6])}" + (" ..." if len(bad_dirs) > 6 else "") + " (add them to exclude_filter in export_presets.cfg)")
    if bad_ext:
        warn(f"{rel} packs files that look like source or notes: {', '.join(bad_ext[:6])}" + (" ..." if len(bad_ext) > 6 else ""))


def main():
    csprojs = glob.glob(os.path.join(ROOT, "*.csproj"))
    if len(csprojs) != 1:
        sys.exit(f"Expected exactly one .csproj in {ROOT}.")
    mod = os.path.basename(csprojs[0])[:-len(".csproj")]
    print(f"Scrub check for {mod}\n\nGoing to the Workshop:")

    # --- the exact set of files ---
    content = os.path.join(HERE, "content")
    expected = {f"{mod}.dll", f"{mod}.json"}
    try:
        has_pck = bool(json.load(open(os.path.join(ROOT, f"{mod}.json"), encoding="utf-8-sig")).get("has_pck"))
    except (OSError, ValueError):
        has_pck = False
    if has_pck:
        expected.add(f"{mod}.pck")
    present = {}
    for dirpath, _, names in os.walk(content):
        for n in names:
            if n != ".gitkeep":
                full = os.path.join(dirpath, n)
                present[os.path.relpath(full, content)] = full
    for extra in sorted(set(present) - expected):
        stop(f"content/{extra} should not be uploaded (only {', '.join(sorted(expected))}). Delete it, then run dotnet publish")
    for missing in sorted(expected - set(present)):
        stop(f"content/{missing} is missing. Run dotnet publish")

    files = [(f"content/{n}", p) for n, p in sorted(present.items())]
    img = os.path.join(HERE, "image.png")
    if os.path.exists(img):
        files.append(("image.png", img))
    gif = os.path.join(HERE, "image.gif")
    if os.path.exists(gif):
        files.append(("image.gif", gif))
    prev = os.path.join(HERE, "previews")
    if os.path.isdir(prev):
        for n in sorted(os.listdir(prev)):
            files.append((f"previews/{n}", os.path.join(prev, n)))
    wj = os.path.join(HERE, "workshop.json")

    for rel, full in files:
        data = open(full, "rb").read()
        print(f"  {rel:34} {len(data) / 1024:8.1f} KB  {hashlib.sha256(data).hexdigest()[:12]}")
        ext = os.path.splitext(rel)[1].lower()
        if ext in (".png", ".jpg", ".jpeg", ".gif"):
            if rel.startswith("previews/") and len(data) >= 1024 * 1024:
                stop(f"{rel} is 1 MB or more (the uploader's limit)")
            scan, problems = metadata_of(rel, data)
            if rel == "image.png" and FIX_IMAGE and problems and png_chunks(data) is not None:
                open(full, "wb").write(clean_png(data))
                print(f"  fixed: rewrote {rel} without metadata chunks ({len(data)} -> {len(clean_png(data))} bytes)")
                data = open(full, "rb").read()
                scan, problems = metadata_of(rel, data)
            for p in problems:
                stop(f"{rel} {p}" + (" (python3 workshop/scrub.py --fix-image removes it from a PNG)" if rel == "image.png" and ext == ".png" else " (re-export the picture without metadata)"))
            scan_text(rel, scan)
        else:
            if ext == ".dll":
                check_dll(rel, data)
            elif ext == ".pck":
                check_pck(rel, data)
            scan_text(rel, data)
    if os.path.exists(wj):
        try:
            w = json.load(open(wj, encoding="utf-8-sig"))
            scan_text("workshop.json", [json.dumps(v, ensure_ascii=False) for v in w.values() if v is not None])
        except ValueError:
            pass

    print()
    for m in warns:
        print("WARN  ", m)
    for m in stops:
        print("STOP  ", m)
    if not warns and not stops:
        print("OK     nothing unnecessary or personal found")
    elif not stops:
        print("\nWarnings are things to look at, not blockers.")
    sys.exit(1 if stops else 0)


if __name__ == "__main__":
    main()
