#nullable enable
using System;
using System.Collections.Generic;
using ManagedDoom;

namespace DoomedSpire.Host;

/// <summary>
/// How the character's face is made. <b>Live-tunable</b> with the VfxSpawner mod (F9, "Show tuner", group "Doomed Spire face"). The faces are made when a new
/// game starts, so a change shows the next time one does (quit from DOOM's menu, then open DOOM again). The VfxSpawner tuner's "Copy values" gives the numbers
/// to bake in as defaults.
/// </summary>
internal static class FaceTuning
{
	private const string Group = "Doomed Spire face";

	[Tunable(Group, 1f, "On: the status bar shows the face of the character you are playing. Off: Doomguy's own face. Takes effect when a new game starts.")]
	public static bool UseCharacterFace = true;

	[Tunable(Group, 1f, "Which picture of the character to take the face from: 0 = the small portrait in the game's top bar, 1 = the character-select picture, 2 = the map marker.", Min = 0, Max = 2)]
	public static int Source = 0;

	[Tunable(Group, 0.01f, "Where the face starts in that picture, left edge, as a fraction of its width (0 = the very left).", Min = 0, Max = 0.9)]
	public static float CropLeft = 0f;

	[Tunable(Group, 0.01f, "Where the face starts in that picture, top edge, as a fraction of its height (0 = the very top).", Min = 0, Max = 0.9)]
	public static float CropTop = 0f;

	[Tunable(Group, 0.01f, "How much of the picture's width to use, as a fraction (1 = all of it). Smaller zooms in on the face.", Min = 0.1, Max = 1)]
	public static float CropWidth = 1f;

	[Tunable(Group, 0.01f, "How much of the picture's height to use, as a fraction (1 = all of it). Smaller zooms in on the face.", Min = 0.1, Max = 1)]
	public static float CropHeight = 1f;

	[Tunable(Group, 0.05f, "How strongly the lower health levels look hurt: paler, redder, with blood. 0 = no change at all, 1 = normal, 2 = very bloody.", Min = 0, Max = 2)]
	public static float Damage = 1f;

	[Tunable(Group, 0.01f, "How bright the face is. 1 = as the picture.", Min = 0.5, Max = 1.5)]
	public static float Brightness = 1f;

	[Tunable(Group, 0.05f, "How many blood marks the hurt faces get. 0 = none, 1 = normal, 2 = twice as many. (The sheet written for you to paint on never has any.)", Min = 0, Max = 3)]
	public static float Blood = 1f;
}

/// <summary>The numbers the face factory works from (<see cref="FaceTuning"/>'s, or fixed ones in the tests).</summary>
internal readonly record struct FaceSettings(float CropLeft, float CropTop, float CropWidth, float CropHeight, float Damage, float Brightness, float Blood = 1f)
{
	public static FaceSettings Default => new(0f, 0f, 1f, 1f, 1f, 1f, 1f);

	public static FaceSettings FromTuning() =>
		new(FaceTuning.CropLeft, FaceTuning.CropTop, FaceTuning.CropWidth, FaceTuning.CropHeight, FaceTuning.Damage, FaceTuning.Brightness, FaceTuning.Blood);
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
			var canonical = Render(baseImage, kind, pain, settings, palette);
			var s = spec(name);
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
	/// How far each look moves the face, in pixels. The turns move it toward the side where their lump has spare room (up to 3 pixels), so nothing is cut off;
	/// the straight looks have no spare room, so they shuffle one pixel and lose at most one pixel column at the edge, which on a round face is mostly outline.
	/// </summary>
	private static (int Dx, int Dy) LookShift(FaceKind kind, int roomLeft, int roomRight)
	{
		switch (kind)
		{
			case FaceKind.Straight1: return (-1, 0);
			case FaceKind.Straight2: return (1, 0);
			case FaceKind.TurnLeft:
			case FaceKind.TurnRight:
				if (roomRight > 0 || roomLeft > 0)
				{
					return roomRight >= roomLeft ? (Math.Min(3, roomRight), 0) : (-Math.Min(3, roomLeft), 0);
				}
				return kind == FaceKind.TurnRight ? (1, 0) : (-1, 0);
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

	/// <summary>The canonical face for one expression: palette indices at the reference size, -1 where transparent. Not yet moved for the look.</summary>
	private static int[] Render(Image baseImage, FaceKind kind, int pain, FaceSettings settings, DoomPalette palette)
	{
		var image = baseImage.Copy();
		var width = image.Width;
		var height = image.Height;

		// how hurt: 0 (health 100 to 80) to 1 (below 20) at normal strength; the setting scales it
		var hurt = Math.Clamp(pain / 4f * settings.Damage, 0f, 2f);
		var saturation = 1f;
		var brightness = settings.Brightness;
		float addR = 0, addG = 0, addB = 0;
		var mixTo = ((float R, float G, float B))(0f, 0f, 0f);
		var mix = 0f;
		var extraBlood = 0;

		switch (kind)
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

		if (kind == FaceKind.Dead)
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

		var pixels = new int[width * height];
		for (var i = 0; i < width * height; i++)
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
	/// at (<paramref name="scale"/> 6 on a dark background) or to paint on (scale 1 on a transparent background: one picture pixel per DOOM pixel).
	/// </summary>
	public static Rgba Sheet(IReadOnlyList<(string Name, byte[] Data)> lumps, DoomPalette palette, int scale, bool transparentBackground = false)
	{
		var byName = new Dictionary<string, byte[]>();
		foreach (var (name, lumpData) in lumps)
		{
			byName[name] = lumpData;
		}

		var cellW = SheetCellWidth * scale;
		var cellH = SheetCellHeight * scale;
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
		if (sheet.Height != expectedHeight)
		{
			throw new FormatException($"the sheet is {sheet.Height} pixels tall but at that width it must be {expectedHeight}");
		}

		var cellW = FaceFactory.SheetCellWidth * scale;
		var cellH = FaceFactory.SheetCellHeight * scale;
		var byName = new Dictionary<string, byte[]>();
		for (var row = 0; row < rows.Count; row++)
		{
			for (var col = 0; col < rows[row].Count; col++)
			{
				var name = rows[row][col];
				var s = spec(name);
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
