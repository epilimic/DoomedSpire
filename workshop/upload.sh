#!/usr/bin/env bash
# Checks everything it can (including a scrub of every file for local paths and personal details), shows exactly what is about to go to the Workshop, asks, then runs the official uploader.
# It never writes or changes any Workshop text: title, description, visibility, tags and the change note come from workshop.json, which only the owner edits
# (it is git-ignored; created once from workshop.example.json). After an upload you confirm it worked, and it records the version in last_upload.txt
# and commits and pushes that record (last_upload.txt, and mod_id.txt after a first upload) itself, so the next run does not stop on uncommitted files.
# Identical in every mod repo: the mod id is the name of the .csproj in the repo root.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
csprojs=("$ROOT"/*.csproj)
{ [ "${#csprojs[@]}" -eq 1 ] && [ -f "${csprojs[0]}" ]; } || { echo "Expected exactly one .csproj in $ROOT." >&2; exit 1; }
MOD="$(basename "${csprojs[0]}" .csproj)"
# The uploader: MODUPLOADER if set, else the gif-capable build we compiled ourselves, else the official release. One place to change if it moves.
PATCHED_UPLOADER="/mnt/stuff/sts/moduploader-src/linux-x64/publish/ModUploader/release_linux-x64/ModUploader"
if [ -n "${MODUPLOADER:-}" ]; then UPLOADER="$MODUPLOADER"
elif [ -x "$PATCHED_UPLOADER" ]; then UPLOADER="$PATCHED_UPLOADER"
else UPLOADER="/mnt/stuff/sts/ModUploader/ModUploader"; fi
FILES=("$MOD.dll" "$MOD.json")
grep -Eq '"has_pck"[[:space:]]*:[[:space:]]*true' "$ROOT/$MOD.json" && FILES+=("$MOD.pck")
stop() { echo "STOP: $*" >&2; exit 1; }

# --- workshop.json: exists, valid, no leftovers ---
[ -f "$HERE/workshop.json" ] || stop "workshop/workshop.json is missing. Copy workshop.example.json to workshop.json and fill it in by hand."
python3 -m json.tool "$HERE/workshop.json" > /dev/null || stop "workshop.json is not valid JSON (look for a trailing comma)."
python3 -I - "$HERE/workshop.json" <<'PY'
import json, sys
w = json.load(open(sys.argv[1], encoding="utf-8-sig"))
ref = [k for k in w if k.startswith("_REFERENCE")]
todo = [k for k, v in w.items() if "EDIT BY HAND" in json.dumps(v)]
if ref:
    sys.exit("STOP: workshop.json still has the reference lines (" + ", ".join(ref) + "). Delete them, and the comma after the last real field, then run again.")
if todo:
    sys.exit("STOP: workshop.json still says EDIT BY HAND in: " + ", ".join(todo) + ". Fill those in by hand, then run again.")
PY

# --- the page picture: workshop/image.png or workshop/image.gif (at least one; yours, under 1 MB) ---
[ -f "$HERE/image.png" ] || [ -f "$HERE/image.gif" ] || stop "workshop has neither image.png nor image.gif. Make your own picture and save it there: PNG (1280x720 recommended, no transparency) or an animated GIF, under 1 MB."
[ -f "$HERE/image.png" ] && python3 -I - "$HERE/image.png" <<'PY'
import os, struct, sys
p = sys.argv[1]
with open(p, "rb") as f:
    head = f.read(24)
if head[:8] != b"\x89PNG\r\n\x1a\n":
    sys.exit("STOP: workshop/image.png is not a PNG file.")
w, h = struct.unpack(">II", head[16:24])
size = os.path.getsize(p)
if size >= 1024 * 1024:
    sys.exit("STOP: workshop/image.png is %.2f MB; the uploader needs it under 1 MB." % (size / 1048576))
if (w, h) != (1280, 720):
    print("Note: image.png is %dx%d; 1280x720 is recommended." % (w, h))
PY

# --- image.gif (optional animated picture): needs our gif-capable uploader, under 1 MB ---
if [ -f "$HERE/image.gif" ]; then
python3 -I - "$HERE/image.gif" "$UPLOADER" <<'PY'
import os, sys
gif, up = sys.argv[1], sys.argv[2]
with open(gif, "rb") as f:
    head = f.read(6)
if head not in (b"GIF87a", b"GIF89a"):
    sys.exit("STOP: workshop/image.gif is not a GIF file.")
size = os.path.getsize(gif)
if size >= 1024 * 1024:
    sys.exit("STOP: workshop/image.gif is %.2f MB; the uploader needs it under 1 MB." % (size / 1048576))
# The gif-capable build has the text "image.png (or image.gif)" in it; the official one does not and would silently upload image.png instead.
needle = "(or image.gif)".encode("utf-16-le")
found = os.environ.get("MODUPLOADER_GIF") == "1"
d = os.path.dirname(up)
for n in ([os.path.basename(up)] + [x for x in os.listdir(d) if x.endswith(".dll")]) if os.path.isdir(d) else []:
    try:
        if needle in open(os.path.join(d, n), "rb").read():
            found = True
            break
    except OSError:
        pass
if not found:
    sys.exit("STOP: workshop/image.gif exists but the uploader at " + up + " does not look like the gif-capable build, so it would upload image.png instead. See workshop/README.md, 'Animated picture'. (If you are sure it is the right build: env MODUPLOADER_GIF=1 bash workshop/upload.sh)")
print("image.gif: %.0f KB, gif-capable uploader found." % (size / 1024))
PY
fi

# --- git: clean and pushed (one release = one pushed commit) ---
if git -C "$ROOT" rev-parse --git-dir > /dev/null 2>&1; then
    [ -z "$(git -C "$ROOT" status --porcelain)" ] || { git -C "$ROOT" status --short >&2; stop "uncommitted changes (listed above). Commit and push first."; }
    if git -C "$ROOT" rev-parse --abbrev-ref '@{u}' > /dev/null 2>&1; then
        [ "$(git -C "$ROOT" rev-list --count '@{u}..HEAD')" = "0" ] || stop "commits not pushed yet. git push first."
    else
        echo "Note: this branch has no upstream, so 'pushed' could not be checked."
    fi
fi

# --- the staged files exist, and the manifest staged with them is the repo's ---
for f in "${FILES[@]}"; do
    [ -f "$HERE/content/$f" ] || stop "workshop/content has no $f. Run: dotnet publish"
done
cmp -s "$HERE/content/$MOD.json" "$ROOT/$MOD.json" || stop "workshop/content/$MOD.json differs from the repo's $MOD.json (version edited after publishing?). Run: dotnet publish"
newer="$(cd "$ROOT" && git ls-files -z -- '*.cs' '*.csproj' '*.props' 2>/dev/null | while IFS= read -r -d '' f; do [ "$f" -nt "workshop/content/$MOD.dll" ] && echo "$f"; done | head -5)" || true
[ -z "$newer" ] || echo "WARN: source newer than the published dll (rebuild if you changed code since): $(echo "$newer" | tr '\n' ' ')"

# --- scrub: nothing unnecessary or personal goes up (see scrub.py) ---
python3 -I "$HERE/scrub.py" || stop "the scrub check found something that must not be uploaded (see above)."

# --- versions: raised, and no two places disagreeing ---
bash "$HERE/versions.sh" --for-upload || stop "see above."

echo
echo "=== About to upload ==="
python3 -I - "$HERE/workshop.json" "$HERE/content/$MOD.json" <<'PY'
import json, sys
w = json.load(open(sys.argv[1], encoding="utf-8-sig"))
m = json.load(open(sys.argv[2], encoding="utf-8-sig"))
print("manifest version :", m.get("version"))
for k in ("title", "visibility", "tags", "changeNote"):
    print(f"{k:17}:", w.get(k, "(not in file: left unchanged on Steam)"))
print("description      :", "set from file" if w.get("description") is not None else "(not in file: left unchanged on Steam)")
PY
echo "content dll      : $(sha256sum "$HERE/content/$MOD.dll" | cut -c1-12)"
echo
read -r -p "Upload this? [y/N] " ans
[ "$ans" = "y" ] || { echo "Cancelled."; exit 0; }

cd "$ROOT"
"$UPLOADER" upload -w workshop || true
mv -f mod-uploader.log /tmp/mod-uploader.log 2>/dev/null || true   # keep the log out of the repo; /tmp/mod-uploader.log if you need it

echo
read -r -p "Did the uploader report success? [y/N] " ok
if [ "$ok" = "y" ]; then
    VER="$(python3 -I -c 'import json,sys; print(json.load(open(sys.argv[1],encoding="utf-8-sig"))["version"])' "$HERE/content/$MOD.json")"
    {
        echo "version=$VER"
        echo "dll=$(sha256sum "$HERE/content/$MOD.dll" | cut -c1-12)"
        echo "date=$(date +%F)"
    } > "$HERE/last_upload.txt"
    echo "Recorded in workshop/last_upload.txt."
    # Commit and push the record (and mod_id.txt, which the uploader writes at a first upload), so nothing is left uncommitted: the next run would
    # stop on it, and losing mod_id.txt would make a second Workshop item. Only these two files are ever added.
    if git -C "$ROOT" rev-parse --git-dir > /dev/null 2>&1; then
        REC=()
        for f in last_upload.txt mod_id.txt; do [ -f "$HERE/$f" ] && REC+=("workshop/$f"); done
        if [ "${#REC[@]}" -gt 0 ] && [ -n "$(git -C "$ROOT" status --porcelain -- "${REC[@]}")" ]; then
            if git -C "$ROOT" add -- "${REC[@]}" && git -C "$ROOT" commit -q -m "Record upload $VER" -- "${REC[@]}"; then
                if git -C "$ROOT" push -q; then
                    echo "Committed and pushed: ${REC[*]}"
                else
                    echo "Committed ${REC[*]} but the push failed. Run: git push"
                fi
            else
                echo "Could not commit ${REC[*]}. Commit them yourself (never lose workshop/mod_id.txt), then push."
            fi
        fi
    else
        echo "This is not a git repository: keep workshop/last_upload.txt and workshop/mod_id.txt safe."
    fi
else
    echo "Nothing recorded. Run again once the cause is fixed."
fi
