// Copied from epilimic/modding-standards, solutions/safe-logging/ModLog.cs at commit 4be03cf (itself adapted from epilimic/BuildTheSpire,
// BuildTheSpireCode/ModLog.cs at commit d2413a6). Only the namespace, the tag and the "Areas" doc line changed. NOT COMPILED here; the owner builds.
using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Logging;

namespace DoomedSpire;

/// <summary>
/// All of the mod's logging goes through here, into the game's own log (godot.log) with a tag so one search finds everything:
/// "[DoomedSpire] [area] message". Areas: boot, map.
/// The game's Logger.Error is deliberately not used: the game may report those upstream. Failures are logged as warnings with the exception
/// type, message and the top of the stack, the first few times; after that they shrink to one line and then stop, so a fault that repeats
/// (a per-frame refresh, say) cannot flood the log.
/// </summary>
internal static class ModLog
{
    /// <summary>Where lines go. Default: the game's log through <c>Log.Info</c>, tagged. If your mod has a <c>Logger</c> (the Godot-export template does), point these at <c>Logger.Info</c> / <c>Logger.Warn</c> instead, which tags and levels the lines for you.</summary>
    public static string Tag = "[DoomedSpire]";
    public static Action<string> InfoSink = s => Log.Info($"{Tag} {s}");
    public static Action<string> WarnSink = s => Log.Info($"{Tag} WARN {s}");

    private const int FullDetailTimes = 3;
    private const int OneLineTimes = 20;

    // Flood guard, the same idea as LobbyLink's: no more than this many lines per second, then a note of how many were left out.
    private const int MaxLinesPerSecond = 40;
    private const int MaxCleanLength = 120;

    private static readonly Dictionary<string, int> Counts = new();
    private static readonly HashSet<string> SeenOnce = new();
    private static long _windowStart;
    private static int _linesInWindow;
    private static int _dropped;

    public static void Info(string area, string message) => Write(area, message, warn: false);

    public static void Warn(string area, string message) => Write(area, message, warn: true);

    private static void Write(string area, string message, bool warn)
    {
        try
        {
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (now - _windowStart >= System.Diagnostics.Stopwatch.Frequency)
            {
                if (_dropped > 0) WarnSink($"[log] {_dropped} line(s) left out in the last second (more than {MaxLinesPerSecond} per second)");
                _windowStart = now;
                _linesInWindow = 0;
                _dropped = 0;
            }
            if (++_linesInWindow > MaxLinesPerSecond) { _dropped++; return; }
            if (warn) WarnSink($"[{area}] {message}"); else InfoSink($"[{area}] {message}");
        }
        catch (Exception) { /* logging must never throw */ }
    }

    /// <summary>
    /// Text that came from another player, made safe to print: control characters (newlines included) become spaces and the length is cut,
    /// so nobody can forge log lines or fill the log. Always use this for anything received.
    /// </summary>
    public static string Clean(string? text, int max = MaxCleanLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var chars = text!.Take(max).Select(c => char.IsControl(c) ? ' ' : c).ToArray();
        return new string(chars) + (text.Length > max ? $"...(+{text.Length - max})" : "");
    }

    /// <summary>Logs a message the first time only (for conditions that would otherwise repeat every refresh).</summary>
    public static void Once(string area, string key, string message)
    {
        if (SeenOnce.Add(area + "|" + key)) Warn(area, message);
    }

    /// <summary>Like <see cref="Once"/> but an ordinary info line.</summary>
    public static void OnceInfo(string area, string key, string message)
    {
        if (SeenOnce.Add(area + "|" + key)) Info(area, message);
    }

    /// <summary>Logs an exception, with the stack the first few times it happens at the same place.</summary>
    public static void Fail(string area, string context, Exception e)
    {
        try
        {
            var key = $"{area}|{context}|{e.GetType().Name}";
            Counts.TryGetValue(key, out var n);
            Counts[key] = n + 1;
            if (n < FullDetailTimes) Warn(area, $"{context} failed: {e.GetType().Name}: {e.Message}\n{TopOfStack(e)}");
            else if (n < OneLineTimes) Warn(area, $"{context} failed again: {e.GetType().Name}: {e.Message}");
            else if (n == OneLineTimes) Warn(area, $"{context} keeps failing; not logging it any more");
        }
        catch (Exception) { /* logging must never throw */ }
    }

    /// <summary>Runs <paramref name="action"/>; a failure is logged and swallowed. Returns whether it worked.</summary>
    public static bool Try(string area, string context, Action action)
    {
        try { action(); return true; }
        catch (Exception e) { Fail(area, context, e); return false; }
    }

    public static T Try<T>(string area, string context, Func<T> func, T fallback)
    {
        try { return func(); }
        catch (Exception e) { Fail(area, context, e); return fallback; }
    }

    private static string TopOfStack(Exception e)
    {
        var lines = (e.StackTrace ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(6).Select(l => "    " + l.Trim());
        var inner = e.InnerException != null ? $"\n    caused by {e.InnerException.GetType().Name}: {e.InnerException.Message}" : "";
        return string.Join("\n", lines) + inner;
    }
}
