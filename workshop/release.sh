#!/usr/bin/env bash
# One command for a Workshop release:   bash workshop/release.sh
#
#   0. asks whether you have updated workshop/workshop.json by hand for this release (and shows what it says)
#   1. git pull
#   2. checks the version is raised above the last upload; offers to raise it (edits the manifest, commits, pushes)
#   3. dotnet publish
#      (publish also stages the build into workshop/content)
#   4. bash workshop/upload.sh           (all its checks and the scrub, shows what goes up, asks y/N, uploads, records it)
#   5. commits and pushes the upload record (workshop/last_upload.txt, and mod_id.txt after a first upload)
#
# It never writes any Workshop text. Identical in every mod repo: the mod id is the name of the .csproj in the repo root.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
csprojs=("$ROOT"/*.csproj)
{ [ "${#csprojs[@]}" -eq 1 ] && [ -f "${csprojs[0]}" ]; } || { echo "Expected exactly one .csproj in $ROOT." >&2; exit 1; }
MOD="$(basename "${csprojs[0]}" .csproj)"
MANIFEST="$ROOT/$MOD.json"
step() { echo; echo "=== $* ==="; }
stop() { echo "STOP: $*" >&2; exit 1; }
cd "$ROOT"

command -v dotnet > /dev/null || stop "dotnet is not installed or not on the PATH."
git rev-parse --git-dir > /dev/null 2>&1 || stop "this is not a git repository."

# --- 0. workshop.json: the question comes first ---
step "0/5  workshop.json"
[ -f "$HERE/workshop.json" ] || stop "workshop/workshop.json is missing. Copy workshop/workshop.example.json to workshop/workshop.json and fill it in by hand."
python3 -I - "$HERE/workshop.json" <<'PY'
import json, sys
w = json.load(open(sys.argv[1], encoding="utf-8-sig"))
for k in ("title", "visibility", "tags", "changeNote"):
    print(f"  {k:11}: {w.get(k, '(not in file: left unchanged on Steam)')}")
PY
read -r -p "Have you updated workshop/workshop.json by hand for THIS release? [y/N] " ans
[ "$ans" = "y" ] || stop "edit workshop/workshop.json (new changeNote; check visibility and tags), then run this again."

# --- 1. pull ---
step "1/5  git pull"
git pull --ff-only
[ -z "$(git status --porcelain)" ] || { git status --short >&2; stop "uncommitted changes (listed above). Commit and push them first."; }
if git rev-parse --abbrev-ref '@{u}' > /dev/null 2>&1; then
    [ "$(git rev-list --count '@{u}..HEAD')" = "0" ] || stop "commits not pushed yet. git push first."
fi

# --- 2. version ---
step "2/5  version"
VERSION="$(python3 -I - "$MANIFEST" "$HERE/last_upload.txt" <<'PY'
import json, os, re, sys

manifest, last_file = sys.argv[1:3]
cur = json.load(open(manifest, encoding="utf-8-sig")).get("version", "")
last = ""
if os.path.exists(last_file):
    for line in open(last_file, encoding="utf-8"):
        if line.startswith("version="):
            last = line.strip().split("=", 1)[1]

def key(v):
    m = re.fullmatch(r"v(\d+)\.(\d+)\.(\d+)", v or "")
    return tuple(int(x) for x in m.groups()) if m else None

ok = key(cur) is not None and (not last or key(last) is None or key(cur) > key(last))
suggest = ""
base = key(last) or key(cur)
if base:
    suggest = "v%d.%d.%d" % (base[0], base[1], base[2] + 1)
print("%s|%s|%s|%s" % (cur, last, "ok" if ok else "bump", suggest))
PY
)"
IFS='|' read -r CUR LAST STATE SUGGEST <<< "$VERSION"
echo "  manifest says ${CUR:-?}; last recorded upload: ${LAST:-none}"
NEW=""
if [ "$STATE" = "ok" ]; then
    read -r -p "Release ${CUR}? [Y/n] " a
    if [ "$a" = "n" ] || [ "$a" = "N" ]; then NEW="ask"; fi
else
    echo "  The manifest version is not newer than the last upload, so it has to be raised."
    NEW="ask"
fi
if [ "$NEW" = "ask" ]; then
    while true; do
        read -r -p "New version [${SUGGEST}] (Enter accepts, q stops): " v
        [ "$v" = "q" ] && stop "no version chosen."
        v="${v:-$SUGGEST}"
        if ! [[ "$v" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then echo "  Use the form v1.2.3."; continue; fi
        NEW="$v"; break
    done
    python3 -I - "$MANIFEST" "$NEW" "$LAST" <<'PY'
import json, re, sys
path, new, last = sys.argv[1:4]
def key(v):
    m = re.fullmatch(r"v(\d+)\.(\d+)\.(\d+)", v or "")
    return tuple(int(x) for x in m.groups()) if m else None
if last and key(last) and key(new) <= key(last):
    sys.exit(f"STOP: {new} is not newer than the last upload ({last}).")
text = open(path, encoding="utf-8").read()
out, n = re.subn(r'("version"\s*:\s*")[^"]*(")', lambda m: m.group(1) + new + m.group(2), text, count=1)
if n != 1:
    sys.exit("STOP: could not find the \"version\" line in the manifest.")
json.loads(out.lstrip("﻿"))
open(path, "w", encoding="utf-8").write(out)
PY
    git add "$MANIFEST"
    git commit -q -m "Version $NEW"
    git push -q
    echo "  Raised to $NEW, committed and pushed."
    CUR="$NEW"
fi

# --- 3. publish ---
step "3/5  dotnet publish (also stages workshop/content)"
dotnet publish

# --- 4. upload (its own checks, the scrub, and the final y/N) ---
step "4/5  upload"
bash "$HERE/upload.sh"

# --- 5. record ---
step "5/5  record the upload"
FILES=()
for f in workshop/last_upload.txt workshop/mod_id.txt; do [ -f "$f" ] && FILES+=("$f"); done
if [ "${#FILES[@]}" -gt 0 ] && [ -n "$(git status --porcelain -- "${FILES[@]}")" ]; then
    git add -- "${FILES[@]}"
    git commit -q -m "Record upload $CUR"
    git push -q
    echo "Committed and pushed: ${FILES[*]}"
    echo
    echo "Done. Steam updates subscribers only while their game is closed. Once Steam has downloaded it here,"
    echo "bash workshop/versions.sh should show the Workshop copy at $CUR."
else
    echo "Nothing was recorded (the upload was cancelled or not confirmed), so nothing to commit."
fi
