using System;
using System.Collections.Generic;
using SkiaSharp;

namespace PanacheUI.Core;

/// <summary>
/// Line breaking for <see cref="TextOverflow.Wrap"/>, plus the measurement primitives the
/// public <c>PUI.MeasureText</c> surface is built on.
/// </summary>
/// <remarks>
/// <para><b>Why the cache.</b> Both the layout engine and the renderer need the exact same
/// list of lines for a wrapping node — layout to know how tall the block is, the renderer
/// to draw it — and layout asks more than once per frame (measure, then place). Breaking
/// lines allocates a substring per line, so recomputing per caller made a wrapped
/// paragraph cost a fresh string array several times a frame, every frame, for text that
/// almost never changes. Keyed on everything that can change the break points, a window's
/// static labels resolve to one cached array for the lifetime of the process.</para>
///
/// <para><b>Threading.</b> <c>[ThreadStatic]</c> for the same reason <see cref="FontCache"/>
/// is: <c>RenderApi</c> lays out and renders from thread-pool threads while the ImGui draw
/// thread is doing the same, and a shared dictionary would need a lock on the hottest path
/// in the framework.</para>
///
/// <para><b>Eviction.</b> The cache is cleared wholesale once it passes
/// <see cref="MaxEntries"/>. Text that varies per frame (a live counter, a clock) would
/// otherwise grow it without bound; a periodic full clear costs one re-break of the stable
/// entries and keeps the ceiling flat. Nothing holds a reference to the arrays across the
/// clear — callers use them within the frame.</para>
/// </remarks>
internal static class TextLayout
{
    private const int MaxEntries = 512;

