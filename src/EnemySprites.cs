using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DoomedSpire.Host;
using Godot;
using ManagedDoom;
using MegaCrit.Sts2.Core.Models;

namespace DoomedSpire;

/// <summary>
/// Replaces DOOM's monsters with Slay the Spire enemies, for the shareware game only (any other game keeps its own monsters). The enemy's animations are
/// captured from the game (<see cref="EnemyCapture"/>), turned into DOOM sprite pictures (<see cref="MonsterSpriteBuilder"/>) and delivered as an add-on WAD that
/// the engine loads on top of the game, like the faces. Nothing from the game is stored in the mod. If anything is missing or goes wrong, the game starts with
/// DOOM's own monsters.
/// <b>This is the first step:</b> only the imp is replaced, by a random enemy of the first act. The other monsters, the choice of act per level and the seeding
/// follow once this is seen working.
/// </summary>
internal static class EnemySprites
{
	/// <summary>How many different enemies to try for the imp if the first ones cannot be drawn.</summary>
	private const int Attempts = 4;

	/// <summary>The monsters of DOOM that are replaced so far.</summary>
	private static readonly MobjType[] Replaced = { MobjType.Troop };

	/// <summary>Builds the add-on WAD of enemy pictures into <paramref name="folder"/> and returns its path, or null (the log says why) to keep DOOM's own monsters.</summary>
	public static async Task<string?> TryBuildAsync(Godot.Node host, string iwadPath, string folder)
	{
		try
		{
			using var wad = new Wad(iwadPath);
			if (!MonsterSprites.IsShareware(wad))
			{
				ModLog.OnceInfo("enemy", "not-shareware", "this is not the shareware DOOM, so its own monsters are kept (only the shareware game gets Slay the Spire enemies)");
				return null;
			}
			if (wad.GetLumpNumber("PLAYPAL") == -1)
			{
				return null;
			}

			var timer = Stopwatch.StartNew();
			var palette = new DoomPalette(wad.ReadLump("PLAYPAL"));
			var act = ModelDb.Acts.First();
			var pool = act.AllMonsters.OrderBy(_ => Random.Shared.Next()).ToList();
			var lumps = new List<(string Name, byte[] Data)>();

			foreach (var type in Replaced)
			{
				var sprite = MonsterSprites.SpriteName(type);
				var letters = MonsterSprites.Letters(wad, sprite);
				var requests = MonsterSprites.Frames(type, letters).Select(MonsterSprites.Request).ToList();
				var height = MonsterSprites.OriginalHeight(wad, sprite);
				if (requests.Count == 0 || height <= 0)
				{
					continue;
				}

				var made = false;
				foreach (var monster in pool.Take(Attempts))
				{
					var frames = await EnemyCapture.CaptureAsync(host, monster, requests);
					if (frames == null || !frames.ContainsKey(letters[0]))
					{
						continue; // could not be drawn (the log says why): try another enemy
					}
					var built = MonsterSpriteBuilder.Build(sprite, letters, frames, letters[0], height, palette);
					lumps.AddRange(built);
					ModLog.Info("enemy", $"the {sprite} ({type}) is now {monster.Id.Entry} from {act.Id.Entry}: {frames.Count} of {requests.Count} poses captured, {built.Count} pictures");
					made = true;
					break;
				}
				if (!made)
				{
					ModLog.Info("enemy", $"no enemy of {act.Id.Entry} could be drawn for the {sprite}, so it keeps DOOM's own picture");
				}
			}

			if (lumps.Count == 0)
			{
				return null;
			}
			Directory.CreateDirectory(folder);
			var path = Path.Combine(folder, "enemies.wad");
			File.WriteAllBytes(path, PwadWriter.Build(MonsterSpriteBuilder.WithMarkers(lumps)));
			ModLog.Info("enemy", $"enemy pictures ready in {timer.ElapsedMilliseconds} ms ({path})");
			return path;
		}
		catch (Exception e)
		{
			ModLog.Fail("enemy", "making the enemies", e);
			return null;
		}
	}
}
