using System;
using System.Collections.Generic;
using System.IO;
using DoomedSpire.Host;
using Godot;
using ManagedDoom;

namespace DoomedSpire;

/// <summary>
/// Gives the original DOOM 1 games the Fool's melodies for their keycards (see KeycardTheme): builds the small add-on WAD with the recoloured cards, the status bar
/// icons, the door strips and the marker that changes the pickup messages. The only art that comes from the mod is the three 7 x 5 status bar icons the owner
/// paints (assets/small_cho.png, small_ki.png and small_wah.png); until they exist the game's own icons stay. No game picture is stored: the rest is made
/// from the loaded game. Quick (a few small pictures), so it is made at every start. If anything goes wrong the game starts with its own keycards.
/// </summary>
internal static class KeyTheme
{
	/// <summary>Builds the add-on WAD into <paramref name="folder"/> and returns its path, or null (the log says why) to keep the game's own keycards.</summary>
	public static string? TryBuild(string iwadPath, string folder)
	{
		try
		{
			using var wad = new Wad(iwadPath);
			if (!MonsterSprites.IsOriginalDoom1(wad))
			{
				return null; // the same games as the enemies: the original DOOM 1 (Freedoom and DOOM II keep their keycards)
			}

			var art = new Dictionary<string, Rgba>();
			foreach (var file in KeycardTheme.HudFiles)
			{
				var picture = ReadEmbedded(file);
				if (picture != null)
				{
					art[file] = picture;
				}
			}

			var lumps = KeycardTheme.Build(wad, art, text => ModLog.OnceInfo("keys", text, text));
			if (lumps == null)
			{
				ModLog.OnceInfo("keys", "no-keys", "this game does not have the keycard pictures, so it keeps its own keycards");
				return null;
			}
			Directory.CreateDirectory(folder);
			var path = Path.Combine(folder, "keys.wad");
			File.WriteAllBytes(path, PwadWriter.Build(lumps));
			ModLog.OnceInfo("keys", "built", $"the keycards are the melody cards (status bar icons: {art.Count} of {KeycardTheme.HudFiles.Count} painted, the rest the game's own recoloured)");
			return path;
		}
		catch (Exception e)
		{
			ModLog.Fail("keys", "making the melody cards", e);
			return null;
		}
	}

	/// <summary>A picture built into the dll, or null if there is none or it cannot be read.</summary>
	private static Rgba? ReadEmbedded(string name)
	{
		try
		{
			using var stream = typeof(KeyTheme).Assembly.GetManifestResourceStream(name);
			if (stream == null)
			{
				return null;
			}
			using var buffer = new MemoryStream();
			stream.CopyTo(buffer);
			var image = new Image();
			return image.LoadPngFromBuffer(buffer.ToArray()) == Godot.Error.Ok ? PlayerFace.ToPixels(image) : null;
		}
		catch (Exception e)
		{
			ModLog.Fail("keys", $"reading {name}", e);
			return null;
		}
	}
}
