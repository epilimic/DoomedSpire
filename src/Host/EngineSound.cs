#nullable enable
using System;
using ManagedDoom;
using ManagedDoom.Audio;

namespace DoomedSpire.Host;

/// <summary>
/// DOOM's sound effects: 8 channels with priorities, distance fall-off, left/right placement from where the source is relative to the player, a little
/// random pitch, and a separate channel for menu sounds. A port of the original frontend's SilkSound with the same rules and numbers; the difference is
/// that OpenAL is replaced by a small software mixer (<see cref="MixInto"/>), because the mod's output is one audio stream that DoomAudio fills.
/// Godot-free (the tests use it). Everything runs on the game's thread: the engine calls it once per game tic, the output pulls from it every frame.
/// </summary>
internal sealed class EngineSound : ISound
{
	/// <summary>The rate the mixer renders at. Sounds are resampled to it; Godot's output stream is created at the same rate.</summary>
	public const int OutputRate = 44100;

	private const int ChannelCount = 8;

	private static readonly float FastDecay = (float)Math.Pow(0.5, 1.0 / (35 / 5));
	private static readonly float SlowDecay = (float)Math.Pow(0.5, 1.0 / 35);

	private const float ClipDist = 1200;
	private const float CloseDist = 160;
	private const float Attenuator = ClipDist - CloseDist;

	private readonly Config config;
	private readonly Clip?[] clips;
	private readonly DoomRandom? random;
	private readonly Voice[] voices;
	private readonly ChannelInfo[] infos;
	private readonly Voice uiVoice = new();

	private Sfx uiReserved = Sfx.NONE;
	private Mobj? listener;
	private float masterVolumeDecay;

	public EngineSound(Config config, GameContent content)
	{
		this.config = config;
		config.audio_soundvolume = Math.Clamp(config.audio_soundvolume, 0, MaxVolume);
		masterVolumeDecay = (float)config.audio_soundvolume / MaxVolume;

		if (config.audio_randompitch)
		{
			random = new DoomRandom();
		}

		clips = new Clip?[DoomInfo.SfxNames.Length];
		for (var i = 0; i < clips.Length; i++)
		{
			var name = "DS" + DoomInfo.SfxNames[i].ToString().ToUpper();
			if (content.Wad.GetLumpNumber(name) == -1)
			{
				continue;
			}
			clips[i] = Clip.FromDmx(content.Wad.ReadLump(name));
			if (clips[i] != null)
			{
				LoadedCount++;
			}
		}

		voices = new Voice[ChannelCount];
		infos = new ChannelInfo[ChannelCount];
		for (var i = 0; i < ChannelCount; i++)
		{
			voices[i] = new Voice();
			infos[i] = new ChannelInfo();
		}
	}

	/// <summary>How many of the game's sound effects were found in the game data (for the tests and the log).</summary>
	public int LoadedCount { get; }

	// ---------------------------------------------------------------- mixing (pulled by the output)

	/// <summary>Adds the next <paramref name="frames"/> frames of every sound that is playing into the two buffers, each scaled by <paramref name="gain"/>.</summary>
	public void MixInto(float[] left, float[] right, int frames, float gain)
	{
		foreach (var voice in voices)
		{
			voice.MixInto(left, right, frames, gain);
		}
		uiVoice.MixInto(left, right, frames, gain);
	}

	/// <summary>The left and right gains for a source at <paramref name="pan"/> (-1 full left, 0 centre, 1 full right) and loudness <paramref name="volume"/>: equal power, so the loudness is the same wherever it is placed.</summary>
	public static void PanGains(float pan, float volume, out float left, out float right)
	{
		var angle = (Math.Clamp(pan, -1f, 1f) + 1f) * MathF.PI / 4f;
		left = MathF.Cos(angle) * volume;
		right = MathF.Sin(angle) * volume;
	}

	// ---------------------------------------------------------------- ISound

	public void SetListener(Mobj listener)
	{
		this.listener = listener;
	}

	/// <summary>Once per game tic: ages the priorities, retires finished sounds and starts the ones that were asked for.</summary>
	public void Update()
	{
		for (var i = 0; i < infos.Length; i++)
		{
			var info = infos[i];
			var voice = voices[i];

			if (info.Playing != Sfx.NONE)
			{
				if (voice.Active)
				{
					info.Priority *= info.Type == SfxType.Diffuse ? SlowDecay : FastDecay;
					SetParam(voice, info);
				}
				else
				{
					info.Playing = Sfx.NONE;
					if (info.Reserved == Sfx.NONE)
					{
						info.Source = null;
					}
				}
			}

			if (info.Reserved != Sfx.NONE)
			{
				var clip = clips[(int)info.Reserved];
				if (clip != null)
				{
					SetParam(voice, info);
					voice.Play(clip, GetPitch(info.Type, info.Reserved));
				}
				info.Playing = info.Reserved;
				info.Reserved = Sfx.NONE;
			}
		}

		if (uiReserved != Sfx.NONE)
		{
			var clip = clips[(int)uiReserved];
			if (clip != null)
			{
				uiVoice.SetGains(0f, masterVolumeDecay);
				uiVoice.Play(clip, 1f);
			}
			uiReserved = Sfx.NONE;
		}
	}

