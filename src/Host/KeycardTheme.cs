#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ManagedDoom;

namespace DoomedSpire.Host;

/// <summary>
/// DOOM's three keycards become the Fool's three melodies: the blue card is the blue Cho Melody Card, the yellow one the yellow Ki Melody Card and the red one
/// the red Wah Melody Card (the skull keys stay as they are). What changes: the card lying in the level is replaced by the melody's own symbol, the game's picture
/// of it (<c>images/packed/fool/symbol_cho.png</c>, <c>symbol_ki.png</c> and <c>symbol_wah.png</c>), made small and put in DOOM's colours at run time (nothing of
/// it is stored; nothing is drawn by the mod); the card's small icon on the status bar (a picture the owner paints, <c>small_cho.png</c>, <c>small_ki.png</c> and
/// <c>small_wah.png</c>; until there is one the game's own icon stays) and the words in the pickup and "you need" messages. A card whose symbol picture is not
/// there keeps the game's own card. DOOM's palette has no cyan or pink, so Cho comes out light blue and Wah coral pink.
/// Godot-free (the tests use it).
/// </summary>
internal static class KeycardTheme
{
	/// <summary>The lump that says a game has been given the melody cards, so that <see cref="ApplyTexts"/> changes the words only then.</summary>
	public const string Marker = "DSKEYS";

	private sealed record Key(string Colour, string Melody, char Letter, string Hud, (int R, int G, int B) Ink, string SymbolPath, string Pickup, string NeedDoor, string NeedObject, string HudFile);

	// The status bar's own order of the three cards: 0 blue, 1 yellow, 2 red (STKEYS0 to STKEYS2). The ink is the melody's colour: it is used when the game's symbol
	// picture is white or grey (a picture meant to be tinted), so the symbol takes the melody's colour.
	private static readonly Key[] Keys =
	{
		new("Blue", "Cho", 'B', "STKEYS0", (0, 244, 248), "res://images/packed/fool/symbol_cho.png", "GOTBLUECARD", "PD_BLUEK", "PD_BLUEO", "small_cho.png"),
		new("Yellow", "Ki", 'Y', "STKEYS1", (248, 248, 0), "res://images/packed/fool/symbol_ki.png", "GOTYELWCARD", "PD_YELLOWK", "PD_YELLOWO", "small_ki.png"),
		new("Red", "Wah", 'R', "STKEYS2", (248, 80, 148), "res://images/packed/fool/symbol_wah.png", "GOTREDCARD", "PD_REDK", "PD_REDO", "small_wah.png"),
	};

	/// <summary>The game's pictures of the three symbols (resource paths in the game), which <see cref="Build"/> takes in <c>symbolArt</c>.</summary>
	public static IReadOnlyList<string> SymbolPaths => Keys.Select(k => k.SymbolPath).ToList();

	/// <summary>The largest side of a symbol in the level, in DOOM pixels (the card was 14 x 16).</summary>
	public const int SymbolSize = 20;

	/// <summary>The names of the status-bar pictures the owner paints (7 x 5 pixels, a transparent background), by the resource name inside the mod.</summary>
	public static IReadOnlyList<string> HudFiles => Keys.Select(k => k.HudFile).ToList();

