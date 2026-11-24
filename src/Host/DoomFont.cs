#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ManagedDoom;

namespace DoomedSpire.Host;

/// <summary>
/// DOOM's own small font (the STCFN lumps of the game data, the one the status bar messages and the menus' list lines use) as pictures of text. Godot-free (the tests
/// use it). The game data has capitals, digits and punctuation only, so a lowercase letter is drawn as its capital, as the engine does. A character the font does not
/// have is left out. The pictures are at the font's own size (one font pixel is one picture pixel) with a transparent background; the caller scales them, because DOOM's
/// pixels are 1.2 times taller than wide.
/// </summary>
internal sealed class DoomFont
{
	/// <summary>The distance from one line to the next, in font pixels (the letters are 7 or 8 tall).</summary>
	public const int LineHeight = 10;

	/// <summary>The width of a space, in font pixels (the engine's).</summary>
	public const int SpaceWidth = 4;

	private readonly Dictionary<char, (int Width, int Height, int Top, int[] Pixels)> glyphs = new();
	private readonly DoomPalette palette;
	private readonly (int Width, int Height, int[] Pixels)[] skulls;

	private DoomFont(DoomPalette palette, (int, int, int[])[] skulls)
	{
		this.palette = palette;
		this.skulls = skulls;
	}

	/// <summary>The font of a game, or null if it has none (no STCFN letters, or no palette).</summary>
	public static DoomFont? Load(Wad wad)
	{
		if (wad.GetLumpNumber("PLAYPAL") == -1 || wad.GetLumpNumber("STCFN065") == -1)
		{
			return null;
		}
		var skulls = new List<(int, int, int[])>();
		foreach (var name in new[] { "M_SKULL1", "M_SKULL2" })
		{
			if (wad.GetLumpNumber(name) != -1)
			{
				var skull = DoomPatch.Decode(wad.ReadLump(name));
				skulls.Add((skull.Width, skull.Height, skull.Pixels));
			}
		}
		var font = new DoomFont(new DoomPalette(wad.ReadLump("PLAYPAL")), skulls.ToArray());
		for (var c = 33; c < 128; c++)
		{
			var lump = wad.GetLumpNumber("STCFN" + c.ToString("000"));
			if (lump != -1)
			{
				var patch = DoomPatch.Decode(wad.ReadLump(lump));
				font.glyphs[(char)c] = (patch.Width, patch.Height, patch.Top, patch.Pixels);
			}
		}
		return font;
	}

	/// <summary>How many skull-cursor pictures the game has (the menu flips between two).</summary>
	public int SkullCount => skulls.Length;

	/// <summary>One of the menu's skull cursors (<c>M_SKULL1</c>, <c>M_SKULL2</c>), 20 x 19.</summary>
	public Rgba Skull(int index)
	{
		var (width, height, pixels) = skulls[index % skulls.Length];
		return ToRgba(width, height, pixels);
	}

	private char Normal(char c) => c is >= 'a' and <= 'z' ? char.ToUpperInvariant(c) : c;

	private bool Has(char c) => c == ' ' || glyphs.ContainsKey(Normal(c));

	private int AdvanceOf(char c) => c == ' ' ? SpaceWidth : glyphs.TryGetValue(Normal(c), out var g) ? g.Width : 0;

	/// <summary>The width of a line of text, in font pixels.</summary>
	public int Measure(string text) => text.Sum(AdvanceOf);

	/// <summary>
	/// Cuts <paramref name="text"/> into lines no wider than <paramref name="maxWidth"/> font pixels: at spaces, and inside a word that is wider than a line by itself
	/// (a path, say). A new-line in the text starts a line. A line that is wider than the width never happens except for a single character wider than it.
	/// </summary>
	public IReadOnlyList<string> Wrap(string text, int maxWidth)
	{
		var lines = new List<string>();
		foreach (var paragraph in text.Replace("\r", "").Split('\n'))
		{
			var line = "";
			foreach (var word in paragraph.Split(' '))
			{
				var rest = word;
				while (true)
				{
					var candidate = line.Length == 0 ? rest : line + " " + rest;
					if (Measure(candidate) <= maxWidth)
					{
						line = candidate;
						break;
					}
					if (line.Length > 0 && Measure(rest) <= maxWidth)
					{
						lines.Add(line);
						line = rest;
						break;
					}
					// the word alone is too wide: break it where the line is full
					if (line.Length > 0)
					{
						lines.Add(line);
						line = "";
					}
					var take = 1;
					while (take < rest.Length && Measure(rest.Substring(0, take + 1)) <= maxWidth)
					{
						take++;
					}
					lines.Add(rest.Substring(0, take));
					rest = rest.Substring(take);
					if (rest.Length == 0)
					{
						break;
					}
				}
			}
			lines.Add(line);
		}
		return lines;
	}

	/// <summary>
	/// A picture of the text: <paramref name="maxWidth"/> font pixels wide at most (0 = no limit, new-lines only), lines <see cref="LineHeight"/> apart, each line as far
	/// left as it goes, or in the middle of the picture when <paramref name="centred"/>. The picture is as wide as its widest line and as tall as its lines; it is 1 pixel
	/// wide and tall at least, even for no text.
	/// </summary>
	public Rgba Render(string text, int maxWidth = 0, bool centred = false)
	{
		var lines = maxWidth > 0 ? Wrap(text, maxWidth) : text.Replace("\r", "").Split('\n');
		var width = Math.Max(1, lines.Max(Measure));
		var height = Math.Max(1, (lines.Count - 1) * LineHeight + 8);
		var pixels = new int[width * height];
		Array.Fill(pixels, -1);
		for (var n = 0; n < lines.Count; n++)
		{
			var x = centred ? (width - Measure(lines[n])) / 2 : 0;
			foreach (var ch in lines[n])
			{
				if (ch == ' ')
				{
					x += SpaceWidth;
					continue;
				}
				if (!glyphs.TryGetValue(Normal(ch), out var glyph))
				{
					continue;
				}
				// the engine puts a letter's top at its line's base - 7 - the patch's top offset; the line's base here is 7 rows down, so the top is -Top
				var top = n * LineHeight - glyph.Top;
				for (var gy = 0; gy < glyph.Height; gy++)
				{
					for (var gx = 0; gx < glyph.Width; gx++)
					{
						var c = glyph.Pixels[gy * glyph.Width + gx];
						if (c >= 0 && top + gy >= 0 && top + gy < height)
						{
							pixels[(top + gy) * width + x + gx] = c;
						}
					}
				}
				x += glyph.Width;
			}
		}
		return ToRgba(width, height, pixels);
	}

	private Rgba ToRgba(int width, int height, int[] pixels)
	{
		var data = new byte[width * height * 4];
		for (var i = 0; i < pixels.Length; i++)
		{
			if (pixels[i] >= 0)
			{
				var (r, g, b) = palette.Colour(pixels[i]);
				data[4 * i] = (byte)r; data[4 * i + 1] = (byte)g; data[4 * i + 2] = (byte)b; data[4 * i + 3] = 255;
			}
		}
		return new Rgba(width, height, data);
	}
}
