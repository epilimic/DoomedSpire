#nullable enable
using System;
using System.IO;
using ManagedDoom;
using ManagedDoom.Audio;
using MeltySynth;

namespace DoomedSpire.Host;

/// <summary>
/// DOOM's music. The engine says which track to play (for a level, its music); the track is a file inside the game data, played by MeltySynth with a
/// SoundFont. Based on the original frontend's SilkMusic. The original DOOM's tracks are in id's own MUS format (<see cref="MusPlayer"/>); Freedoom's and
/// many other games' are standard MIDI files (MeltySynth's sequencer). Both are played the same way and the track's format is found from its first bytes.
/// Godot-free (the tests use it). Rendering is pulled by the output, on the game's thread.
/// </summary>
internal sealed class EngineMusic : IMusic
{
	private readonly Config config;
	private readonly Wad wad;
	private readonly Synthesizer synthesizer;
	private readonly Action<string>? report;

	private Bgm current = Bgm.NONE;
	private ITrack? track;
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
			// MUS scores tick 140 times a second and are played one tick per block, so the blocks have exactly that length (MIDI does not mind).
			BlockSize = MusPlayer.BlockLength,
			EnableReverbAndChorus = config.audio_musiceffect,
		};
		synthesizer = new Synthesizer(new SoundFont(soundFont), settings);
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
				track = null;
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
			ITrack newTrack;
			if (MusPlayer.HasHeader(data))
			{
				newTrack = new MusTrack(new MusPlayer(data, loop));
			}
			else if (data.Length >= 4 && data[0] == 'M' && data[1] == 'T' && data[2] == 'h' && data[3] == 'd')
			{
				newTrack = new MidiTrack(new MidiFile(new MemoryStream(data)), loop);
			}
			else
			{
				playing = false;
				report?.Invoke($"music track {lump} is neither a MUS nor a MIDI file; it is skipped");
				return;
			}

			synthesizer.Reset();
			track = newTrack;
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
			track!.Render(synthesizer, scratchLeft.AsSpan(0, frames), scratchRight.AsSpan(0, frames));

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

	/// <summary>One music track being played, whatever its format.</summary>
	private interface ITrack
	{
		void Render(Synthesizer synthesizer, Span<float> left, Span<float> right);
	}

	private sealed class MusTrack : ITrack
	{
		private readonly MusPlayer player;

		public MusTrack(MusPlayer player)
		{
			this.player = player;
		}

		public void Render(Synthesizer synthesizer, Span<float> left, Span<float> right) => player.Render(synthesizer, left, right);
	}

	private sealed class MidiTrack : ITrack
	{
		private readonly MidiFile midi;
		private readonly bool loop;
		private MidiFileSequencer? sequencer;

		public MidiTrack(MidiFile midi, bool loop)
		{
			this.midi = midi;
			this.loop = loop;
		}

		public void Render(Synthesizer synthesizer, Span<float> left, Span<float> right)
		{
			if (sequencer == null)
			{
				sequencer = new MidiFileSequencer(synthesizer);
				sequencer.Play(midi, loop);
			}
			sequencer.Render(left, right);
		}
	}
}
