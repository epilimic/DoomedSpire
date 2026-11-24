#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ManagedDoom;

namespace DoomedSpire.Host;

/// <summary>
/// The numbers the face factory works from. <paramref name="Neutral"/> makes every face the same
/// colour, with no blood and no expression tints: only the head's position and angle differ (the tests use it; it is how a sheet to paint on was once made).
/// </summary>
internal readonly record struct FaceSettings(float CropLeft, float CropTop, float CropWidth, float CropHeight, float Damage, float Brightness, float Blood = 1f, bool Neutral = false)
{
	public static FaceSettings Default => new(0f, 0f, 1f, 1f, 1f, 1f, 1f);
}

/// <summary>The size of one picture in the game data and where its corner sits (read from the original face, so a replacement fits exactly).</summary>
internal readonly record struct PatchSpec(int Width, int Height, int Left, int Top);

internal enum FaceKind { Straight0, Straight1, Straight2, TurnRight, TurnLeft, Ouch, Evil, Kill, God, Dead }

/// <summary>
/// Makes DOOM's status-bar faces from one picture of a face. DOOM draws one of 42 faces (STFST00 to STFDEAD0): five health levels, each with three
/// straight looks, a turn right and a turn left, "ouch" (hit hard), "evil grin" (just picked up a weapon) and "rampage" (firing), plus god mode and dead.
/// All 42 are made from the one picture: the health level adds paleness, redness and blood marks, the looks move the face a little, and the other
/// expressions tint it. Only a few pixels wide each, so the result is deliberately simple. Godot-free (the tests use it, and write a contact sheet to look at).
/// </summary>
internal static class FaceFactory
{
	public const int PainLevels = 5;

	/// <summary>The 42 lumps the engine reads (StatusBarRenderer.cs:461-475), in this order, with how each is made.</summary>
	public static IReadOnlyList<(string Name, FaceKind Kind, int Pain)> Faces { get; } = ListFaces();

	private static List<(string, FaceKind, int)> ListFaces()
	{
		var list = new List<(string, FaceKind, int)>();
		for (var i = 0; i < PainLevels; i++)
		{
			list.Add(("STFST" + i + "0", FaceKind.Straight0, i));
			list.Add(("STFST" + i + "1", FaceKind.Straight1, i));
			list.Add(("STFST" + i + "2", FaceKind.Straight2, i));
			list.Add(("STFTR" + i + "0", FaceKind.TurnRight, i));
			list.Add(("STFTL" + i + "0", FaceKind.TurnLeft, i));
			list.Add(("STFOUCH" + i, FaceKind.Ouch, i));
			list.Add(("STFEVL" + i, FaceKind.Evil, i));
			list.Add(("STFKILL" + i, FaceKind.Kill, i));
		}
		list.Add(("STFGOD0", FaceKind.God, 0));
		list.Add(("STFDEAD0", FaceKind.Dead, PainLevels - 1));
		return list;
	}

	/// <summary>
	/// The size and corner a face lump is made with. The game's own straight faces are exactly as wide as the head, so a head that looks to one side (moved
	/// a pixel) would lose a column of itself at the edge it moves toward. So the two glances (STFST<i>n</i>1 and STFST<i>n</i>2) are made one pixel wider on each
	/// side than the game's; the picture is drawn in the same place on the screen. Used when the faces are made and when a painted sheet is read.
	/// </summary>
	public static PatchSpec LumpSpec(string name, PatchSpec original)
	{
		var glance = name.Length == 7 && name.StartsWith("STFST", StringComparison.Ordinal) && (name[6] == '1' || name[6] == '2');
		return glance ? new PatchSpec(original.Width + 2, original.Height, original.Left + 1, original.Top) : original;
	}

	/// <summary>The lump whose size and corner define the "canonical" face: the normal straight-ahead look at full health.</summary>
	private const string ReferenceFace = "STFST00";

