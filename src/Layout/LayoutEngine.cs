using System;
using System.Collections.Generic;
using PanacheUI.Core;
using SkiaSharp;

namespace PanacheUI.Layout;

/// <summary>
/// Two-pass CSS box-model layout engine.
///
/// Pass 1 (bottom-up): measure intrinsic content size of each node.
/// Pass 2 (top-down):  assign absolute screen positions given parent bounds.
///
/// Results are stored in a Dictionary&lt;Node, LayoutBox&gt; keyed by node.
/// </summary>
public class LayoutEngine
{
    private readonly Dictionary<Node, LayoutBox> _layout = new();

    /// <summary>Run layout on the full tree rooted at <paramref name="root"/>.</summary>
    /// <param name="root">Root node of the UI tree.</param>
    /// <param name="availWidth">Total width available (surface width).</param>
    /// <param name="availHeight">Total height available (surface height).</param>
    /// <returns>Dictionary mapping every node in the tree to its screen rect.</returns>
    public Dictionary<Node, LayoutBox> Compute(Node root, float availWidth, float availHeight)
    {
        _layout.Clear();

        // Pass 1: measure content sizes bottom-up
        MeasureNode(root);

        // Pass 2: place nodes top-down
        PlaceNode(root, 0, 0, availWidth, availHeight);

        return _layout;
    }

    // ── Pass 1: measure ─────────────────────────────────────────────────────

    /// <summary>Returns the intrinsic content size (excluding padding) of this node.</summary>
    private (float w, float h) MeasureNode(Node node)
    {
        Style s = node.Style;

        // Leaf with text
        if (node.Children.Count == 0)
        {
            if (!string.IsNullOrEmpty(node.NodeValue))
            {
                using var font = CreateFont(s);
                float tw = font.MeasureText(node.NodeValue);
                font.GetFontMetrics(out var m);
                float th = (m.Descent - m.Ascent) * s.LineHeight;
                return (tw, th);
            }
            return (0, 0);
        }

        // Container — measure children first
        float contentW = 0, contentH = 0;

        if (s.Flow == Flow.Horizontal)
        {
            float cursorX = 0, maxH = 0;
            for (int i = 0; i < node.Children.Count; i++)
            {
                var child = node.Children[i];
                var (cw, ch) = MeasureChild(child);
                cursorX += child.Style.Margin.Left + cw + child.Style.Margin.Right;
                if (i < node.Children.Count - 1) cursorX += s.Gap;
                maxH = Math.Max(maxH, child.Style.Margin.Top + ch + child.Style.Margin.Bottom);
            }
            contentW = cursorX;
            contentH = maxH;
        }
        else // Vertical
        {
            float cursorY = 0, maxW = 0;
            for (int i = 0; i < node.Children.Count; i++)
            {
                var child = node.Children[i];
                var (cw, ch) = MeasureChild(child);
                cursorY += child.Style.Margin.Top + ch + child.Style.Margin.Bottom;
                if (i < node.Children.Count - 1) cursorY += s.Gap;
                maxW = Math.Max(maxW, child.Style.Margin.Left + cw + child.Style.Margin.Right);
            }
            contentW = maxW;
            contentH = cursorY;
        }

        return (contentW, contentH);
    }

    /// <summary>Returns the outer size of a child (content + padding), for use in parent measurement.</summary>
    private (float w, float h) MeasureChild(Node child)
    {
        Style s = child.Style;
        var (contentW, contentH) = MeasureNode(child);

        float w = s.WidthMode  == SizeMode.Fixed ? s.Width  : contentW + s.Padding.Horizontal;
        float h = s.HeightMode == SizeMode.Fixed ? s.Height : contentH + s.Padding.Vertical;

        // Fill-mode children contribute 0 to parent's intrinsic size (they expand later)
        if (s.WidthMode  == SizeMode.Fill) w = 0;
        if (s.HeightMode == SizeMode.Fill) h = 0;

        return (w, h);
    }

    /// <summary>Returns the natural outer size of a node ignoring Fill mode (used for flex distribution).</summary>
    private (float w, float h) MeasureNaturalOuter(Node child)
    {
        Style s = child.Style;
        var (contentW, contentH) = MeasureNode(child);
        float w = s.WidthMode  == SizeMode.Fixed ? s.Width  : contentW + s.Padding.Horizontal;
        float h = s.HeightMode == SizeMode.Fixed ? s.Height : contentH + s.Padding.Vertical;
        return (w, h);
    }

    // ── Pass 2: place ────────────────────────────────────────────────────────

