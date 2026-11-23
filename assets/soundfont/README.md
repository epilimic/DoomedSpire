# TimGM6mb soundfont (embedded in the mod's dll)

- **What:** `TimGM6mb.sf2`, a small General MIDI SoundFont (about 6 MB) by Tim Brechbill, with additions by David Bolton. It is the soundfont the ManagedDoom engine itself uses for DOOM's music.
- **Where it came from:** the Ubuntu 24.04 package `timgm6mb-soundfont` 1.3-5 (`/usr/share/sounds/sf2/TimGM6mb.sf2`). SHA-256 `c5378b62028c920cb11e4803327983fee2f2cdff5dc89c708e39da417e51c854`, 5,969,788 bytes. Upstream: https://github.com/musescore/musescore-old (the commit named in `LICENSE.txt`).
- **License:** GPL-2 (`LICENSE.txt` is the package's copyright file, which also records where its samples come from; it is embedded in the dll too). GPL-2 is the license this mod is going to be published under, see `docs/PLAN.md`.
- **How it is used:** read straight from the dll's embedded copy by the synthesizer (`src/Host/EngineMusic.cs`); nothing is written to disk.
