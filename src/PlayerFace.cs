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

			// A sheet to paint on for every playable character (so all of them can be painted without playing each), made again at every new game, so it is never the
			// file that holds the player's work (that is the "painted" one below). See WriteSheetsToPaint.
			var toPaint = Path.Combine(folder, id + "-faces-to-paint.png");
			WriteSheetsToPaint(folder, spec, palette, settings);

			// The faces painted for this character, if there are any, are used instead of the generated ones: first the player's own file in the data folder (to try
			// out a sheet before it is built in), then the one built into the mod (assets/<character>-faces.png).
			var painted = Path.Combine(folder, id + "-faces-painted.png");
			string? how = null;
			if (File.Exists(painted))
			{
				try
				{
					lumps = FaceSheetImport.Read(LoadPicture(painted), spec, palette);
					how = "using your painted faces from the data folder for";
				}
				catch (Exception e)
				{
					ModLog.Fail("face", "reading your painted face sheet (the next choice is used instead)", e);
				}
			}
			if (how == null)
			{
				try
				{
					var shipped = BuiltInSheet(id);
					if (shipped != null)
					{
						lumps = FaceSheetImport.Read(shipped, spec, palette);
						how = "using the painted faces built into the mod for";
					}
				}
				catch (Exception e)
				{
					ModLog.Fail("face", "reading the painted face sheet built into the mod (the generated faces are used instead)", e);
				}
			}

			var preview = Path.Combine(folder, "face-sheet.png");
			WriteSheet(FaceFactory.Sheet(lumps, palette, 6), preview);
			var path = Path.Combine(folder, "face.wad");
			File.WriteAllBytes(path, PwadWriter.Build(lumps));

			ModLog.Info("face", $"{how ?? "made the faces of"} {character.Id.Entry} ({lumps.Count} faces, from a {picture.Width}x{picture.Height} picture, source {FaceTuning.Source}); " +
				$"a preview is in {preview}; to paint your own, edit {toPaint} and save it as {painted}");
			return path;
		}
		catch (Exception e)
		{
			ModLog.Fail("face", "making the character's face", e);
			return null;
		}
	}

	/// <summary>
	/// Writes <c>&lt;character&gt;-faces-to-paint.png</c> for every playable character, not only the one in this run. A character that cannot be made is skipped (the log says why).
	/// The sheet is every face on a transparent background, 1:1, the same colour in every one and without blood (<see cref="FaceSettings.Neutral"/>).
	/// </summary>
	private static void WriteSheetsToPaint(string folder, Func<string, PatchSpec> spec, DoomPalette palette, FaceSettings settings)
	{
		foreach (var character in MegaCrit.Sts2.Core.Models.ModelDb.AllCharacters)
		{
			try
			{
				if (!character.IsPlayable)
				{
					continue;
				}
				var picture = ReadPicture(character);
				if (picture == null)
				{
					continue;
				}
				var faces = FaceFactory.Build(picture, spec, palette, settings with { Blood = 0f, Neutral = true });
				WriteSheet(FaceFactory.Sheet(faces, palette, 1, transparentBackground: true), Path.Combine(folder, character.Id.Entry.ToLowerInvariant() + "-faces-to-paint.png"));
			}
			catch (Exception e)
			{
				ModLog.Fail("face", "writing the sheet to paint for " + character.Id.Entry, e);
			}
		}
	}

	/// <summary>The faces painted for a character and built into the mod, <c>assets/&lt;character&gt;-faces.png</c> (the same layout as the sheet to paint on), or null if there is none.</summary>
	private static Rgba? BuiltInSheet(string id)
	{
		using var stream = typeof(PlayerFace).Assembly.GetManifestResourceStream(id + "-faces.png");
		if (stream == null)
		{
			return null;
		}
		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		var image = new Image();
		if (image.LoadPngFromBuffer(buffer.ToArray()) != Godot.Error.Ok)
		{
			throw new InvalidDataException(id + "-faces.png could not be read as a picture");
		}
		return ToPixels(image) ?? throw new InvalidDataException(id + "-faces.png is empty");
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
	internal static Rgba? ToPixels(Image? image)
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
