using System;
using System.Collections.Generic;
using System.Linq;
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
    public Dictionary<Node, LayoutBox> Compute(Node root, float availWidth, float availHeight)
    {
        _layout.Clear();
        MeasureNode(root);
        PlaceNode(root, 0, 0, availWidth, availHeight);
        return _layout;
    }

    // ── Pass 1: measure ──────────────────────────────────────────────────────

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

        // Only flow children contribute to intrinsic size
        var flowChildren = GetFlowChildren(node);

        float contentW = 0, contentH = 0;

        if (s.Flow == Flow.Horizontal && s.FlowWrap)
        {
            // Wrapped horizontal: simulate rows to get intrinsic height
            // We don't know final width here so we use a loose estimate.
            // The actual wrap happens in PlaceNode. Here we just sum all children widths
            // and heights to get a rough intrinsic — PlaceNode will correct it.
            float cursorX = 0, maxH = 0, totalH = 0;
            float approxWidth = s.WidthMode == SizeMode.Fixed ? s.Width : float.MaxValue;
            float innerW = approxWidth - s.Padding.Horizontal;

            foreach (var child in flowChildren)
            {
                var (cw, ch) = MeasureChild(child);
                float childOuter = child.Style.Margin.Left + cw + child.Style.Margin.Right;
                float childH     = child.Style.Margin.Top  + ch + child.Style.Margin.Bottom;

                if (cursorX > 0 && cursorX + childOuter > innerW)
                {
                    totalH  += maxH + s.Gap;
                    cursorX  = 0;
                    maxH     = 0;
                }
                cursorX += childOuter + (cursorX > 0 ? s.Gap : 0);
                maxH     = Math.Max(maxH, childH);
                contentW = Math.Max(contentW, cursorX);
            }
            contentH = totalH + maxH;
        }
        else if (s.Flow == Flow.Horizontal)
        {
            float cursorX = 0, maxH = 0;
            for (int i = 0; i < flowChildren.Count; i++)
            {
                var child = flowChildren[i];
                var (cw, ch) = MeasureChild(child);
                cursorX += child.Style.Margin.Left + cw + child.Style.Margin.Right;
                if (i < flowChildren.Count - 1) cursorX += s.Gap;
                maxH = Math.Max(maxH, child.Style.Margin.Top + ch + child.Style.Margin.Bottom);
            }
            contentW = cursorX;
            contentH = maxH;
        }
        else // Vertical
        {
            float cursorY = 0, maxW = 0;
            for (int i = 0; i < flowChildren.Count; i++)
            {
                var child = flowChildren[i];
                var (cw, ch) = MeasureChild(child);
                cursorY += child.Style.Margin.Top + ch + child.Style.Margin.Bottom;
                if (i < flowChildren.Count - 1) cursorY += s.Gap;
                maxW = Math.Max(maxW, child.Style.Margin.Left + cw + child.Style.Margin.Right);
            }
            contentW = maxW;
            contentH = cursorY;
        }

        return (contentW, contentH);
    }

    private (float w, float h) MeasureChild(Node child)
    {
        Style s = child.Style;
        var (contentW, contentH) = MeasureNode(child);

        float w = s.WidthMode  == SizeMode.Fixed ? s.Width  : contentW + s.Padding.Horizontal;
        float h = s.HeightMode == SizeMode.Fixed ? s.Height : contentH + s.Padding.Vertical;

        if (s.WidthMode  == SizeMode.Fill) w = 0;
        if (s.HeightMode == SizeMode.Fill) h = 0;

        return (w, h);
    }

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

        // ── Min / Max constraints ────────────────────────────────────────────
        if (s.MinWidth  > 0) nodeW = Math.Max(nodeW, s.MinWidth);
        if (s.MaxWidth  > 0) nodeW = Math.Min(nodeW, s.MaxWidth);
        if (s.MinHeight > 0) nodeH = Math.Max(nodeH, s.MinHeight);
        if (s.MaxHeight > 0) nodeH = Math.Min(nodeH, s.MaxHeight);

        // ── Aspect ratio ─────────────────────────────────────────────────────
        if (s.AspectRatio > 0)
        {
            if (s.WidthMode != SizeMode.Fit || s.HeightMode == SizeMode.Fit)
                nodeH = nodeW / s.AspectRatio;  // derive height from width
            else
                nodeW = nodeH * s.AspectRatio;  // derive width from height
        }

        float nodeX = x + s.Margin.Left;
        float nodeY = y + s.Margin.Top;

        _layout[node] = new LayoutBox(nodeX, nodeY, nodeW, nodeH);

        if (node.Children.Count == 0) return;

        float contentX = nodeX + s.Padding.Left;
        float contentY = nodeY + s.Padding.Top;
        float contentW = nodeW - s.Padding.Horizontal;
        float contentH = nodeH - s.Padding.Vertical;

        // ── Flow layout ──────────────────────────────────────────────────────

        var flowChildren = GetFlowChildren(node);
        int n = flowChildren.Count;

        if (s.Flow == Flow.Horizontal && s.FlowWrap)
        {
            PlaceHorizontalWrap(node, flowChildren, contentX, contentY, contentW, contentH, s);
        }
        else if (s.Flow == Flow.Horizontal)
        {
            PlaceHorizontal(flowChildren, contentX, contentY, contentW, contentH, s, n);
        }
        else // Vertical
        {
            // For OverflowY.Scroll, allow children to lay out at their natural height
            float childAvailH = s.OverflowY == OverflowMode.Scroll
                ? float.MaxValue / 2f
                : contentH;
            PlaceVertical(flowChildren, contentX, contentY, contentW, childAvailH, s, n);

            // Record total content height for scroll clamping
            if (s.OverflowY == OverflowMode.Scroll && flowChildren.Count > 0)
            {
                float maxBottom = contentY;
                foreach (var child in flowChildren)
                {
                    if (_layout.TryGetValue(child, out var cb))
                        maxBottom = Math.Max(maxBottom, cb.Bottom + child.Style.Margin.Bottom);
                }
                float totalContentH = (maxBottom - contentY) + s.Padding.Bottom;
                _layout[node] = _layout[node] with { ContentHeight = totalContentH };
            }
        }

        // ── Absolute children ─────────────────────────────────────────────────
        foreach (var child in node.Children)
        {
            if (child.Style.Position != PositionMode.Absolute) continue;
            var (natW, natH) = MeasureNaturalOuter(child);
            float childAvailW = child.Style.WidthMode == SizeMode.Fill ? contentW : natW;
            float childAvailH = child.Style.HeightMode == SizeMode.Fill ? contentH : natH;
            // Place relative to parent content origin at (Left, Top)
            PlaceNode(child,
                contentX + child.Style.Left - child.Style.Margin.Left,
                contentY + child.Style.Top  - child.Style.Margin.Top,
                childAvailW, childAvailH);
        }
    }

    private void PlaceHorizontal(
        List<Node> children, float contentX, float contentY,
        float contentW, float contentH, Style s, int n)
    {
        float totalGap    = n > 1 ? s.Gap * (n - 1) : 0;
        int   fillWCount  = 0;
        float fixedWTotal = totalGap;

        foreach (var child in children)
        {
            fixedWTotal += child.Style.Margin.Horizontal;
            if (child.Style.WidthMode == SizeMode.Fill)
                fillWCount++;
            else
                fixedWTotal += MeasureNaturalOuter(child).w;
        }

        float fillW = fillWCount > 0
            ? Math.Max(0, (contentW - fixedWTotal) / fillWCount)
            : 0;

        float cursor = 0;
        for (int i = 0; i < n; i++)
        {
            var child = children[i];
            float childAvailW = child.Style.WidthMode == SizeMode.Fill
                ? fillW
                : MeasureNaturalOuter(child).w;
            PlaceNode(child, contentX + cursor, contentY, childAvailW, contentH);
            var box = _layout[child];
            cursor += child.Style.Margin.Left + box.Width + child.Style.Margin.Right;
            if (i < n - 1) cursor += s.Gap;
        }
    }

    private void PlaceHorizontalWrap(
        Node parent, List<Node> children,
        float contentX, float contentY,
        float contentW, float contentH, Style s)
    {
        // Measure natural sizes upfront
        var sizes = new (float w, float h)[children.Count];
        for (int i = 0; i < children.Count; i++)
            sizes[i] = MeasureNaturalOuter(children[i]);

        // Build rows greedily
        var rows = new List<List<int>>();
        var currentRow = new List<int>();
        float rowWidth = 0;

        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            float childOuterW = child.Style.Margin.Horizontal + sizes[i].w;
            float gapAdd = currentRow.Count > 0 ? s.Gap : 0;

            if (currentRow.Count > 0 && rowWidth + gapAdd + childOuterW > contentW)
            {
                rows.Add(currentRow);
                currentRow = new List<int>();
                rowWidth = 0;
            }

            currentRow.Add(i);
            rowWidth += (currentRow.Count > 1 ? s.Gap : 0) + childOuterW;
        }
        if (currentRow.Count > 0) rows.Add(currentRow);

        // Place each row
        float rowY = 0;
        foreach (var row in rows)
        {
            // Find row height
            float rowH = 0;
            foreach (int idx in row)
                rowH = Math.Max(rowH, children[idx].Style.Margin.Vertical + sizes[idx].h);

            float cursorX = 0;
            for (int ri = 0; ri < row.Count; ri++)
            {
                int idx   = row[ri];
                var child = children[idx];
                float childW = child.Style.WidthMode == SizeMode.Fill
                    ? contentW - (sizes[idx].w == 0 ? 0 : sizes[idx].w)  // treat Fill as Fit in wrap
                    : sizes[idx].w;
                PlaceNode(child, contentX + cursorX, contentY + rowY, childW, rowH);
                var box = _layout[child];
                cursorX += child.Style.Margin.Left + box.Width + child.Style.Margin.Right;
                if (ri < row.Count - 1) cursorX += s.Gap;
            }
            rowY += rowH + s.Gap;
        }
    }

    private void PlaceVertical(
        List<Node> children, float contentX, float contentY,
        float contentW, float contentH, Style s, int n)
    {
        float totalGap    = n > 1 ? s.Gap * (n - 1) : 0;
        int   fillHCount  = 0;
        float fixedHTotal = totalGap;

        foreach (var child in children)
        {
            fixedHTotal += child.Style.Margin.Vertical;
            if (child.Style.HeightMode == SizeMode.Fill)
                fillHCount++;
            else
                fixedHTotal += MeasureNaturalOuter(child).h;
        }

        float fillH = fillHCount > 0 && contentH < float.MaxValue / 2f
            ? Math.Max(0, (contentH - fixedHTotal) / fillHCount)
            : 0;

        float cursor = 0;
        for (int i = 0; i < n; i++)
        {
            var child = children[i];
            float childAvailH = child.Style.HeightMode == SizeMode.Fill
                ? fillH
                : MeasureNaturalOuter(child).h;
            PlaceNode(child, contentX, contentY + cursor, contentW, childAvailH);
            var box = _layout[child];
            cursor += child.Style.Margin.Top + box.Height + child.Style.Margin.Bottom;
            if (i < n - 1) cursor += s.Gap;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<Node> GetFlowChildren(Node node)
    {
        var list = new List<Node>(node.Children.Count);
        foreach (var child in node.Children)
            if (child.Style.Position == PositionMode.Flow) list.Add(child);
        return list;
    }

    private static SKFont CreateFont(Style s)
    {
        var typeface = s.Bold
            ? SKTypeface.FromFamilyName(null, SKFontStyleWeight.Bold,   SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            : s.Italic
                ? SKTypeface.FromFamilyName(null, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic)
                : SKTypeface.Default;
        return new SKFont(typeface, s.FontSize);
    }

    public void Dispose() { }
}

/// <summary>The computed screen-space rect for a node after layout.</summary>
public readonly record struct LayoutBox(float X, float Y, float Width, float Height)
{
    /// <summary>For OverflowY.Scroll: total natural height of children (may exceed Height).</summary>
    public float ContentHeight { get; init; } = 0f;

    public float Right  => X + Width;
    public float Bottom => Y + Height;

    public SKRect ToSkRect() => new(X, Y, Right, Bottom);

    /// <summary>Uniform-radius rounded rect.</summary>
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

    /// <summary>Per-corner rounded rect. Order: top-left, top-right, bottom-right, bottom-left.</summary>
    public SKRoundRect ToSkRoundRect(float topLeft, float topRight, float bottomRight, float bottomLeft)
    {
        var rr = new SKRoundRect();
        rr.SetRectRadii(ToSkRect(), new SKPoint[]
        {
            new(topLeft,      topLeft),
            new(topRight,     topRight),
            new(bottomRight,  bottomRight),
            new(bottomLeft,   bottomLeft),
        });
        return rr;
    }
}
