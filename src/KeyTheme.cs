using System;
using System.Collections.Generic;
using System.IO;
using DoomedSpire.Host;
using Godot;
using ManagedDoom;

namespace DoomedSpire;

/// <summary>
/// Gives the original DOOM 1 games the Fool's melodies for their keycards (see KeycardTheme): builds the small add-on WAD with the melody symbols that replace the cards, the status bar
/// icons and the marker that changes the pickup messages. The symbols are the game's own pictures of them (res://images/packed/fool/symbol_*.png), read when the
/// game starts and never stored; both the card in the level and its status bar icon are made from them. Quick (a few small pictures), so it is made at every
/// start. If anything goes wrong the game starts with its own keycards.
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

			var symbols = new Dictionary<string, Rgba>();
			foreach (var symbolPath in KeycardTheme.SymbolPaths)
			{
				var picture = ReadGameSymbol(symbolPath);
				if (picture != null)
				{
					symbols[symbolPath] = picture;
				}
			}

			var lumps = KeycardTheme.Build(wad, symbols, text => ModLog.OnceInfo("keys", text, text));
			if (lumps == null)
			{
				ModLog.OnceInfo("keys", "no-keys", "this game does not have the keycard pictures, so it keeps its own keycards");
				return null;
			}
			Directory.CreateDirectory(folder);
			var path = Path.Combine(folder, "keys.wad");
			File.WriteAllBytes(path, PwadWriter.Build(lumps));
			ModLog.OnceInfo("keys", "built", $"the keycards are the melody cards (symbols from the game: {symbols.Count} of {KeycardTheme.SymbolPaths.Count})");
			return path;
		}
		catch (Exception e)
		{
			ModLog.Fail("keys", "making the melody cards", e);
			return null;
		}
	}

	/// <summary>One of the game's own pictures of a melody's symbol as plain pixels, or null (logged) if the game does not have it.</summary>
	private static Rgba? ReadGameSymbol(string path)
	{
		try
		{
			if (!ResourceLoader.Exists(path) || ResourceLoader.Load<Texture2D>(path) is not { } texture)
			{
				ModLog.OnceInfo("keys", "no-symbol-" + path, $"the game's picture {path} was not found, so that card keeps the game's own picture");
				return null;
			}
			return PlayerFace.ToPixels(texture.GetImage());
		}
		catch (Exception e)
		{
			ModLog.Fail("keys", $"reading the game's {path}", e);
			return null;
		}
	}
}
