#nullable enable
using System;
using System.IO;
using ManagedDoom;

namespace DoomedSpire.Host;

/// <summary>What the audio needs from outside the engine.</summary>
internal sealed class AudioOptions
{
	/// <summary>Opens the SoundFont for the music (a new stream each call). Without it there is no music, only sound effects.</summary>
	public Func<Stream>? OpenSoundFont { get; set; }

	/// <summary>False for no music (the tests use this to hear only the sound effects).</summary>
	public bool Music { get; set; } = true;

	/// <summary>Told about problems that do not stop the game (a missing music track, a SoundFont that cannot be read). May be called more than once with the same text.</summary>
	public Action<string>? Report { get; set; }
}

/// <summary>
/// All of DOOM's audio as one stream: the sound effects and the music mixed together. The output asks for the next few hundred frames whenever it has
/// room (<see cref="Render"/>); nothing here plays by itself, so a game that is not being run makes no sound and costs nothing.
/// Godot-free (the tests render it to numbers and to a WAV file).
/// </summary>
internal sealed class DoomAudio
{
	public DoomAudio(Config config, GameContent content, AudioOptions options)
	{
		Sound = new EngineSound(config, content);

		if (options.Music && options.OpenSoundFont != null)
		{
			try
			{
				using var soundFont = options.OpenSoundFont();
				Music = new EngineMusic(config, content, soundFont, options.Report);
			}
			catch (Exception e)
			{
				options.Report?.Invoke($"no music: the SoundFont could not be read: {e.GetType().Name}: {e.Message}");
			}
		}
	}

	public EngineSound Sound { get; }

	/// <summary>Null when the music is switched off or its SoundFont could not be read; the sound effects still work.</summary>
	public EngineMusic? Music { get; }

	/// <summary>How loud the sound effects and the music are overall, 0 to 1. The mod sets these from the game's own volume settings.</summary>
	public float SoundGain { get; set; } = 1f;
	public float MusicGain { get; set; } = 1f;

	/// <summary>Fills <paramref name="left"/> and <paramref name="right"/> with the next <paramref name="frames"/> frames at 44100 Hz, each in the range -1 to 1.</summary>
	public void Render(float[] left, float[] right, int frames)
	{
		Array.Clear(left, 0, frames);
		Array.Clear(right, 0, frames);

		Music?.MixInto(left, right, frames, MusicGain);
		Sound.MixInto(left, right, frames, SoundGain);

		for (var i = 0; i < frames; i++)
		{
			left[i] = Clamp(left[i]);
			right[i] = Clamp(right[i]);
		}
	}

	/// <summary>Keeps a sample inside -1 to 1 (louder would distort in the output), and a broken value (not a number) as silence.</summary>
	private static float Clamp(float value)
	{
		if (float.IsNaN(value))
		{
			return 0f;
		}
		return value > 1f ? 1f : value < -1f ? -1f : value;
	}
}
