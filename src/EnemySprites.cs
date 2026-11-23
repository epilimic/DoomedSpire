using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DoomedSpire.Host;
using ManagedDoom;
using MegaCrit.Sts2.Core.Models;
using Monsters = MegaCrit.Sts2.Core.Models.Monsters;

namespace DoomedSpire;

/// <summary>
/// Replaces DOOM's monsters with Slay the Spire enemies, for the original DOOM 1 games only (shareware, registered, Ultimate; any other game keeps its own
/// monsters). Each monster has the enemy the owner chose for it (<see cref="Replacements"/>). The enemy's animations are captured from the game
/// (<see cref="EnemyCapture"/>), turned into DOOM sprite pictures (<see cref="MonsterSpriteBuilder"/>) and delivered as an add-on WAD that the engine loads on
/// top of the game, like the faces. Nothing from the game is stored in the mod. A monster the game data does not have (the shareware game has no cacodemon,
/// for one), or an enemy that cannot be drawn, is skipped and the monster keeps DOOM's own picture. The spectre is the demon's pictures drawn fuzzy, so it
/// is the demon's enemy.
/// </summary>
internal static class EnemySprites
{
	/// <summary>The enemy that takes the place of each DOOM monster. To try another enemy, change the class here.</summary>
	private static readonly (MobjType Monster, Func<MonsterModel> Enemy)[] Replacements =
	{
		(MobjType.Possessed, () => ModelDb.Monster<Monsters.ThievingHopper>()), // zombieman
		(MobjType.Shotguy, () => ModelDb.Monster<Monsters.Fogmog>()),           // shotgun guy
		(MobjType.Troop, () => ModelDb.Monster<Monsters.Exoskeleton>()),        // imp
		(MobjType.Sergeant, () => ModelDb.Monster<Monsters.Axebot>()),          // demon (and the spectre)
		(MobjType.Bruiser, () => ModelDb.Monster<Monsters.SkulkingColony>()),   // baron of hell
		(MobjType.Skull, () => ModelDb.Monster<Monsters.TheLost>()),            // lost soul
		(MobjType.Head, () => ModelDb.Monster<Monsters.SewerClam>()),           // cacodemon
		(MobjType.Cyborg, () => ModelDb.Monster<Monsters.KnowledgeDemon>()),    // cyberdemon (the other candidate is Entomancer)
		(MobjType.Spider, () => ModelDb.Monster<Monsters.Fabricator>()),        // spider mastermind
	};

	/// <summary>Builds the add-on WAD of enemy pictures into <paramref name="folder"/> and returns its path, or null (the log says why) to keep DOOM's own monsters.</summary>
	public static async Task<string?> TryBuildAsync(Godot.Node host, string iwadPath, string folder)
	{
		try
		{
			using var wad = new Wad(iwadPath);
			if (!MonsterSprites.IsOriginalDoom1(wad))
			{
				ModLog.OnceInfo("enemy", "not-doom1", "this is not the original DOOM 1 (shareware, registered or Ultimate), so its own monsters are kept (only those get Slay the Spire enemies)");
				return null;
			}
			if (wad.GetLumpNumber("PLAYPAL") == -1)
			{
				return null;
			}

			var timer = Stopwatch.StartNew();
			var palette = new DoomPalette(wad.ReadLump("PLAYPAL"));
			var lumps = new List<(string Name, byte[] Data)>();
			var done = new List<string>();

			foreach (var (type, enemy) in Replacements)
			{
				var sprite = MonsterSprites.SpriteName(type);
				var letters = MonsterSprites.Letters(wad, sprite);
				var height = MonsterSprites.OriginalHeight(wad, sprite);
				if (letters.Count == 0 || height <= 0)
				{
					continue; // this game does not have that monster
				}
				var requests = MonsterSprites.Frames(type, letters).Select(MonsterSprites.Request).ToList();

				var monster = enemy();
				var frames = await EnemyCapture.CaptureAsync(host, monster, requests);
				if (frames == null || !frames.ContainsKey(letters[0]))
				{
					ModLog.Info("enemy", $"{monster.Id.Entry} could not be drawn for the {sprite} ({type}), so it keeps DOOM's own picture (the lines above say why)");
					continue;
				}
				var built = MonsterSpriteBuilder.Build(sprite, letters, frames, letters[0], height, palette);
				lumps.AddRange(built);
				done.Add($"{type}={monster.Id.Entry}");
				ModLog.Info("enemy", $"the {sprite} ({type}) is now {monster.Id.Entry}: {frames.Count} of {requests.Count} poses captured, {built.Count} pictures, scaled to {height} tall");
			}

			if (lumps.Count == 0)
			{
				return null;
			}
			Directory.CreateDirectory(folder);
			var path = Path.Combine(folder, "enemies.wad");
			File.WriteAllBytes(path, PwadWriter.Build(MonsterSpriteBuilder.WithMarkers(lumps)));
			ModLog.Info("enemy", $"enemy pictures ready in {timer.ElapsedMilliseconds} ms: {string.Join(", ", done)} ({path})");
			return path;
		}
		catch (Exception e)
		{
			ModLog.Fail("enemy", "making the enemies", e);
			return null;
		}
	}
}
