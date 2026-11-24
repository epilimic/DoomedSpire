using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace DoomedSpire;

/// <summary>
/// The list of DOOM's keys, drawn where the map screen's own legend is while DOOM's popup is open. The real legend is not touched (players need it on the map):
/// our panel, made with the same paper picture, font and colours, is simply put over it, under the popup's dimming like everything else on the popup's layer,
/// and goes away with the popup, so the real legend is there again on the map. Plain nodes only, all guarded: if anything is missing it falls back to plain
/// colours and fonts, and if even that fails there is no panel and DOOM still works.
/// </summary>
internal static class HotkeyLegend
{
	/// <summary>The keys, as the mod binds them (the engine's defaults, Config.cs, with the backtick key as the toggle). The toggle key is shown as ~, which people know better.</summary>
	private static readonly (string Key, string Action)[] Rows =
	{
		("~", "Toggle Map"),
		("Esc", "DOOM Menu"),
		("WASD", "Move"),
		("Mouse", "Turn"),
		("L-Click / Ctrl", "Fire"),
		("R-Click / Space", "Use"),
		("1 - 7", "Weapons"),
		("Tab", "Map"),
	};

	/// <summary>The real legend's size on the map screen (map_screen.tscn: 340 x 454), used if it cannot be found.</summary>
	private static readonly Vector2 DefaultSize = new(340f, 454f);

	/// <summary>Builds the panel inside <paramref name="popupRoot"/> and returns the action that puts it over the real legend (call it when the popup opens), or null if it could not be built.</summary>
	public static Action? Build(NMapScreen map, Control popupRoot)
	{
		try
		{
			var real = map.GetNodeOrNull<TextureRect>("%MapLegend");
			var realHeader = map.GetNodeOrNull<Control>("MapLegend/Header");
			var realRow = map.GetNodeOrNull<Control>("MapLegend/LegendItems/UnknownLegendItem/MegaLabel");

			var ink = realHeader?.GetThemeColor("font_color") ?? new Color(0.082f, 0.161f, 0.243f);
			var rowInk = realRow?.GetThemeColor("font_color") ?? ink;
			var headerFont = realHeader?.GetThemeFont("font");
			var rowFont = realRow?.GetThemeFont("font");

			var panel = new TextureRect
			{
				Name = "DoomedSpireLegend",
				Texture = real?.Texture,
				ExpandMode = TextureRect.ExpandModeEnum.FitHeight,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Size = DefaultSize,
			};
			if (panel.Texture == null)
			{
				var paper = new ColorRect { Color = new Color(0.36f, 0.43f, 0.5f, 1f), MouseFilter = Control.MouseFilterEnum.Ignore };
				paper.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
				panel.AddChild(paper);
			}

			var title = new Label { Text = "Hotkeys", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
			title.AddThemeColorOverride("font_color", ink);
			title.AddThemeFontSizeOverride("font_size", 40);
			if (headerFont != null)
			{
				title.AddThemeFontOverride("font", headerFont);
			}
			title.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
			title.OffsetTop = 3f;
			title.OffsetBottom = 123f;
			panel.AddChild(title);

			var grid = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Ignore };
			grid.AddThemeConstantOverride("h_separation", 12);
			grid.AddThemeConstantOverride("v_separation", 4);
			grid.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
			grid.OffsetTop = 108f;
			grid.OffsetLeft = 48f;
			grid.OffsetRight = -34f;
			foreach (var (key, action) in Rows)
			{
				grid.AddChild(MakeLabel(key, rowInk, rowFont, 18, 136f, wrap: false));
				grid.AddChild(MakeLabel(action, rowInk, rowFont, 18, 110f, wrap: true)); // a long action ("Toggle Map / DOOM") goes on to a second line instead of out of the panel
			}
			panel.AddChild(grid);

			popupRoot.AddChild(panel);
			popupRoot.MoveChild(panel, 0); // under the dimming, like everything else on the popup's layer

			return () =>
			{
				if (!GodotObject.IsInstanceValid(panel))
				{
					return;
				}
				if (real != null && GodotObject.IsInstanceValid(real))
				{
					var rect = real.GetGlobalRect();
					panel.Position = rect.Position;
					panel.Size = rect.Size;
				}
				else
				{
					panel.Size = DefaultSize;
					panel.Position = new Vector2(popupRoot.Size.X - DefaultSize.X - 80f, (popupRoot.Size.Y - DefaultSize.Y) / 2f);
				}
			};
		}
		catch (Exception e)
		{
			ModLog.Fail("map", "making the hotkey legend", e);
			return null;
		}
	}

	private static Label MakeLabel(string text, Color ink, Font? font, int size, float minWidth, bool wrap)
	{
		var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(minWidth, 0f) };
		if (wrap)
		{
			label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		}
		label.AddThemeColorOverride("font_color", ink);
		label.AddThemeFontSizeOverride("font_size", size);
		if (font != null)
		{
			label.AddThemeFontOverride("font", font);
		}
		return label;
	}
}
