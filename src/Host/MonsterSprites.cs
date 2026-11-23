#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ManagedDoom;

namespace DoomedSpire.Host;

/// <summary>What DOOM uses one frame letter of a monster's sprite for.</summary>
internal enum FrameRole { Stand, Walk, Attack, Pain, Death, Gib, Other }

/// <summary>One frame letter (A, B, ...) of a monster's sprite, what DOOM shows it for, and which one it is of the frames used for that (<paramref name="Index"/> of <paramref name="Count"/>, from 0).</summary>
internal readonly record struct MonsterFrame(char Letter, FrameRole Role, int Index, int Count);

/// <summary>
/// What to draw for one frame letter: the first of <paramref name="Animations"/> the creature has, held at <paramref name="Fraction"/> (0 to 1) of the way
/// through it.
/// </summary>
internal readonly record struct FrameRequest(char Letter, string[] Animations, double Fraction);

/// <summary>A picture of a creature in one pose, and the pixel of it that is where the creature stands (so it can be placed on DOOM's floor).</summary>
internal sealed record CapturedFrame(Rgba Image, int OriginX, int OriginY);

/// <summary>
/// Which frame of a DOOM monster's sprite is which: walking, attacking, hurt, dying. Read from the engine's own state tables (the chain of states a monster
/// goes through when it walks, attacks, is hurt, dies), so it is right for every monster and for the game data in use. Godot-free.
/// </summary>
internal static class MonsterSprites
{
	/// <summary>The four-letter name of the monster's sprite, for example TROO for the imp.</summary>
	public static string SpriteName(MobjType type)
	{
		var spawn = DoomInfo.MobjInfos[(int)type].SpawnState;
		return DoomInfo.SpriteNames[(int)DoomInfo.States[(int)spawn].Sprite].ToString();
	}

	/// <summary>The frame letters a sprite has in the game data (from the names of its picture lumps, such as TROOA1, TROOB2B8), in order.</summary>
	public static IReadOnlyList<char> Letters(Wad wad, string sprite)
	{
		var letters = new SortedSet<char>();
		foreach (var info in wad.LumpInfos)
		{
			var name = info.Name;
			if (!name.StartsWith(sprite, StringComparison.Ordinal) || (name.Length != 6 && name.Length != 8))
			{
				continue;
			}
			letters.Add(name[4]);
			if (name.Length == 8)
			{
				letters.Add(name[6]);
			}
		}
		return letters.ToList();
	}

	/// <summary>
	/// The height of the monster's own first frame (A) in the game data, which is how tall a replacement should be. Zero if the data has no such picture.
	/// </summary>
	public static int OriginalHeight(Wad wad, string sprite)
	{
		foreach (var info in wad.LumpInfos)
		{
			var name = info.Name;
			if (name.StartsWith(sprite + "A", StringComparison.Ordinal) && (name.Length == 6 || name.Length == 8) && info.Size > 0)
			{
				return Patch.FromData(name, wad.ReadLump(name)).Height;
			}
		}
		return 0;
	}

	/// <summary>
	/// What each of the given frame letters is for: the letters of the walking states first, then standing, attacking (missile, then melee), being hurt,
	/// dying and being gibbed; a letter used for two things counts for the first. Letters no state uses are <see cref="FrameRole.Other"/>.
	/// </summary>
	public static IReadOnlyList<MonsterFrame> Frames(MobjType type, IReadOnlyList<char> letters)
	{
		var info = DoomInfo.MobjInfos[(int)type];
		var sprite = DoomInfo.States[(int)info.SpawnState].Sprite;
		var assigned = new Dictionary<char, MonsterFrame>();

		void Chain(FrameRole role, params MobjState[] starts)
		{
			var seen = new HashSet<MobjState>();
			var chain = new List<char>();
			foreach (var start in starts)
			{
				var state = start;
				while (state != MobjState.Null && seen.Add(state))
				{
					var definition = DoomInfo.States[(int)state];
					if (definition.Sprite == sprite)
					{
						var letter = (char)('A' + (definition.Frame & 0x7FFF)); // the top bit only asks for full brightness
						if (!chain.Contains(letter))
						{
							chain.Add(letter);
						}
					}
					state = definition.Next;
				}
			}
			for (var i = 0; i < chain.Count; i++)
			{
				if (!assigned.ContainsKey(chain[i]))
				{
					assigned[chain[i]] = new MonsterFrame(chain[i], role, i, chain.Count);
				}
			}
		}

		Chain(FrameRole.Walk, info.SeeState);
		Chain(FrameRole.Stand, info.SpawnState);
		Chain(FrameRole.Attack, info.MissileState, info.MeleeState);
		Chain(FrameRole.Pain, info.PainState);
		Chain(FrameRole.Death, info.DeathState);
		Chain(FrameRole.Gib, info.XdeathState);

		return letters.Select(l => assigned.TryGetValue(l, out var frame) ? frame : new MonsterFrame(l, FrameRole.Other, 0, 1)).ToList();
	}

