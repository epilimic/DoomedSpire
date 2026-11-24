using Godot;
using ManagedDoom;

namespace DoomedSpire;

/// <summary>Godot's key names to DOOM's. Same table as the original frontend (SilkUserInput.SilkToDoom); keys DOOM does not use map to Unknown and are ignored.</summary>
internal static class KeyMap
{
	/// <summary>The backtick key, by its label or by its place on the keyboard (so it is the key under Esc on keyboards where that key is not labelled with a backtick).</summary>
	public static bool IsToggleKey(InputEventKey e) => e.Keycode == Key.Quoteleft || e.PhysicalKeycode == Key.Quoteleft;

	public static DoomKey ToDoom(InputEventKey e)
	{
		// The backtick key is not DOOM's: it toggles between DOOM and the map screen (DoomView, ToggleHotkey), so DOOM never gets it.
		if (IsToggleKey(e))
		{
			return DoomKey.Unknown;
		}

		// Keycode is the key as labelled (so a French keyboard's "W" is W); PhysicalKeycode is only the fallback.
		var key = e.Keycode != Key.None ? e.Keycode : e.PhysicalKeycode;

		if (key >= Key.A && key <= Key.Z) return (DoomKey)((int)DoomKey.A + (int)(key - Key.A));
		if (key >= Key.Key0 && key <= Key.Key9) return (DoomKey)((int)DoomKey.Num0 + (int)(key - Key.Key0));
		if (key >= Key.F1 && key <= Key.F15) return (DoomKey)((int)DoomKey.F1 + (int)(key - Key.F1));
		if (key >= Key.Kp0 && key <= Key.Kp9) return (DoomKey)((int)DoomKey.Numpad0 + (int)(key - Key.Kp0));

		return key switch
		{
			Key.Space => DoomKey.Space,
			Key.Comma => DoomKey.Comma,
			Key.Minus => DoomKey.Subtract,
			Key.Period => DoomKey.Period,
			Key.Slash => DoomKey.Slash,
			Key.Semicolon => DoomKey.Semicolon,
			Key.Equal => DoomKey.Equal,
			Key.Bracketleft => DoomKey.LBracket,
			Key.Backslash => DoomKey.Backslash,
			Key.Bracketright => DoomKey.RBracket,
			Key.Escape => DoomKey.Escape,
			Key.Enter => DoomKey.Enter,
			Key.KpEnter => DoomKey.Enter,
			Key.Tab => DoomKey.Tab,
			Key.Backspace => DoomKey.Backspace,
			Key.Insert => DoomKey.Insert,
			Key.Delete => DoomKey.Delete,
			Key.Right => DoomKey.Right,
			Key.Left => DoomKey.Left,
			Key.Down => DoomKey.Down,
			Key.Up => DoomKey.Up,
			Key.Pageup => DoomKey.PageUp,
			Key.Pagedown => DoomKey.PageDown,
			Key.Home => DoomKey.Home,
			Key.End => DoomKey.End,
			Key.Pause => DoomKey.Pause,
			Key.KpDivide => DoomKey.Divide,
			Key.KpMultiply => DoomKey.Multiply,
			Key.KpSubtract => DoomKey.Subtract,
			Key.KpAdd => DoomKey.Add,
			Key.Shift => DoomKey.LShift,
			Key.Ctrl => DoomKey.LControl,
			Key.Alt => DoomKey.LAlt,
			Key.Menu => DoomKey.Menu,
			_ => DoomKey.Unknown,
		};
	}
}