	public void StartSound(Sfx sfx)
	{
		if (clips[(int)sfx] == null)
		{
			return;
		}
		uiReserved = sfx;
	}

	public void StartSound(Mobj mobj, Sfx sfx, SfxType type)
	{
		StartSound(mobj, sfx, type, 100);
	}

	public void StartSound(Mobj mobj, Sfx sfx, SfxType type, int volume)
	{
		var clip = clips[(int)sfx];
		if (clip == null || listener == null)
		{
			return;
		}

		var x = (mobj.X - listener.X).ToFloat();
		var y = (mobj.Y - listener.Y).ToFloat();
		var dist = MathF.Sqrt(x * x + y * y);

		float priority;
		if (type == SfxType.Diffuse)
		{
			priority = volume;
		}
		else
		{
			priority = clip.Amplitude * GetDistanceDecay(dist) * volume;
		}

		// The same source making the same kind of sound again takes over its own channel.
		for (var i = 0; i < infos.Length; i++)
		{
			var info = infos[i];
			if (info.Source == mobj && info.Type == type)
			{
				info.Reserved = sfx;
				info.Priority = priority;
				info.Volume = volume;
				return;
			}
		}

		// Otherwise a free channel.
		for (var i = 0; i < infos.Length; i++)
		{
			var info = infos[i];
			if (info.Reserved == Sfx.NONE && info.Playing == Sfx.NONE)
			{
				info.Reserved = sfx;
				info.Priority = priority;
				info.Source = mobj;
				info.Type = type;
				info.Volume = volume;
				return;
			}
		}

		// Otherwise the quietest channel, if this one is at least as important.
		var minPriority = float.MaxValue;
		var minChannel = -1;
		for (var i = 0; i < infos.Length; i++)
		{
			if (infos[i].Priority < minPriority)
			{
				minPriority = infos[i].Priority;
				minChannel = i;
			}
		}
		if (minChannel >= 0 && priority >= minPriority)
		{
			var info = infos[minChannel];
			info.Reserved = sfx;
			info.Priority = priority;
			info.Source = mobj;
			info.Type = type;
			info.Volume = volume;
		}
	}

	public void StopSound(Mobj mobj)
	{
		for (var i = 0; i < infos.Length; i++)
		{
			var info = infos[i];
			if (info.Source == mobj)
			{
				info.LastX = info.Source.X;
				info.LastY = info.Source.Y;
				info.Source = null;
				info.Volume /= 5;
			}
		}
	}

	public void Reset()
	{
		random?.Clear();
		for (var i = 0; i < infos.Length; i++)
		{
			voices[i].Stop();
			infos[i].Clear();
		}
		listener = null;
	}

	/// <summary>The game was paused: sounds that still have a while to go wait where they are (a nearly finished one is left to end).</summary>
	public void Pause()
	{
		foreach (var voice in voices)
		{
			if (voice.Active && !voice.Paused && voice.RemainingSeconds > 0.2)
			{
				voice.Paused = true;
			}
		}
	}

	public void Resume()
	{
		foreach (var voice in voices)
		{
			voice.Paused = false;
		}
	}

	public int MaxVolume => 15;

	public int Volume
	{
		get => config.audio_soundvolume;
		set
		{
			config.audio_soundvolume = value;
			masterVolumeDecay = (float)config.audio_soundvolume / MaxVolume;
		}
	}

	// ---------------------------------------------------------------- placement

	private void SetParam(Voice voice, ChannelInfo info)
	{
		if (listener == null)
		{
			return;
		}

		if (info.Type == SfxType.Diffuse)
		{
			voice.SetGains(0f, 0.01f * masterVolumeDecay * info.Volume);
			return;
		}

		Fixed sourceX;
		Fixed sourceY;
		if (info.Source == null)
		{
			sourceX = info.LastX;
			sourceY = info.LastY;
		}
		else
		{
			sourceX = info.Source.X;
			sourceY = info.Source.Y;
		}

		var x = (sourceX - listener.X).ToFloat();
		var y = (sourceY - listener.Y).ToFloat();

		if (Math.Abs(x) < 16 && Math.Abs(y) < 16)
		{
			voice.SetGains(0f, 0.01f * masterVolumeDecay * info.Volume);
		}
		else
		{
			var dist = MathF.Sqrt(x * x + y * y);
			var angle = MathF.Atan2(y, x) - (float)listener.Angle.ToRadian();
			// Left of the player is a positive angle in DOOM, and the left channel here is negative, hence the minus.
			voice.SetGains(-MathF.Sin(angle), 0.01f * masterVolumeDecay * GetDistanceDecay(dist) * info.Volume);
		}
	}

