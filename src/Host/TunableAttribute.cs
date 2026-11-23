// Copied from epilimic/VfxSpawner (README.md, "Making your own numbers tunable") at commit 85365cb; see also solutions/live-tuner/ in epilimic/modding-standards.
// Only the namespace differs. The VfxSpawner mod finds this attribute by its NAME, so no reference to it is needed.
using System;

namespace DoomedSpire.Host;

/// <summary>
/// Marks a <c>public static</c> float, int or bool field (not const, not readonly) as a value the VfxSpawner tuner can change live: press F9 in a run,
/// choose "Show tuner", pick the group. See <see cref="CrtTuning"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class TunableAttribute : Attribute
{
	public string Group { get; }
	public float Step { get; }
	public string Tip { get; }                        // optional: hover text saying what the value does
	public double Min { get; set; } = double.NaN;     // optional: the knob's range (give both, or neither)
	public double Max { get; set; } = double.NaN;

	public TunableAttribute(string group, float step = 1f, string tip = "")
	{
		Group = group;
		Step = step;
		Tip = tip;
	}
}
