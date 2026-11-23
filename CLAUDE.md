# DoomedSpire

Read `SESSION_SETUP.md` from the standards repo `epilimic/modding-standards` (add it to the session with `add_repo`) before changing anything.

<!-- standards:begin (identical in every mod repo: copy this block unchanged from the standards repo's templates/common/CLAUDE.md; scripts/check-standard.py compares it) -->
## How to work with the owner

- Do only what is asked. Do not add features, modes, keys, settings, tools, files or clean-ups that were not requested, even if they seem helpful.
- If something extra looks worth doing, say so in one sentence and wait. Do not build it first.
- If you are not sure of something, ask. Do not guess or assume.
- Do not repeat instructions or re-raise settled items.
- **Never write, rewrite or "improve" Workshop text**: `title`, `description`, `changeNote`, `tags` and `visibility` in `workshop/workshop.json` are edited by hand by the owner only. The one thing a session may do is create that file the first time by copying `workshop/workshop.example.json` to `workshop/workshop.json` as it is (placeholders stay). If a field is missing or still a placeholder, say so and stop. `workshop.json` is git-ignored.
- Never create `workshop/image.png` or `workshop/mod_id.txt`. If the image is missing, remind the owner to make one (PNG, 1280x720 recommended, under 1 MB).
- Never run `workshop/release.sh` or `workshop/upload.sh`: only the owner uploads to Steam.
- Work directly on `main`, never on a branch or a PR, even if the session's own setup names a `claude/...` branch. Only the owner can say otherwise. `git pull` before starting, push when the task is done. Never force-push, never rebase `main`.
- Cloud sessions have no game DLLs, so game-facing code cannot be built here; the owner builds with `dotnet publish`. Say when something is unbuilt, once; do not repeat it after every change.
- The owner's machine is Linux (CachyOS) with the fish shell: `set -x NAME value`, not `export`; `env NAME=value cmd`.
- **Attach the game source at the start of every session:** `add_repo` `epilimic/sts2src`, and read its `CLAUDE.md`. It is a decompiled dump with a generated index and a query tool (`python3 tools/sts2.py find|show|hook|cmd|ui|sym ...`): use the index before grepping blind, cite `file:line` for anything you rely on, and never edit it.
- Game reference source lives in the epilimic/sts2src repo on main. After a game update, read docs/patches/<version>.md there first. It lists breaking API changes, renames and silent balance changes. Compiling against the new DLL isn’t enough: check the “silent changes” section for any mod that touches vanilla content.
- Before solving something non-trivial (lobby networking, UI that needs the game's art, logging, reaching private game members, testing without the game), look in `solutions/` of `epilimic/modding-standards` (index: `solutions/README.md`). If you solve something that looks reusable, say so in one sentence and wait; do not add it unasked.
- Never put secrets in the repo. In a fresh clone, enable the secret-scan hook once: `git config core.hooksPath .githooks`.
<!-- standards:end -->

## This mod

- Local project path: `/mnt/stuff/sts/doomedspire/`.
- The mod must never break the game: guard every handler and every per-frame callback. (Godot-export mods: the code folder name, `<ModId>Code`, is a Godot script path and must be unique among all installed mods.)
- **Check code with the owner's compiler.** The owner builds with the .NET 10 SDK (C# 14); a cloud container only has .NET 8 (C# 12) until you run `apt-get install -y dotnet-sdk-10.0`. C# 14 treats some code differently (it already broke two lines of the vendored MeltySynth: passing an array now prefers `ReadOnlySpan` overloads, see `src/MeltySynth/SOURCE.md`). Everything under `src/Host`, `src/ManagedDoom` and `src/MeltySynth` is free of Godot and the game, so compile those in a scratch project with `TargetFramework` `net9.0`, `LangVersion` `latest` and `Nullable` `enable`: it must give 0 errors and 0 warnings. The other `src/*.cs` files use Godot or the game and only the owner can build them.
- **Tests:** `dotnet run --project tests/Engine.Tests -c Release` (needs the Freedoom WAD files, see `tests/Engine.Tests/README.md`). It runs the engine, the keyboard and mouse input, the sound effects and the music without the game. Run it after any change under `src/Host` or the engine.
- **No custom Godot classes:** a dll-only mod cannot register scripts, so nothing can subclass a Godot node and override `_Process` or `_Input`. Wire behaviour with signals instead (see `src/DoomView.cs`).
- **The monitor frame is live-tunable.** `src/Host/CrtLayout.cs` (`CrtTuning`) holds the numbers that place the old-monitor picture (`assets/crt.png`) and the game on its glass, marked with the `[Tunable]` attribute the VfxSpawner mod (`epilimic/VfxSpawner`) looks for by name. In a run: F9, Show tuner, group "Doomed Spire CRT frame"; "Copy values" gives the numbers to paste back as the new defaults. DOOM steps aside while a canvas layer at or above 100 is showing something (VfxSpawner's menu is on 128), so the mouse and keys are the tuner's (`DoomView.ToolIsOpenOnTop`). The picture's own measurements (glass at 78,78 and 692 x 535 in its 848 x 744 pixels) are in `CrtPicture`; if the picture is replaced, measure it again.
- **The status-bar face is the player's character, made at run time.** `PlayerFace.cs` reads the character's picture from the game (never stored in the mod), `src/Host/FaceFactory.cs` makes DOOM's 42 faces from it (5 health levels x looks, ouch, grin, rampage, plus god and dead), and they go to the engine as an add-on WAD (`face.wad` in the mod's `data` folder) loaded on top of the game, which overrides Doomguy's lumps (`DoomSession.Create(..., extraWads)`). Its numbers are tunable like the monitor's (VfxSpawner group "Doomed Spire face", `FaceTuning`), taking effect when a new game starts; `face-sheet.png` next to `face.wad` shows all 42 for a look. The owner can paint over the faces: the mod writes `<character>-faces-to-paint.png` (transparent, 1:1, never any blood) and reads back `<character>-faces-painted.png` if it exists (`FaceSheetImport`); `docs/FACES.md` has the layout, the health-level mapping and the steps. The original face pictures differ in size, so one face is drawn once and placed in each lump's own rectangle (see the comment on `FaceFactory.Build`). The same add-on-WAD route can carry other replacements (the planned STS enemy sprites).
- The mod's own gotchas go here.