	/// <summary>
	/// Builds all the face lumps: palette-matched, encoded as DOOM patches, ready for <see cref="PwadWriter"/>.
	/// The original faces are not all the same size (24 x 29 up to 30 x 31, each with its own corner offset, the turning ones wider). So that the face does not
	/// jump in size or place from one expression to the next, ONE face is drawn at the reference size and each lump is a window onto it: the face is placed
	/// where the reference face sits on screen (using each lump's own offsets), moved a little for the looks, and cut to the lump's rectangle.
	/// </summary>
	/// <param name="spec">The size and corner of the original face lump with that name.</param>
	public static IReadOnlyList<(string Name, byte[] Data)> Build(Rgba source, Func<string, PatchSpec> spec, DoomPalette palette, FaceSettings settings)
	{
		var reference = spec(ReferenceFace);
		var baseImage = Resample(source, settings, reference.Width, reference.Height);

		var lumps = new List<(string, byte[])>();
		foreach (var (name, kind, pain) in Faces)
		{
			var shaded = Shade(baseImage, kind, pain, settings);
			var s = LumpSpec(name, spec(name));
			if (kind == FaceKind.TurnLeft || kind == FaceKind.TurnRight)
			{
				// The turned head is drawn straight into the turn-left lump's own rectangle (wider than the straight faces, to hold the turned head). The turn right
				// is that very face mirrored about the middle of the head, then cut to the turn-right lump's rectangle.
				var leftSpec = kind == FaceKind.TurnLeft ? s : spec("STFTL" + pain + "0");
				var turned = TurnHead(shaded, TurnAmount, reference, leftSpec);
				var image = kind == FaceKind.TurnLeft ? turned : Mirror(turned, reference, leftSpec, s);
				lumps.Add((name, DoomPatch.Encode(s.Width, s.Height, s.Left, s.Top, Quantize(image, palette))));
				continue;
			}
			var canonical = Quantize(shaded, palette);
			// spare room in this lump, left and right of where the face sits (the turning faces are wider than the straight ones, but on different sides)
			var roomLeft = s.Left - reference.Left;
			var roomRight = (-s.Left + s.Width) - (-reference.Left + reference.Width);
			var (dx, dy) = LookShift(kind, roomLeft, roomRight);
			var pixels = new int[s.Width * s.Height];
			for (var py = 0; py < s.Height; py++)
			{
				for (var px = 0; px < s.Width; px++)
				{
					// where this lump's pixel is on screen, relative to where faces are drawn, and so which pixel of the canonical face is there
					var cx = px - s.Left + reference.Left - dx;
					var cy = py - s.Top + reference.Top - dy;
					pixels[py * s.Width + px] = cx >= 0 && cx < reference.Width && cy >= 0 && cy < reference.Height ? canonical[cy * reference.Width + cx] : -1;
				}
			}
			lumps.Add((name, DoomPatch.Encode(s.Width, s.Height, s.Left, s.Top, pixels)));
		}
		return lumps;
	}

	/// <summary>
	/// How far a look moves the face, in pixels: the glances move the whole head one pixel to the side (their lumps are made two pixels wider than the game's, see
	/// <see cref="LumpSpec"/>, so no column of the head is cut off) and "ouch" moves it up one (its lump has the room). The turns slide what is inside the head
	/// (<see cref="TurnHead"/>) and leave its outline.
	/// </summary>
	private static (int Dx, int Dy) LookShift(FaceKind kind, int roomLeft, int roomRight)
	{
		switch (kind)
		{
			case FaceKind.Straight1: return (-1, 0);
			case FaceKind.Straight2: return (1, 0);
			case FaceKind.Ouch: return (0, -1);
			default: return (0, 0);
		}
	}

	// ---------------------------------------------------------------- the picture, made small

	/// <summary>A picture as floating-point channels, 0 to 255 for colour and 0 to 1 for opacity.</summary>
	private sealed class Image
	{
		public Image(int width, int height)
		{
			Width = width;
			Height = height;
			R = new float[width * height];
			G = new float[width * height];
			B = new float[width * height];
			A = new float[width * height];
		}

		public int Width { get; }
		public int Height { get; }
		public float[] R { get; }
		public float[] G { get; }
		public float[] B { get; }
		public float[] A { get; }

