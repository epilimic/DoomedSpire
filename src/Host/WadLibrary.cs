#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DoomedSpire.Host;

/// <summary>A game the player saved locally: its file name (what the picker shows), where it is, and how big.</summary>
internal sealed record WadFile(string Name, string Path, long Size);

/// <summary>
/// Finds the DOOM games the player has saved in the mod's folder for them. A game is a "WAD" file whose first four bytes say IWAD (an "internal" WAD:
/// a whole game with its own levels, graphics and sounds). Add-on WADs (PWAD) only change or extend a game and cannot be played alone, so they are left
/// out and reported. Godot-free (the tests use it).
/// </summary>
internal static class WadLibrary
{
	/// <summary>True for the full DOOM, a file called <c>doom.wad</c> (any case).</summary>
	public static bool IsFullDoom(WadFile wad) => string.Equals(wad.Name, "doom.wad", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// What the game picker lists, in order: the built-in shareware game (a null file, shown as <paramref name="builtInName"/>) and then each saved game by its file name.
	/// The shareware game is left out when the full DOOM is among the saved games.
	/// </summary>
	public static IReadOnlyList<(string Text, WadFile? Wad)> PickerChoices(IReadOnlyList<WadFile> saved, string builtInName)
	{
		var choices = new List<(string, WadFile?)>();
		if (!saved.Any(IsFullDoom))
		{
			choices.Add((builtInName, null));
		}
		choices.AddRange(saved.Select(w => (w.Name, (WadFile?)w)));
		return choices;
	}

	/// <summary>The games in <paramref name="folder"/> (not its subfolders), in alphabetical order. A folder that does not exist has none.</summary>
	/// <param name="report">Told about files that are skipped and why (not a game, unreadable).</param>
	public static IReadOnlyList<WadFile> FindGames(string folder, Action<string>? report = null)
	{
		if (!Directory.Exists(folder))
		{
			return Array.Empty<WadFile>();
		}

		var found = new List<WadFile>();
		foreach (var path in Directory.EnumerateFiles(folder).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
		{
			if (!path.EndsWith(".wad", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var name = Path.GetFileName(path);
			try
			{
				if (!IsGame(path))
				{
					report?.Invoke($"{name} is not a whole game (an add-on WAD needs a base game, and cannot be played alone); it is left out of the list");
					continue;
				}
				found.Add(new WadFile(name, path, new FileInfo(path).Length));
			}
			catch (Exception e)
			{
				report?.Invoke($"could not read {name}: {e.GetType().Name}: {e.Message}");
			}
		}
		return found;
	}

	/// <summary>True if the file starts with the four letters IWAD.</summary>
	public static bool IsGame(string path)
	{
		using var stream = File.OpenRead(path);
		Span<byte> header = stackalloc byte[4];
		return stream.Read(header) == 4 && header[0] == 'I' && header[1] == 'W' && header[2] == 'A' && header[3] == 'D';
	}
}
