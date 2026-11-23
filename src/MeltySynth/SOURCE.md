# MeltySynth (vendored)

- **Source:** https://github.com/sinshu/meltysynth, commit `4ba079c9ee1453b03be7e164d254f48426f0aac3`, folder `MeltySynth/src/` (the project's own `ProjectInfo.cs`, which only exposes internals to its test project, is left out).
- **What it is:** a SoundFont synthesizer and MIDI sequencer in plain C#. It plays DOOM's music (Freedoom's music tracks are standard MIDI files) with the soundfont in `assets/soundfont/`.
- **License:** MIT (`LICENSE.txt` here; it also carries the notice of the code MeltySynth derives from).
- **Changed:** two lines, so that it compiles with C# 14 (the .NET 10 SDK). C# 14 prefers `ReadOnlySpan` over `Span` when an array is passed to an overloaded method, so `MemoryMarshal.Cast(array)` returned a read-only span where the code writes to it or needs a writable one. Both fixed by writing `.AsSpan()` on the array, which means the same thing in every C# version: `ArrayMath.cs` line 19 (`destination.AsSpan()`) and `SoundFontSampleData.cs` line 40 (`samples.AsSpan()`). Everything else is untouched. Namespace stays `MeltySynth`.
- **Why vendored:** a mod can only ship its own dll, so a NuGet package would not arrive next to it. Updating = replace the `.cs` files from a newer upstream commit and update the commit above.
