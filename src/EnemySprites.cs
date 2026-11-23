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
	/// <summary>
	/// The enemy that takes the place of each DOOM monster. To try another enemy, change the class here.
	/// <b>Some of these are swapped for now</b>, so that the enemies the owner wants to look at come up in the first levels instead of the last: the baron has the
	/// zombieman's enemy (ThievingHopper) and the zombieman has the baron's (SkulkingColony); the cacodemon has the shotgun guy's (Fogmog); the imp has the
	/// cacodemon's (SewerClam) and the shotgun guy has the imp's (Exoskeleton). Next the lost soul's and the spider mastermind's enemies (TheLost, Fabricator)
	/// take those places, then the cyberdemon's (KnowledgeDemon, with Entomancer as the other candidate); then it all goes back to the lines marked "final".
	/// </summary>
	private static readonly (MobjType Monster, Func<MonsterModel> Enemy)[] Replacements =
	{
		(MobjType.Possessed, () => ModelDb.Monster<Monsters.SkulkingColony>()), // zombieman (final: ThievingHopper)
		(MobjType.Shotguy, () => ModelDb.Monster<Monsters.Exoskeleton>()),      // shotgun guy (final: Fogmog)
		(MobjType.Troop, () => ModelDb.Monster<Monsters.SewerClam>()),          // imp (final: Exoskeleton)
		(MobjType.Sergeant, () => ModelDb.Monster<Monsters.Axebot>()),          // demon (and the spectre)
		(MobjType.Bruiser, () => ModelDb.Monster<Monsters.ThievingHopper>()),   // baron of hell (final: SkulkingColony)
		(MobjType.Skull, () => ModelDb.Monster<Monsters.TheLost>()),            // lost soul
		(MobjType.Head, () => ModelDb.Monster<Monsters.Fogmog>()),              // cacodemon (final: SewerClam)
		(MobjType.Cyborg, () => ModelDb.Monster<Monsters.KnowledgeDemon>()),    // cyberdemon (the other candidate is Entomancer)
		(MobjType.Spider, () => ModelDb.Monster<Monsters.Fabricator>()),        // spider mastermind
	};

	/// <summary>After this long the rest of the enemies are not drawn: the game starts with what there is (the rest keep DOOM's own pictures).</summary>
	private static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(30);

	/// <summary>
	/// Builds the add-on WAD of enemy pictures into <paramref name="folder"/> and returns its path, or null (the log says why) to keep DOOM's own monsters.
	/// The result is kept: the pictures only change with the game, the mod or the DOOM game, so while those are the same (and every enemy could be drawn) the
	/// saved file is used and nothing is drawn, which makes starting a game instant. Delete <c>enemies.key</c> in the data folder to draw them again.
	/// </summary>
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
			var chosen = Replacements.Select(r => (r.Monster, Enemy: r.Enemy())).ToList();
			var path = Path.Combine(folder, "enemies.wad");
			var keyPath = Path.Combine(folder, "enemies.key");
			var key = CacheKey(iwadPath, chosen.Select(c => $"{c.Monster}={c.Enemy.Id.Entry}"));
			if (key != null && File.Exists(path) && File.Exists(keyPath) && File.ReadAllText(keyPath) == key)
			{
				ModLog.Info("enemy", $"using the enemy pictures saved earlier ({path}); delete enemies.key to draw them again");
				return path;
			}
			if (File.Exists(keyPath))
			{
				File.Delete(keyPath); // whatever is saved is out of date from here on
			}
			var palette = new DoomPalette(wad.ReadLump("PLAYPAL"));
			var complete = true;
			var lumps = new List<(string Name, byte[] Data)>();
			var done = new List<string>();
			var gibs = MonsterSprites.GibAnimations(wad);

			foreach (var (type, monster) in chosen)
			{
				if (timer.Elapsed > TimeLimit)
				{
					ModLog.Info("enemy", $"{TimeLimit.TotalSeconds:0} seconds are up, so the rest keep DOOM's own pictures");
					complete = false;
					break;
				}
				var sprite = MonsterSprites.SpriteName(type);
				var letters = MonsterSprites.Letters(wad, sprite);
				var height = MonsterSprites.OriginalHeight(wad, sprite);
				if (letters.Count == 0 || height <= 0)
				{
					continue; // this game does not have that monster
				}
				// Only the living poses get the enemy's picture; the dying and gibbed frames become DOOM's gibs (below).
				var replaced = MonsterSprites.ReplacedFrames(type, letters);
				var keptLetters = replaced.Select(f => f.Letter).ToList();
				var requests = replaced.Select(MonsterSprites.Request).ToList();
				if (requests.Count == 0)
				{
					continue;
				}

				Dictionary<char, CapturedFrame>? frames;
				try
				{
					frames = await EnemyCapture.CaptureAsync(host, monster, requests);
				}
				catch (TimeoutException e)
				{
					ModLog.Fail("enemy", $"drawing {monster.Id.Entry} (the game stopped drawing, so the rest keep DOOM's own pictures)", e);
					complete = false;
					break;
				}
				if (frames == null || !frames.ContainsKey(letters[0]))
				{
					ModLog.Info("enemy", $"{monster.Id.Entry} could not be drawn for the {sprite} ({type}), so it keeps DOOM's own picture (the lines above say why)");
					complete = false;
					continue;
				}
				var built = MonsterSpriteBuilder.Build(sprite, keptLetters, frames, letters[0], height, palette);
				lumps.AddRange(built);
				// ...and the enemy turns into gibs when it dies: the dying and gibbed frames are DOOM's gib animations, eight variations of them.
				var dying = MonsterSprites.Frames(type, letters).Where(f => f.Role == FrameRole.Death || f.Role == FrameRole.Gib).ToList();
				var gibLumps = MonsterSpriteBuilder.DeathLumps(sprite, dying, gibs);
				lumps.AddRange(gibLumps);
				done.Add($"{type}={monster.Id.Entry}");
				ModLog.Info("enemy", $"the {sprite} ({type}) is now {monster.Id.Entry}: {frames.Count} of {requests.Count} poses captured, {built.Count} pictures, scaled to {height} tall; {gibLumps.Count} gib pictures for its death");
			}

			if (lumps.Count == 0)
			{
				return null;
			}
			Directory.CreateDirectory(folder);
			var temp = path + ".tmp"; // written under another name and moved into place, so a crash half way never leaves a short file under the real name
			File.WriteAllBytes(temp, PwadWriter.Build(MonsterSpriteBuilder.WithMarkers(lumps)));
			File.Move(temp, path, overwrite: true);
			if (complete && key != null)
			{
				File.WriteAllText(keyPath, key);
			}
			ModLog.Info("enemy", $"enemy pictures ready in {timer.ElapsedMilliseconds} ms: {string.Join(", ", done)} ({path}){(complete && key != null ? "; saved for the next game" : "; not saved (something was left out or the game version is not known), so they are drawn again next time")}");
			return path;
		}
		catch (Exception e)
		{
			ModLog.Fail("enemy", "making the enemies", e);
			return null;
		}
	}

	/// <summary>
	/// What the saved pictures depend on: the game's version, this build of the mod, the DOOM game (name and size) and who plays which monster. Null when the game's
	/// version cannot be read (then nothing is saved, as a game update could not be noticed).
	/// </summary>
	private static string? CacheKey(string iwadPath, IEnumerable<string> enemies)
	{
		string? gameVersion = null;
		try
		{
			gameVersion = MegaCrit.Sts2.Core.Debug.ReleaseInfoManager.Instance.ReleaseInfo?.Version;
		}
		catch (Exception e)
		{
			ModLog.Fail("enemy", "reading the game's version", e);
		}
		if (string.IsNullOrEmpty(gameVersion))
		{
			return null;
		}
		var build = typeof(EnemySprites).Assembly.ManifestModule.ModuleVersionId;
		return string.Join("|", gameVersion, build, Path.GetFileName(iwadPath), new FileInfo(iwadPath).Length, MonsterSprites.FramesBeforeTheBits, string.Join(",", enemies));
	}
}