		public Image Copy()
		{
			var copy = new Image(Width, Height);
			R.CopyTo(copy.R, 0);
			G.CopyTo(copy.G, 0);
			B.CopyTo(copy.B, 0);
			A.CopyTo(copy.A, 0);
			return copy;
		}
	}

	/// <summary>
	/// The wanted part of the source, scaled to fill the target size (the whole target is covered; what does not fit is cut off at the sides or the top and
	/// bottom, centred), by averaging the source pixels each target pixel covers (weighted by how much of each it covers, and by their opacity).
	/// </summary>
	private static Image Resample(Rgba source, FaceSettings settings, int width, int height)
	{
		double cropX = Math.Clamp(settings.CropLeft, 0f, 0.95f) * source.Width;
		double cropY = Math.Clamp(settings.CropTop, 0f, 0.95f) * source.Height;
		var cropW = Math.Max(1.0, Math.Min(Math.Clamp(settings.CropWidth, 0.05f, 1f) * source.Width, source.Width - cropX));
		var cropH = Math.Max(1.0, Math.Min(Math.Clamp(settings.CropHeight, 0.05f, 1f) * source.Height, source.Height - cropY));

		var scale = Math.Max(width / cropW, height / cropH);
		var windowW = width / scale;
		var windowH = height / scale;
		var x0 = cropX + (cropW - windowW) / 2;
		var y0 = cropY + (cropH - windowH) / 2;

		var result = new Image(width, height);
		for (var ty = 0; ty < height; ty++)
		{
			var top = y0 + ty * windowH / height;
			var bottom = y0 + (ty + 1) * windowH / height;
			for (var tx = 0; tx < width; tx++)
			{
				var left = x0 + tx * windowW / width;
				var right = x0 + (tx + 1) * windowW / width;

				double sumR = 0, sumG = 0, sumB = 0, sumA = 0, weight = 0;
				for (var sy = (int)Math.Floor(top); sy < Math.Ceiling(bottom); sy++)
				{
					var wy = Math.Min(bottom, sy + 1) - Math.Max(top, sy);
					if (wy <= 0 || sy < 0 || sy >= source.Height) { continue; }
					for (var sx = (int)Math.Floor(left); sx < Math.Ceiling(right); sx++)
					{
						var wx = Math.Min(right, sx + 1) - Math.Max(left, sx);
						if (wx <= 0 || sx < 0 || sx >= source.Width) { continue; }
						var w = wx * wy;
						var i = 4 * (sy * source.Width + sx);
						var a = source.Data[i + 3] / 255.0;
						sumR += source.Data[i] * a * w;
						sumG += source.Data[i + 1] * a * w;
						sumB += source.Data[i + 2] * a * w;
						sumA += a * w;
						weight += w;
					}
				}
				var o = ty * width + tx;
				if (weight > 0 && sumA > 0)
				{
					result.R[o] = (float)(sumR / sumA);
					result.G[o] = (float)(sumG / sumA);
					result.B[o] = (float)(sumB / sumA);
					result.A[o] = (float)(sumA / weight);
				}
			}
		}
		return result;
	}

	// ---------------------------------------------------------------- one face

