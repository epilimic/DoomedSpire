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
	/// The frames that get an enemy's picture: every one except the dying and gibbed ones, which keep DOOM's own dead and mangled bodies (an enemy's death
	/// animation does not make a body that can lie on the floor).
	/// </summary>
	public static IReadOnlyList<MonsterFrame> ReplacedFrames(MobjType type, IReadOnlyList<char> letters) =>
		Frames(type, letters).Where(f => f.Role != FrameRole.Death && f.Role != FrameRole.Gib).ToList();

	/// <summary>The lump holding a frame seen from every side (for example TROOU0), or null if the game data has none.</summary>
	public static string? FindLump(Wad wad, string sprite, char letter)
	{
		foreach (var info in wad.LumpInfos)
		{
			var name = info.Name;
			if (info.Size > 0 && ((name.Length == 6 && name == sprite + letter + "0") || (name.Length == 8 && name.StartsWith(sprite, StringComparison.Ordinal) && name[6] == letter && name[7] == '0')))
			{
				return name;
			}
		}
		return null;
	}

	/// <summary>How many frames at the start of each of DOOM's gib animations still show the creature whole or bursting (the first is the creature itself, the second its body in pieces).</summary>
	public const int FramesBeforeTheBits = 2;

	/// <summary>
	/// DOOM's own gib animations, as the pictures of each in order, ending on the heap and without their first frames (<see cref="FramesBeforeTheBits"/>): the player's, the imp's, the zombieman's and the shotgun guy's (the
	/// frames each is gibbed through, from the engine's state tables). A set the game data does not have in full is left out.
	/// </summary>
	public static IReadOnlyList<IReadOnlyList<byte[]>> GibAnimations(Wad wad)
	{
		var sets = new List<IReadOnlyList<byte[]>>();
		foreach (var type in new[] { MobjType.Player, MobjType.Troop, MobjType.Possessed, MobjType.Shotguy })
		{
			var sprite = SpriteName(type);
			var chain = Frames(type, Letters(wad, sprite)).Where(f => f.Role == FrameRole.Gib).OrderBy(f => f.Index).ToList();
			var pictures = new List<byte[]>();
			foreach (var frame in chain)
			{
				var name = FindLump(wad, sprite, frame.Letter);
				if (name == null)
				{
					pictures.Clear();
					break;
				}
				pictures.Add(wad.ReadLump(name));
			}
			// The first frames of a gib animation are the creature itself bursting (the player with his gun, the imp, the zombieman), which is what must not show:
			// start from the frame where it is only bits flying.
			if (pictures.Count > FramesBeforeTheBits)
			{
				sets.Add(pictures.Skip(FramesBeforeTheBits).ToList());
			}
		}
		return sets;
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
	/// True for the original DOOM 1 (shareware, registered or Ultimate), the games whose monsters are replaced: it has E1M1 and the imp's pictures, no DOOM II
	/// maps, and none of the lumps Freedoom carries (Freedoom is a replacement game, with its own monsters and a second episode like the full DOOM, so the
	/// maps alone cannot tell them apart). DOOM II and any other game keep their own monsters.
	/// </summary>
	public static bool IsOriginalDoom1(Wad wad) =>
		wad.GetLumpNumber("E1M1") != -1 && wad.GetLumpNumber("MAP01") == -1 && wad.GetLumpNumber("TROOA1") != -1
		&& wad.GetLumpNumber("FREEDOOM") == -1 && wad.GetLumpNumber("DEHACKED") == -1;
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

	/// <summary>
	/// The dying and gibbed frames of a monster, each as eight variations: side 1 to 8 of the frame are the same moment of eight different gib animations
	/// (<paramref name="gibs"/>, cycled if there are fewer than eight), the frame's place in its own chain (first to last) picking the moment, so the last
	/// frame, the one that stays on the floor, is the heap. Named like <c>TROOI3</c>. Which side a corpse shows is chosen by <see cref="CorpseVariant"/>.
	/// </summary>
	public static IReadOnlyList<(string Name, byte[] Data)> DeathLumps(string sprite, IReadOnlyList<MonsterFrame> frames, IReadOnlyList<IReadOnlyList<byte[]>> gibs)
	{
		var lumps = new List<(string, byte[])>();
		if (gibs.Count == 0)
		{
			return lumps;
		}
		foreach (var frame in frames)
		{
			for (var side = 1; side <= 8; side++)
			{
				var set = gibs[(side - 1) % gibs.Count];
				var index = frame.Count <= 1 ? set.Count - 1 : (int)Math.Round(frame.Index * (set.Count - 1) / (double)(frame.Count - 1));
				lumps.Add((sprite + frame.Letter + side, set[index]));
			}
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