	private static float GetDistanceDecay(float dist)
	{
		return dist < CloseDist ? 1f : Math.Max((ClipDist - dist) / Attenuator, 0f);
	}

	private float GetPitch(SfxType type, Sfx sfx)
	{
		if (random == null || sfx == Sfx.ITEMUP || sfx == Sfx.TINK || sfx == Sfx.RADIO)
		{
			return 1f;
		}
		return type == SfxType.Voice
			? 1f + 0.075f * (random.Next() - 128) / 128
			: 1f + 0.025f * (random.Next() - 128) / 128;
	}

	// ---------------------------------------------------------------- data

	/// <summary>One sound effect, decoded to floats.</summary>
	private sealed class Clip
	{
		public Clip(float[] samples, int rate, float amplitude)
		{
			Samples = samples;
			Rate = rate;
			Amplitude = amplitude;
		}

		public float[] Samples { get; }
		public int Rate { get; }

		/// <summary>How loud the start of the sound is, 0 to 1, used to rank sounds when all channels are busy.</summary>
		public float Amplitude { get; }

		/// <summary>DOOM's sound lump: 8 bytes of header (format, sample rate, sample count), then 8-bit unsigned mono samples. Most carry 16 bytes of padding at each end, which is dropped.</summary>
		public static Clip? FromDmx(byte[] data)
		{
			if (data.Length < 8)
			{
				return null;
			}

			var rate = BitConverter.ToUInt16(data, 2);
			var count = BitConverter.ToInt32(data, 4);
			count = Math.Min(count, data.Length - 8);

			var offset = 8;
			if (HasPadding(data, count))
			{
				offset += 16;
				count -= 32;
			}
			if (count <= 0 || rate == 0)
			{
				return null;
			}

			var samples = new float[count];
			var max = 0f;
			var amplitudeSpan = Math.Min(rate / 5, count);
			for (var i = 0; i < count; i++)
			{
				samples[i] = (data[offset + i] - 128) / 128f;
				if (i < amplitudeSpan)
				{
					max = Math.Max(max, Math.Abs(samples[i]));
				}
			}
			return new Clip(samples, rate, max);
		}

		private static bool HasPadding(byte[] data, int count)
		{
			if (count < 32)
			{
				return false;
			}
			var first = data[8];
			for (var i = 1; i < 16; i++)
			{
				if (data[8 + i] != first)
				{
					return false;
				}
			}
			var last = data[8 + count - 1];
			for (var i = 1; i < 16; i++)
			{
				if (data[8 + count - i - 1] != last)
				{
					return false;
				}
			}
			return true;
		}
	}

	/// <summary>One playing sound: where it is in its clip, how fast it plays, and how loud it is in each ear.</summary>
	private sealed class Voice
	{
		private Clip? clip;
		private double position;
		private double step;
		private float gainLeft;
		private float gainRight;

		public bool Active { get; private set; }
		public bool Paused { get; set; }

		public double RemainingSeconds => clip == null || step <= 0 ? 0 : (clip.Samples.Length - position) / (step * OutputRate);

		public void Play(Clip newClip, float pitch)
		{
			clip = newClip;
			position = 0;
			step = newClip.Rate * (double)pitch / OutputRate;
			Paused = false;
			Active = true;
		}

		public void Stop()
		{
			Active = false;
			Paused = false;
		}

		public void SetGains(float pan, float volume)
		{
			PanGains(pan, volume, out gainLeft, out gainRight);
		}

		public void MixInto(float[] left, float[] right, int frames, float gain)
		{
			if (!Active || Paused || clip == null)
			{
				return;
			}

			var samples = clip.Samples;
			var l = gainLeft * gain;
			var r = gainRight * gain;
			var pos = position;
			for (var i = 0; i < frames; i++)
			{
				var index = (int)pos;
				if (index >= samples.Length)
				{
					Active = false;
					break;
				}
				// linear interpolation between neighbouring samples, because the clip's rate is lower than the output's
				var next = index + 1 < samples.Length ? samples[index + 1] : samples[index];
				var value = samples[index] + (next - samples[index]) * (float)(pos - index);
				left[i] += value * l;
				right[i] += value * r;
				pos += step;
			}
			position = pos;
		}
	}

	private sealed class ChannelInfo
	{
		public Sfx Reserved;
		public Sfx Playing;
		public float Priority;
		public Mobj? Source;
		public SfxType Type;
		public int Volume;
		public Fixed LastX;
		public Fixed LastY;

		public void Clear()
		{
			Reserved = Sfx.NONE;
			Playing = Sfx.NONE;
			Priority = 0;
			Source = null;
			Type = 0;
			Volume = 0;
			LastX = Fixed.Zero;
			LastY = Fixed.Zero;
		}
	}
}