	/// <summary>The face for one expression at the reference size, coloured for its health level and expression (and with its blood marks). Not yet moved for the look.</summary>
	private static Image Shade(Image baseImage, FaceKind kind, int pain, FaceSettings settings)
	{
		var image = baseImage.Copy();
		var width = image.Width;
		var height = image.Height;

		// how hurt: 0 (health 100 to 80) to 1 (below 20) at normal strength; the setting scales it
		var neutral = settings.Neutral;
		var hurt = neutral ? 0f : Math.Clamp(pain / 4f * settings.Damage, 0f, 2f);
		var saturation = 1f;
		var brightness = settings.Brightness;
		float addR = 0, addG = 0, addB = 0;
		var mixTo = ((float R, float G, float B))(0f, 0f, 0f);
		var mix = 0f;
		var extraBlood = 0;

		switch (neutral ? FaceKind.Straight0 : kind)
		{
			case FaceKind.Ouch:
				hurt = Math.Min(2f, hurt + 0.7f);
				addR += 70; addG -= 20; addB -= 20;
				break;
			case FaceKind.Evil:
				brightness *= 1.12f; saturation *= 1.25f;
				addR += 10; addG += 4; addB -= 6;
				break;
			case FaceKind.Kill:
				mixTo = (200f, 30f, 20f); mix = 0.30f; brightness *= 1.08f;
				break;
			case FaceKind.God:
				mixTo = (255f, 210f, 60f); mix = 0.55f; brightness *= 1.12f;
				break;
			case FaceKind.Dead:
				saturation = 0f; brightness *= 0.55f; hurt = 0f; // grey and dark, with blood marks but no red wash over it
				extraBlood = 9;
				break;
		}

		saturation *= Math.Max(0f, 1f - 0.35f * hurt);
		brightness *= Math.Max(0.2f, 1f - 0.18f * hurt);
		addR += 45f * hurt; addG -= 12f * hurt; addB -= 12f * hurt;

		for (var i = 0; i < width * height; i++)
		{
			var gray = 0.299f * image.R[i] + 0.587f * image.G[i] + 0.114f * image.B[i];
			var r = gray + (image.R[i] - gray) * saturation;
			var g = gray + (image.G[i] - gray) * saturation;
			var b = gray + (image.B[i] - gray) * saturation;
			if (mix > 0)
			{
				r += (mixTo.R - r) * mix; g += (mixTo.G - g) * mix; b += (mixTo.B - b) * mix;
			}
			image.R[i] = Math.Clamp(r * brightness + addR, 0f, 255f);
			image.G[i] = Math.Clamp(g * brightness + addG, 0f, 255f);
			image.B[i] = Math.Clamp(b * brightness + addB, 0f, 255f);
		}

		// blood marks: the same places at the same health level, whatever the expression, so the wounds stay put as the face changes
		DrawBlood(image, (int)Math.Round((2.2f * hurt + extraBlood) * Math.Max(0f, settings.Blood)), seed: (uint)(pain * 7919 + 17));

		if (kind == FaceKind.Dead && !neutral)
		{
			for (var y = 2 * height / 3; y < height; y++)
			{
				for (var x = 0; x < width; x++)
				{
					var o = y * width + x;
					image.R[o] *= 0.7f; image.G[o] *= 0.7f; image.B[o] *= 0.7f;
				}
			}
		}

		return image;
	}

	/// <summary>A picture matched to DOOM's palette: palette indices, -1 where (mostly) transparent.</summary>
	private static int[] Quantize(Image image, DoomPalette palette)
	{
		var pixels = new int[image.Width * image.Height];
		for (var i = 0; i < pixels.Length; i++)
		{
			if (image.A[i] < 0.5f)
			{
				pixels[i] = -1; // transparent: the status bar shows through
				continue;
			}
			var a = image.A[i];
			const float back = 24f; // a dark grey for the soft edge of the face to blend into
			pixels[i] = palette.Nearest(back + (image.R[i] - back) * a, back + (image.G[i] - back) * a, back + (image.B[i] - back) * a);
		}
		return pixels;
	}

	/// <summary>How far the features of the turning faces move toward the side they turn to, as a fraction of half the head's width (0 = not at all, 0.5 = the most possible).</summary>
	private const double TurnAmount = 0.35;

