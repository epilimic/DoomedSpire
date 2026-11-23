# Engine tests

Tests for the vendored DOOM engine (`src/ManagedDoom`), run headless: no game, no Godot, no screen. They play recorded demos on tiny test levels and compare a hash of every
monster, item and projectile on every tic with the value real DOOM produces, so a single wrong tic anywhere fails the test.

```bash
dotnet run --project tests/Engine.Tests -c Release        # all tests
dotnet run --project tests/Engine.Tests -c Release -- Monsters   # only names containing "Monsters"; add --verbose to list passes too
```

Needs `freedoom2.wad` and `freedoom1.wad` from Freedoom, which are **not in this repo** (about 29 MB each). Get them from the Freedoom release page (https://github.com/freedoom/freedoom/releases, the zip holds both) or your
distribution's package (Debian/Ubuntu: `freedoom`; on Arch/CachyOS I believe `freedoom1` and `freedoom2`, not checked), then either put the two files in `tests/Engine.Tests/wads/` (git-ignored) or set `FREEDOOM_DIR` to their folder.
The packages' usual folders (`/usr/share/games/doom`, `/usr/share/doom`) are found without any setting. A test whose WAD is missing is reported as SKIP, never as a pass.
The project is built for .NET 8 and is set to run on any newer runtime.

**Last result (Freedoom 0.13.0, .NET 8): 113 passed, 0 failed, 0 skipped, 3 seconds.** Checked that it can fail: making the fixed-point multiply off by one makes 40 of the 97 fail.

`FrameTests.cs` is our own (not upstream): it runs the engine glue in `src/Host` on Freedoom: renders a frame, walks, turns, shoots, opens the menu, quits, and turns keyboard and mouse input into the player's commands. It also saves the picture to `tests/Engine.Tests/out/first-frame.png` (git-ignored), so a person can look at it.

## Where the tests come from

`upstream/` is generated from `sinshu/managed-doom` (`ManagedDoomTest/src`, commit `9365696`) by `make-upstream-tests.py`; `data/` and `demos/` are its test levels and recorded demos (same commit,
GPLv2 like the rest of ManagedDoom). `MsTestShim.cs` stands in for MSTest, `Program.cs` is the runner, `WadPath.cs` replaces upstream's.

Upstream runs on the commercial DOOM.WAD and DOOM2.WAD. We use Freedoom instead, which is fine for tests that load their own test level on top of it, so only those are kept (97 of 163): the math tests and
every test that passes a test `.wad` to `CreateDummy`. The 66 that play the original maps (IwadDemo, E1M1 comparisons, ...) are dropped, because their expected values only hold for the real WADs.
That also means **the engine's behaviour on the original DOOM levels is not covered here**, only its rules (movement, monsters, weapons, doors, lifts, crushers, teleporters, level changes).
