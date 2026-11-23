using System;
using System.IO;
using DoomedSpire.Host;
using Godot;
using ManagedDoom;

namespace DoomedSpire;

/// <summary>
/// Makes the status-bar faces from the face of the character the player is playing. Reads the character's picture from the game (its own art, from the
/// player's installed game, read at run time and never stored in the mod), has FaceFactory turn it into DOOM's 42 faces, and writes them as a small add-on
/// WAD that the engine loads on top of the game, so its faces replace Doomguy's. If anything is missing or goes wrong the game just starts with Doomguy.
/// The numbers that decide how it looks are in FaceTuning (live-tunable with VfxSpawner).
/// </summary>
internal static class PlayerFace
{
	/// <summary>Builds the add-on WAD for the current character into <paramref name="folder"/> and returns its path, or null (log says why) to keep Doomguy.</summary>
	public static string? TryBuild(string iwadPath, string folder)
	{
		if (!FaceTuning.UseCharacterFace)
		{
			return null;
		}

		try
		{
			var character = ActiveRun.LocalCharacter();
			if (character == null)
			{
				ModLog.OnceInfo("face", "no-character", "no character known for this run, so Doomguy's own face is used");
				return null;
			}

			var picture = ReadPicture(character);
			if (picture == null)
			{
				return null;
			}

			using var wad = new Wad(iwadPath);
			if (wad.GetLumpNumber("STFST00") == -1 || wad.GetLumpNumber("PLAYPAL") == -1)
			{
				ModLog.OnceInfo("face", "no-faces-in-wad", "this game has no status-bar faces to replace, so it keeps its own");
				return null;
			}

			var palette = new DoomPalette(wad.ReadLump("PLAYPAL"));
			var lumps = FaceFactory.Build(picture, name =>
			{
				var original = Patch.FromWad(wad, name);
				return new PatchSpec(original.Width, original.Height, original.LeftOffset, original.TopOffset);
			}, palette, FaceSettings.FromTuning());

			Directory.CreateDirectory(folder);
			var path = Path.Combine(folder, "face.wad");
			File.WriteAllBytes(path, PwadWriter.Build(lumps));
			WritePreview(lumps, palette, Path.Combine(folder, "face-sheet.png"));

			ModLog.Info("face", $"made the {lumps.Count} faces of {character.Id.Entry} from a {picture.Width}x{picture.Height} picture (source {FaceTuning.Source}); a preview is in {Path.Combine(folder, "face-sheet.png")}");
			return path;
		}
		catch (Exception e)
		{
			ModLog.Fail("face", "making the character's face", e);
			return null;
		}
	}

	/// <summary>The character's picture, as the game has it (the source is FaceTuning.Source), as plain pixels. Null if it cannot be read.</summary>
	private static Rgba? ReadPicture(MegaCrit.Sts2.Core.Models.CharacterModel character)
	{
		Texture2D? texture = FaceTuning.Source switch
		{
			1 => character.CharacterSelectIcon,
			2 => character.MapMarker,
			_ => character.IconTexture,
		};
		if (texture == null)
		{
			ModLog.OnceInfo("face", "no-texture", $"the character {character.Id.Entry} has no picture for source {FaceTuning.Source}, so Doomguy's own face is used");
			return null;
		}

		var image = texture.GetImage();
		if (image == null || image.IsEmpty())
		{
			ModLog.OnceInfo("face", "empty-image", "the character's picture could not be read, so Doomguy's own face is used");
			return null;
		}
		if (image.IsCompressed())
		{
			image.Decompress(); // pictures in the game are stored compressed for the graphics card; this turns it back into pixels
		}
		image.Convert(Image.Format.Rgba8);
		return new Rgba(image.GetWidth(), image.GetHeight(), image.GetData());
	}

	/// <summary>A picture of all 42 faces next to the add-on WAD, for a person to look at and decide what to tune. Failing to write it is not a problem.</summary>
	private static void WritePreview(System.Collections.Generic.IReadOnlyList<(string Name, byte[] Data)> lumps, DoomPalette palette, string path)
	{
		try
		{
			var sheet = FaceFactory.Sheet(lumps, palette, 6);
			Image.CreateFromData(sheet.Width, sheet.Height, false, Image.Format.Rgba8, sheet.Data).SavePng(path);
		}
		catch (Exception e)
		{
			ModLog.Fail("face", "saving the face preview", e);
		}
	}
}
