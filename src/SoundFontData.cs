using System.IO;

namespace DoomedSpire;

/// <summary>The SoundFont that DOOM's music is played with, embedded in the mod's dll (assets/soundfont/README.md says where it came from). Read straight from the dll, nothing is written to disk.</summary>
internal static class SoundFontData
{
	private const string ResourceName = "TimGM6mb.sf2";

	/// <summary>A new stream over the SoundFont each call. Throws if it is missing from the dll.</summary>
	public static Stream Open()
	{
		return typeof(SoundFontData).Assembly.GetManifestResourceStream(ResourceName)
			?? throw new FileNotFoundException("the SoundFont is missing from the mod's dll");
	}
}
