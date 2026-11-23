#nullable enable
using System;
using System.IO;

namespace DoomedSpire.Host;

/// <summary>
/// The engine reads its game data from a file, but the mod ships the data inside its dll. This writes the embedded copy to disk once and hands back
/// the path; later calls find the file already there and do nothing. Godot-free (the tests use it).
/// </summary>
internal static class WadCache
{
	/// <summary>
	/// Makes sure <paramref name="folder"/> holds a file called <paramref name="fileName"/> with the same bytes as the stream <paramref name="open"/> returns,
	/// and returns its path. An existing file of the same length is trusted as it is (the version is in the file name, so a new game data version gets a new
	/// name); a file of another length, or none, is written again. It is written under a temporary name and moved into place, so a crash half way never
	/// leaves a short file under the real name.
	/// </summary>
	public static string EnsureOnDisk(string folder, string fileName, Func<Stream> open)
	{
		Directory.CreateDirectory(folder);
		var path = Path.Combine(folder, fileName);

		using var source = open();
		if (File.Exists(path) && new FileInfo(path).Length == source.Length)
		{
			return path;
		}

		var temp = path + ".tmp";
		using (var target = File.Create(temp)) // starts empty, even if a crashed run left a temporary file behind
		{
			source.CopyTo(target);
		}
		File.Move(temp, path, overwrite: true);
		return path;
	}
}
