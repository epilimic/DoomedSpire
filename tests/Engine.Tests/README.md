# Engine tests

Tests for the vendored DOOM engine (`src/ManagedDoom`), run headless: no game, no Godot, no screen. They play recorded demos on tiny test levels and compare a hash of every
monster, item and projectile on every tic with the value real DOOM produces, so a single wrong tic anywhere fails the test.

```bash
dotnet run --project tests/Engine.Tests -c Release        # all tests
dotnet run --project tests/Engine.Tests -c Release -- Monsters   # only names containing "Monsters"; add --verbose to list passes too
```

Needs `freedoom2.wad` and `freedoom1.wad` from Freedoom for the upstream tests that load their own test level, which are **not in this repo** (about 29 MB each). The one original game the tests use, the shareware `doom1.wad` the mod ships, **is** in the repo (`assets/doom/doom1.wad`). Get them from the Freedoom release page (https://github.com/freedoom/freedoom/releases, the zip holds both) or your
distribution's package (Debian/Ubuntu: `freedoom`; on Arch/CachyOS I believe `freedoom1` and `freedoom2`, not checked), then either put the two files in `tests/Engine.Tests/wads/` (git-ignored) or set `FREEDOOM_DIR` to their folder.
The packages' usual folders (`/usr/share/games/doom`, `/usr/share/doom`) are found without any setting. A test whose WAD is missing is reported as SKIP, never as a pass.
The project is built for .NET 8 and is set to run on any newer runtime.

**Last result (.NET 8 and .NET 10 SDKs, Freedoom 0.13.0 installed): 166 passed, 0 failed, 0 skipped, 6 seconds.** Checked that it can fail: making the fixed-point multiply off by one makes dozens of them fail (40 of the first 97).

`FrameTests.cs` is our own (not upstream): it runs the engine glue in `src/Host` on Freedoom: renders a frame, walks, turns, shoots, opens the menu, quits, and turns keyboard and mouse input into the player's commands. It also saves the picture to `tests/Engine.Tests/out/first-frame.png` (git-ignored), so a person can look at it.

**Order matters, on purpose:** the runner runs upstream's tests first and ours after. Starting a game from Freedoom (our own tests do) applies the WAD's `DEHACKED` lump to the engine's static tables, and they stay changed for the rest of the process, which breaks upstream's hash tests (they assume the unchanged tables). The mod itself is not affected: it only ever plays Freedoom. `AudioTests.cs` renders the sound effects and the music to numbers and saves `out/audio-sample.wav` (git-ignored) so a person can listen to it (original DOOM's MUS music through the soundfont `assets/soundfont/TimGM6mb.sf2`, which is in the repo); it also reads all 13 original music tracks end to end and plays Freedoom's MIDI music. `WadLibraryTests.cs` covers finding the games the player saved locally. `FaceTests.cs` checks the status-bar faces: the patch encoder against the engine's own reader, that all 42 face names exist in the real game data and the replacements have the original sizes, the health levels, looks and expressions, that the face shows in the running game and nowhere else on screen; it also checks the blood setting and that a painted face sheet reads back into exactly the faces it shows (and only the painted pixel changes); it writes `out/face-sheet.png` from a made-up portrait (the real characters' art is the game's and is not in the repo).

## Where the tests come from

`upstream/` is generated from `sinshu/managed-doom` (`ManagedDoomTest/src`, commit `9365696`) by `make-upstream-tests.py`; `data/` and `demos/` are its test levels and recorded demos (same commit,
GPLv2 like the rest of ManagedDoom). `MsTestShim.cs` stands in for MSTest, `Program.cs` is the runner, `WadPath.cs` replaces upstream's.

Upstream runs on the commercial DOOM.WAD and DOOM2.WAD. We use Freedoom instead, which is fine for tests that load their own test level on top of it, and the shareware DOOM1.WAD for the three upstream tests that replay DOOM's own demos on it, so only those are kept (100 of 163): the math tests, every test that passes a test `.wad` to `CreateDummy`, and the three shareware demos. The other 63 play the registered or DOOM II maps (they need DOOM.WAD, DOOM2.WAD, TNT.WAD or PLUTONIA.WAD), so they are dropped.
The engine's behaviour on the original episode-one levels is therefore covered by the three shareware demos (real DOOM's own fingerprints), plus its rules on the test levels (movement, monsters, weapons, doors, lifts, crushers, teleporters, level changes). The later episodes and DOOM II are not covered.