	/// <summary>
	/// The face with its features slid to the right, drawn into the rectangle of the turn-left lump. The head's outline stays where it is and what is inside
	/// it slides to the right: the features move over, the far side is squeezed and the near side stretched (across the head, a position
	/// <c>u</c> from -1 to 1 goes to <c>u + d (1 - u squared)</c>, which moves the middle by <c>d</c> and leaves both edges where they are). The rows stay where they are.
	/// </summary>
	private static Image TurnHead(Image face, double amount, PatchSpec reference, PatchSpec lump)
	{
		var result = new Image(lump.Width, lump.Height);
		var d = amount;
		var half = face.Width / 2.0;
		const int samples = 4; // across each output pixel, averaged so squeezed columns do not look ragged
		for (var py = 0; py < lump.Height; py++)
		{
			var fy = py - lump.Top + reference.Top; // the row of the straight face that is on this screen row
			if (fy < 0 || fy >= face.Height) { continue; }
			for (var px = 0; px < lump.Width; px++)
			{
				var fx = px - lump.Left + reference.Left; // the column of the straight face that is on this screen column
				double sumR = 0, sumG = 0, sumB = 0, sumA = 0;
				for (var k = 0; k < samples; k++)
				{
					var t = (fx + (k + 0.5) / samples - half) / half; // -1 at the left edge of the head, +1 at the right
					if (Math.Abs(t) > 1) { continue; }
					var u = Math.Clamp((1 - Math.Sqrt(Math.Max(0, 1 - 4 * d * (t - d)))) / (2 * d), -1, 1); // where on the straight face this point comes from
					var column = Math.Min(face.Width - 1, (int)(half + half * u));
					var o = fy * face.Width + column;
					var a = face.A[o];
					sumR += face.R[o] * a; sumG += face.G[o] * a; sumB += face.B[o] * a; sumA += a;
				}
				if (sumA > 0)
				{
					var o = py * lump.Width + px;
					result.R[o] = (float)(sumR / sumA); result.G[o] = (float)(sumG / sumA); result.B[o] = (float)(sumB / sumA); result.A[o] = (float)(sumA / samples);
				}
			}
		}
		return result;
	}

	/// <summary>
	/// A picture drawn in the rectangle of one lump, shown the other way round (left and right swapped about the middle of the head, which is where the straight
	/// face's middle is on screen) and put in the rectangle of another lump. Rows stay where they are on screen.
	/// </summary>
	private static Image Mirror(Image from, PatchSpec reference, PatchSpec fromLump, PatchSpec toLump)
	{
		var result = new Image(toLump.Width, toLump.Height);
		var mirrorSum = -2 * reference.Left + reference.Width - 1; // a column u on screen goes to (this - u)
		for (var py = 0; py < toLump.Height; py++)
		{
			var sy = py - toLump.Top + fromLump.Top;
			if (sy < 0 || sy >= from.Height) { continue; }
			for (var px = 0; px < toLump.Width; px++)
			{
				var sx = mirrorSum - (px - toLump.Left) + fromLump.Left;
				if (sx < 0 || sx >= from.Width) { continue; }
				var s = sy * from.Width + sx;
				var d = py * toLump.Width + px;
				result.R[d] = from.R[s]; result.G[d] = from.G[s]; result.B[d] = from.B[s]; result.A[d] = from.A[s];
			}
		}
		return result;
	}

	private static void DrawBlood(Image image, int marks, uint seed)
	{
		var state = seed;
		int Next(int max)
		{
			state = state * 1664525u + 1013904223u;
			return (int)((state >> 8) % (uint)max);
		}

		var width = image.Width;
		var height = image.Height;
		for (var n = 0; n < marks; n++)
		{
			int x = 0, y = 0;
			for (var tries = 0; tries < 12; tries++)
			{
				x = width * 18 / 100 + Next(Math.Max(1, width * 64 / 100));
				y = height * 8 / 100 + Next(Math.Max(1, height * 58 / 100));
				if (image.A[y * width + x] >= 0.5f)
				{
					break;
				}
			}
			var length = (n % 3) switch { 1 => 2 + Next(3), _ => 1 }; // a dot, a streak running down, or a short smear across
			var across = n % 3 == 2;
			var shade = 140f + Next(60);
			for (var k = 0; k < length + (across ? 1 : 0); k++)
			{
				var px = across ? x + k : x;
				var py = across ? y : y + k;
				if (px >= width || py >= height || image.A[py * width + px] < 0.5f)
				{
					continue;
				}
				var o = py * width + px;
				image.R[o] = shade; image.G[o] = 8f; image.B[o] = 10f;
			}
		}
	}

