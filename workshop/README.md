# Steam Workshop workspace (official uploader)

Uploader: https://github.com/megacrit/sts2-mod-uploader (the program is run with `upload -w workshop` from the repo root).
This folder is the whole upload workspace, kept inside the repo, so every mod looks the same. **This file is the one place the Workshop process is described.**

## Releasing

**Every update** (one command):

1. Edit `workshop/workshop.json` **by hand**: new `changeNote`; check `visibility`, `tags` and `description` still say what the page should say (every upload re-applies whatever is in the file).
2. With Steam running and logged in: `bash workshop/release.sh`. It stops at the first problem and does, in order:
   1. shows the title, visibility, tags and note it will send, and asks "Have you updated workshop.json by hand for THIS release?";
   2. `git pull` (stops on uncommitted or unpushed changes);
   3. checks the manifest version is higher than your last upload and, if not, asks for the new one (Enter accepts the next patch number), edits `"version"` in `<ModId>.json`, commits and pushes it;
   4. `dotnet publish`, which also stages the build into `workshop/content/`;
   5. `bash workshop/upload.sh`: the checks and the scrub below, what is about to go up, `Upload this? [y/N]`, the upload, "Did the uploader report success?", then it records the upload and **commits and pushes `workshop/last_upload.txt` (and `mod_id.txt` after a first upload) itself**, so nothing is left uncommitted;
   6. says whether the upload was recorded (and commits and pushes the record if `upload.sh` could not).
3. Check the item on the Workshop page. Steam may ask you to accept its Workshop agreement once. Subscribers get the update only while their game is closed; tell people to restart the game after it downloads.
4. When Steam has delivered it to you (game closed), `bash workshop/versions.sh` should show the Workshop copy at the new version.

**First upload of a new mod:** before step 1, create `workshop/workshop.json` by copying `workshop.example.json` (a session does this, unchanged), and make your own `image.png` (or an animated `image.gif`). Use
`"visibility": "private"` for the first upload, then set it on the Workshop page after checking. The uploader writes `mod_id.txt` itself.

**By hand**, if you ever want to: `git pull`, raise the version in `<ModId>.json`, commit and push, `dotnet publish`, `bash workshop/upload.sh` (it commits and pushes `last_upload.txt` itself).

## Animated picture (optional)

The official uploader only sends `image.png`. We run a **gif-capable build** of it (one small patch: it uses `image.gif` instead of `image.png` when that file exists, and behaves exactly like the official one when it does not). To use it, save an animated **`workshop/image.gif`** (under 1 MB; GIFs of 642x364 and ~800 KB are known to work). **You need one of `image.png` and `image.gif`, not both**; if both exist the gif is sent. `upload.sh` then
uses the gif-capable build (`/mnt/stuff/sts/moduploader-src/linux-x64/publish/ModUploader/release_linux-x64/ModUploader`, or whatever `MODUPLOADER` points to) and **stops if the uploader it would run is not that build**, because the official one would silently upload the PNG. `scrub.py` checks the gif like every other picture.
The patch (`docs/uploader-gif-preview.patch`) and the build steps (`docs/UPLOADER_GIF.md`) live in the standards repo. If the patch is ever accepted upstream, this section goes away.

## Text is hand-edited, only by the owner

`title`, `description`, `changeNote`, `tags` and `visibility` in `workshop.json` are written and changed **by hand, by the owner, and by nobody
else**. No session, script or tool writes, rewrites, "improves" or fills in an update note or a Workshop description, including a first draft. If one
is empty or still says `EDIT BY HAND`, say so and stop; do not make one up. Sessions may keep the file valid JSON and nothing more.

