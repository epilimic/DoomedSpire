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

		var popup = BuildPopup(out var closeButton, out var view, out var layer);
		var button = BuildButton();

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
				view.Start();
			}
			else
			{
				view.Stop();
			}
		}

		view.RequestClose = () => SetOpen(false);
		button.Pressed += () => ModLog.Try("map", "opening the popup", () => SetOpen(true));
		closeButton.Pressed += () => ModLog.Try("map", "closing the popup", () => SetOpen(false));
		// The map screen's own Closed signal (NMapScreen.cs, emitted at the start of Close) also closes the popup,
		// so the player is never left inside it while the game moves on.
		map.Closed += () => ModLog.Try("map", "closing the popup with the map", () => SetOpen(false));

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
	/// A full-screen dimmed backdrop and a centred panel with a title, a Close button and a 4:3 area for the picture, all on a high canvas layer.
	/// The full-screen root is the only thing that takes mouse events (everything inside it ignores the mouse, except the Close button, which
	/// DoomView hides while a game runs), so a click anywhere on the screen lands on the root and nothing else in the game can be clicked.
	/// </summary>
	private static Control BuildPopup(out Button closeButton, out DoomView view, out CanvasLayer layer)
	{
		layer = new CanvasLayer { Name = LayerName, Layer = PopupLayer };

		var root = new Control { Name = PopupName, Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
		root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(root);

		var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.65f), MouseFilter = Control.MouseFilterEnum.Ignore };
		dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		root.AddChild(dim);

		var panel = new PanelContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			AnchorLeft = 0.5f,
			AnchorTop = 0.5f,
			AnchorRight = 0.5f,
			AnchorBottom = 0.5f,
			OffsetLeft = -440f,
			OffsetTop = -350f,
			OffsetRight = 440f,
			OffsetBottom = 350f,
		};
		root.AddChild(panel);

		var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
		{
			margin.AddThemeConstantOverride(side, 16);
		}
		panel.AddChild(margin);

		var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		margin.AddChild(box);

		var header = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		box.AddChild(header);
		header.AddChild(new Label { Text = "Doomed Spire", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
		closeButton = new Button { Text = "Close", FocusMode = Control.FocusModeEnum.None };
		header.AddChild(closeButton);

		var screen = new ColorRect
		{
			Color = Colors.Black,
			CustomMinimumSize = new Vector2(800, 600), // 4:3
			SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		box.AddChild(screen);

		view = new DoomView(screen, root, closeButton);

		return root;
	}
}
