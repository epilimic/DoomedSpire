#!/usr/bin/env bash
# Shows every place this mod's version lives, local and Workshop, and flags the ones that disagree. Read-only.
#   bash workshop/versions.sh              show the table
#   bash workshop/versions.sh --for-upload same, but exit 1 if it is not safe to upload (upload.sh runs this)
# Identical in every mod repo. The game folder comes from STS2_PATH, else <Sts2Path> in Directory.Build.props, else the owner's default.
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
csprojs=("$ROOT"/*.csproj)
{ [ "${#csprojs[@]}" -eq 1 ] && [ -f "${csprojs[0]}" ]; } || { echo "Expected exactly one .csproj in $ROOT." >&2; exit 1; }
MOD="$(basename "${csprojs[0]}" .csproj)"

STS2="${STS2_PATH:-}"
if [ -z "$STS2" ] && [ -f "$ROOT/Directory.Build.props" ]; then
    # XML comments are removed first: a commented-out <Sts2Path> line (an old path, a "?" placeholder) must never be taken for the real one.
    STS2="$(python3 -I - "$ROOT/Directory.Build.props" <<'PYPROPS'
import re, sys
try:
    text = open(sys.argv[1], encoding="utf-8-sig", errors="replace").read()
except OSError:
    sys.exit(0)
text = re.sub(r"<!--.*?-->", "", text, flags=re.S)
m = re.search(r"<Sts2Path>(.*?)</Sts2Path>", text)
print(m.group(1).strip() if m else "")
PYPROPS
)"
fi
STS2="${STS2:-/mnt/stuff/SteamLibrary/steamapps/common/Slay the Spire 2}"

exec python3 -I - "$MOD" "$ROOT" "$HERE" "$STS2" "${1:-}" <<'PY'
import json, os, re, sys

mod, root, here, sts2, mode = sys.argv[1:6]
upload = mode == "--for-upload"

def manifest_version(path):
    try:
        with open(path, encoding="utf-8-sig") as f:
            return json.load(f).get("version")
    except (OSError, ValueError):
        return None

def key(v):
    m = re.fullmatch(r"v?(\d+)\.(\d+)\.(\d+)", v or "")
    return tuple(int(x) for x in m.groups()) if m else None

def same(a, b):
    return key(a) is not None and key(a) == key(b)

repo = manifest_version(f"{root}/{mod}.json")
installed = manifest_version(f"{sts2}/mods/{mod}/{mod}.json")
staged = manifest_version(f"{here}/content/{mod}.json")

csproj_version = None
try:
    with open(f"{root}/{mod}.csproj", encoding="utf-8-sig") as f:
        m = re.search(r"<Version>\s*([^<$]+?)\s*</Version>", f.read())
        csproj_version = m.group(1) if m else None
except OSError:
    pass

last = {}
try:
    with open(f"{here}/last_upload.txt", encoding="utf-8") as f:
        for line in f:
            if "=" in line:
                k, v = line.strip().split("=", 1)
                last[k] = v
except OSError:
    pass

mod_id = None
try:
    with open(f"{here}/mod_id.txt", encoding="utf-8") as f:
        mod_id = f.read().strip() or None
except OSError:
    pass
workshop_copy = None
if mod_id:
    steamapps = os.path.dirname(os.path.dirname(sts2))
    workshop_copy = manifest_version(f"{steamapps}/workshop/content/2868840/{mod_id}/{mod}.json")

rows = [
    ("repo manifest", repo, f"{mod}.json: the version of record"),
    ("csproj <Version>", csproj_version, "should not exist (the manifest is the only place); if it does it must equal the manifest"),
    ("game folder copy", installed, f"mods/{mod}/ on this machine: what you test"),
    ("staged for upload", staged, "workshop/content/: what dotnet publish staged"),
    ("last recorded upload", last.get("version"), "workshop/last_upload.txt, written by upload.sh"),
    ("Workshop copy here", workshop_copy, "what Steam delivered to this machine (needs mod_id.txt, a subscription and the game closed)"),
]
print(f"Versions for {mod}")
for name, value, note in rows:
    print(f"  {name:21}: {value if value else '-':10}  {note}")
print()

block, warn = [], []
if repo is None:
    block.append(f"cannot read the version from {mod}.json")
elif not re.fullmatch(r"v\d+\.\d+\.\d+", repo):
    block.append(f"the manifest version '{repo}' is not in the form v1.2.3")
if csproj_version and repo and not same(csproj_version, repo):
    block.append(f"the .csproj <Version> ({csproj_version}) differs from the manifest ({repo}): remove it or make them equal")
if installed and repo and not same(installed, repo):
    warn.append(f"the game folder has {installed} but the repo says {repo}: run dotnet publish, or you are testing another build")
if staged and repo and not same(staged, repo):
    warn.append(f"workshop/content has {staged} but the repo says {repo}: run dotnet publish")  # upload.sh blocks on the manifest differing, byte for byte
import hashlib
def sha(path):
    try:
        with open(path, "rb") as f:
            return hashlib.sha256(f.read()).hexdigest()[:12]
    except OSError:
        return None
tested, staged_dll = sha(f"{sts2}/mods/{mod}/{mod}.dll"), sha(f"{here}/content/{mod}.dll")
if tested and staged_dll and tested != staged_dll:
    warn.append(f"the dll in the game folder ({tested}) is not the staged one ({staged_dll}): the build you tested is not the build you would upload; run dotnet publish")
baseline, baseline_name = last.get("version"), "the last recorded upload"
if not baseline and workshop_copy:
    baseline, baseline_name = workshop_copy, "the Workshop copy on this machine"
if baseline and repo and key(baseline) and key(repo):
    if key(repo) <= key(baseline):
        (block if upload else warn).append(f"the manifest ({repo}) is not newer than {baseline_name} ({baseline}): raise the version before uploading")
elif upload and mod_id and not baseline:
    warn.append("no record of an earlier upload (no workshop/last_upload.txt): cannot check that the version was raised")
if workshop_copy and last.get("version") and not same(workshop_copy, last["version"]):
    warn.append(f"Steam's copy here is {workshop_copy} but the last recorded upload was {last['version']}: not downloaded yet (close the game), or that upload did not go through")
if workshop_copy and repo and key(workshop_copy) and key(repo) and key(workshop_copy) > key(repo):
    warn.append(f"the Workshop copy ({workshop_copy}) is newer than the repo manifest ({repo}): git pull?")

for w in warn:
    print("WARN  ", w)
for b in block:
    print("STOP  " if upload else "DRIFT ", b)
if not warn and not block:
    print("OK     no disagreements found")
sys.exit(1 if (upload and block) else 0)
PY
