using DoomedSpire.Host;
using Godot;

namespace DoomedSpire;

/// <summary>
/// Plays DOOM's audio through Godot: one AudioStreamGenerator, which is a stream the mod fills itself. Every frame <see cref="Pump"/> tops it up with the
/// next few hundred frames of audio from the session, keeping only about 70 ms queued so that a shot is heard soon after it happens.
/// The mod mixes sound effects and music itself (DoomAudio) so there is only this one stream. The game plays its own audio through FMOD, not Godot's
/// audio server, so this adds a Godot player to the default bus without touching the game's sound.
/// </summary>
internal sealed class AudioOutput
{
	private const int MixRate = EngineSound.OutputRate;

	/// <summary>The generator's total room. Only a small part is kept full (see TargetQueued); the rest is slack for a slow frame.</summary>
	private const float BufferSeconds = 0.25f;

	/// <summary>The audio is rendered and pushed in pieces of this many frames, so the arrays can be reused.</summary>
	private const int Chunk = 512;

	/// <summary>How much audio to keep queued ahead of what is playing: about 70 ms. Less risks gaps when a frame is slow; more delays every sound.</summary>
	private const int TargetQueued = 3072;

	private readonly Node parent;
	private readonly float[] left = new float[Chunk];
	private readonly float[] right = new float[Chunk];
	private readonly Vector2[] frames = new Vector2[Chunk];

	private AudioStreamPlayer? player;
	private AudioStreamGeneratorPlayback? playback;
	private int capacity;

	public AudioOutput(Node parent)
	{
		this.parent = parent;
	}

	/// <summary>Starts playing (silence until the first Pump). Throws if Godot gives no generator playback.</summary>
	public void Start()
	{
		var generator = new AudioStreamGenerator { MixRate = MixRate, BufferLength = BufferSeconds };
		player = new AudioStreamPlayer { Stream = generator };
		parent.AddChild(player);
		player.Play();
		playback = player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
		if (playback == null)
		{
			Stop();
			throw new System.InvalidOperationException("Godot gave no playback for the audio generator");
		}
		capacity = (int)(BufferSeconds * MixRate);
	}

	/// <summary>Tops the stream up from <paramref name="session"/>. Call every frame.</summary>
	public void Pump(DoomSession session)
	{
		var current = playback;
		if (current == null)
		{
			return;
		}

		var queued = capacity - current.GetFramesAvailable();
		var pieces = 0;
		while (queued < TargetQueued && pieces++ < 16 && current.CanPushBuffer(Chunk))
		{
			session.RenderAudio(left, right, Chunk);
			for (var i = 0; i < Chunk; i++)
			{
				frames[i] = new Vector2(left[i], right[i]);
			}
			current.PushBuffer(frames);
			queued += Chunk;
		}
	}

	/// <summary>Stops at once; what was queued is dropped. Safe to call any number of times.</summary>
	public void Stop()
	{
		if (player != null && GodotObject.IsInstanceValid(player))
		{
			player.Stop();
			player.QueueFree();
		}
		player = null;
		playback = null;
	}
}
