#nullable enable
using System;
using System.IO;
using ManagedDoom;
using ManagedDoom.Audio;
using MeltySynth;

namespace DoomedSpire.Host;

/// <summary>
/// DOOM's music. The engine says which track to play (for a level, its music); the track is a MIDI file inside the game data, played by MeltySynth with
/// a SoundFont. Based on the MIDI half of the original frontend's SilkMusic. Freedoom's tracks are MIDI; the original DOOM's are in a related format
/// called MUS, which is not supported here (not needed for the data the mod ships), and a MUS track is reported and skipped.
/// Godot-free (the tests use it). Rendering is pulled by the output, on the game's thread.
/// </summary>
internal sealed class EngineMusic : IMusic
{
	private readonly Config config;
	private readonly Wad wad;
	private readonly Synthesizer synthesizer;
	private readonly MidiFileSequencer sequencer;
	private readonly Action<string>? report;

	private Bgm current = Bgm.NONE;
	private bool playing;
	private float[] scratchLeft = new float[0];
	private float[] scratchRight = new float[0];

	/// <param name="soundFont">The SoundFont data. Read completely during construction; the stream is not kept.</param>
	public EngineMusic(Config config, GameContent content, Stream soundFont, Action<string>? report)
	{
		this.config = config;
		this.wad = content.Wad;
		this.report = report;

		config.audio_musicvolume = Math.Clamp(config.audio_musicvolume, 0, MaxVolume);

		var settings = new SynthesizerSettings(EngineSound.OutputRate)
		{
			EnableReverbAndChorus = config.audio_musiceffect,
		};
		synthesizer = new Synthesizer(new SoundFont(soundFont), settings);
		sequencer = new MidiFileSequencer(synthesizer);
	}

	public void StartMusic(Bgm bgm, bool loop)
	{
		if (bgm == current)
		{
			return;
		}
		current = bgm; // remembered even if the track fails, so a bad track is not retried every time the engine asks

		try
		{
			if (bgm == Bgm.NONE)
			{
				sequencer.Stop();
				playing = false;
				return;
			}

			var lump = "D_" + DoomInfo.BgmNames[(int)bgm].ToString().ToUpper();
			if (wad.GetLumpNumber(lump) == -1)
			{
				playing = false;
				report?.Invoke($"the game data has no music track {lump}");
				return;
			}

			var data = wad.ReadLump(lump);
			if (data.Length < 4 || data[0] != 'M' || data[1] != 'T' || data[2] != 'h' || data[3] != 'd')
			{
				playing = false;
				report?.Invoke($"music track {lump} is not a MIDI file (the older MUS format is not supported); it is skipped");
				return;
			}

			synthesizer.Reset();
			sequencer.Play(new MidiFile(new MemoryStream(data)), loop);
			playing = true;
		}
		catch (Exception e)
		{
			playing = false;
			report?.Invoke($"could not start the music: {e.GetType().Name}: {e.Message}");
		}
	}

	/// <summary>Adds the next <paramref name="frames"/> frames of music into the two buffers, scaled by the engine's music volume and by <paramref name="gain"/>.</summary>
	public void MixInto(float[] left, float[] right, int frames, float gain)
	{
		if (!playing)
		{
			return;
		}

		try
		{
			if (scratchLeft.Length < frames)
			{
				scratchLeft = new float[frames];
				scratchRight = new float[frames];
			}
			sequencer.Render(scratchLeft.AsSpan(0, frames), scratchRight.AsSpan(0, frames));

			// The original frontend's scale: the engine's volume setting (0 to 15) maps to 0 to 2.
			var volume = 2.0f * config.audio_musicvolume / MaxVolume * gain;
			for (var i = 0; i < frames; i++)
			{
				left[i] += scratchLeft[i] * volume;
				right[i] += scratchRight[i] * volume;
			}
		}
		catch (Exception e)
		{
			playing = false;
			report?.Invoke($"the music stopped: {e.GetType().Name}: {e.Message}");
		}
	}

	public int MaxVolume => 15;

	public int Volume
	{
		get => config.audio_musicvolume;
		set => config.audio_musicvolume = value;
	}
}
