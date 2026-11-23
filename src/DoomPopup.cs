using System;
using DoomedSpire.Host;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace DoomedSpire;

/// <summary>
/// The button on the map screen and the popup panel it opens. Plain Godot nodes only, no custom Godot classes: a dll-only mod has no script
/// registration, so a subclass of a Godot node could not override _Ready or _Input. Behaviour is wired with signals instead.
/// The popup runs DOOM (see DoomView).
/// </summary>
internal static class DoomPopup
{
	private const string ButtonName = "DoomedSpireButton";
	private const string PopupName = "DoomedSpirePopup";
	private const string LayerName = "DoomedSpireLayer";

	/// <summary>
	/// The popup lives on its own canvas layer so it is drawn above, and takes the mouse before, everything else in the game, including the top bar
	/// and relic bar (they sit in the same container as the map screen and some are drawn above it). The game's own layers: everything is on the
	/// default layer 0 except the dev console on 10 (scenes/debug/dev_console.tscn), which cannot open while DOOM runs.
	/// </summary>
	private const int PopupLayer = 8;


	public static void AttachTo(NMapScreen map)
	{
		if (map.GetNodeOrNull(ButtonName) != null)
		{
			ModLog.OnceInfo("map", "already-attached", "the map screen already has the DOOM button; not adding a second one");
			return;
		}

		var popup = BuildPopup(out var closeButton, out var view, out var layer, out var refreshLayout);
		var button = BuildButton();

		// The monitor and the game are placed from the tunable numbers (CrtTuning) every frame while the popup is showing, so a change in the VfxSpawner
		// tuner shows at once. A per-frame callback: guarded, and its failures are logged in a shrinking way.
		Action layoutEveryFrame = () =>
		{
			try
			{
				refreshLayout();
			}
			catch (Exception e)
			{
				ModLog.Fail("layout", "placing the monitor and the game", e);
			}
		};
		SceneTree? layoutTree = null;

		void StopLayoutUpdates()
		{
			if (layoutTree != null && GodotObject.IsInstanceValid(layoutTree))
			{
				layoutTree.ProcessFrame -= layoutEveryFrame;
			}
			layoutTree = null;
		}

		void SetOpen(bool open)
		{
			if (!GodotObject.IsInstanceValid(popup) || popup.Visible == open)
			{
				return;
			}
			popup.Visible = open;
			ModLog.Info("map", open ? "popup opened" : "popup closed");
			if (open)
			{
				layoutEveryFrame(); // in place before the first frame is drawn
				layoutTree = popup.GetTree();
				layoutTree.ProcessFrame += layoutEveryFrame;
				view.Start();
			}
			else
			{
				StopLayoutUpdates();
				view.Suspend(); // the game stays where it is; opening again carries on
			}
		}

		view.RequestClose = () => SetOpen(false);
		button.Pressed += () => ModLog.Try("map", "opening the popup", () => SetOpen(true));
		closeButton.Pressed += () => ModLog.Try("map", "closing the popup", () => SetOpen(false));
		// The map screen's own Closed signal (NMapScreen.cs, emitted at the start of Close) also closes the popup,
		// so the player is never left inside it while the game moves on.
		map.Closed += () => ModLog.Try("map", "closing the popup with the map", () => SetOpen(false));
		// The run is over when the map screen leaves the tree: only then is a paused game thrown away.
		map.TreeExited += () => ModLog.Try("map", "ending DOOM with the run", () =>
		{
			StopLayoutUpdates();
			view.Shutdown();
		});

		map.AddChildSafely(button);
		map.AddChildSafely(layer);
		ModLog.Info("map", "DOOM button added to the map screen");
	}

