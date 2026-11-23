# DOOM on the Slay the Spire 2 map screen: plan

**Status: plan only. Nothing is built, nothing is compiled.** Written 2026-11-23 from a read of the three reference repos (commits below). Items marked **(decide)** still need the owner's answer (standard rule 2: ask, don't guess). The decisions already made are in section 9.

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
- Popup = a **Control panel** (dimmer + framed `TextureRect`) added to the map screen, **not** a real OS window. Reason: no focus, fullscreen or Steam Deck problems, and no second window to leak if the mod fails. A real OS window is possible later, but not recommended. **Decided: in-game panel.**
- Every handler is `try/catch`-wrapped and guarded with `IsInstanceValid` (standard §10). A DOOM bug must never break the run.
- Manifest: `affects_gameplay: false`, so multiplayer players do not have to match mods (primer §3). It runs locally only, nothing is networked. In multiplayer the map vote carries on while you play: the popup closes when the map screen does.

## 4. Pieces to build

1. **Engine core**: vendored ManagedDoom minus `src/Silk/`, retargeted to net9. Per the standard's shared-code rule, the copy's header names the source repo and commit, and the GPL license text ships with it.
2. **`GodotVideo : IVideo`**: ManagedDoom renders into a 320x200 palettized buffer. Convert it to RGBA each frame into a Godot `Image`/`ImageTexture` and draw it with nearest-neighbour filtering at 4:3. It also keeps DOOM's own frame interpolation, so it can run at the display's rate.
3. **Game loop**: DOOM ticks at 35 Hz. Drive it from the node's `_Process` with a time accumulator and render with the leftover fraction (`frameFrac`), pausing when the popup is closed.
4. **`GodotInput : IUserInput`**: the popup's `_Input` turns Godot key events into DOOM's ticcmd and calls `SetInputAsHandled` so the map screen and the game's hotkeys do not also react. Keyboard and **mouse** are in scope (mouse capture must always be released on close, in a `try/finally`). **Controller is later.**
5. **Sound** (in scope): DOOM's effects are 8-bit PCM in the WAD, so they can become Godot `AudioStreamWav` on a small pool of players.
6. **Music** (in scope, after sound): ManagedDoom uses MeltySynth (MIT, pure C#) plus a soundfont. In Godot it would feed an `AudioStreamGenerator`. The default soundfont is GPLv2 and about 6 MB, so this is the heaviest piece. The game's own music could be ducked while playing.
7. **Map screen hook**: **a button** near the drawing tools for now. A hotkey may come later; it must not collide with the game's input map, which has to be checked in `sts2src` before a key is chosen.
8. **STS enemies instead of DOOM's** (section 5).

## 5. WAD and the STS enemies

**WAD: decided (revised): the shareware DOOM1.WAD is built in, and the player can load their own games from a folder** (see decision 4 in section 9). It was Freedoom at first; the owner preferred the original, which is freely distributable as shareware. Freedoom still works as a saved local game.

**Enemies: decided, use Slay the Spire enemies instead of DOOM's.** What was found:

- Every STS2 enemy is a **Spine skeletal animation**, not a flat picture: `scenes/creature_visuals/*.tscn` has 130 scenes, 135 `SpineSprite` nodes, all built on `NCreatureVisuals` (`MegaCrit/Sts2/Core/Nodes/Combat/NCreatureVisuals.cs`, `SpineBody` / `SpineAnimation`). DOOM wants flat frames, so something has to turn animation into frames.
- DOOM's sprites are `Patch` objects (palettized columns) behind the `ISpriteLookup` interface (`ManagedDoom/src/Doom/Graphics/SpriteLookup.cs`, `Patch.cs`: public constructor, read from the WAD by sprite name like `TROOA1`). That is a clean seam: **wrap `ISpriteLookup` and hand back our own patches for the monster sprite names.** (Read, not built.)

**Approach: capture at runtime, never ship the art.**
1. On first open, for each mapped enemy, instantiate its visual scene in an off-screen transparent `SubViewport`, play its Spine animations (idle, attack, hurt, death) and snapshot a few frames per state into `Image`s.
2. Convert each image to a DOOM `Patch`: scale to the DOOM monster's size, quantize to DOOM's palette (so DOOM's lighting tables and fuzz effects keep working), set the offsets.
3. STS enemies face one way, so each frame is used for all 8 rotations (DOOM supports a single-angle sprite).
4. Cache the result in memory (and possibly in `user://<ModId>/`) so the capture happens once.

The game's art is read from the player's own installed game at runtime and is **never committed or uploaded** (standard §10: offline/art tools never commit the game's art). That also keeps the public GPL source free of MegaCrit's art.

Open points:
- **Mapping rule (decided): random, one act ("land") per level.** Each level picks one act, and every DOOM monster type in that level is assigned a random enemy **from that act only**, so Hive and Glory enemies never share a level. The act's enemy pool comes from the game itself, not from a list we keep: each act's `GenerateAllEncounters()` (`MegaCrit/Sts2/Core/Models/Acts/{Overgrowth,Underdocks,Hive,Glory}.cs`) names its encounters, and each encounter names its monsters (`index/catalog/encounters.md`). Pool sizes from a read of that data: **Overgrowth 29, Underdocks 22, Hive 26, Glory 23** distinct enemies, against about 20 DOOM monster types, so a level can nearly always give every monster type its own enemy. Not verified: that the encounter-to-monsters link can be read through the game's API at runtime (it may need an instance), and that every enemy in a pool has a usable visual scene.
- **Defaults I would use unless told otherwise** (each is a one-line change): the act for each level is itself random; the draw is seeded by the run seed plus the level, so the same level looks the same every time you open it in a run; each enemy is scaled to its DOOM monster's size, whatever the enemy's own size is. A possible extra, not planned: matching tiers (elites for the tough DOOM monsters, an act boss for the DOOM bosses).
- A quantized 256-colour version of STS art will look "retro" and may muddy. If it looks bad, the fallback is extending the renderer to draw RGBA sprites with a light tint. That is more work, so we try the palette route first.
- The shareware game has one episode (9 levels), so the act is chosen for each level (random), as decided; if the player saves a full DOOM, its four episodes would match the four acts too.
- Capturing from a viewport means a GPU read-back and the Spine animation names per enemy. Both are unverified until the game is running; step 5 below is where this gets proven.
- Projectiles (fireballs) and the player weapons are not enemies, so they stay Freedoom's unless the owner asks otherwise.

## 6. License **(decided: private until publishing)**

The repo stays **private** until the owner decides to publish. The facts to keep in mind for that day: `LICENSE.TXT` in id's repo is **GPLv2** (its README also says "released for your non-profit use"), and ManagedDoom is GPLv2. GPLv2 has no non-commercial restriction but is copyleft: once the dll is **distributed** (Workshop upload), the mod's complete source must be available under GPLv2, so the repo goes public. Whether a GPL plugin in a proprietary game is a "combined work" is a gray area; not legal advice, a short check is worth it before the first upload. The DOOM name belongs to ZeniMax/id, which matters for the Workshop title and picture (which the owner writes by hand, per the standard). The default music soundfont (TimGM6mb) is GPLv2 too.

## 7. Order of work, and how each step is checked

A cloud session cannot build against the game (no game DLLs, and `dotnet` is not even installed in this container), so each step says what can be tested where.

1. **Scaffold.** Owner runs `bash <modding-standards>/scripts/new-mod.sh dll <ModId> "<Display Name>"` (needs the owner's paths, standard §12), then pushes the repo.
2. **Engine core headless** (testable in a cloud session with the .NET 8 SDK, per `solutions/testing-without-the-game/`): vendor ManagedDoom, run the shareware WAD, tick a recorded demo with a null video and compare the result. ManagedDoom ships demo-based tests (`ManagedDoomTest/`) we can port. This proves the engine core before Godot is involved. It is the main risk reducer.
3. **Godot popup**: video and keyboard input, opened from a temporary hotkey. The owner builds and plays. This is the first time anything is compiled against the game.
4. **Map screen button**, open/close behaviour, closing on map close.
5. **STS enemies**: capture one enemy through the viewport and show it in DOOM as a proof, then map the rest (needs the game running).
6. **Sound**, then **music**, then **mouse**.
7. **Polish** only as asked: remember state between opens, volume, controller (later), a quit confirmation.

## 8. Risks

| Risk | Plan |
|---|---|
| ManagedDoom needs porting from net10 to net9 | Step 2 finds it in a cloud session, before the game is involved |
| Per-frame cost inside the game (software renderer on the main thread) | DOOM at 320x200 is tiny; measure in step 3; fall back to a fixed 35 fps render |
| Keys leak to the game, or DOOM eats keys when closed | Handle input only while open, always `SetInputAsHandled`, always release on close |
| A game update changes `NMapScreen` | Standard's game-update playbook (`docs/GAME_UPDATES.md`); patches target `_Ready`/`Close` only |
| Spine capture or palette quantization looks bad or is slow | Prove one enemy first (step 5); fall back to RGBA sprites, or fewer frames per enemy |
| License questions (section 6) | Decide before the first Workshop upload, not before building |

## 9. Decisions made (2026-11-23) and what is still open

Decided by the owner:
1. Display name **Doomed Spire**; mod id **`DoomedSpire`** (published ids never change).
2. Popup is an **in-game panel**.
3. A **button** on the map screen for now; a hotkey maybe later.
4. **The built-in game is the shareware DOOM1.WAD** (id's original, episode one: 9 levels, original graphics, sounds and MUS music), embedded unmodified in the dll and written to the mod's data folder the first time DOOM opens (assets/doom/README.md has its source, checksum and license). Freedoom is no longer shipped. **Local games:** any whole game (an IWAD) the player saves in the mod's `wads` folder (`user://DoomedSpire/wads/`, for example their registered DOOM.WAD, DOOM II or Freedoom) is offered in a short list when DOOM opens a new game; with none saved, DOOM just starts. The list is skipped when a paused game is resumed.
5. **Sound, music and mouse** now (all built in pieces 5, 8 and 9: sound effects and MIDI music mixed in the mod and played through one Godot audio stream; DOOM follows the game's own master, sound effects and music volume settings); **controller later**. Mouse, as built in piece 5: while DOOM is open it acts as if DOOM were fullscreen: the mouse is captured (locked to the game window, cursor hidden, unlimited movement), any sideways movement turns the player with no stop at a screen edge, and nothing else in the game can be clicked (the popup is on its own high canvas layer), left click fires and right click uses, from anywhere on the screen (Space also uses); WASD walks and strafes, the mouse never walks. Every key stays in DOOM; Esc leaves DOOM and returns to the map screen; the backtick key is DOOM's own Esc (its menu).
6. **Publish eventually**, everything public when the time comes; **private until then**.
7. **`main` only**: first commit on `main`, stay on `main`, the `claude/...` branch is to be deleted.
8b. **Old-monitor frame:** the game is drawn on the glass of a picture of an old CRT monitor (`assets/crt.png`, supplied by the owner), placed with absolute positions (no containers, so nothing reflows when the picker, error text or Close button changes). Its size, position and where the game sits on the glass are live-tunable with the VfxSpawner mod (group "Doomed Spire CRT frame"). Mouse turning starts at DOOM sensitivity 1 (the owner brought it down from 8 in steps), with the fractional part carried between tics so slow movement still turns.
8c. **The status-bar face is the player's STS character** (asked for before the enemies): made at run time from the character's own picture in the game (never stored in the mod), as 42 faces (5 health levels with paleness, redness and blood; looks; ouch; grin; rampage; god; dead), delivered as an add-on WAD that overrides Doomguy's lumps. Tunable (source picture, crop, damage strength, brightness) with VfxSpawner, applied at each new game; a preview sheet is written for looking at. The add-on-WAD route is how the STS enemy sprites can be delivered too.
8a. **Pause in place:** Esc does not end the game. It stays in memory, paused exactly where it was, and opening DOOM again carries on, through rooms and acts, until the run ends or the player quits from DOOM's own menu (then the next open starts a new game).
8. DOOM's enemies are replaced with **STS enemies**, **randomly, one act per level** (section 5).
9. The owner renames the repo to **`DoomedSpire`** (GitHub redirects the old name, but a session's access is scoped by repo name, so the next session re-attaches it by the new name).

Still open:
- Nothing blocks the first step. The defaults in section 5 stand until the owner changes them.

Next step (the owner, per standard §12, on their machine): `bash <modding-standards>/scripts/new-mod.sh dll DoomedSpire "Doomed Spire"`.

## Sources

- `epilimic/modding-standards` (standard, `CLAUDE.md`, `SESSION_SETUP.md`, templates, `solutions/`)
- `epilimic/sts2src` `daf0fdb`: `CLAUDE.md`, `docs/MODDING_PRIMER.md`, `NMapScreen.cs`
- `id-Software/DOOM` `a77dfb9`: `README.TXT`, `LICENSE.TXT`
- `sinshu/managed-doom` `9365696`: `README.md`, `licenses/`, `ManagedDoom/src/`