	/// <summary>
	/// The add-on lumps: the cards in the level (sprite lumps, between S_START and S_END), each made from the game's symbol picture in <paramref name="symbolArt"/> (by the path in <see cref="SymbolPaths"/>; a card without one is left as the game has it), a status-bar icon for each card the owner has painted one for, and the
	/// marker. <paramref name="hudArt"/> maps a resource name from <see cref="HudFiles"/> to the owner's picture; one that is missing leaves the game's own icon, and
	/// one that is not the icon's size (7 x 5) is reported and left out. Null if the game does not have the pictures this needs.
	/// </summary>
	public static IReadOnlyList<(string Name, byte[] Data)>? Build(Wad wad, IReadOnlyDictionary<string, Rgba>? hudArt = null, Action<string>? report = null, IReadOnlyDictionary<string, Rgba>? symbolArt = null)
	{
		if (wad.GetLumpNumber("PLAYPAL") == -1 || Keys.Any(k => wad.GetLumpNumber(k.Letter + "KEYA0") == -1 || wad.GetLumpNumber(k.Hud) == -1))
		{
			return null;
		}
		var palette = new DoomPalette(wad.ReadLump("PLAYPAL"));
		var sprites = new List<(string, byte[])>();
		var icons = new List<(string, byte[])>();

		foreach (var key in Keys)
		{
			// the card in the level is replaced by the melody's symbol: two frames (DOOM flashes its items between them), the second lighter
			if (symbolArt != null && symbolArt.TryGetValue(key.SymbolPath, out var symbol))
			{
				var made = new List<(string, byte[])>();
				foreach (var (name, flash) in new[] { (key.Letter + "KEYA0", false), (key.Letter + "KEYB0", true) })
				{
					var frame = Shrink(symbol, key.Ink, flash, palette);
					if (frame == null)
					{
						report?.Invoke($"{key.SymbolPath} has nothing to show, so the game's own card is kept");
						made.Clear();
						break;
					}
					// hung the way the card was: its middle over the item's place, and 3 pixels above the floor
					made.Add((name, DoomPatch.Encode(frame.Value.Width, frame.Value.Height, frame.Value.Width / 2, frame.Value.Height + 3, frame.Value.Pixels)));
				}
				sprites.AddRange(made);
			}

			// the card's icon on the status bar, if the owner has painted one
			if (hudArt != null && hudArt.TryGetValue(key.HudFile, out var art))
			{
				var hud = DoomPatch.Decode(wad.ReadLump(key.Hud));
				if (art.Width == hud.Width && art.Height == hud.Height)
				{
					var pixels = Enumerable.Range(0, art.Width * art.Height)
						.Select(i => art.Data[4 * i + 3] < 128 ? -1 : ToPalette(palette, art.Data[4 * i], art.Data[4 * i + 1], art.Data[4 * i + 2])).ToArray();
					icons.Add((key.Hud, DoomPatch.Encode(hud.Width, hud.Height, hud.Left, hud.Top, pixels)));
				}
				else
				{
					report?.Invoke($"{key.HudFile} is {art.Width} x {art.Height}; the status bar icon must be {hud.Width} x {hud.Height}, so the game's own icon is kept");
				}
			}
		}

		var lumps = new List<(string, byte[])> { ("S_START", Array.Empty<byte>()) };
		lumps.AddRange(sprites);
		lumps.Add(("S_END", Array.Empty<byte>()));
		lumps.AddRange(icons);
		lumps.Add((Marker, new byte[] { 1 }));
		return lumps;
	}

	/// <summary>
	/// Changes the pickup and "you need" messages of the three cards to name the melody cards, if the game was given them (it has the marker lump). The engine's texts
	/// are changed in place and put back by <see cref="EngineBaseline"/> before the next game. The skull keys' messages are not touched.
	/// </summary>
	public static void ApplyTexts(Wad wad)
	{
		if (wad.GetLumpNumber(Marker) == -1)
		{
			return;
		}
		foreach (var key in Keys)
		{
			var card = $"{key.Colour} {key.Melody} Melody Card";
			DoomString.ReplaceByName(key.Pickup, $"Picked up a {card}.");
			DoomString.ReplaceByName(key.NeedDoor, $"You need a {card} to open this door");
			DoomString.ReplaceByName(key.NeedObject, $"You need a {card} to activate this object");
		}
	}

	/// <summary>
	/// The palette index for a colour of the owner's status-bar icon. DOOM's palette has no cyan (the nearest colour to Cho's cyan is a light green), so a cyan is
	/// shown as the light blue it is closest to, keeping its brightness; every other colour is simply the nearest the palette has.
	/// </summary>
	private static readonly (int R, int G, int B)[] BlueRamp =
	{
		(171, 171, 255), (143, 143, 255), (115, 115, 255), (83, 83, 255), (55, 55, 255), (27, 27, 255), (0, 0, 255), (0, 0, 203), (0, 0, 155), (0, 0, 107),
	};

	private static int ToPalette(DoomPalette palette, int r, int g, int b)
	{
		if (g >= r + 60 && b >= r + 60 && Math.Abs(g - b) <= 60)
		{
			var v = Math.Max(g, b) / 255.0; // how bright the cyan is: 1 is the lightest blue of the ramp, 0 the darkest
			var step = Math.Clamp((int)Math.Round((1 - v) * (BlueRamp.Length - 1)), 0, BlueRamp.Length - 1);
			return palette.Nearest(BlueRamp[step].R, BlueRamp[step].G, BlueRamp[step].B);
		}
		return palette.Nearest(r, g, b);
	}

