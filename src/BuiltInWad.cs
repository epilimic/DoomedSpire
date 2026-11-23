using System.IO;
using DoomedSpire.Host;
using Godot;

namespace DoomedSpire;

/// <summary>
/// DOOM's built-in game data: the shareware DOOM1.WAD, embedded unmodified in the mod's dll (assets/doom/README.md says where it came from and on what terms).
/// Written to the mod's data folder the first time DOOM opens, because the engine reads game data from a file.
/// </summary>
internal static class BuiltInWad
{
	/// <summary>The name the file is embedded under (the csproj's LogicalName).</summary>
	private const string ResourceName = "doom1.wad";

	/// <summary>The version is in the file name, so a mod update that ships other game data writes a new file instead of trusting the old one.</summary>
	private const string FileName = "doom1-shareware-1.9.wad";

	/// <summary>The name shown in the picker.</summary>
	public const string Label = "DOOM shareware (built in)";

	/// <summary>The folder the mod keeps its files in, in the game's user data folder.</summary>
	public static string DataFolder => ProjectSettings.GlobalizePath("user://DoomedSpire");

	/// <summary>The path of the built-in game data on disk, writing it first if it is not there yet. Throws if the disk write fails or the data is missing from the dll.</summary>
	public static string EnsureOnDisk()
	{
		return WadCache.EnsureOnDisk(Path.Combine(DataFolder, "data"), FileName, () =>
			typeof(BuiltInWad).Assembly.GetManifestResourceStream(ResourceName)
			?? throw new FileNotFoundException("the game data is missing from the mod's dll"));
	}
}
