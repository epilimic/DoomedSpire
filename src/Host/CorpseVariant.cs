#nullable enable
using ManagedDoom;

namespace DoomedSpire.Host;

/// <summary>
/// Which of the eight pictures of a frame a corpse shows. DOOM picks the picture of a frame by the side it is seen from; a corpse that is a heap of gibs has no
/// sides, so the mod gives the frame eight variations of the heap instead, and the renderer (ThreeDRenderer.cs, marked "DoomedSpire change") asks here which one,
/// so a corpse looks the same from wherever it is seen and different corpses differ. The choice comes from where the monster was placed on the map, which does
/// not change when it is knocked about. Godot-free.
/// </summary>
internal static class CorpseVariant
{
	/// <summary>0 to 7: the picture this thing shows.</summary>
	public static int Pick(Mobj thing)
	{
		var spawn = thing.SpawnPoint;
		var x = (uint)(spawn != null ? spawn.X.Data : thing.X.Data);
		var y = (uint)(spawn != null ? spawn.Y.Data : thing.Y.Data);
		return Mix(x, y);
	}

	/// <summary>A small hash of two numbers into 0 to 7 (every bit of both takes part, so nearby places do not give the same answer).</summary>
	public static int Mix(uint x, uint y)
	{
		var h = x * 73856093u ^ y * 19349663u;
		h ^= h >> 15;
		h *= 2246822519u;
		h ^= h >> 13;
		h *= 3266489917u;
		h ^= h >> 16;
		return (int)(h & 7);
	}
}
