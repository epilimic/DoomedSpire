# DOOM on the Slay the Spire 2 map screen: plan

**Status: plan only. Nothing is built, nothing is compiled.** Written 2026-11-23 from a read of the three reference repos (commits below). Items marked **(decide)** need the owner's answer before work starts (standard rule 2: ask, don't guess).

## 1. The idea

On the map screen, press a button and a popup opens in which DOOM runs. Close it and you are back on the map. The mod is a **dll-only** mod (template `templates/dll-mod/` in `epilimic/modding-standards`): no `.pck`, no loose files.

## 2. The central decision: which DOOM

`id-Software/DOOM` (the repo you named, commit `a77dfb9`) is the original 1997 `linuxdoom-1.10` C source. It is the right reference but it is not directly usable in a C# mod:

- It only builds on Linux and talks to X11 and a separate sound-server process (`i_video.c`, `i_sound.c`, `sndserv/`). Those files would all be rewritten anyway.
- A mod can only ship a dll, a manifest and a `.pck` to the Workshop. A native `.so`/`.dll` (and one per OS: your Windows laptop is a second test machine) cannot ride along safely.

| Option | What it is | Verdict |
|---|---|---|
| **A. ManagedDoom** (`sinshu/managed-doom`, commit `9365696`) | A faithful C# port of that same linuxdoom source, GPLv2, about 51k lines | **Recommended.** Pure C#, goes inside the dll |
| B. Native build (e.g. doomgeneric) loaded by P/Invoke | C engine compiled per OS | Rejected: native binaries per OS, extracted at runtime, fragile on the Workshop |
| C. Port `linuxdoom-1.10` to C# ourselves | What ManagedDoom already is | Rejected: months of work to reproduce an existing port |