	/// <summary>
	/// The creature's pose for a frame: standing and walking use its idle loop (spread over the loop), attacking its attack, being hurt its hurt, dying and
	/// being gibbed its death (ending on the last pose). The names are the ones the game's creatures use; the first one a creature has is taken.
	/// </summary>
	public static FrameRequest Request(MonsterFrame frame)
	{
		var idle = new[] { "idle_loop", "idle" };
		switch (frame.Role)
		{
			case FrameRole.Stand:
			case FrameRole.Walk:
				return new FrameRequest(frame.Letter, idle, (double)frame.Index / Math.Max(1, frame.Count));
			case FrameRole.Attack:
				return new FrameRequest(frame.Letter, new[] { "attack", "cast" }, (frame.Index + 1.0) / (frame.Count + 1));
			case FrameRole.Pain:
				return new FrameRequest(frame.Letter, new[] { "hurt", "hit" }, (frame.Index + 1.0) / (frame.Count + 1));
			case FrameRole.Death:
			case FrameRole.Gib:
				return new FrameRequest(frame.Letter, new[] { "die", "hurt" }, (frame.Index + 1.0) / Math.Max(1, frame.Count));
			default:
				return new FrameRequest(frame.Letter, idle, 0);
		}
	}

	/// <summary>
	/// True for the shareware DOOM (episode 1 only), the one game whose monsters are replaced: it has E1M1, no second episode and no DOOM II maps, and the
	/// imp's pictures. Freedoom, the full DOOM and DOOM II keep their own monsters.
	/// </summary>
	public static bool IsShareware(Wad wad) =>
		wad.GetLumpNumber("E1M1") != -1 && wad.GetLumpNumber("E2M1") == -1 && wad.GetLumpNumber("MAP01") == -1 && wad.GetLumpNumber("TROOA1") != -1;
}

/// <summary>Turns captured pictures of a creature into the picture lumps of a DOOM sprite. Godot-free.</summary>
internal static class MonsterSpriteBuilder
{
	/// <summary>A picture with an alpha below this (out of 255) is not counted as part of the creature when finding its edges.</summary>
	private const int EdgeAlpha = 32;
	/// <summary>No sprite is made bigger than this in either direction (DOOM pictures are small; this only guards against a mistake).</summary>
	private const int MaxSize = 200;

	/// <summary>
	/// Makes one lump per frame letter, named like <c>TROOA0</c> (rotation 0: the one picture is used from every side). Every frame is scaled by the same
	/// amount, so the creature does not change size as it moves: the amount that makes the first frame (<paramref name="referenceLetter"/>) as tall as
	/// <paramref name="targetHeight"/>. The creature's standing point is where DOOM puts its feet. A letter with no captured picture gets the first frame's.
	/// </summary>
	public static IReadOnlyList<(string Name, byte[] Data)> Build(string sprite, IReadOnlyList<char> letters, IReadOnlyDictionary<char, CapturedFrame> frames, char referenceLetter, int targetHeight, DoomPalette palette)
	{
		if (frames.Count == 0 || targetHeight <= 0)
		{
			return Array.Empty<(string, byte[])>();
		}
		var reference = frames.TryGetValue(referenceLetter, out var r) ? r : frames.Values.First();
		var referenceBox = Edges(reference.Image) ?? throw new InvalidOperationException("the creature's first picture is empty");
		var scale = targetHeight / (double)referenceBox.Height;
		foreach (var frame in frames.Values)
		{
			var box = Edges(frame.Image);
			if (box != null)
			{
				scale = Math.Min(scale, Math.Min(MaxSize / (double)box.Value.Width, MaxSize / (double)box.Value.Height));
			}
		}

		var lumps = new List<(string, byte[])>();
		foreach (var letter in letters)
		{
			var source = frames.TryGetValue(letter, out var f) && Edges(f.Image) != null ? f : reference;
			var rendered = Render(source, scale, palette);
			lumps.Add((sprite + letter + "0", DoomPatch.Encode(rendered.Width, rendered.Height, rendered.Left, rendered.Top, rendered.Pixels)));
		}
		return lumps;
	}

