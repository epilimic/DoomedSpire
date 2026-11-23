using System;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace DoomedSpire;

/// <summary>
/// Template entry point. No Harmony patches, no custom Godot classes: a bug here must never be able to break the game, so everything is guarded.
/// </summary>
[ModInitializer(nameof(Init))]
public static class ModEntry
{
	public static void Init()
	{
		try
		{
			Log.Info("[DoomedSpire] Initializing");
		}
		catch (Exception e)
		{
			Log.Error("[DoomedSpire] Initialization failed, the mod is inactive: " + e);
		}
	}
}