	// ---------------------------------------------------------------- the face sheet (for looking at, and for painting on)

	/// <summary>
	/// The grid of the face sheet: one row per health level, left to right: three straight looks, turn right, turn left, ouch, evil grin, rampage; and a last
	/// row with god mode and dead. The same grid is used to write a sheet and to read an edited one back (<see cref="FaceSheetImport"/>).
	/// </summary>
	public static IReadOnlyList<IReadOnlyList<string>> SheetRows { get; } = ListSheetRows();

	public const int SheetColumns = 8;
	/// <summary>Each cell is this many pixels wide and tall at 1 pixel per DOOM pixel. Every face is drawn relative to the same point in its cell, so faces line up.</summary>
	public const int SheetCellWidth = 34;
	public const int SheetCellHeight = 35;
	/// <summary>The empty border around the whole sheet, in pixels (not scaled).</summary>
	public const int SheetMargin = 4;
	/// <summary>
	/// A sheet written to paint on has this many extra pixels under every row, with a small text in it for each face: the health it stands for. The band is below every
	/// face's own box, so whatever is in it is ignored when the sheet is read back (<see cref="FaceSheetImport"/> reads both sheets with and without it).
	/// </summary>
	public const int SheetLabelBand = 7;

	/// <summary>DOOM's health that shows a pain level's faces, from the engine (<c>StatusBar.CalcPainOffset</c>: level = (100 - health) * 5 / 101, health above 100 counting as 100).</summary>
	public static (int Highest, int Lowest) HealthRange(int level)
	{
		int highest = -1, lowest = -1;
		for (var health = 100; health >= 1; health--)
		{
			if ((100 - health) * PainLevels / 101 == level)
			{
				highest = highest == -1 ? health : highest;
				lowest = health;
			}
		}
		return (highest, lowest);
	}

	/// <summary>The text under a face on a sheet to paint on: the health it is shown at, for example <c>100-80%</c>, or <c>GOD</c> and <c>DEAD 0%</c> for the two last faces.</summary>
	public static string HealthLabel(int row, int col)
	{
		if (row < PainLevels)
		{
			var (highest, lowest) = HealthRange(row);
			return $"{highest}-{lowest}%";
		}
		return col == 0 ? "GOD" : "DEAD 0%";
	}

	// A tiny font for those texts: 3 pixels wide and 5 tall, a row of three bits each.
	private static readonly Dictionary<char, int[]> LabelFont = new()
	{
		['0'] = new[] { 7, 5, 5, 5, 7 }, ['1'] = new[] { 2, 6, 2, 2, 7 }, ['2'] = new[] { 7, 1, 7, 4, 7 }, ['3'] = new[] { 7, 1, 7, 1, 7 }, ['4'] = new[] { 5, 5, 7, 1, 1 },
		['5'] = new[] { 7, 4, 7, 1, 7 }, ['6'] = new[] { 7, 4, 7, 5, 7 }, ['7'] = new[] { 7, 1, 1, 1, 1 }, ['8'] = new[] { 7, 5, 7, 5, 7 }, ['9'] = new[] { 7, 5, 7, 1, 7 },
		['-'] = new[] { 0, 0, 7, 0, 0 }, ['%'] = new[] { 5, 1, 2, 4, 5 },
		['G'] = new[] { 7, 4, 5, 5, 7 }, ['O'] = new[] { 7, 5, 5, 5, 7 }, ['D'] = new[] { 6, 5, 5, 5, 6 }, ['E'] = new[] { 7, 4, 7, 4, 7 }, ['A'] = new[] { 2, 5, 7, 5, 5 },
		[' '] = new[] { 0, 0, 0, 0, 0 },
	};

	/// <summary>The colour of the texts: a blue that no face uses, so it is plainly not part of the painting.</summary>
	private static readonly (byte R, byte G, byte B) LabelColour = (0, 190, 255);

