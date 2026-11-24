using System;
using System.IO;
using System.Linq;
using DoomedSpire.Host;
using Godot;
using ManagedDoom;

namespace DoomedSpire;

/// <summary>
/// Makes the status-bar faces from the face of the character the player is playing. Reads the character's picture from the game (its own art, from the
/// player's installed game, read at run time and never stored in the mod), has FaceFactory turn it into DOOM's 42 faces, and writes them as a small add-on
/// WAD that the engine loads on top of the game, so its faces replace Doomguy's. If anything is missing or goes wrong the game just starts with Doomguy.
/// Every playable character has its own painted faces built into the mod (assets/&lt;character&gt;-faces.png); the faces are made from the character's picture only
/// for a character that has none (a modded one, say).
/// </summary>
internal static class PlayerFace
{
	/// <summary>How many pixels to the right of where the sheets put them the faces are drawn (the faces looked slightly left of centre in the face panel).</summary>
	private const int FaceShift = 1;

	/// <summary>Builds the add-on WAD for the current character into <paramref name="folder"/> and returns its path, or null (log says why) to keep Doomguy.</summary>
	public static string? TryBuild(string iwadPath, string folder)
	{
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
			var lumps = FaceFactory.Build(picture, spec, palette, FaceSettings.Default);

			Directory.CreateDirectory(folder);
			var id = character.Id.Entry.ToLowerInvariant();

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

			// Every face is drawn a pixel to the right of where the sheets put it: the faces sat a little left of the middle of the status bar's face panel.
			lumps = lumps.Select(l => (l.Name, DoomPatch.ShiftRight(l.Data, FaceShift))).ToList();
			var path = Path.Combine(folder, "face.wad");
			File.WriteAllBytes(path, PwadWriter.Build(lumps));

			ModLog.Info("face", $"{how ?? "made the faces of"} {character.Id.Entry} ({lumps.Count} faces, from a {picture.Width}x{picture.Height} picture)");
			return path;
		}
		catch (Exception e)
		{
			ModLog.Fail("face", "making the character's face", e);
			return null;
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

	/// <summary>The character's picture (the small portrait in the game's top bar), as plain pixels. Null if it cannot be read.</summary>
	private static Rgba? ReadPicture(MegaCrit.Sts2.Core.Models.CharacterModel character)
	{
		Texture2D? texture = character.IconTexture;
		if (texture == null)
		{
			ModLog.OnceInfo("face", "no-texture", $"the character {character.Id.Entry} has no portrait, so Doomguy's own face is used");
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
}
