#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ManagedDoom;

namespace DoomedSpire.Host;

/// <summary>
/// Puts the engine's game tables back the way they were before any game was loaded. The engine keeps its tables (the monsters' states and health, the
/// weapons, the texts, the constants) in static fields, and a game that carries a DeHackEd patch (Freedoom does, and so do add-ons) changes them in place and
/// nothing changes them back, so the next game started in the same run of the program would play with the first one's changes: the shareware DOOM with
/// Freedoom's. Every session is created after a call to <see cref="Restore"/>: the first call remembers the tables as they are (untouched, as long as it
/// comes before any game is loaded), every later one writes that back. Godot-free (the tests use it).
/// </summary>
internal static class EngineBaseline
{
	private static readonly object Gate = new();
	private static List<Action>? restores;

	/// <summary>Remembers the tables the first time it is called; puts them back every time after.</summary>
	public static void Restore()
	{
		lock (Gate)
		{
			if (restores == null)
			{
				restores = Capture();
				return;
			}
			foreach (var restore in restores)
			{
				restore();
			}
		}
	}

	private static List<Action> Capture()
	{
		var actions = new List<Action>();
		var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);

		foreach (var type in TypesUnder(typeof(DoomInfo)))
		{
			RuntimeHelpers.RunClassConstructor(type.TypeHandle); // the tables are made when first used
			foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
			{
				if (field.IsLiteral)
				{
					continue;
				}
				var value = field.GetValue(null);
				if (!field.IsInitOnly)
				{
					actions.Add(() => field.SetValue(null, value)); // a plain static (a constant a patch can change): the value goes back
				}
				Walk(value, actions, seen); // a readonly static is a table: what is in it goes back
			}
		}
		return actions;
	}

	/// <summary>The type and every type nested in it.</summary>
	private static IEnumerable<Type> TypesUnder(Type type)
	{
		yield return type;
		foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
		{
			foreach (var inner in TypesUnder(nested))
			{
				yield return inner;
			}
		}
	}

	/// <summary>Remembers what is in an array, or in the fields of an engine object, and everything that holds, so that <paramref name="actions"/> can put it back.</summary>
	private static void Walk(object? value, List<Action> actions, HashSet<object> seen)
	{
		if (value == null || !seen.Add(value))
		{
			return;
		}
		var type = value.GetType();
		if (type.IsArray)
		{
			var array = (Array)value;
			var copy = (Array)array.Clone();
			actions.Add(() => Array.Copy(copy, array, copy.Length));
			if (!type.GetElementType()!.IsValueType)
			{
				foreach (var item in array)
				{
					Walk(item, actions, seen);
				}
			}
			return;
		}
		if (type.IsValueType || typeof(Delegate).IsAssignableFrom(type) || type.Namespace == null || !type.Namespace.StartsWith("ManagedDoom", StringComparison.Ordinal))
		{
			return; // numbers, texts, delegates and anything that is not the engine's own: not a table
		}

		var fields = FieldsOf(type).Select(f => (Field: f, Value: f.GetValue(value))).ToArray();
		actions.Add(() =>
		{
			foreach (var (field, saved) in fields)
			{
				field.SetValue(value, saved);
			}
		});
		foreach (var (_, saved) in fields)
		{
			Walk(saved, actions, seen);
		}
	}

	/// <summary>Every instance field of the type and of the types it comes from, private ones too.</summary>
	private static IEnumerable<FieldInfo> FieldsOf(Type type)
	{
		for (var t = type; t != null && t != typeof(object); t = t.BaseType)
		{
			foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
			{
				yield return field;
			}
		}
	}
}