	/// <summary>
	/// The game's symbol picture made into a DOOM sprite: cropped to what is drawn, scaled down so its longer side is <see cref="SymbolSize"/> (averaging, so thin
	/// lines stay), a pixel being drawn where at least half of its square of the picture is. Its colours are the picture's own; a picture that is white or grey is
	/// tinted with the melody's colour by its brightness. The lighter frame (<paramref name="flash"/>) mixes a third of white in. Null if nothing in the picture is drawn.
	/// </summary>
	internal static (int Width, int Height, int[] Pixels)? Shrink(Rgba art, (int R, int G, int B) ink, bool flash, DoomPalette palette)
	{
		int x0 = art.Width, y0 = art.Height, x1 = -1, y1 = -1;
		for (var y = 0; y < art.Height; y++)
		{
			for (var x = 0; x < art.Width; x++)
			{
				if (art.Data[4 * (y * art.Width + x) + 3] >= 128)
				{
					x0 = Math.Min(x0, x);
					x1 = Math.Max(x1, x);
					y0 = Math.Min(y0, y);
					y1 = Math.Max(y1, y);
				}
			}
		}
		if (x1 < 0)
		{
			return null;
		}
		double cw = x1 - x0 + 1, ch = y1 - y0 + 1, scale = SymbolSize / Math.Max(cw, ch);
		int w = Math.Max(1, (int)Math.Round(cw * scale)), h = Math.Max(1, (int)Math.Round(ch * scale));

		// the picture's own colours, or the melody's if the picture is white or grey
		double maxLight = 1;
		var grey = true;
		for (var i = 0; i < art.Width * art.Height; i++)
		{
			if (art.Data[4 * i + 3] < 128)
			{
				continue;
			}
			int r = art.Data[4 * i], g = art.Data[4 * i + 1], b = art.Data[4 * i + 2];
			if (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) > 40)
			{
				grey = false;
			}
			maxLight = Math.Max(maxLight, Math.Max(r, Math.Max(g, b)));
		}

		var pixels = new int[w * h];
		for (var py = 0; py < h; py++)
		{
			for (var px = 0; px < w; px++)
			{
				// the square of the picture this pixel covers
				double sx0 = x0 + px / scale, sx1 = x0 + (px + 1) / scale, sy0 = y0 + py / scale, sy1 = y0 + (py + 1) / scale;
				double cover = 0, area = 0, r = 0, g = 0, b = 0;
				for (var sy = (int)Math.Floor(sy0); sy < Math.Ceiling(sy1) && sy < art.Height; sy++)
				{
					for (var sx = (int)Math.Floor(sx0); sx < Math.Ceiling(sx1) && sx < art.Width; sx++)
					{
						var weight = (Math.Min(sx + 1, sx1) - Math.Max(sx, sx0)) * (Math.Min(sy + 1, sy1) - Math.Max(sy, sy0));
						var o = 4 * (sy * art.Width + sx);
						var a = art.Data[o + 3] / 255.0;
						area += weight;
						cover += weight * a;
						r += weight * a * art.Data[o];
						g += weight * a * art.Data[o + 1];
						b += weight * a * art.Data[o + 2];
					}
				}
				if (area <= 0 || cover < area / 2 || cover <= 0)
				{
					pixels[py * w + px] = -1;
					continue;
				}
				r /= cover;
				g /= cover;
				b /= cover;
				if (grey)
				{
					var light = Math.Max(r, Math.Max(g, b)) / maxLight; // 1 is the picture's brightest
					r = ink.R * light;
					g = ink.G * light;
					b = ink.B * light;
				}
				var index = ToPalette(palette, (int)Math.Round(r), (int)Math.Round(g), (int)Math.Round(b));
				if (flash)
				{
					// lighter: the colour the palette gave (light blue for a cyan), a third of the way to white
					var c = palette.Colour(index);
					index = palette.Nearest(c.R + (255 - c.R) / 3.0, c.G + (255 - c.G) / 3.0, c.B + (255 - c.B) / 3.0);
				}
				pixels[py * w + px] = index;
			}
		}
		return (w, h, pixels);
	}
}
