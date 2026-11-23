using System;
using MegaCrit.Sts2.Core.Saves;

namespace DoomedSpire;

/// <summary>
/// DOOM follows the volume sliders in the game's own settings, so it is not louder than everything else. The game stores master, sound effects and music
/// volumes as 0 to 1 (SettingsSave.cs:55-61, default 0.5 each); the sound effects get master x sound effects, the music master x music.
/// </summary>
internal static class GameVolume
{
	/// <summary>
	/// The game's defaults (0.5 x 0.5) would give 0.25, which is quiet next to DOOM's own mixing, so the product is doubled: the defaults give half of
	/// full scale and a maxed-out slider gives full scale. One number to change if DOOM turns out too quiet or too loud.
	/// </summary>
	private const float Boost = 2f;

	/// <summary>The overall gains, 0 to 1, for DOOM's sound effects and for its music. If the settings cannot be read, half of full scale.</summary>
	public static (float Sound, float Music) Read()
	{
		try
		{
			var settings = SaveManager.Instance.SettingsSave;
			return (Scale(settings.VolumeMaster * settings.VolumeSfx), Scale(settings.VolumeMaster * settings.VolumeBgm));
		}
		catch (Exception e)
		{
			ModLog.Fail("audio", "reading the game's volume settings", e);
			return (0.5f, 0.5f);
		}
	}

	private static float Scale(float volume)
	{
		return Math.Clamp(volume * Boost, 0f, 1f);
	}
}
