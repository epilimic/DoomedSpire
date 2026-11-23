using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DoomedSpire.Host;
using Godot;

namespace DoomedSpire;

/// <summary>
/// The picture area inside the popup. For now: draws one still frame of DOOM (episode 1, map 1) the first time the popup opens, or says what is missing.
/// The Godot side only; the engine work is in DoomSession (src/Host), which the tests run without the game.
/// </summary>
internal sealed class FrameView
{
	/// <summary>Looked for in the mod's data folder, in this order. (Piece 6 will embed the game data in the dll and this goes away.)</summary>
	private static readonly string[] WadNames = { "freedoom1.wad", "freedoom2.wad" };

	private readonly TextureRect picture;
	private readonly Label message;
	private bool drawn;

	/// <summary>Adds the picture and the message label to <paramref name="screen"/>, the 4:3 black area of the popup.</summary>
	public FrameView(Control screen)
	{
		// DOOM's pixels are not square: its 640x400 picture is meant to be shown as 4:3, so the picture is stretched to fill the whole area.
		picture = new TextureRect
		{
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		picture.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		screen.AddChild(picture);

		message = new Label
		{
			Text = "DOOM goes here",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		message.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.Minsize, 24);
		screen.AddChild(message);
	}

	/// <summary>Called every time the popup opens. Draws the frame once; if the game data is missing it says where to put it, and tries again the next time.</summary>
	public void EnsureDrawn()
	{
		if (drawn)
		{
			return;
		}

		var folder = ProjectSettings.GlobalizePath("user://DoomedSpire");
		ModLog.Try("frame", "creating the data folder", () => DirAccess.MakeDirRecursiveAbsolute(folder));
		var wad = WadNames.Select(name => Path.Combine(folder, name)).FirstOrDefault(File.Exists);
		if (wad == null)
		{
			ModLog.OnceInfo("frame", "no-wad", $"no game data found; looked for {string.Join(" or ", WadNames)} in {folder}");
			Say($"DOOM needs the Freedoom game data.\n\nCopy freedoom1.wad into this folder:\n{folder}\n\nthen open this again.");
			return;
		}

		try
		{
			var timer = Stopwatch.StartNew();
			using var session = DoomSession.Create(wad);
			var rgba = new byte[session.Width * session.Height * 4];
			session.RenderFirstFrame(rgba);
			var image = Image.CreateFromData(session.Width, session.Height, false, Image.Format.Rgba8, rgba);
			picture.Texture = ImageTexture.CreateFromImage(image);
			message.Visible = false;
			drawn = true;
			ModLog.Info("frame", $"drew the first frame from {Path.GetFileName(wad)} ({session.Width}x{session.Height}) in {timer.ElapsedMilliseconds} ms");
		}
		catch (Exception e)
		{
			ModLog.Fail("frame", "drawing the first frame", e);
			Say($"Could not start DOOM.\n\n{e.GetType().Name}: {ModLog.Clean(e.Message)}");
		}
	}

	private void Say(string text)
	{
		message.Text = text;
		message.Visible = true;
	}
}
