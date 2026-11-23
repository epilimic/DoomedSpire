using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace DoomedSpire;

/// <summary>
/// Adds the DOOM button (and its closed popup) to the map screen once the game has built it.
/// Target: NMapScreen._Ready, MegaCrit/Sts2/Core/Nodes/Screens/Map/NMapScreen.cs:667 (game v0.113.1).
/// </summary>
[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen._Ready))]
internal static class MapScreenReadyPatch
{
	private static void Postfix(NMapScreen __instance)
	{
		ModLog.Try("map", "adding the DOOM button to the map screen", () => DoomPopup.AttachTo(__instance));
	}
}
