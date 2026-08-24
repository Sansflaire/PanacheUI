using System;
using System.Collections.Generic;
using SkiaSharp;

namespace PanacheUI.Core;

/// <summary>
/// Cache of <see cref="SKFont"/> instances and their metrics, keyed by the exact
/// (size, bold, italic) triple a <see cref="Style"/> asks for.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Both the layout engine and the renderer used to do
/// <c>SKTypeface.FromFamilyName(...)</c> + <c>new SKFont(...)</c> + <c>Dispose()</c>
/// on <i>every</i> text measurement and <i>every</i> text draw. The layout engine
/// measures each text node once per ancestor level, so a 300-node window was
/// creating and destroying several thousand native font objects per frame. That
/// alone was the single largest cost in a PanacheUI frame.</para>
///
/// <para><b>Keying.</b> The size is keyed on its exact IEEE bits, not a quantised
/// bucket, so a cached font is always byte-identical to the one the old code would
/// have constructed. There is no rendering difference — only the allocation is gone.</para>
///
/// <para><b>Threading.</b> The cache dictionary is <c>[ThreadStatic]</c>. Panache
/// renders from the ImGui draw thread, but <c>RenderApi</c> also renders effect
/// strips from thread-pool threads, so a single shared dictionary would need a lock
/// on the hottest path in the framework. Per-thread dictionaries cost a handful of
/// font objects per thread and no synchronisation at all.</para>
///
/// <para><b>Lifetime.</b> Entries are never evicted — a UI uses a small fixed set of
/// font sizes. The typefaces are process-lifetime and deliberately never disposed;
/// they are owned here, not by whichever <see cref="Rendering.SkiaRenderer"/> happened
/// to be constructed first.</para>
/// </remarks>
internal static class FontCache
{
    /// <summary>A cached font plus the metrics that would otherwise be re-queried per call.</summary>
    internal sealed class Entry
    {
        public required SKFont        Font;
        public          SKFontMetrics Metrics;

        /// <summary>Descent − Ascent: the distance the layout engine uses for a line box.</summary>
        public float TextHeight;
    }

    private static readonly object TypefaceLock = new();
    private static SKTypeface? _regular;
    private static SKTypeface? _bold;
    private static SKTypeface? _italic;

    [ThreadStatic] private static Dictionary<long, Entry>? _cache;

    /// <summary>Cached font for a style's text settings. Never dispose the result.</summary>
    public static Entry Get(Style s) => Get(s.FontSize, s.Bold, s.Italic);

    /// <summary>Cached font for an explicit size/weight/slant. Never dispose the result.</summary>
    public static Entry Get(float fontSize, bool bold, bool italic)
    {
        // Exact float bits in the high 32, style flags in the low 2 — no quantisation,
        // so the cached font is identical to a freshly constructed one.
        long key = ((long)BitConverter.SingleToInt32Bits(fontSize) << 2)
                 | (bold ? 1L : 0L)
                 | (italic ? 2L : 0L);

        var cache = _cache ??= new Dictionary<long, Entry>();
        if (cache.TryGetValue(key, out var entry)) return entry;

        var font = new SKFont(Typeface(bold, italic), fontSize);
        font.GetFontMetrics(out var metrics);

        entry = new Entry
        {
            Font       = font,
            Metrics    = metrics,
            TextHeight = metrics.Descent - metrics.Ascent,
        };
        cache[key] = entry;
        return entry;
    }

    private static SKTypeface Typeface(bool bold, bool italic)
    {
        // Bold wins over italic — same precedence the renderer and layout engine
        // used before this cache existed.
        if (bold)
        {
            if (_bold != null) return _bold;
            lock (TypefaceLock)
                return _bold ??= SKTypeface.FromFamilyName(
                    null, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        }

        if (italic)
        {
            if (_italic != null) return _italic;
            lock (TypefaceLock)
                return _italic ??= SKTypeface.FromFamilyName(
                    null, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic);
        }

        if (_regular != null) return _regular;
        lock (TypefaceLock)
            return _regular ??= SKTypeface.Default;
    }
}
