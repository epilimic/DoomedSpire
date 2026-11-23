using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace DoomedSpire;

/// <summary>The run being played, so the mod can tell which character the player is. The map screen is given the run when a run starts, and tells us.</summary>
internal static class ActiveRun
{
	public static RunState? State { get; set; }

	/// <summary>The character the local player is playing, or null if there is no run or it cannot be told.</summary>
	public static MegaCrit.Sts2.Core.Models.CharacterModel? LocalCharacter()
	{
		try
		{
			return State == null ? null : LocalContext.GetMe(State)?.Character;
		}
		catch (Exception e)
		{
			ModLog.Fail("face", "finding the local player's character", e);
			return null;
		}
	}
}

/// <summary>Remembers the run when the game gives it to the map screen. NMapScreen.Initialize, MegaCrit/Sts2/Core/Nodes/Screens/Map/NMapScreen.cs:722 (game v0.113.1).</summary>
[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Initialize))]
internal static class MapScreenInitializePatch
{
	private static void Postfix(RunState runState)
	{
		ModLog.Try("face", "remembering the run", () => ActiveRun.State = runState);
	}
}
