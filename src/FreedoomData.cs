using System.IO;
using DoomedSpire.Host;
using Godot;

namespace DoomedSpire;

/// <summary>
/// DOOM's game data: Freedoom Phase 1, embedded in the mod's dll (assets/freedoom/README.md says where it came from). Written to the mod's data folder
/// the first time DOOM opens, because the engine reads it from a file.
/// </summary>
internal static class FreedoomData
{
	/// <summary>The name the file is embedded under (the csproj's LogicalName).</summary>
	private const string ResourceName = "freedoom1.wad";

	/// <summary>The version is in the file name, so a mod update that ships newer game data writes a new file instead of trusting the old one.</summary>
	private const string FileName = "freedoom1-0.13.0.wad";

	/// <summary>The path of the game data on disk, writing it first if it is not there yet. Throws if the disk write fails or the data is missing from the dll.</summary>
	public static string EnsureOnDisk()
	{
		var folder = Path.Combine(ProjectSettings.GlobalizePath("user://DoomedSpire"), "data");
		return WadCache.EnsureOnDisk(folder, FileName, () =>
			typeof(FreedoomData).Assembly.GetManifestResourceStream(ResourceName)
			?? throw new FileNotFoundException("the game data is missing from the mod's dll"));
	}
}