	private static void DrawLabel(byte[] data, int width, int x0, int y0, string text, int scale)
	{
		var x = x0;
		foreach (var ch in text)
		{
			var glyph = LabelFont[ch];
			var advance = ch == ' ' ? 2 : 4;
			for (var gy = 0; gy < 5; gy++)
			{
				for (var gx = 0; gx < 3; gx++)
				{
					if (ch == ' ' || (glyph[gy] >> (2 - gx) & 1) == 0)
					{
						continue;
					}
					for (var sy = 0; sy < scale; sy++)
					{
						for (var sx = 0; sx < scale; sx++)
						{
							var o = 4 * ((y0 + gy * scale + sy) * width + x + gx * scale + sx);
							data[o] = LabelColour.R; data[o + 1] = LabelColour.G; data[o + 2] = LabelColour.B; data[o + 3] = 255;
						}
					}
				}
			}
			x += advance * scale;
		}
	}

	/// <summary>The width of a text in the tiny font, in pixels at scale 1 (a gap of one pixel after every letter but the last).</summary>
	private static int LabelWidth(string text) => text.Sum(c => c == ' ' ? 2 : 4) - (text.EndsWith(' ') ? 0 : 1);

	private static List<IReadOnlyList<string>> ListSheetRows()
	{
		var rows = new List<IReadOnlyList<string>>();
		for (var i = 0; i < PainLevels; i++)
		{
			rows.Add(new[] { "STFST" + i + "0", "STFST" + i + "1", "STFST" + i + "2", "STFTR" + i + "0", "STFTL" + i + "0", "STFOUCH" + i, "STFEVL" + i, "STFKILL" + i });
		}
		rows.Add(new[] { "STFGOD0", "STFDEAD0" });
		return rows;
	}

	/// <summary>
	/// A sheet of the finished faces, decoded back from their patches the way the engine reads them, laid out as <see cref="SheetRows"/>. For a person to look
	/// at (<paramref name="scale"/> 6 on a dark background) or to paint on (scale 1 on a transparent background: one picture pixel per DOOM pixel). With
	/// <paramref name="labels"/> every row is <see cref="SheetLabelBand"/> pixels taller and under every face is the health it stands for (<see cref="HealthLabel"/>).
	/// </summary>
	public static Rgba Sheet(IReadOnlyList<(string Name, byte[] Data)> lumps, DoomPalette palette, int scale, bool transparentBackground = false, bool labels = false)
	{
		var byName = new Dictionary<string, byte[]>();
		foreach (var (name, lumpData) in lumps)
		{
			byName[name] = lumpData;
		}

		var cellW = SheetCellWidth * scale;
		var cellH = (SheetCellHeight + (labels ? SheetLabelBand : 0)) * scale;
		var width = cellW * SheetColumns + 2 * SheetMargin;
		var height = cellH * SheetRows.Count + 2 * SheetMargin;
		var data = new byte[width * height * 4];
		if (!transparentBackground)
		{
			for (var i = 0; i < data.Length; i += 4)
			{
				data[i] = 40; data[i + 1] = 40; data[i + 2] = 44; data[i + 3] = 255;
			}
		}

		for (var row = 0; row < SheetRows.Count; row++)
		{
			for (var col = 0; col < SheetRows[row].Count; col++)
			{
				if (labels)
				{
					var text = HealthLabel(row, col);
					var left = SheetMargin + col * cellW + (SheetCellWidth * scale - LabelWidth(text) * scale) / 2;
					DrawLabel(data, width, left, SheetMargin + row * cellH + (SheetCellHeight + 1) * scale, text, scale);
				}
				var patch = Patch.FromData(SheetRows[row][col], byName[SheetRows[row][col]]);
				for (var x = 0; x < patch.Width; x++)
				{
					foreach (var post in patch.Columns[x])
					{
						for (var k = 0; k < post.Length; k++)
						{
							var (r, g, b) = palette.Colour(post.Data[post.Offset + k]);
							var u = x - patch.LeftOffset;               // where this pixel is on screen, relative to where faces are drawn
							var v = post.TopDelta + k - patch.TopOffset;
							for (var sy = 0; sy < scale; sy++)
							{
								for (var sx = 0; sx < scale; sx++)
								{
									var ox = SheetMargin + col * cellW + u * scale + sx;
									var oy = SheetMargin + row * cellH + v * scale + sy;
									var o = 4 * (oy * width + ox);
									data[o] = (byte)r; data[o + 1] = (byte)g; data[o + 2] = (byte)b; data[o + 3] = 255;
								}
							}
						}
					}
				}
			}
		}
		return new Rgba(width, height, data);
	}
}