	/// <summary>Bottom right, next to the map's Share button (map_screen.tscn: ShareButton is at -220..-48 by -112..-48 from the bottom right corner).</summary>
	private static Button BuildButton()
	{
		var button = new Button
		{
			Name = ButtonName,
			Text = "DOOM",
			FocusMode = Control.FocusModeEnum.None, // controller support comes later; this keeps it out of the map's focus handling
			CustomMinimumSize = new Vector2(172, 64),
			AnchorLeft = 1f,
			AnchorTop = 1f,
			AnchorRight = 1f,
			AnchorBottom = 1f,
			OffsetLeft = -420f,
			OffsetTop = -112f,
			OffsetRight = -232f,
			OffsetBottom = -48f,
			GrowHorizontal = Control.GrowDirection.Begin,
			GrowVertical = Control.GrowDirection.Begin,
		};
		return button;
	}

	/// <summary>
	/// A full-screen dimmed backdrop, the picture of an old monitor (assets/crt.png) and the game on its glass, all on a high canvas layer. Nothing is in a
	/// container: the monitor and the game are placed by absolute position and size (<see cref="CrtLayout"/>), so nothing can reflow or change size when the
	/// text, the Close button or the game picker changes: the picture of the game stays exactly where it is.
	/// The full-screen root is the only thing that takes mouse events (everything inside it ignores the mouse, except the Close button, which DoomView
	/// hides while a game runs), so a click anywhere on the screen lands on the root and nothing else in the game can be clicked.
	/// </summary>
	private static Control BuildPopup(out Button closeButton, out DoomView view, out CanvasLayer layer, out Action refreshLayout)
	{
		layer = new CanvasLayer { Name = LayerName, Layer = PopupLayer };

		var root = new Control { Name = PopupName, Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
		root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(root);

		var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.65f), MouseFilter = Control.MouseFilterEnum.Ignore };
		dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		root.AddChild(dim);

		var monitorPicture = EmbeddedArt.Get("crt.png");
		if (monitorPicture == null)
		{
			ModLog.OnceInfo("layout", "no-monitor", "the monitor picture (crt.png) could not be loaded from the dll; the game is shown without a frame");
		}
		var monitor = new TextureRect
		{
			Name = "DoomedSpireMonitor",
			Texture = monitorPicture,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		root.AddChild(monitor);

		// The glass: the game is drawn here, on top of the monitor picture. Its position and size are set by refreshLayout.
		var screen = new ColorRect { Name = "DoomedSpireGame", Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
		root.AddChild(screen);

		// Top right of the screen, out of the way. Only shown while no game runs (the error and picker states), so a click meant for DOOM cannot close it.
		closeButton = new Button
		{
			Text = "Close",
			FocusMode = Control.FocusModeEnum.None,
			CustomMinimumSize = new Vector2(140, 44),
			AnchorLeft = 1f,
			AnchorTop = 0f,
			AnchorRight = 1f,
			AnchorBottom = 0f,
			OffsetLeft = -176f,
			OffsetTop = 24f,
			OffsetRight = -36f,
			OffsetBottom = 68f,
			GrowHorizontal = Control.GrowDirection.Begin,
		};
		root.AddChild(closeButton);

		view = new DoomView(screen, root, closeButton);

		var lastSize = Vector2.Zero;
		var lastValues = new float[7];
		refreshLayout = () =>
		{
			var size = root.Size;
			var values = new[] { CrtTuning.Scale, CrtTuning.OffsetX, CrtTuning.OffsetY, CrtTuning.ScreenLeft, CrtTuning.ScreenTop, CrtTuning.ScreenWidth, CrtTuning.ScreenHeight };
			if (size == lastSize && values.AsSpan().SequenceEqual(lastValues))
			{
				return;
			}
			lastSize = size;
			lastValues = values;

			var (monitorBox, gameBox) = CrtLayout.Current(size.X, size.Y);
			monitor.Position = new Vector2(monitorBox.X, monitorBox.Y);
			monitor.Size = new Vector2(monitorBox.Width, monitorBox.Height);
			screen.Position = new Vector2(gameBox.X, gameBox.Y);
			screen.Size = new Vector2(gameBox.Width, gameBox.Height);
		};

		return root;
	}
}
