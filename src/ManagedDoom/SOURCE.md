# ManagedDoom (vendored engine)

- **Source:** https://github.com/sinshu/managed-doom, commit `9365696eb44326a3aab72c4bab217f7db8a87c96` (2025-11-24), folder `ManagedDoom/src/`.
  ManagedDoom is a C# port of id Software's linuxdoom-1.10 (https://github.com/id-Software/DOOM, GPLv2).
- **License:** GNU General Public License v2 (`LICENSE.txt` here). Every file keeps its original copyright header. The mod is distributed under the same license when it is published.
- **What was taken:** every `.cs` file except the `Silk/` folder (the Silk.NET window, OpenGL, OpenAL and MIDI code). The engine talks to the outside only through
  `IVideo`, `IUserInput`, `ISound` and `IMusic`; the mod provides those itself. NuGet packages are not needed.
- **What was changed:** nothing in the `.cs` files. Added `.editorconfig` (silences the nullable-annotation warnings the engine produces when the mod's nullable checking is on)
  and this file. Namespaces stay `ManagedDoom.*` so a newer upstream copy can replace the folder directly.
- **Updating:** replace the `.cs` files with the same folder from a newer upstream commit (without `Silk/`), update the commit above, and rebuild.
- Upstream targets net10.0; the mod targets net9.0. Compiled against net8.0 in a cloud session (0 errors, 0 warnings); the owner's net9.0 build is the real check.