    private readonly struct Key : IEquatable<Key>
    {
        public readonly string Text;
        public readonly float  FontSize;
        public readonly float  MaxWidth;
        public readonly int    MaxLines;
        public readonly bool   Bold;
        public readonly bool   Italic;

        public Key(string text, float fontSize, bool bold, bool italic, float maxWidth, int maxLines)
        {
            Text = text; FontSize = fontSize; Bold = bold; Italic = italic;
            MaxWidth = maxWidth; MaxLines = maxLines;
        }

        public bool Equals(Key o) =>
            MaxWidth.Equals(o.MaxWidth) && FontSize.Equals(o.FontSize) &&
            MaxLines == o.MaxLines && Bold == o.Bold && Italic == o.Italic &&
            string.Equals(Text, o.Text, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is Key k && Equals(k);

        public override int GetHashCode() =>
            HashCode.Combine(Text, FontSize, MaxWidth, MaxLines, Bold, Italic);
    }

    /// <summary>A broken block of text: the lines themselves plus the metrics both callers want.</summary>
    internal sealed class Block
    {
        public required string[] Lines;

        /// <summary>Width of the widest line — the block's intrinsic width.</summary>
        public float MaxLineWidth;
    }

    [ThreadStatic] private static Dictionary<Key, Block>? _cache;

    /// <summary>
    /// Break <paramref name="text"/> to fit <paramref name="maxWidth"/> pixels.
    /// </summary>
    /// <remarks>
    /// Greedy, breaking on whitespace; a single word wider than the line is hard-broken
    /// mid-word rather than allowed to overflow. Embedded <c>\n</c> (and <c>\r\n</c>) is
    /// always a hard break. When <paramref name="maxLines"/> is above 0 and text remains
    /// after the last kept line, that line is ellipsized — the same "…" budget arithmetic
    /// <c>SkiaRenderer.DrawText</c> uses for a single Ellipsis line.
    /// </remarks>
    /// <param name="maxWidth">Content width in pixels. Values at or below 0, or non-finite,
    /// produce a single unbroken line — there is no width to break against.</param>
    /// <param name="maxLines">Line cap, 0 for unlimited.</param>
    public static Block Wrap(string text, float fontSize, bool bold, bool italic, float maxWidth, int maxLines)
    {
        var cache = _cache ??= new Dictionary<Key, Block>();
        var key   = new Key(text, fontSize, bold, italic, maxWidth, maxLines);
        if (cache.TryGetValue(key, out var hit)) return hit;

        var block = Break(text, fontSize, bold, italic, maxWidth, maxLines);

        if (cache.Count >= MaxEntries) cache.Clear();
        cache[key] = block;
        return block;
    }

    private static Block Break(string text, float fontSize, bool bold, bool italic, float maxWidth, int maxLines)
    {
        var font = FontCache.Get(fontSize, bold, italic).Font;

        if (!(maxWidth > 0f) || float.IsInfinity(maxWidth) || float.IsNaN(maxWidth))
            return new Block { Lines = new[] { text }, MaxLineWidth = font.MeasureText(text) };

        var lines = new List<string>(4);

        foreach (var paragraph in SplitHardBreaks(text))
        {
            if (paragraph.Length == 0) { lines.Add(string.Empty); continue; }
            BreakParagraph(paragraph, font, maxWidth, lines);
        }

        if (maxLines > 0 && lines.Count > maxLines)
        {
            string last = lines[maxLines - 1];
            lines.RemoveRange(maxLines, lines.Count - maxLines);
            lines[maxLines - 1] = Ellipsize(last, font, maxWidth);
        }

        float widest = 0f;
        for (int i = 0; i < lines.Count; i++)
            widest = Math.Max(widest, font.MeasureText(lines[i]));

        return new Block { Lines = lines.ToArray(), MaxLineWidth = widest };
    }

    private static IEnumerable<string> SplitHardBreaks(string text)
    {
        // Fast path: the overwhelming majority of labels contain no newline at all, and
        // String.Split would allocate an array for every one of them.
        if (text.IndexOf('\n') < 0) { yield return text.TrimEnd('\r'); yield break; }
        foreach (var part in text.Split('\n'))
            yield return part.TrimEnd('\r');
    }

    private static void BreakParagraph(string text, SKFont font, float maxWidth, List<string> outLines)
    {
        int lineStart = 0;          // start of the line being built
        int lastBreak = -1;         // index of the last whitespace seen on this line
        var span = text.AsSpan();

        for (int i = 0; i < span.Length; i++)
        {
            if (char.IsWhiteSpace(span[i])) lastBreak = i;

            float w = font.MeasureText(span[lineStart..(i + 1)]);
            if (w <= maxWidth) continue;

            if (lastBreak > lineStart)
            {
                // Break at the whitespace, and swallow it — a wrapped line never starts
                // with the space it broke on.
                outLines.Add(span[lineStart..lastBreak].ToString());
                lineStart = lastBreak + 1;
                while (lineStart < span.Length && char.IsWhiteSpace(span[lineStart])) lineStart++;
                i = lineStart - 1;
            }
            else
            {
                // A single word wider than the whole line: hard-break mid-word. Never emit
                // an empty line, or a line too narrow for even one glyph loops forever.
                int end = Math.Max(lineStart + 1, i);
                outLines.Add(span[lineStart..end].ToString());
                lineStart = end;
                i = lineStart - 1;
            }
            lastBreak = -1;
        }

        if (lineStart < span.Length) outLines.Add(span[lineStart..].ToString());
        else if (outLines.Count == 0) outLines.Add(string.Empty);
    }

    /// <summary>Truncate one line to <paramref name="maxWidth"/> and append an ellipsis.</summary>
    public static string Ellipsize(string text, SKFont font, float maxWidth)
    {
        if (text.Length == 0) return "…";

        // The ellipsis has to fit too — a line that fits on its own can still overflow
        // once the marker is appended.
        float ellipsisW = font.MeasureText("…");
        if (font.MeasureText(text) + ellipsisW <= maxWidth) return text + "…";

        float budget = maxWidth - ellipsisW;
        if (budget <= 0f) return "…";

        var span = text.AsSpan();
        int lo = 0, hi = span.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (font.MeasureText(span[..mid]) <= budget) lo = mid;
            else hi = mid - 1;
        }
        return string.Concat(span[..lo].TrimEnd(), "…");
    }

    /// <summary>Per-line advance in pixels for a style — font line box times LineHeight.</summary>
    public static float LineStep(float fontSize, bool bold, bool italic, float lineHeight) =>
        FontCache.Get(fontSize, bold, italic).TextHeight * lineHeight;
}
