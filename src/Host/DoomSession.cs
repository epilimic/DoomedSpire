#nullable enable
using System;
using ManagedDoom;

namespace DoomedSpire.Host;

/// <summary>
/// One running copy of the engine: the game data, the engine itself and its screen, with no window, no sound and no input yet.
/// Godot-free (the tests use it). Replaces what the original frontend, SilkDoom, wired together.
/// </summary>
internal sealed class DoomSession : IDisposable
{
	private readonly GameContent content;
	private readonly EngineVideo video;
	private readonly Doom doom;

	private DoomSession(GameContent content, EngineVideo video, Doom doom)
	{
		this.content = content;
		this.video = video;
		this.doom = doom;
	}

	public int Width => video.Width;
	public int Height => video.Height;

	/// <summary>True while the engine is playing its screen-change animation (the melt between screens).</summary>
	public bool Wiping => doom.Wiping;

	/// <summary>Starts the game straight into episode 1, map 1 on the WAD at <paramref name="iwadPath"/>. Throws if the file is missing or not a usable WAD.</summary>
	public static DoomSession Create(string iwadPath)
	{
		var args = new CommandLineArgs(new[] { "-iwad", iwadPath, "-warp", "1", "1", "-nosound", "-nomusic", "-nomouse" });
		var config = new Config();
		GameContent? content = null;
		try
		{
			content = new GameContent(args);
			var video = new EngineVideo(config, content);
			var doom = new Doom(args, config, content, video, null, null, null);
			return new DoomSession(content, video, doom);
		}
		catch
		{
			content?.Dispose();
			throw;
		}
	}

	/// <summary>Runs the engine forward one tic (DOOM's clock runs at 35 tics a second).</summary>
	public void Tick() => doom.Update();

	/// <summary>Draws the current moment of the game into <paramref name="rgba"/> (Width * Height * 4 bytes, row after row, R G B A).</summary>
	public void RenderTo(byte[] rgba)
	{
		video.Render(doom, Fixed.One);
		video.CopyRowMajor(rgba);
	}

	/// <summary>
	/// Lets the level start up (the engine fades in from black) and then draws it. For a still picture: at least one second of game time, and never
	/// during the screen-change animation.
	/// </summary>
	public void RenderFirstFrame(byte[] rgba)
	{
		for (var tic = 0; tic < 400; tic++)
		{
			Tick();
			if (tic >= 35 && !Wiping)
			{
				break;
			}
		}
		RenderTo(rgba);
	}

	public void Dispose() => content.Dispose();
}
