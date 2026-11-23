#nullable enable
using System;
using ManagedDoom;
using ManagedDoom.Video;

namespace DoomedSpire.Host;

/// <summary>
/// The engine's screen. DOOM draws into its own Renderer; this keeps the finished picture in memory instead of putting it on a window.
/// Godot-free on purpose, so the tests can run it without the game. Replaces the original frontend's SilkVideo (modding-standards plan, piece 4).
/// </summary>
internal sealed class EngineVideo : IVideo
{
	private readonly Renderer renderer;
	/// <summary>What the engine writes: 4 bytes per pixel (R, G, B, A), one whole column after another (x * Height + y), not row after row.</summary>
	private readonly byte[] columnMajor;

	public EngineVideo(Config config, GameContent content)
	{
		renderer = new Renderer(config, content);
		columnMajor = new byte[4 * renderer.Width * renderer.Height];
	}

	public int Width => renderer.Width;
	public int Height => renderer.Height;

	/// <summary>Copies the last rendered picture into <paramref name="rgba"/> as an ordinary image: row after row, 4 bytes per pixel (R, G, B, A).</summary>
	public void CopyRowMajor(byte[] rgba)
	{
		int width = renderer.Width, height = renderer.Height;
		if (rgba.Length < 4 * width * height)
		{
			throw new ArgumentException($"the buffer must hold {4 * width * height} bytes", nameof(rgba));
		}
		var src = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(columnMajor.AsSpan());
		var dst = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(rgba.AsSpan());
		for (var x = 0; x < width; x++)
		{
			var column = x * height;
			for (var y = 0; y < height; y++)
			{
				dst[y * width + x] = src[column + y];
			}
		}
	}

	public void Render(Doom doom, Fixed frameFrac) => renderer.Render(doom, columnMajor, frameFrac);

	public void InitializeWipe() => renderer.InitializeWipe();

	public bool HasFocus() => true;

	public int MaxWindowSize => renderer.MaxWindowSize;

	public int WindowSize
	{
		get => renderer.WindowSize;
		set => renderer.WindowSize = value;
	}

	public bool DisplayMessage
	{
		get => renderer.DisplayMessage;
		set => renderer.DisplayMessage = value;
	}

	public int MaxGammaCorrectionLevel => renderer.MaxGammaCorrectionLevel;

	public int GammaCorrectionLevel
	{
		get => renderer.GammaCorrectionLevel;
		set => renderer.GammaCorrectionLevel = value;
	}

	public int WipeBandCount => renderer.WipeBandCount;
	public int WipeHeight => renderer.WipeHeight;
}
