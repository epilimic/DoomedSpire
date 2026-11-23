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
			Func<string, PatchSpec> spec = name =>
			{
				var original = Patch.FromWad(wad, name);
				return new PatchSpec(original.Width, original.Height, original.LeftOffset, original.TopOffset);
			};
			var settings = FaceSettings.FromTuning();
			var lumps = FaceFactory.Build(picture, spec, palette, settings);

			Directory.CreateDirectory(folder);
			var id = character.Id.Entry.ToLowerInvariant();

			// A sheet to paint on: every face at one picture pixel per face pixel on a transparent background, , the same colour in every one (no paleness, redness or tints) and never any
			// blood: only the head's position and angle differ, as in Doomguy's own faces (the colour and blood are for the player to add). It is made again at every new game, so it is never the file that holds the player's work (that is the "painted" one below).
			var toPaint = Path.Combine(folder, id + "-faces-to-paint.png");
			WriteSheet(FaceFactory.Sheet(FaceFactory.Build(picture, spec, palette, settings with { Blood = 0f, Neutral = true }), palette, 1, transparentBackground: true), toPaint);

			// The player's own painted version of that sheet, if they saved one under this name, is used instead of the generated faces.
			var painted = Path.Combine(folder, id + "-faces-painted.png");
			var usedPainted = false;
			if (File.Exists(painted))
			{
				try
				{
					var sheet = LoadPicture(painted);
					lumps = FaceSheetImport.Read(sheet, spec, palette);
					usedPainted = true;
				}
				catch (Exception e)
				{
					ModLog.Fail("face", "reading your painted face sheet (the generated faces are used instead)", e);
				}
			}

			var preview = Path.Combine(folder, "face-sheet.png");
			WriteSheet(FaceFactory.Sheet(lumps, palette, 6), preview);
			var path = Path.Combine(folder, "face.wad");
			File.WriteAllBytes(path, PwadWriter.Build(lumps));

			ModLog.Info("face", $"{(usedPainted ? "using your painted faces for" : "made the faces of")} {character.Id.Entry} ({lumps.Count} faces, from a {picture.Width}x{picture.Height} picture, source {FaceTuning.Source}); " +
				$"a preview is in {preview}; to paint your own, edit {toPaint} and save it as {painted}");
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

		var picture = ToPixels(texture.GetImage());
		if (picture == null)
		{
			ModLog.OnceInfo("face", "empty-image", "the character's picture could not be read, so Doomguy's own face is used");
		}
		return picture;
	}

	/// <summary>A picture as plain pixels (R, G, B, A each). Null if it is empty.</summary>
	private static Rgba? ToPixels(Image? image)
	{
		if (image == null || image.IsEmpty())
		{
			return null;
		}
		if (image.IsCompressed())
		{
			image.Decompress(); // pictures in the game are stored compressed for the graphics card; this turns it back into pixels
		}
		image.Convert(Image.Format.Rgba8);
		return new Rgba(image.GetWidth(), image.GetHeight(), image.GetData());
	}

	/// <summary>A picture file of the player's, as plain pixels. Throws if it cannot be read.</summary>
	private static Rgba LoadPicture(string path)
	{
		return ToPixels(Image.LoadFromFile(path)) ?? throw new InvalidDataException($"{Path.GetFileName(path)} could not be read as a picture");
	}

	/// <summary>Saves a face sheet as a PNG. Failing to write it is not a problem for the game.</summary>
	private static void WriteSheet(Rgba sheet, string path)
	{
		try
		{
			Image.CreateFromData(sheet.Width, sheet.Height, false, Image.Format.Rgba8, sheet.Data).SavePng(path);
		}
		catch (Exception e)
		{
			ModLog.Fail("face", "saving a face sheet", e);
		}
	}
}
