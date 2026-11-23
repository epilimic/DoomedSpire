#nullable enable
using System;
using MeltySynth;

namespace DoomedSpire.Host;

/// <summary>
/// Plays a MUS track: id Software's original music format, a compact score of note events with a time delay between groups, which the synthesizer plays.
/// Ported from the MUS decoder in the original frontend (ManagedDoom, sinshu/managed-doom commit 9365696, GPLv2, <c>src/Silk/SilkMusic.cs</c>, class MusDecoder);
/// the logic and numbers are the same. The score ticks 140 times a second, so the synthesizer must use blocks of exactly <see cref="BlockLength"/> frames
/// (EngineMusic sets that up) and one score tick is processed per block.
/// Godot-free (the tests use it).
/// </summary>
internal sealed class MusPlayer
{
	/// <summary>The score's clock: 140 ticks a second at the 44100 Hz output rate.</summary>
	public const int BlockLength = EngineSound.OutputRate / 140;

	public static readonly byte[] Header = { (byte)'M', (byte)'U', (byte)'S', 0x1A };

	private readonly byte[] data;
	private readonly bool loop;
	private readonly int scoreStart;
	private readonly MusEvent[] events;
	private readonly int[] lastVolume = new int[16];

	private int eventCount;
	private int p;
	private int delay;
	private int blockWrote;

	public MusPlayer(byte[] data, bool loop)
	{
		if (!HasHeader(data))
		{
			throw new FormatException("not a MUS track");
		}
		this.data = data;
		this.loop = loop;
		scoreStart = BitConverter.ToUInt16(data, 6);

		events = new MusEvent[128];
		for (var i = 0; i < events.Length; i++)
		{
			events[i] = new MusEvent();
		}

		Reset();
		blockWrote = BlockLength; // so the first render starts by reading the first group of events
	}

	public static bool HasHeader(byte[] data)
	{
		if (data.Length < 16)
		{
			return false;
		}
		for (var i = 0; i < Header.Length; i++)
		{
			if (data[i] != Header[i])
			{
				return false;
			}
		}
		return true;
	}

	/// <summary>True once a track that does not loop has played to its end.</summary>
	public bool Finished => delay == -1;

	/// <summary>How long the score runs, in seconds, found by reading through it without playing it. Throws if the score is damaged.</summary>
	public static double ScoreSeconds(byte[] data)
	{
		var player = new MusPlayer(data, false);
		long ticks = 0;
		while (true)
		{
			var delay = player.ReadSingleEventGroup();
			if (delay == -1)
			{
				return ticks / 140.0;
			}
			ticks += delay;
		}
	}

	/// <summary>Plays the next <c>left.Length</c> frames into the two buffers (overwriting them).</summary>
	public void Render(Synthesizer synthesizer, Span<float> left, Span<float> right)
	{
		var wrote = 0;
		while (wrote < left.Length)
		{
			if (blockWrote == synthesizer.BlockSize)
			{
				ProcessMidiEvents(synthesizer);
				blockWrote = 0;
			}

			var srcRem = synthesizer.BlockSize - blockWrote;
			var dstRem = left.Length - wrote;
			var rem = Math.Min(srcRem, dstRem);

			synthesizer.Render(left.Slice(wrote, rem), right.Slice(wrote, rem));

			blockWrote += rem;
			wrote += rem;
		}
	}

	private void ProcessMidiEvents(Synthesizer synthesizer)
	{
		if (delay > 0)
		{
			delay--;
		}

		if (delay == 0)
		{
			delay = ReadSingleEventGroup();
			SendEvents(synthesizer);

			if (delay == -1)
			{
				synthesizer.NoteOffAll(false);

				if (loop)
				{
					Reset();
				}
			}
		}
	}

	private void Reset()
	{
		Array.Clear(lastVolume, 0, lastVolume.Length);
		p = scoreStart;
		delay = 0;
	}

	/// <summary>Reads the next group of events (all that happen at the same moment) and the delay in score ticks before the next group; -1 at the end of the score.</summary>
	private int ReadSingleEventGroup()
	{
		eventCount = 0;
		while (true)
		{
			var result = ReadSingleEvent();
			if (result == ReadResult.EndOfGroup)
			{
				break;
			}
			if (result == ReadResult.EndOfFile)
			{
				return -1;
			}
		}

		var time = 0;
		while (true)
		{
			var value = data[p++];
			time = time * 128 + (value & 127);
			if ((value & 128) == 0)
			{
				break;
			}
		}
		return time;
	}