/// <summary>
/// Reads a face sheet (as written by <see cref="FaceFactory.Sheet"/>, possibly painted over by a person) back into the 42 face lumps, so hand-made faces can
/// replace the generated ones. The sheet may be at any whole-number scale (1 picture pixel per DOOM pixel, or 6, ...); each DOOM pixel is read from the middle of
/// its block. Transparent pixels stay transparent; every other colour is matched to the nearest of DOOM's 256.
/// </summary>
internal static class FaceSheetImport
{
	public static IReadOnlyList<(string Name, byte[] Data)> Read(Rgba sheet, Func<string, PatchSpec> spec, DoomPalette palette)
	{
		var rows = FaceFactory.SheetRows;
		var gridWidth = FaceFactory.SheetCellWidth * FaceFactory.SheetColumns;
		var innerWidth = sheet.Width - 2 * FaceFactory.SheetMargin;
		if (innerWidth <= 0 || innerWidth % gridWidth != 0)
		{
			throw new FormatException($"the sheet is {sheet.Width} pixels wide; it must be {gridWidth} x a whole number + {2 * FaceFactory.SheetMargin} (for example {gridWidth + 2 * FaceFactory.SheetMargin} at 1 pixel per face pixel). Save it at its original size, or enlarged by a whole number.");
		}
		var scale = innerWidth / gridWidth;
		var expectedHeight = FaceFactory.SheetCellHeight * scale * rows.Count + 2 * FaceFactory.SheetMargin;
		var labelledHeight = (FaceFactory.SheetCellHeight + FaceFactory.SheetLabelBand) * scale * rows.Count + 2 * FaceFactory.SheetMargin;
		if (sheet.Height != expectedHeight && sheet.Height != labelledHeight)
		{
			throw new FormatException($"the sheet is {sheet.Height} pixels tall but at that width it must be {expectedHeight}, or {labelledHeight} with the health texts under the faces");
		}

		var cellW = FaceFactory.SheetCellWidth * scale;
		var cellH = (FaceFactory.SheetCellHeight + (sheet.Height == labelledHeight ? FaceFactory.SheetLabelBand : 0)) * scale;
		var byName = new Dictionary<string, byte[]>();
		for (var row = 0; row < rows.Count; row++)
		{
			for (var col = 0; col < rows[row].Count; col++)
			{
				var name = rows[row][col];
				var s = FaceFactory.LumpSpec(name, spec(name));
				var pixels = new int[s.Width * s.Height];
				for (var py = 0; py < s.Height; py++)
				{
					for (var px = 0; px < s.Width; px++)
					{
						var u = px - s.Left;
						var v = py - s.Top;
						var x = FaceFactory.SheetMargin + col * cellW + u * scale + scale / 2;
						var y = FaceFactory.SheetMargin + row * cellH + v * scale + scale / 2;
						if (u < 0 || v < 0 || u >= FaceFactory.SheetCellWidth || v >= FaceFactory.SheetCellHeight)
						{
							pixels[py * s.Width + px] = -1;
							continue;
						}
						var o = 4 * (y * sheet.Width + x);
						pixels[py * s.Width + px] = sheet.Data[o + 3] < 128 ? -1 : palette.Nearest(sheet.Data[o], sheet.Data[o + 1], sheet.Data[o + 2]);
					}
				}
				byName[name] = DoomPatch.Encode(s.Width, s.Height, s.Left, s.Top, pixels);
			}
		}

		var lumps = new List<(string, byte[])>();
		foreach (var (name, _, _) in FaceFactory.Faces)
		{
			lumps.Add((name, byName[name]));
		}
		return lumps;
	}
}
