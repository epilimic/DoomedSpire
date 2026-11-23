#nullable enable
using System;

namespace DoomedSpire.Host;

/// <summary>A rectangle in canvas pixels.</summary>
internal readonly record struct Box(float X, float Y, float Width, float Height);

/// <summary>
/// The numbers that put the game on the old-monitor picture (assets/crt.png): how big the monitor is and where it sits on the screen, and where the
/// glass is on the picture. <b>Live-tunable</b>: the VfxSpawner mod (F9, "Show tuner", group "Doomed Spire CRT frame") changes these while the game
/// runs, and "Copy values" there gives the numbers to bake in as the new defaults. The popup reads them every frame, so a change shows at once.
/// The picture's own measurements are in the CrtPicture constants below.
/// </summary>
internal static class CrtTuning
{
	private const string Group = "Doomed Spire CRT frame";

	[Tunable(Group, 0.01f, "How big the whole monitor is. 1 is the picture's own size (848 x 744); 1.45 is the most that fits a 1080p screen with all of it showing. Bigger pushes the edges off screen, so use Offset Y with it.", Min = 0.5, Max = 2)]
	public static float Scale = 1.44f;

	[Tunable(Group, 1f, "Moves the whole monitor sideways, in screen pixels (positive is right). 0 is centred.", Min = -400, Max = 400)]
	public static float OffsetX = 0f;

	[Tunable(Group, 1f, "Moves the whole monitor up or down, in screen pixels (positive is down). 0 is centred.", Min = -400, Max = 400)]
	public static float OffsetY = 0f;

	[Tunable(Group, 1f, "Where the left edge of the game is on the monitor picture, in the picture's own pixels. The glass starts at 78.", Min = 0, Max = 400)]
	public static float ScreenLeft = CrtPicture.GlassLeft;

	[Tunable(Group, 1f, "Where the top edge of the game is on the monitor picture, in the picture's own pixels. The glass starts at 78.", Min = 0, Max = 400)]
	public static float ScreenTop = CrtPicture.GlassTop;

	[Tunable(Group, 1f, "How wide the game is, in the picture's own pixels. The glass is 692 wide.", Min = 200, Max = 848)]
	public static float ScreenWidth = CrtPicture.GlassWidth;

	[Tunable(Group, 1f, "How tall the game is, in the picture's own pixels. The glass is 535 tall.", Min = 200, Max = 744)]
	public static float ScreenHeight = CrtPicture.GlassHeight;
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

/// <summary>Works out where the monitor picture and the game go on a canvas, from the tunable numbers. Pure arithmetic, so the tests cover it.</summary>
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

	/// <summary>The same, from the current tunable values.</summary>
	public static (Box Monitor, Box Game) Current(float canvasWidth, float canvasHeight) =>
		Compute(canvasWidth, canvasHeight, CrtTuning.Scale, CrtTuning.OffsetX, CrtTuning.OffsetY,
			CrtTuning.ScreenLeft, CrtTuning.ScreenTop, CrtTuning.ScreenWidth, CrtTuning.ScreenHeight);
}
