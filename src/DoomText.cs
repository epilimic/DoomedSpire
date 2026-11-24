using System;
using DoomedSpire.Host;
using Godot;
using ManagedDoom;

namespace DoomedSpire;

/// <summary>
/// Text in DOOM's own font for the mod's own screens (the game picker): pictures made by <see cref="DoomFont"/> from the built-in game data's STCFN letters, shown with
/// nearest-neighbour scaling so every pixel stays sharp. DOOM's pixels are not square (its 320 x 200 picture is shown as 4:3), so a font pixel is drawn 3.2 wide and
/// 3.84 tall, as the game's own picture is on the 1024 x 768 screen area. If the font cannot be made (the log says why) <see cref="Font"/> is null and the screens keep
/// their plain game text.
/// </summary>
internal static class DoomText
{
	/// <summary>How big one font pixel is on the screen area (1024 x 768 for DOOM's 320 x 200).</summary>
	public const float PixelWidth = 3.2f, PixelHeight = 3.84f;

	private static DoomFont? font;
	private static bool tried;
	private static readonly ImageTexture?[] skulls = new ImageTexture?[2];

	/// <summary>DOOM's font, made once from the built-in game data; null if it could not be made.</summary>
	public static DoomFont? Font
	{
		get
		{
			if (!tried)
			{
				tried = true;
				try
				{
					using var wad = new Wad(BuiltInWad.EnsureOnDisk());
					font = DoomFont.Load(wad);
					if (font == null)
					{
						ModLog.Warn("text", "the built-in game data has no font, so the game picker keeps the game's plain text");
					}
				}
				catch (Exception e)
				{
					ModLog.Fail("text", "making DOOM's font", e);
				}
			}
			return font;
		}
	}

	/// <summary>A picture as a texture.</summary>
	public static ImageTexture Texture(Rgba picture) =>
		ImageTexture.CreateFromImage(Image.CreateFromData(picture.Width, picture.Height, false, Image.Format.Rgba8, picture.Data));

	/// <summary>
	/// The text in DOOM's font as a control, wrapped to <paramref name="maxWidth"/> pixels of the screen area, zoomed <paramref name="zoom"/> times (1 = as the game's
	/// own lines). Does not take the mouse.
	/// </summary>
	public static TextureRect Make(DoomFont doomFont, string text, float maxWidth, bool centred = false, int zoom = 1)
	{
		var picture = doomFont.Render(text, (int)(maxWidth / (PixelWidth * zoom)), centred);
		return new TextureRect
		{
			Texture = Texture(picture),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			CustomMinimumSize = new Vector2(picture.Width * PixelWidth * zoom, picture.Height * PixelHeight * zoom),
			SizeFlagsHorizontal = centred ? Control.SizeFlags.ShrinkCenter : Control.SizeFlags.ShrinkBegin,
		};
	}

	/// <summary>The menu's skull cursor, frame 0 or 1, as a control that does not take the mouse; null if the game data has none.</summary>
	public static TextureRect? MakeSkull(DoomFont doomFont)
	{
		if (doomFont.SkullCount == 0)
		{
			return null;
		}
		var skull = doomFont.Skull(0);
		return new TextureRect
		{
			Texture = SkullTexture(doomFont, 0),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			CustomMinimumSize = new Vector2(skull.Width * PixelWidth, skull.Height * PixelHeight),
			Size = new Vector2(skull.Width * PixelWidth, skull.Height * PixelHeight),
		};
	}

	/// <summary>One of the two skull pictures as a texture (made once).</summary>
	public static Texture2D SkullTexture(DoomFont doomFont, int frame)
	{
		var index = frame % Math.Max(1, doomFont.SkullCount);
		return skulls[index % skulls.Length] ??= Texture(doomFont.Skull(index));
	}
}
