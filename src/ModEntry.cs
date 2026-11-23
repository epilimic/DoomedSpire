using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace DoomedSpire;

/// <summary>
/// Entry point. Every Harmony patch class is applied on its own and the log says which worked, so one patch that no longer fits the game
/// cannot stop the rest (pattern: modding-standards solutions/safe-private-members, section 2). A bug here must never break the game.
/// </summary>
[ModInitializer(nameof(Init))]
public static class ModEntry
{
	public const string ModId = "DoomedSpire";

	public static void Init()
	{
		try
		{
			ModLog.Info("boot", $"Initializing, version {ReadVersion()}");
			ApplyPatches();
		}
		catch (Exception e)
		{
			ModLog.Fail("boot", "initialization", e);
		}
	}

	/// <summary>The version comes from the manifest (the only place it lives), read through the game's own mod list.</summary>
	private static string ReadVersion()
	{
		try
		{
			return ModManager.GetLoadedMods().FirstOrDefault(m => m.manifest?.id == ModId)?.manifest?.version ?? "unknown";
		}
		catch (Exception)
		{
			return "unknown";
		}
	}

	private static void ApplyPatches()
	{
		var harmony = new Harmony(ModId);
		var patchClasses = typeof(ModEntry).Assembly.GetTypes()
			.Where(t => t.GetCustomAttributes<HarmonyPatch>().Any())
			.OrderBy(t => t.Name)
			.ToList();
		var failed = new List<string>();
		foreach (var type in patchClasses)
		{
			try
			{
				harmony.CreateClassProcessor(type).Patch();
				ModLog.Info("boot", $"patch ok: {type.Name}");
			}
			catch (Exception e)
			{
				failed.Add(type.Name);
				ModLog.Fail("boot", $"patch {type.Name}", e);
			}
		}
		ModLog.Info("boot", $"hooked into the game ({patchClasses.Count - failed.Count} of {patchClasses.Count} patches)");
		if (failed.Count > 0)
		{
			ModLog.Warn("boot", $"patches that did NOT apply: {string.Join(", ", failed)}. Those features will not work; the rest still do.");
		}
	}
}