    private void PlaceNode(Node node, float x, float y, float availW, float availH)
    {
        Style s = node.Style;

        // Determine this node's outer size
        var (intrinsicW, intrinsicH) = MeasureNode(node);

        float nodeW = s.WidthMode switch
        {
            SizeMode.Fixed => s.Width,
            SizeMode.Fill  => Math.Max(0, availW - s.Margin.Horizontal),
            SizeMode.Fit   => intrinsicW + s.Padding.Horizontal,
            _              => availW,
        };

        float nodeH = s.HeightMode switch
        {
            SizeMode.Fixed => s.Height,
            SizeMode.Fill  => Math.Max(0, availH - s.Margin.Vertical),
            SizeMode.Fit   => intrinsicH + s.Padding.Vertical,
            _              => availH,
        };

        float nodeX = x + s.Margin.Left;
        float nodeY = y + s.Margin.Top;

        _layout[node] = new LayoutBox(nodeX, nodeY, nodeW, nodeH);

        if (node.Children.Count == 0) return;

        float contentX = nodeX + s.Padding.Left;
        float contentY = nodeY + s.Padding.Top;
        float contentW = nodeW - s.Padding.Horizontal;
        float contentH = nodeH - s.Padding.Vertical;
        int   n        = node.Children.Count;
        float totalGap = n > 1 ? s.Gap * (n - 1) : 0;

        if (s.Flow == Flow.Horizontal)
        {
            // Pre-pass: compute how much width fixed/fit children need,
            // then divide what's left equally among Fill children.
            int   fillW_count  = 0;
            float fixedW_total = totalGap;
            for (int i = 0; i < n; i++)
            {
                var child = node.Children[i];
                fixedW_total += child.Style.Margin.Horizontal;
                if (child.Style.WidthMode == SizeMode.Fill)
                    fillW_count++;
                else
                    fixedW_total += MeasureNaturalOuter(child).w;
            }
            float fillW = fillW_count > 0
                ? Math.Max(0, (contentW - fixedW_total) / fillW_count)
                : 0;

            float cursor = 0;
            for (int i = 0; i < n; i++)
            {
                var child = node.Children[i];
                float childAvailW = child.Style.WidthMode == SizeMode.Fill
                    ? fillW
                    : MeasureNaturalOuter(child).w;
                PlaceNode(child, contentX + cursor, contentY, childAvailW, contentH);
                var box = _layout[child];
                cursor += child.Style.Margin.Left + box.Width + child.Style.Margin.Right;
                if (i < n - 1) cursor += s.Gap;
            }
        }
        else // Vertical
        {
            // Pre-pass: divide remaining height among Fill children.
            int   fillH_count  = 0;
            float fixedH_total = totalGap;
            for (int i = 0; i < n; i++)
            {
                var child = node.Children[i];
                fixedH_total += child.Style.Margin.Vertical;
                if (child.Style.HeightMode == SizeMode.Fill)
                    fillH_count++;
                else
                    fixedH_total += MeasureNaturalOuter(child).h;
            }
            float fillH = fillH_count > 0
                ? Math.Max(0, (contentH - fixedH_total) / fillH_count)
                : 0;

            float cursor = 0;
            for (int i = 0; i < n; i++)
            {
                var child = node.Children[i];
                float childAvailH = child.Style.HeightMode == SizeMode.Fill
                    ? fillH
                    : MeasureNaturalOuter(child).h;
                PlaceNode(child, contentX, contentY + cursor, contentW, childAvailH);
                var box = _layout[child];
                cursor += child.Style.Margin.Top + box.Height + child.Style.Margin.Bottom;
                if (i < n - 1) cursor += s.Gap;
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static SKFont CreateFont(Style s)
    {
        var typeface = s.Bold
            ? SKTypeface.FromFamilyName(null, SKFontStyleWeight.Bold,   SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            : s.Italic
                ? SKTypeface.FromFamilyName(null, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic)
                : SKTypeface.Default;
        return new SKFont(typeface, s.FontSize);
    }

    public void Dispose()
    {
    }
}

/// <summary>The computed screen-space rect for a node after layout.</summary>
public readonly record struct LayoutBox(float X, float Y, float Width, float Height)
{
    public float Right  => X + Width;
    public float Bottom => Y + Height;

    public SKRect ToSkRect() => new(X, Y, Right, Bottom);

    public SKRoundRect ToSkRoundRect(float radius)
    {
        var rr = new SKRoundRect();
        rr.SetRectRadii(ToSkRect(), new SKPoint[]
        {
            new(radius, radius), new(radius, radius),
            new(radius, radius), new(radius, radius),
        });
        return rr;
    }
}
