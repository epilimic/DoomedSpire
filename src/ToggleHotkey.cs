using System;
using Godot;

namespace DoomedSpire;

/// <summary>
/// The backtick key toggles between the map screen and DOOM, but only once DOOM has been opened in this run (until then it is the dev console's key, as in the
/// game; after, it is the toggle on the map screen and in DOOM's windows and stays the console's key everywhere else). Inside DOOM, DoomView reads it (it is not DOOM's key). On the map screen it is read here, from
/// KeyLock's rule that sits in front of every key-reading handler of the game: the game's own dev console opens on that same key, so the key has to be taken
/// before the console sees it (otherwise both would open). The key is only taken when it will do something: the map screen is showing with nothing over it,
/// the dev console is not open (its own backtick closes it) and no text is being typed; or when DOOM's popup is open but no game is running yet (the game
/// list, a message, or the enemies being got ready), where it closes the popup.
/// </summary>
internal static class ToggleHotkey
{
	private static Func<bool>? applies;
	private static Action? toggle;
	private static ulong lastEvent;

	/// <summary>The latest map screen's rule and action win (a new map screen is made for each run).</summary>
	public static void Register(Func<bool> appliesNow, Action toggleNow)
	{
		applies = appliesNow;
		toggle = toggleNow;
	}

	/// <summary>Called when a map screen goes away: only takes its own registration back.</summary>
	public static void Unregister(Action toggleNow)
	{
		if (toggle == toggleNow)
		{
			applies = null;
			toggle = null;
		}
	}

	/// <summary>
	/// Is this key event the toggle key, taken here? True means the game must not see it (a press, a repeat and a release of the key are all taken while it applies).
	/// The toggle itself happens once per key press, a moment later (not in the middle of the game's input handling).
	/// </summary>
	public static bool Take(InputEventKey key)
	{
		try
		{
			if (!KeyMap.IsToggleKey(key) || applies == null || toggle == null || !applies())
			{
				return false;
			}
			if (key.Pressed && !key.Echo)
			{
				var id = key.GetInstanceId();
				if (id != lastEvent) // every handler of the game is asked about the same event; the toggle happens once
				{
					lastEvent = id;
					Callable.From(toggle).CallDeferred();
				}
			}
			return true;
		}
		catch (Exception)
		{
			return false; // when in doubt, let the game have it
		}
	}
}
