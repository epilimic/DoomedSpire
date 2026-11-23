// Copied from epilimic/modding-standards, solutions/embedded-art/EmbeddedArt.cs at commit 4be03cf (itself generalised from epilimic/BingBong, src/SpriteSource.cs
// at commit 71e435d). Only the namespace changed. See that entry's README.md.
using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace DoomedSpire;

/// <summary>
/// Your pictures, baked into the mod's dll (see README.md in this folder). Get("name.png") returns null if the picture is missing or cannot be read,
/// so the caller draws a stand-in. Results are cached; call Clear() if you ever need to reload.
/// </summary>
internal static class EmbeddedArt
{
    private static readonly Dictionary<string, Texture2D?> Cache = new();

    /// <param name="name">The resource name: the path under assets/, with forward slashes, e.g. "rock.png" or "icons/crown.png".</param>
    public static Texture2D? Get(string name)
    {
        if (Cache.TryGetValue(name, out var cached) && (cached == null || GodotObject.IsInstanceValid(cached))) return cached;
        Texture2D? texture = null;
        try
        {
            using Stream? stream = typeof(EmbeddedArt).Assembly.GetManifestResourceStream(name);
            if (stream != null)
            {
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                var bytes = buffer.ToArray();
                var image = new Image();
                var error = name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? image.LoadWebpFromBuffer(bytes) : image.LoadPngFromBuffer(bytes);
                if (error == Error.Ok && !image.IsEmpty())
                {
                    texture = ImageTexture.CreateFromImage(image);
                }
            }
        }
        catch (Exception)
        {
            texture = null;   // a picture must never break the game
        }
        Cache[name] = texture;
        return texture;
    }

    public static void Clear() => Cache.Clear();
}