	/// <summary>The lumps with the markers that tell the engine they are sprites (it only reads sprite pictures from between S_START and S_END).</summary>
	public static IReadOnlyList<(string Name, byte[] Data)> WithMarkers(IReadOnlyList<(string Name, byte[] Data)> lumps)
	{
		var all = new List<(string, byte[])> { ("S_START", Array.Empty<byte>()) };
		all.AddRange(lumps);
		all.Add(("S_END", Array.Empty<byte>()));
		return all;
	}

	private readonly record struct Box(int Left, int Top, int Width, int Height);

	/// <summary>The smallest rectangle holding every pixel that is clearly there (not transparent), or null if there is none.</summary>
	private static Box? Edges(Rgba image)
	{
		int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
		for (var y = 0; y < image.Height; y++)
		{
			for (var x = 0; x < image.Width; x++)
			{
				if (image.Data[4 * (y * image.Width + x) + 3] >= EdgeAlpha)
				{
					minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
					minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
				}
			}
		}
		return maxX < 0 ? null : new Box(minX, minY, maxX - minX + 1, maxY - minY + 1);
	}

	/// <summary>One frame scaled, cut to its edges and matched to the palette; with where the standing point is in it.</summary>
	private static (int[] Pixels, int Width, int Height, int Left, int Top) Render(CapturedFrame frame, double scale, DoomPalette palette)
	{
		var image = frame.Image;
		var box = Edges(image)!.Value;
		var width = Math.Max(1, (int)Math.Ceiling(box.Width * scale));
		var height = Math.Max(1, (int)Math.Ceiling(box.Height * scale));
		var pixels = new int[width * height];
		const float back = 24f; // a dark grey for the soft edge of the creature to blend into, as for the faces
		for (var oy = 0; oy < height; oy++)
		{
			var top = box.Top + oy / scale;
			var bottom = Math.Min(box.Top + box.Height, box.Top + (oy + 1) / scale);
			for (var ox = 0; ox < width; ox++)
			{
				var left = box.Left + ox / scale;
				var right = Math.Min(box.Left + box.Width, box.Left + (ox + 1) / scale);
				double sumR = 0, sumG = 0, sumB = 0, sumA = 0, weight = 0;
				for (var sy = (int)Math.Floor(top); sy < Math.Ceiling(bottom); sy++)
				{
					var wy = Math.Min(bottom, sy + 1) - Math.Max(top, sy);
					if (wy <= 0 || sy < 0 || sy >= image.Height) { continue; }
					for (var sx = (int)Math.Floor(left); sx < Math.Ceiling(right); sx++)
					{
						var wx = Math.Min(right, sx + 1) - Math.Max(left, sx);
						if (wx <= 0 || sx < 0 || sx >= image.Width) { continue; }
						var w = wx * wy;
						var i = 4 * (sy * image.Width + sx);
						var a = image.Data[i + 3] / 255.0;
						sumR += image.Data[i] * a * w; sumG += image.Data[i + 1] * a * w; sumB += image.Data[i + 2] * a * w; sumA += a * w; weight += w;
					}
				}
				var alpha = weight > 0 ? sumA / weight : 0;
				if (alpha < 0.5 || sumA <= 0)
				{
					pixels[oy * width + ox] = -1; // transparent
					continue;
				}
				pixels[oy * width + ox] = palette.Nearest(back + (sumR / sumA - back) * alpha, back + (sumG / sumA - back) * alpha, back + (sumB / sumA - back) * alpha);
			}
		}
		return (pixels, width, height, (int)Math.Round((frame.OriginX - box.Left) * scale), (int)Math.Round((frame.OriginY - box.Top) * scale));
	}
}
