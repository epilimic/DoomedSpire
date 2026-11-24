#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ManagedDoom;

namespace DoomedSpire.Host;

/// <summary>
/// DOOM's three keycards become the Fool's three melodies: the blue card is the blue Cho Melody Card, the yellow one the yellow Ki Melody Card and the red one
/// the red Wah Melody Card (the skull keys stay as they are). What changes: the card lying in the level gets the melody's symbol on its face (the game's own picture
/// with the symbol drawn over it: DOOM's palette has no cyan or pink, so the cards keep the game's blue, yellow and red), the card's small icon on the status bar
/// (a picture the owner paints, <c>small_cho.png</c>, <c>small_ki.png</c> and <c>small_wah.png</c>; until there is one the game's own icon stays) and the words in the pickup and "you need" messages. Nothing
/// of the game's art is stored in the mod: the card is the loaded game's, and only the symbols (a few pixels each) are drawn here. Godot-free (the tests use it).
/// </summary>
internal static class KeycardTheme
{
	/// <summary>The lump that says a game has been given the melody cards, so that <see cref="ApplyTexts"/> changes the words only then.</summary>
	public const string Marker = "DSKEYS";

	private sealed record Key(string Colour, string Melody, char Letter, string Hud, (int R, int G, int B) Ink, string[] Symbol, string Pickup, string NeedDoor, string NeedObject, string HudFile);

	// The symbols are drawn here, a few pixels each, in the spirit of the melodies' icons in the game (a zigzag, two linked rings, a cross).
	private static readonly string[] Rings =
	{
		".XX....",
		"X..X...",
		"X..XXX.",
		".XXX..X",
		"...X..X",
		"....XX.",
	};

	private static readonly string[] Plus =
	{
		"...X...",
		"...X...",
		"...X...",
		"XXX.XXX",
		"...X...",
		"...X...",
		"...X...",
	};

	private static readonly string[] Zigzag =
	{
		"X.....X",
		"X..X..X",
		"X.X.X.X",
		"XX...XX",
	};

	// The status bar's own order of the three cards: 0 blue, 1 yellow, 2 red (STKEYS0 to STKEYS2). The ink is the symbol's colour: as near as DOOM's palette comes to
	// the melody's own light colour (Cho's cyan has no match, so it is a light blue; Ki is yellow; Wah's pink comes out a light pink-red).
	private static readonly Key[] Keys =
	{
		new("Blue", "Cho", 'B', "STKEYS0", (150, 200, 255), Rings, "GOTBLUECARD", "PD_BLUEK", "PD_BLUEO", "small_cho.png"),
		new("Yellow", "Ki", 'Y', "STKEYS1", (255, 255, 90), Plus, "GOTYELWCARD", "PD_YELLOWK", "PD_YELLOWO", "small_ki.png"),
		new("Red", "Wah", 'R', "STKEYS2", (255, 150, 190), Zigzag, "GOTREDCARD", "PD_REDK", "PD_REDO", "small_wah.png"),
	};

	/// <summary>The names of the status-bar pictures the owner paints (7 x 5 pixels, a transparent background), by the resource name inside the mod.</summary>
	public static IReadOnlyList<string> HudFiles => Keys.Select(k => k.HudFile).ToList();

	/// <summary>
	/// The add-on lumps: the three cards in the level (sprite lumps, between S_START and S_END), a status-bar icon for each card the owner has painted one for, and the
	/// marker. <paramref name="hudArt"/> maps a resource name from <see cref="HudFiles"/> to the owner's picture; one that is missing leaves the game's own icon, and
	/// one that is not the icon's size (7 x 5) is reported and left out. Null if the game does not have the pictures this needs.
	/// </summary>
	public static IReadOnlyList<(string Name, byte[] Data)>? Build(Wad wad, IReadOnlyDictionary<string, Rgba>? hudArt = null, Action<string>? report = null)
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
			// the card in the level: two frames that differ only in the three little lights on its edge (off, then on); the symbol goes on the face of both
			var a = DoomPatch.Decode(wad.ReadLump(key.Letter + "KEYA0"));
			var b = DoomPatch.Decode(wad.ReadLump(key.Letter + "KEYB0"));
			var lights = Enumerable.Range(0, a.Pixels.Length).Where(i => a.Pixels[i] != b.Pixels[i]).ToHashSet();
			foreach (var (name, frame) in new[] { (key.Letter + "KEYA0", a), (key.Letter + "KEYB0", b) })
			{
				var pixels = (int[])frame.Pixels.Clone();
				Stamp(pixels, frame.Width, frame.Pixels, palette, key, lights);
				sprites.Add((name, DoomPatch.Encode(frame.Width, frame.Height, frame.Left, frame.Top, pixels)));
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

	/// <summary>The melody's symbol on the card's face, in the melody's light colour with a dark edge, over the body only (the card's outline and its lights are left alone).</summary>
	private static void Stamp(int[] pixels, int width, int[] original, DoomPalette palette, Key key, HashSet<int> lights)
	{
		var symbol = key.Symbol;
		var w = symbol[0].Length;
		var h = symbol.Length;
		int x0 = 5 - w / 2, y0 = 8 - h / 2; // the middle of the card's face
		var light = palette.Nearest(key.Ink.R, key.Ink.G, key.Ink.B);
		var dark = palette.Nearest(8, 8, 8); // a dark edge round the symbol, so it reads on the card
		var height = original.Length / width;

		bool Inside(int x, int y) => x >= 0 && x < width && y >= 0 && y < height && original[y * width + x] >= 0 && !lights.Contains(y * width + x);
		bool Ink(int x, int y) => x >= x0 && x < x0 + w && y >= y0 && y < y0 + h && symbol[y - y0][x - x0] == 'X';

		for (var y = y0 - 1; y <= y0 + h; y++)
		{
			for (var x = x0 - 1; x <= x0 + w; x++)
			{
				if (!Inside(x, y))
				{
					continue;
				}
				if (Ink(x, y))
				{
					pixels[y * width + x] = light;
				}
				else if (Ink(x - 1, y) || Ink(x + 1, y) || Ink(x, y - 1) || Ink(x, y + 1) || Ink(x - 1, y - 1) || Ink(x + 1, y - 1) || Ink(x - 1, y + 1) || Ink(x + 1, y + 1))
				{
					pixels[y * width + x] = dark;
				}
			}
		}
	}
}
