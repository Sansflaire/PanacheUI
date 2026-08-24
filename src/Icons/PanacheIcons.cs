using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;

namespace PanacheUI.Icons;

/// <summary>
/// PanacheUI's own bundled icon set — a small library of flat white-on-transparent
/// glyphs shipped with the framework so consumer plugins don't each need to source,
/// crop, and ship their own copies of common UI icons (locks, marks, checks, ...).
/// </summary>
/// <remarks>
/// <para><b>Icons are addressed only by numeric ID</b> — <c>#0001</c>, <c>#0002</c>, and so
/// on — never by name. Call <see cref="Get"/> with the ID; there is no name-based lookup
/// and none is planned, so don't build one against a name that might not stay accurate.</para>
///
/// <para><b>Files live at <c>devPlugins/PanacheUI/Icons/0001.png</c> … </b> — 4-digit
/// zero-padded IDs, opaque white glyph, fully transparent background. Because every
/// consumer of PanacheUI carries its own private copy of <c>PanacheUI.dll</c> in its own
/// Dalamud <c>AssemblyLoadContext</c> (see the framework's cross-plugin notes on
/// <c>PanacheStats</c> for why), this cache is <i>per load context</i> — each consumer
/// that calls <see cref="Get"/> decodes and caches its own copy independently. That's
/// deliberate and cheap: these are a handful of small PNGs, decoded once per consumer
/// and cached forever, not a resource worth the complexity of cross-context sharing.</para>
///
/// <para><b>Never dispose a bitmap this returns.</b> It is cached and shared by every
/// caller that asks for that ID within this load context — the same contract
/// <c>GameIconCache</c> already uses elsewhere in this plugin suite.</para>
/// </remarks>
public static class PanacheIcons
{
    private static readonly ConcurrentDictionary<int, SKBitmap?> Cache = new();
    private static readonly Lazy<string> ResolvedFolder = new(ResolveIconsFolder);

    /// <summary>The folder icons are being read from. Exposed for diagnostics — see the
    /// <c>/panacheui icons</c> chat command, which prints this alongside what it found.</summary>
    public static string IconsFolder => ResolvedFolder.Value;

    /// <summary>
    /// Returns the cached bitmap for <paramref name="id"/>, decoding it on first request.
    /// Returns <c>null</c> if the id has no corresponding file — callers should treat that
    /// as "not available" and fall back to a placeholder, not throw. See
    /// <see cref="Components.PUI.Icon"/> for a ready-made Node that already does this.
    /// </summary>
    public static SKBitmap? Get(int id)
    {
        if (Cache.TryGetValue(id, out var cached)) return cached;

        SKBitmap? loaded = null;
        try
        {
            string path = Path.Combine(IconsFolder, $"{id:0000}.png");
            if (File.Exists(path)) loaded = SKBitmap.Decode(path);
        }
        catch
        {
            // A missing or corrupt file must not be able to crash the caller — same
            // best-effort contract GameIconCache already uses for game icons.
        }

        Cache[id] = loaded;
        return loaded;
    }

    /// <summary>True if an icon exists for this id (attempts to load it, same as <see cref="Get"/>).</summary>
    public static bool Exists(int id) => Get(id) != null;

    /// <summary>Every icon ID currently found on disk, ascending. Scans <see cref="IconsFolder"/>
    /// each call — cheap (a directory listing of a handful of files), not cached, so it stays
    /// accurate if icons are added after the process starts.</summary>
    public static IReadOnlyList<int> AllIds()
    {
        try
        {
            return Directory.EnumerateFiles(IconsFolder, "*.png")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => n is { Length: 4 } && n.All(char.IsDigit))
                .Select(n => int.Parse(n!))
                .OrderBy(i => i)
                .ToList();
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    /// <summary>
    /// Locates the shipped <c>Icons</c> folder. Primary strategy is the well-known path
    /// every project in this dev-plugin ecosystem already hardcodes at build time via
    /// <c>$(PanacheUIPath)</c> in its own <c>.csproj</c> — reliable in this specific
    /// single-machine private setup. Falls back to walking out from wherever this
    /// assembly's own copy of <c>PanacheUI.dll</c> actually sits, for robustness if that
    /// well-known path ever doesn't apply (Dalamud dev-plugin loading can shadow-copy
    /// assemblies to a temp location, so <c>Assembly.Location</c> is not guaranteed to
    /// reflect the real devPlugins folder — this is why it's the fallback, not the primary).
    /// </summary>
    private static string ResolveIconsFolder()
    {
        string wellKnown = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncher", "devPlugins", "PanacheUI", "Icons");
        if (Directory.Exists(wellKnown)) return wellKnown;

        try
        {
            var dir = Path.GetDirectoryName(typeof(PanacheIcons).Assembly.Location);
            if (!string.IsNullOrEmpty(dir))
            {
                // A copy of PanacheUI.dll that shipped its own Icons folder alongside it.
                var sideBySide = Path.Combine(dir, "Icons");
                if (Directory.Exists(sideBySide)) return sideBySide;

                // A copy sitting in a sibling plugin folder — devPlugins/<Consumer>/PanacheUI.dll
                // walking to devPlugins/PanacheUI/Icons.
                var sibling = Path.GetFullPath(Path.Combine(dir, "..", "PanacheUI", "Icons"));
                if (Directory.Exists(sibling)) return sibling;
            }
        }
        catch
        {
            // Best-effort fallback only — fall through to reporting the primary candidate.
        }

        return wellKnown;
    }
}
