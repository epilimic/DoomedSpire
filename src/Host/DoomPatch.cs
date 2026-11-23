#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace DoomedSpire.Host;

/// <summary>A picture as plain pixels: 4 bytes each (R, G, B, A), row after row.</summary>
internal sealed record Rgba(int Width, int Height, byte[] Data);

/// <summary>DOOM's 256 colours (the PLAYPAL lump: three bytes per colour), and the nearest of them to any colour.</summary>
internal sealed class DoomPalette
{
	private readonly byte[] colours;

	/// <param name="playpal">The PLAYPAL lump. Only its first palette (768 bytes) is used.</param>
	public DoomPalette(byte[] playpal)
	{
		if (playpal.Length < 768)
		{
			throw new ArgumentException("the palette lump is shorter than 768 bytes", nameof(playpal));
		}
		colours = new byte[768];
		Array.Copy(playpal, colours, 768);
	}

	public (int R, int G, int B) Colour(int index) => (colours[3 * index], colours[3 * index + 1], colours[3 * index + 2]);

	/// <summary>The palette index nearest to the colour, by the "redmean" distance (a cheap measure that follows how the eye sees colour better than plain RGB).</summary>
	public int Nearest(double r, double g, double b)
	{
		var best = 0;
		var bestDistance = double.MaxValue;
		for (var i = 0; i < 256; i++)
		{
			double pr = colours[3 * i], pg = colours[3 * i + 1], pb = colours[3 * i + 2];
			var meanR = (r + pr) / 2;
			var dr = r - pr;
			var dg = g - pg;
			var db = b - pb;
			var distance = (2 + meanR / 256) * dr * dr + 4 * dg * dg + (2 + (255 - meanR) / 256) * db * db;
			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = i;
			}
		}
		return best;
	}
}

/// <summary>
/// DOOM's picture format ("patch"): a small header (width, height, and where the picture's corner sits relative to where it is drawn), then the picture one
/// column at a time, each column a list of "posts": a run of opaque pixels with the row it starts at. Rows between posts are transparent.
/// Layout (from the engine's own reader, ManagedDoom Patch.FromData): four int16 (width, height, left offset, top offset), one int32 per column (where that
/// column starts), then per column the posts [top row, length, one unused byte, the length pixels, one unused byte] and a final 0xFF.
/// Godot-free (the tests read what this writes with the engine's reader).
/// </summary>
internal static class DoomPatch
{
	/// <param name="pixels">Width * height palette indices, row after row; a negative number is transparent.</param>
	public static byte[] Encode(int width, int height, int leftOffset, int topOffset, int[] pixels)
	{
		if (pixels.Length != width * height)
		{
			throw new ArgumentException("the pixel count must be width * height", nameof(pixels));
		}
		if (height > 254)
		{
			throw new ArgumentException("a patch can be at most 254 rows tall here");
		}

		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream);
		writer.Write((short)width);
		writer.Write((short)height);
		writer.Write((short)leftOffset);
		writer.Write((short)topOffset);
		var tableStart = stream.Position;
		for (var x = 0; x < width; x++)
		{
			writer.Write(0); // column offsets, filled in below
		}

		var offsets = new int[width];
		for (var x = 0; x < width; x++)
		{
			offsets[x] = (int)stream.Position;
			var y = 0;
			while (y < height)
			{
				if (pixels[y * width + x] < 0)
				{
					y++;
					continue;
				}
				var start = y;
				while (y < height && pixels[y * width + x] >= 0)
				{
					y++;
				}
				writer.Write((byte)start);
				writer.Write((byte)(y - start));
				writer.Write((byte)0);
				for (var row = start; row < y; row++)
				{
					writer.Write((byte)pixels[row * width + x]);
				}
				writer.Write((byte)0);
			}
			writer.Write((byte)0xFF);
		}

		writer.Flush();
		var data = stream.ToArray();
		for (var x = 0; x < width; x++)
		{
			BitConverter.GetBytes(offsets[x]).CopyTo(data, (int)tableStart + 4 * x);
		}
		return data;
	}
}

/// <summary>Writes a "PWAD": a small add-on game-data file. Where it has a lump with the same name as one in the main game, the engine uses the add-on's (later files win).</summary>
internal static class PwadWriter
{
	public static byte[] Build(IReadOnlyList<(string Name, byte[] Data)> lumps)
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream);
		writer.Write(new[] { 'P', 'W', 'A', 'D' });
		writer.Write(lumps.Count);
		var directoryAt = 12;
		foreach (var lump in lumps)
		{
			directoryAt += lump.Data.Length;
		}
		writer.Write(directoryAt);

		var positions = new int[lumps.Count];
		for (var i = 0; i < lumps.Count; i++)
		{
			positions[i] = (int)stream.Position;
			writer.Write(lumps[i].Data);
		}
		for (var i = 0; i < lumps.Count; i++)
		{
			var name = lumps[i].Name.ToUpperInvariant();
			if (name.Length > 8)
			{
				throw new ArgumentException($"lump name {name} is longer than 8 characters");
			}
			writer.Write(positions[i]);
			writer.Write(lumps[i].Data.Length);
			var bytes = new byte[8];
			for (var c = 0; c < name.Length; c++)
			{
				bytes[c] = (byte)name[c];
			}
			writer.Write(bytes);
		}
		writer.Flush();
		return stream.ToArray();
	}
}