What was **verified** in ManagedDoom (read, not built): the engine talks to the outside world through four small interfaces, `IVideo`, `IUserInput`, `ISound`, `IMusic` (`ManagedDoom/src/Video`, `UserInput`, `Audio`). Everything Silk.NET/OpenGL/OpenAL specific lives in one folder, `src/Silk/`, plus the three NuGet packages in its `.csproj`. So the plan is: **keep the engine, drop `src/Silk/`, write our own Godot-side implementations of those interfaces.**
Not verified: it targets `net10.0` and the game is `net9.0` (a C# 14 feature or new API may need a small fix when we first build); it uses `unsafe` in one place (`AllowUnsafeBlocks` needed).

## 3. How it fits the game

All from `epilimic/sts2src` (commit `daf0fdb`, game v0.113.1):

- The map screen is `NMapScreen` (`MegaCrit/Sts2/Core/Nodes/Screens/Map/NMapScreen.cs:41`, scene `scenes/screens/map/map_screen.tscn`). It already has a `%DrawingTools` control and a legend; `_Ready` is at `:667`, `Open` at `:1502`, `Close` at `:1449`, `_Input` at `:1642`.
- Primer §5 gives the pattern: a Harmony **postfix on `_Ready`** that adds our own child node. A postfix on `Close` closes the popup too, so you can never be left inside DOOM while the game travels to the next room.
- Popup = a **Control panel** (dimmer + framed `TextureRect`) added to the map screen, **not** a real OS window. Reason: no focus, fullscreen or Steam Deck problems, and no second window to leak if the mod fails. A real OS window is possible later, but not recommended. **(decide)**
- Every handler is `try/catch`-wrapped and guarded with `IsInstanceValid` (standard §10). A DOOM bug must never break the run.
- Manifest: `affects_gameplay: false`, so multiplayer players do not have to match mods (primer §3). It runs locally only, nothing is networked. In multiplayer the map vote carries on while you play: the popup closes when the map screen does.

## 4. Pieces to build

1. **Engine core**: vendored ManagedDoom minus `src/Silk/`, retargeted to net9. Per the standard's shared-code rule, the copy's header names the source repo and commit, and the GPL license text ships with it.
2. **`GodotVideo : IVideo`**: ManagedDoom renders into a 320x200 palettized buffer. Convert it to RGBA each frame into a Godot `Image`/`ImageTexture` and draw it with nearest-neighbour filtering at 4:3. It also keeps DOOM's own frame interpolation, so it can run at the display's rate.
3. **Game loop**: DOOM ticks at 35 Hz. Drive it from the node's `_Process` with a time accumulator and render with the leftover fraction (`frameFrac`), pausing when the popup is closed.
4. **`GodotInput : IUserInput`**: the popup's `_Input` turns Godot key events into DOOM's ticcmd and calls `SetInputAsHandled` so the map screen and the game's hotkeys do not also react. Keyboard first. Mouse capture and controller are later and optional, because mouse capture must always be released on close (try/finally).
5. **Sound** (second step): DOOM's effects are 8-bit PCM in the WAD, so they can become Godot `AudioStreamWav` on a small pool of players.
6. **Music** (third step, optional): ManagedDoom uses MeltySynth (MIT, pure C#) plus a soundfont. In Godot it would feed an `AudioStreamGenerator`. The default soundfont is GPLv2 and about 6 MB, so this is the heaviest piece. The game's own music could be ducked while playing. **(decide whether wanted)**
7. **Map screen hook**: a button near the drawing tools and/or a hotkey. A key must not collide with the game's input map, which has to be checked in `sts2src` before a key is chosen. **(decide: button, hotkey, or both)**

## 5. WAD (the game data) **(decide)**

DOOM needs a `.wad`. The source release says you "still need real DOOM data". Options:

| Option | Notes |
|---|---|
| Bundle `DOOM1.WAD` (shareware, episode 1) as an embedded dll resource (`solutions/embedded-art/` pattern, about 4 MB) | Everyone gets a working game. I believe shareware redistribution is permitted by id's shareware terms, but that is from memory: **verify before uploading** |
| Bundle **Freedoom** (BSD-style, made to be freely redistributable) | The cleanest legally. Different levels and art from real DOOM |
| Player supplies their own `.wad` (looked up in `user://<ModId>/`, as the standard's log/data folder rule) | No legal question; every player has to set it up; can also support DOOM II / registered |

A reasonable combination: bundle one free WAD, and prefer the player's own if one is present.

## 6. License: this needs your attention **(decide)**

You said it is free for non-commercial use. The repo is actually broader and stricter in a different way: `LICENSE.TXT` is **GPLv2**, and the README in the repo says "released for your non-profit use". ManagedDoom is GPLv2 too. GPLv2 has no non-commercial restriction, but it is copyleft:

- If the dll is **distributed** (Workshop upload), the complete source of the mod must be available under GPLv2 too. That means this mod's repo (currently private) would have to be public, or the source offered with the Workshop item.
- Whether a GPL plugin loaded into a proprietary game is a "combined work" is a gray area. I am not a lawyer; if you plan to publish on the Workshop, it is worth a short check.
- The DOOM name and art belong to ZeniMax/id. That matters for the Workshop title and picture (which you write by hand, per the standard).

## 7. Order of work, and how each step is checked

A cloud session cannot build against the game (no game DLLs, and `dotnet` is not even installed in this container), so each step says what can be tested where.

1. **Scaffold.** Owner runs `bash <modding-standards>/scripts/new-mod.sh dll <ModId> "<Display Name>"` (needs the owner's paths, standard §12), then pushes the repo.
2. **Engine core headless** (testable in a cloud session with the .NET 8 SDK, per `solutions/testing-without-the-game/`): vendor ManagedDoom, run the shareware WAD, tick a recorded demo with a null video and compare the result. ManagedDoom ships demo-based tests (`ManagedDoomTest/`) we can port. This proves the engine core before Godot is involved. It is the main risk reducer.
3. **Godot popup**: video and keyboard input, opened from a temporary hotkey. The owner builds and plays. This is the first time anything is compiled against the game.
4. **Map screen button**, open/close behaviour, closing on map close.
5. **Sound**, then **music** if wanted.
6. **Polish** only as asked: remember state between opens, volume, controller, a quit confirmation.

## 8. Risks

| Risk | Plan |
|---|---|
| ManagedDoom needs porting from net10 to net9 | Step 2 finds it in a cloud session, before the game is involved |
| Per-frame cost inside the game (software renderer on the main thread) | DOOM at 320x200 is tiny; measure in step 3; fall back to a fixed 35 fps render |
| Keys leak to the game, or DOOM eats keys when closed | Handle input only while open, always `SetInputAsHandled`, always release on close |
| A game update changes `NMapScreen` | Standard's game-update playbook (`docs/GAME_UPDATES.md`); patches target `_Ready`/`Close` only |
| License and WAD questions (sections 5 and 6) | Decide before the first Workshop upload, not before building |

## 9. Questions to answer tomorrow

1. Mod id and display name (your ids use underscores, e.g. `Doom_Spire`)?
2. Popup as an in-game panel (recommended) or a real OS window?
3. Open it with an on-screen button, a hotkey, or both?
4. WAD: bundled shareware, bundled Freedoom, player's own, or a mix?
5. Wanted: sound, music, mouse, controller? (Sound is cheap, music is the heavy one.)
6. Publish to the Workshop (so GPL source must be public) or keep it for yourself?
7. Process: the standard says work on `main`. This repo is empty, and the session was told to use `claude/elegant-wright-y05l6m`. Should the first commit go to `main`?

## Sources

- `epilimic/modding-standards` (standard, `CLAUDE.md`, `SESSION_SETUP.md`, templates, `solutions/`)
- `epilimic/sts2src` `daf0fdb`: `CLAUDE.md`, `docs/MODDING_PRIMER.md`, `NMapScreen.cs`
- `id-Software/DOOM` `a77dfb9`: `README.TXT`, `LICENSE.TXT`
- `sinshu/managed-doom` `9365696`: `README.md`, `licenses/`, `ManagedDoom/src/`
