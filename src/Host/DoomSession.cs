#nullable enable
using System;
using System.Collections.Generic;
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
	private readonly PlayerInput input;
	private readonly DoomAudio? audio;
	private readonly Doom doom;

	private DoomSession(GameContent content, EngineVideo video, PlayerInput input, DoomAudio? audio, Doom doom)
	{
		this.content = content;
		this.video = video;
		this.input = input;
		this.audio = audio;
		this.doom = doom;
	}

	public int Width => video.Width;
	public int Height => video.Height;

	/// <summary>True while the engine is playing its screen-change animation (the melt between screens).</summary>
	public bool Wiping => doom.Wiping;

	/// <summary>
	/// How far the mouse turns the player: the engine's own scale, 0 to 15 (<c>Config.mouse_sensitivity</c>). The engine's default is 8; the owner found that
	/// too fast with this mouse handling and brought it down in steps (8, then 5, now 1). It can be changed while playing, in DOOM's own options menu (the
	/// backtick key, then Options and Mouse Sensitivity); it starts at this value each time a new game starts.
	/// </summary>
	internal const int MouseSensitivity = 1;

	/// <summary>Starts the game straight into episode 1, map 1 on the WAD at <paramref name="iwadPath"/>, with any add-on WADs on top. Throws if a file is missing or not a usable WAD.</summary>
	public static DoomSession Create(string iwadPath, AudioOptions? audioOptions = null, IReadOnlyList<string>? extraWads = null)
	{
		var arguments = new List<string> { "-iwad", iwadPath, "-warp", "1", "1", "-nosound", "-nomusic", "-nomouse" };
		if (extraWads != null && extraWads.Count > 0)
		{
			// Add-on WADs (here, the character's faces): where they have a lump with the same name as the game's, the add-on's is used.
			arguments.Add("-file");
			arguments.AddRange(extraWads);
		}
		var args = new CommandLineArgs(arguments.ToArray());
		var config = new Config { mouse_sensitivity = MouseSensitivity };
		GameContent? content = null;
		try
		{
			content = new GameContent(args);
			var video = new EngineVideo(config, content);
			var input = new PlayerInput(config);
			var audio = audioOptions == null ? null : new DoomAudio(config, content, audioOptions);
			ISound sound = audio != null ? audio.Sound : NullSound.GetInstance();
			IMusic music = audio?.Music != null ? audio.Music : NullMusic.GetInstance();
			var doom = new Doom(args, config, content, video, sound, music, input);
			return new DoomSession(content, video, input, audio, doom);
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

	/// <summary>A mouse button went down or up (left is fire, right is use, as set in the engine's default bindings).</summary>
	public void PostMouseButton(DoomMouseButton button, bool pressed) => input.SetMouseButton(button, pressed);

	/// <summary>The mouse moved sideways by this many pixels (right is positive); the player turns by it on the next tic.</summary>
	public void AddMouseTurn(double pixels) => input.AddMouseTurn(pixels);

	/// <summary>Lets go of every key and mouse button (the popup closed, or the game window lost focus), so nothing stays held.</summary>
	public void ReleaseAllInput()
	{
		foreach (var key in input.PressedKeys().ToArray())
		{
			doom.PostEvent(new DoomEvent(EventType.KeyUp, key)); // the menu, if it is showing, hears the key go up too
		}
		input.ReleaseAll();
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

	/// <summary>Fills the two buffers with the next <paramref name="frames"/> frames of sound and music (44100 Hz, -1 to 1). Silence if the session has no audio.</summary>
	public void RenderAudio(float[] left, float[] right, int frames)
	{
		if (audio == null)
		{
			Array.Clear(left, 0, frames);
			Array.Clear(right, 0, frames);
			return;
		}
		audio.Render(left, right, frames);
	}

	/// <summary>How loud the sound effects and the music are overall, 0 to 1 (the mod follows the game's own volume settings).</summary>
	public void SetAudioGains(float sound, float music)
	{
		if (audio != null)
		{
			audio.SoundGain = sound;
			audio.MusicGain = music;
		}
	}

	// What the tests look at.
	internal int LoadedSoundCount => audio?.Sound.LoadedCount ?? 0;
	internal bool HasMusic => audio?.Music != null;
	internal bool MenuActive => doom.Menu.Active;
	internal int Ammo => doom.Game.World.ConsolePlayer.Ammo[(int)AmmoType.Clip];
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
