using System;
using PanacheUI.Core;

namespace PanacheUI.Components;

public static partial class PUI
{
    // ── Text measurement ────────────────────────────────────────────────────
    //
    // The framework has always measured text exactly — the layout engine cannot place a
    // Fit-width label without doing so — it just never exposed it. Consumers therefore
    // invented per-plugin constants for "average glyph width" and hand-maintained
    // reserved-width numbers that every font-size change silently invalidated. These are
    // thin wrappers over the same cached SKFont the layout engine and renderer use, so
    // they agree with what will actually be painted, to the pixel.

    /// <summary>
    /// Width in pixels of <paramref name="text"/> rendered on one line at these font
    /// settings — the same value the layout engine uses to size a Fit-width text node.
    /// </summary>
    /// <remarks>
    /// Cheap enough to call per frame: the font comes from a process-lifetime cache keyed
    /// on the exact (size, bold, italic) triple, so nothing is allocated per call.
    /// </remarks>
    public static float MeasureText(string text, float fontSize, bool bold = false, bool italic = false) =>
        string.IsNullOrEmpty(text) ? 0f : FontCache.Get(fontSize, bold, italic).Font.MeasureText(text);

    /// <summary>Measure against a <see cref="Style"/>'s font settings rather than loose arguments.</summary>
    public static float MeasureText(string text, Style style) =>
        MeasureText(text, style.FontSize, style.Bold, style.Italic);

    /// <summary>
    /// Height of a single line at these font settings — the font's line box (descent minus
    /// ascent) times <paramref name="lineHeight"/>, matching <see cref="Style.LineHeight"/>'s
    /// default of 1.2.
    /// </summary>
    public static float MeasureLineHeight(float fontSize, bool bold = false, bool italic = false,
        float lineHeight = 1.2f) =>
        TextLayout.LineStep(fontSize, bold, italic, lineHeight);

    /// <summary>
    /// Height in pixels <paramref name="text"/> occupies once wrapped to
    /// <paramref name="maxWidth"/> — what a <see cref="TextOverflow.Wrap"/> node with
    /// <see cref="SizeMode.Fit"/> height will measure to.
    /// </summary>
    /// <remarks>
    /// Prefer letting layout do this: set <c>TextOverflow = TextOverflow.Wrap</c> and the
    /// node sizes itself. This exists for the cases where a caller genuinely needs the
    /// number up front — reserving space in a fixed-height strip, deciding whether a
    /// tooltip fits above or below the cursor.
    /// </remarks>
    /// <param name="maxLines">Line cap, 0 for unlimited. Matches <see cref="Style.MaxLines"/>.</param>
    public static float MeasureWrappedHeight(
        string text, float fontSize, float maxWidth,
        bool bold = false, bool italic = false, float lineHeight = 1.2f, int maxLines = 0)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        var block = TextLayout.Wrap(text, fontSize, bold, italic, maxWidth, maxLines);
        return block.Lines.Length * TextLayout.LineStep(fontSize, bold, italic, lineHeight);
    }

    /// <summary>
    /// The lines <paramref name="text"/> breaks into at <paramref name="maxWidth"/>. Same
    /// break points the renderer will paint. Do not mutate the returned array — it is
    /// shared from a cache.
    /// </summary>
    public static string[] WrapText(
        string text, float fontSize, float maxWidth,
        bool bold = false, bool italic = false, int maxLines = 0) =>
        string.IsNullOrEmpty(text)
            ? Array.Empty<string>()
            : TextLayout.Wrap(text, fontSize, bold, italic, maxWidth, maxLines).Lines;

    /// <summary>
    /// A text node that wraps to whatever width it is given and grows to hold every line.
    /// </summary>
    /// <remarks>
    /// This is the replacement for hand-rolled character-budget wrapping — the
    /// <c>HintCharW</c> / <c>HintReservedW</c> / <c>WrapHint</c> family of constants and
    /// helpers that a plugin ends up maintaining because <see cref="TextOverflow"/> only
    /// offered Clip and Ellipsis. Width stays Fill and height stays Fit, so it takes the
    /// column it is placed in and reports its real height back to the parent.
    /// </remarks>
    /// <param name="maxLines">Cap the block and ellipsize the last kept line. 0 = unlimited.</param>
    public static Node Paragraph(string text, PColor color, float fontSize = 11f, int maxLines = 0) =>
        new Node().WithText(text).WithStyle(s =>
        {
            s.WidthMode     = SizeMode.Fill;
            s.HeightMode    = SizeMode.Fit;
            s.TextOverflow  = TextOverflow.Wrap;
            s.MaxLines      = maxLines;
            s.FontSize      = fontSize;
            s.Color         = color;
            s.PointerEvents = PointerEvents.None;
        });
}
