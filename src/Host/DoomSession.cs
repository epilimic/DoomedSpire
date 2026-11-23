#nullable enable
using System;
using System.Linq;
using ManagedDoom;
using ManagedDoom.Audio;

namespace DoomedSpire.Host;

/// <summary>
/// One running copy of the engine: the game data, the engine itself and its screen, with no window, no sound and no input yet.
/// Godot-free (the tests use it). Replaces what the original frontend, SilkDoom, wired together.
/// </summary>
internal sealed class DoomSession : IDisposable
{
	private readonly GameContent content;
	private readonly EngineVideo video;
	private readonly KeyboardInput input;
	private readonly Doom doom;

	private DoomSession(GameContent content, EngineVideo video, KeyboardInput input, Doom doom)
	{
		this.content = content;
		this.video = video;
		this.input = input;
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
			var input = new KeyboardInput(config);
			var doom = new Doom(args, config, content, video, NullSound.GetInstance(), NullMusic.GetInstance(), input);
			return new DoomSession(content, video, input, doom);
		}
		catch
		{
			content?.Dispose();
			throw;
		}
	}

	/// <summary>Runs the engine forward one tic (DOOM's clock runs at 35 tics a second). <see cref="UpdateResult.Completed"/> means the player quit from DOOM's menu.</summary>
	public UpdateResult Tick() => doom.Update();

	/// <summary>A key went down or up. The engine needs it twice: as the held state for moving and shooting, and as an event for its menu.</summary>
	public void PostKey(DoomKey key, bool pressed)
	{
		input.SetKey(key, pressed);
		doom.PostEvent(new DoomEvent(pressed ? EventType.KeyDown : EventType.KeyUp, key));
	}

	/// <summary>Lets go of every key (the popup closed, or the game window lost focus), so nothing stays held.</summary>
	public void ReleaseAllKeys()
	{
		foreach (var key in input.PressedKeys().ToArray())
		{
			PostKey(key, false);
		}
	}

	/// <summary>
	/// Draws the game into <paramref name="rgba"/> (Width * Height * 4 bytes, row after row, R G B A). <paramref name="progress"/> is how far we are between
	/// the last tic and the next one, 0 to 1; the engine blends the two so movement is smooth at any frame rate. 1 means exactly the last tic.
	/// </summary>
	public void RenderTo(byte[] rgba, double progress = 1.0)
	{
		video.Render(doom, Fixed.FromDouble(Math.Clamp(progress, 0.0, 1.0)));
		video.CopyRowMajor(rgba);
	}

	// What the tests look at.
	internal bool MenuActive => doom.Menu.Active;
	internal (int X, int Y, uint Angle) PlayerState
	{
		get
		{
			var mobj = doom.Game.World.ConsolePlayer.Mobj;
			return (mobj.X.Data, mobj.Y.Data, mobj.Angle.Data);
		}
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