	private ReadResult ReadSingleEvent()
	{
		var channelNumber = data[p] & 0xF;
		if (channelNumber == 15)
		{
			channelNumber = 9; // MUS keeps percussion on channel 15; MIDI on 9
		}
		else if (channelNumber >= 9)
		{
			channelNumber++;
		}

		var eventType = (data[p] & 0x70) >> 4;
		var last = (data[p] >> 7) != 0;
		p++;

		if (eventCount >= events.Length)
		{
			throw new FormatException("too many events at the same moment in a MUS track");
		}
		var me = events[eventCount];
		eventCount++;

		switch (eventType)
		{
			case 0: // release note
				me.Type = 0;
				me.Channel = channelNumber;
				me.Data1 = data[p++];
				me.Data2 = 0;
				break;

			case 1: // play note
				me.Type = 1;
				me.Channel = channelNumber;
				var playNote = data[p++];
				var noteNumber = playNote & 127;
				var noteVolume = (playNote & 128) != 0 ? data[p++] : -1;
				me.Data1 = noteNumber;
				if (noteVolume == -1)
				{
					me.Data2 = lastVolume[channelNumber];
				}
				else
				{
					me.Data2 = noteVolume;
					lastVolume[channelNumber] = noteVolume;
				}
				break;

			case 2: // pitch wheel
				me.Type = 2;
				me.Channel = channelNumber;
				var pitchWheel = data[p++];
				var pw2 = (pitchWheel << 7) / 2;
				var pw1 = pw2 & 127;
				pw2 >>= 7;
				me.Data1 = pw1;
				me.Data2 = pw2;
				break;

			case 3: // system event
				me.Type = 3;
				me.Channel = channelNumber;
				me.Data1 = data[p++];
				me.Data2 = 0;
				break;

			case 4: // controller change
				me.Type = 4;
				me.Channel = channelNumber;
				me.Data1 = data[p++];
				me.Data2 = data[p++];
				break;

			case 6: // end of score
				return ReadResult.EndOfFile;

			default:
				throw new FormatException("unknown event type in a MUS track");
		}

		return last ? ReadResult.EndOfGroup : ReadResult.Ongoing;
	}

	private void SendEvents(Synthesizer synthesizer)
	{
		for (var i = 0; i < eventCount; i++)
		{
			var me = events[i];
			switch (me.Type)
			{
				case 0:
					synthesizer.NoteOff(me.Channel, me.Data1);
					break;

				case 1:
					synthesizer.NoteOn(me.Channel, me.Data1, me.Data2);
					break;

				case 2:
					synthesizer.ProcessMidiMessage(me.Channel, 0xE0, me.Data1, me.Data2);
					break;

				case 3:
					switch (me.Data1)
					{
						case 11: // all notes off
							synthesizer.NoteOffAll(me.Channel, false);
							break;
						case 14: // reset all controllers
							synthesizer.ResetAllControllers(me.Channel);
							break;
					}
					break;

				case 4:
					switch (me.Data1)
					{
						case 0: // program change
							synthesizer.ProcessMidiMessage(me.Channel, 0xC0, me.Data2, 0);
							break;
						case 1: // bank selection
							synthesizer.ProcessMidiMessage(me.Channel, 0xB0, 0x00, me.Data2);
							break;
						case 2: // modulation
							synthesizer.ProcessMidiMessage(me.Channel, 0xB0, 0x01, me.Data2);
							break;
						case 3: // volume
							synthesizer.ProcessMidiMessage(me.Channel, 0xB0, 0x07, me.Data2);
							break;
						case 4: // pan
							synthesizer.ProcessMidiMessage(me.Channel, 0xB0, 0x0A, me.Data2);
							break;
						case 5: // expression
							synthesizer.ProcessMidiMessage(me.Channel, 0xB0, 0x0B, me.Data2);
							break;
						case 6: // reverb
							synthesizer.ProcessMidiMessage(me.Channel, 0xB0, 0x5B, me.Data2);
							break;
						case 7: // chorus
							synthesizer.ProcessMidiMessage(me.Channel, 0xB0, 0x5D, me.Data2);
							break;
						case 8: // pedal
							synthesizer.ProcessMidiMessage(me.Channel, 0xB0, 0x40, me.Data2);
							break;
					}
					break;
			}
		}
	}

	private sealed class MusEvent
	{
		public int Type;
		public int Channel;
		public int Data1;
		public int Data2;
	}

	private enum ReadResult
	{
		Ongoing,
		EndOfGroup,
		EndOfFile,
	}
}