`workshop.json` is **git-ignored** (so it exists only on the owner's machine; keep your own copy of it somewhere you back up). A session creates it once, for a new mod, by copying
`workshop.example.json` to `workshop.json` unchanged (placeholders and all). From then on only the owner edits it. `workshop.example.json` is the full reference copy and is never edited.
At its bottom are two `_REFERENCE_...` lines (the visibility values and the tag list): delete them from `workshop.json` once you have chosen, and remove the comma after `"maxBranch"` too.
`upload.sh` refuses to run while those lines remain, or while any field still says `EDIT BY HAND`.

## Files

| File | What it is |
|---|---|
| `workshop.example.json` | Committed, never edited: all nine fields with placeholders. The source for a new `workshop.json`. |
| `workshop.json` | Git-ignored. What is sent to Steam. Hand-edited by the owner. Fields below. Plain UTF-8, no trailing commas (a trailing comma makes the uploader fail with "Exception thrown while parsing the workshop config"; check with `python3 -m json.tool workshop/workshop.json`). |
| `image.png` | The Workshop page picture (or `image.gif` instead, below: one of the two is required). **Not in the template: you make your own** and save it here. PNG, **1280x720 recommended**, under 1 MB, no transparency (Steam shows transparency as black). Sent on every upload. |
| `image.gif` | Animated page picture, instead of `image.png`; needs the gif-capable uploader (above). |
| `previews/` | Optional extra pictures (each under 1 MB). If the folder exists, pictures missing from it are removed from the item (matched by file name). If the folder does not exist, previews are left alone. |
| `content/` | What gets uploaded: `<ModId>.dll`, `<ModId>.json` and, for a Godot-export mod, `<ModId>.pck`. Written by `dotnet publish` (it clears anything stale first); its files are not committed (only `.gitkeep`). |
| `mod_id.txt` | **You never create it.** The uploader writes it during the first upload of a new mod. **It is committed and pushed for you right after the upload** (`upload.sh`). Never lose it: it ties this folder to the Workshop item. Without it the next upload creates a second item. Delete it only to deliberately make a new item. |
| `last_upload.txt` | Written by `upload.sh` when you confirm an upload worked: version, DLL fingerprint, date. `upload.sh` commits and pushes it for you. It is what `versions.sh` and `upload.sh` compare the next version against. |
| `release.sh` | The one command for a release (above). |
| `upload.sh` | The checks (below), prints version, note, visibility, tags and the fingerprint, asks `y/N`, runs the uploader, then asks whether it worked and records it. Writes nothing to `workshop.json`. |
| `versions.sh` | The drift check: lists the version in the repo manifest, a stray `.csproj` `<Version>`, the game folder, `content/`, the last recorded upload and the Workshop copy Steam delivered to this machine, and flags disagreements (including a game-folder dll that is not the staged one). Read-only; `--for-upload` makes the blocking ones exit 1. |
| `scrub.py` | The scrub check (below). Read-only except `--fix-image`. `upload.sh` runs it. |

`release.sh`, `upload.sh`, `versions.sh` and `scrub.py` are **identical in every repo and are never edited per mod**: the mod id is the name of the `.csproj` in the repo root, and
the `.pck` is included when the manifest says `"has_pck": true`. If a repo's copy differs from the standards repo's, re-copy it (`scripts/check-standard.py` reports this).

## What `upload.sh` checks

It refuses to start unless: `workshop.json` is valid with no `EDIT BY HAND` or `_REFERENCE` lines; `image.png` or `image.gif` exists and is a real picture under 1 MB; the git working copy is clean and pushed; `content/` has the
expected files and its manifest equals the repo's; the scrub finds nothing it must stop on; and the manifest version is higher than the last recorded upload (or the Workshop copy here) with
no two places disagreeing. It only warns when source files are newer than the published DLL. The game folder comes from `STS2_PATH`, else `<Sts2Path>` in `Directory.Build.props`.

## `workshop.json` fields

A field that is missing, or `null`, is left unchanged on Steam (with the exceptions marked). A field that is present is **sent on every upload and
overwrites the Workshop page**, so keep the file true to what the page should say.

| Field | Meaning |
|---|---|
| `title` | Item title. |
| `description` | Item description (the Workshop page text). |
| `visibility` | `"private"`, `"public"`, `"unlisted"` or `"friends_only"`. Present = every upload resets the item to this value (a public mod with `"private"` here goes private again). |
| `changeNote` | The note for this update, shown in the item's change log. Write a new one by hand for every upload. |
| `tags` | List of tags. Present = the item's tags become exactly this list. Exact spelling matters (below). `"Tools & APIs"` is reserved for tools and APIs. |
| `dependencies` | List of Workshop item ids (the number at the end of the item's URL). **Present = the item's required items become exactly this list**; an empty list `[]` removes them all. Keep `null` unless the mod needs another mod. |
| `contentDescriptors` | Mature-content descriptors: `"nudity"`, `"frequent_violence"`, `"adult_only"`, `"gratuitous_nudity"`, `"general_mature"`. Present = reconciled exactly, so `[]` clears them. |
| `minBranch`, `maxBranch` | Minimum/maximum game branch (`"public-beta"`, `"public"`). The uploader's own docs say these behave oddly: **prefer the Workshop page**. Unlike the others, `null` or missing here is sent as "supports all versions". **The game enforces the range** (since v0.113.1, `ModManager.EvaluateBranchSupport`): it reads each Workshop mod's supported branches from Steam and **does not load the mod** for a player whose branch is outside them, with the error `MOD_ERROR.STEAM_BRANCH_UNSUPPORTED`. An empty range means every branch. Leave both `null` unless you mean to exclude players. |

**Tags the Workshop offers** (from its tag dropdown; spell them exactly like this, for example `QoL`, not `QOL`): Acts, Ancients, Audio, Cards, Characters, Cosmetics, Events, Expansion, Extensions, Humor, Modifiers, Monsters, Potions, QoL, Relics, Rooms, Tools & APIs, Utility, Misc.

## What gets scrubbed

Only these go to the Workshop: `content/`, `image.png`, `previews/*`, and the text fields of `workshop.json`. `python3 workshop/scrub.py` (run by `upload.sh`, and fine to run any time) looks at all of them and **stops** on:

- any file in `content/` other than the expected ones (a `.pdb`, a stray `.json` the game would parse as a manifest, a log, a copy of a library);
- your home folder, user name, computer name, this repo's folder or the game folder, found inside any file (looked up when it runs, never stored in the repo), including inside
  the dll's UTF-16 string literals and the `.pck`;
- the full path of your build folder embedded in the dll's debug-file path, and a dll built in the Debug configuration;
- secrets: GitHub, AWS, Slack, Google and similar tokens, private keys;
- picture metadata (PNG text/time/EXIF chunks, JPEG EXIF/comment segments, GIF comments), which can name the program, your files or your camera; `--fix-image` rewrites
  `image.png` without those chunks and leaves the pixels alone. A JPEG or GIF preview has to be re-exported without metadata.

It **warns** (look, then decide) on other absolute paths, Windows paths, email addresses, Steam account ids, and files packed into the `.pck` from folders that should not ship (`tests/`,
`docs/`, `tools/` ...; add them to `exclude_filter` in `export_presets.cfg`) or that look like source or notes. A Godot C# project's `.pck` always lists every `.cs` file as an empty placeholder (`exclude_filter` does not remove them; the code is in the dll), so those are not reported by folder; the scrub warns if the `.pck` contains what looks like real C# source instead.

Keeping paths out of the dll is done at the source: the `.csproj` has the two `PathMap` lines, and a Godot-export mod's `.sln` must map `Release|Any CPU` to `Release|Any CPU`
(the BaseLib template maps it to Debug, which makes `dotnet publish` produce a Debug dll). The scrub is the check that this worked.

## If something goes wrong

| Message or symptom | Usual cause and fix |
|---|---|
| `STOP ... scrub` | `python3 workshop/scrub.py` lists what. A stray file in `content/`: run `dotnet publish` again. Your home folder, user name or a path inside the dll or `.pck`: the `.csproj` needs its `PathMap` lines; rebuild. A Debug dll: the Godot `.sln` must map Release to Release; rebuild. Metadata in `image.png`: `python3 workshop/scrub.py --fix-image`. A secret or a path in `workshop.json`: edit it by hand. Warnings do not stop an upload; read them. |
| `STOP ... uncommitted changes` / `commits not pushed` | Commit and push first; one release is one pushed commit. |
| `STOP ... not newer than the last recorded upload` | Raise `"version"` in `<ModId>.json` (`release.sh` offers to). |
| "Exception thrown while parsing the workshop config" | `workshop.json` is not valid JSON, most often a trailing comma. `python3 -m json.tool workshop/workshop.json` names the line. |
| "There is no file named image.png (or image.gif) in the workspace" (the gif-capable build; the official one says just `image.png`) | The picture is missing or the uploader was run from the wrong folder. Use `release.sh` / `upload.sh`, which run from the repo root with `-w workshop`. |
| It uploads, but the old DLL is on the Workshop | `content/` was not refreshed by `dotnet publish`. `versions.sh` and `upload.sh` flag a game-folder dll that is not the staged one; run `dotnet publish` and upload again. |
| "Tried to upload to workshop item ... but it doesn't exist" | `mod_id.txt` is wrong or from another account. Do not delete it to "fix" it unless you want a second item. |
| "Couldn't confirm existence… k_EResultFail" | A Steam hiccup: run it again. |

`mod-uploader.log` appears wherever the uploader is run (`upload.sh` moves it to `/tmp`). It is git-ignored; send it to the game's devs with any bug report.
