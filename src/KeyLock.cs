using System;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace DoomedSpire;

/// <summary>
/// While DOOM is running, no key press reaches the game. The popup's picture area already accepts every key event it receives, which stops the game's
/// hotkey handlers (they read keys in _UnhandledInput, after the GUI). But a handful of game nodes read keys earlier, in _Input, before any control
/// sees them, and some of those would act on DOOM's keys: for example NFeedbackScreenOpener opens the feedback screen on F2 (DOOM's save key), and
/// NControllerManager switches the whole game into keyboard-navigation mode (disabling the mouse) on arrow and WASD keys when that preference is on.
/// Instead of listing such nodes one by one (a game update would add new ones), this puts one rule in front of EVERY keyboard-reading input method
/// the game declares (_Input, _ShortcutInput, _UnhandledKeyInput, _UnhandledInput on any Node in the game's assembly): while <see cref="Active"/>,
/// a key event is not passed on to it. Mouse and other events always pass, so the game's mouse handling is untouched.
/// </summary>
internal static class KeyLock
{
	/// <summary>True while the DOOM popup is running its game. Set by DoomView.</summary>
	public static bool Active { get; set; }

	private static readonly string[] HandlerNames = { "_Input", "_ShortcutInput", "_UnhandledKeyInput", "_UnhandledInput" };

	/// <summary>Puts the rule in front of every such method. A method that cannot be patched is logged and skipped; the others still work.</summary>
	public static void Apply(Harmony harmony)
	{
		var prefix = new HarmonyMethod(typeof(KeyLock).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic));
		var patched = 0;
		var failed = 0;

		Type[] types;
		try
		{
			types = typeof(NMapScreen).Assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException e)
		{
			types = e.Types.Where(t => t != null).ToArray()!;
		}

		foreach (var type in types)
		{
			if (!typeof(Node).IsAssignableFrom(type))
			{
				continue;
			}
			foreach (var name in HandlerNames)
			{
				var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, new[] { typeof(InputEvent) }, null);
				if (method == null || method.IsAbstract)
				{
					continue;
				}
				try
				{
					harmony.Patch(method, prefix: prefix);
					patched++;
				}
				catch (Exception e)
				{
					failed++;
					ModLog.Fail("lock", $"patching {type.Name}.{name}", e);
				}
			}
		}

		ModLog.Info("lock", $"key lock in front of {patched} game input handlers");
		if (failed > 0)
		{
			ModLog.Warn("lock", $"{failed} input handlers could not be patched; DOOM's keys may reach those parts of the game");
		}
		if (patched == 0)
		{
			ModLog.Warn("lock", "no input handlers found; the key lock does nothing (the game may have been updated)");
		}
	}

	/// <summary>Runs before each patched method. Returning false skips it. The single argument of all these methods is the InputEvent.</summary>
	private static bool Prefix(object[] __args)
	{
		try
		{
			return !(Active && __args.Length > 0 && __args[0] is InputEventKey);
		}
		catch (Exception)
		{
			return true; // when in doubt, let the game have it
		}
	}
}
