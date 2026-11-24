#nullable enable
using System;

namespace DoomedSpire.Host;

/// <summary>A rectangle in canvas pixels.</summary>
internal readonly record struct Box(float X, float Y, float Width, float Height);

/// <summary>
/// Where the old-monitor picture (assets/crt.png) goes on the screen and where the game goes on it. The monitor is <see cref="Scale"/> times the picture's own size
/// (1.44 is the biggest that fits a 1080p screen with all of it showing), centred and moved by the offsets, and the game fills the glass (the numbers in
/// <see cref="CrtPicture"/>). The picture's own measurements are in the CrtPicture constants below.
/// </summary>
internal static class CrtPlacement
{
	public const float Scale = 1.44f;
	public const float OffsetX = 0f;
	public const float OffsetY = 0f;
	public const float ScreenLeft = CrtPicture.GlassLeft;
	public const float ScreenTop = CrtPicture.GlassTop;
	public const float ScreenWidth = CrtPicture.GlassWidth;
	public const float ScreenHeight = CrtPicture.GlassHeight;
}

/// <summary>What was measured on assets/crt.png (848 x 744 pixels): the dark glass of the screen, found from where the brightness jumps from the lighter bevel around it.</summary>
internal static class CrtPicture
{
	public const float Width = 848f;
	public const float Height = 744f;
	public const float GlassLeft = 78f;
	public const float GlassTop = 78f;
	public const float GlassWidth = 692f;   // right edge at 770
	public const float GlassHeight = 535f;  // bottom edge at 613
}

/// <summary>Works out where the monitor picture and the game go on a canvas. Pure arithmetic, so the tests cover it.</summary>
internal static class CrtLayout
{
	/// <summary>
	/// The monitor picture (centred on the canvas, then moved by the offsets) and the game's rectangle on it.
	/// The game's rectangle is given in the picture's own pixels, so it stays glued to the glass whatever the scale.
	/// </summary>
	public static (Box Monitor, Box Game) Compute(
		float canvasWidth, float canvasHeight,
		float scale, float offsetX, float offsetY,
		float screenLeft, float screenTop, float screenWidth, float screenHeight)
	{
		var width = CrtPicture.Width * scale;
		var height = CrtPicture.Height * scale;
		var x = (canvasWidth - width) / 2f + offsetX;
		var y = (canvasHeight - height) / 2f + offsetY;
		var monitor = new Box(x, y, width, height);
		var game = new Box(x + screenLeft * scale, y + screenTop * scale, screenWidth * scale, screenHeight * scale);
		return (monitor, game);
	}

	/// <summary>The same, with the monitor's own placement (<see cref="CrtPlacement"/>).</summary>
	public static (Box Monitor, Box Game) Current(float canvasWidth, float canvasHeight) =>
		Compute(canvasWidth, canvasHeight, CrtPlacement.Scale, CrtPlacement.OffsetX, CrtPlacement.OffsetY,
			CrtPlacement.ScreenLeft, CrtPlacement.ScreenTop, CrtPlacement.ScreenWidth, CrtPlacement.ScreenHeight);
}
