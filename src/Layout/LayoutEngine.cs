using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
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
    /// <summary>
    /// Reference identity is the only identity a Node has, and it is what both the
    /// layout map and the measure memo want. The default comparer would go through
    /// virtual <c>Equals</c>/<c>GetHashCode</c> for every one of the thousands of
    /// lookups a frame performs; this one is a pointer compare.
    /// </summary>
    private sealed class NodeRefComparer : IEqualityComparer<Node>
    {
        public static readonly NodeRefComparer Instance = new();
        public bool Equals(Node? a, Node? b)   => ReferenceEquals(a, b);
        public int  GetHashCode(Node n)        => RuntimeHelpers.GetHashCode(n);
    }

    private readonly Dictionary<Node, LayoutBox> _layout = new(NodeRefComparer.Instance);

    /// <summary>
    /// Intrinsic content size per node, memoised for the duration of one
    /// <see cref="Compute"/> call.
    /// </summary>
    /// <remarks>
    /// <para><see cref="MeasureNode"/> is a pure function of a node's own subtree for every
    /// tree that contains no wrapping text — it reads styles and children and nothing about
    /// available space — but the placement pass calls it repeatedly: once for the node
    /// itself, then twice more per child (once to total the non-Fill track, once to hand the
    /// child its available size), and then again when recursing into that child. That makes
    /// the un-memoised cost O(N · depth) measurements of the whole subtree each. Memoising
    /// collapses it to exactly one measurement per node per frame.</para>
    ///
    /// <para>A subtree containing <see cref="TextOverflow.Wrap"/> text is <i>not</i>
    /// width-independent — that is the entire point of wrapping — so it cannot use this
    /// map. Those nodes go through <see cref="_measureWrapped"/>, which keys on the
    /// available width as well. <see cref="HasWrap"/> decides which, so the fast path is
    /// untouched for the trees that make up almost all of a real window.</para>
    /// </remarks>
    private readonly Dictionary<Node, (float w, float h)> _measure = new(NodeRefComparer.Instance);

    /// <summary>Width-keyed measure memo for subtrees that contain wrapping text.</summary>
    private readonly Dictionary<WrapMeasureKey, (float w, float h)> _measureWrapped = new();

    /// <summary>(node, available content width) — identity for a width-dependent measurement.</summary>
    private readonly struct WrapMeasureKey : IEquatable<WrapMeasureKey>
    {
        private readonly Node  _node;
        private readonly float _availW;

        public WrapMeasureKey(Node node, float availW) { _node = node; _availW = availW; }

        public bool Equals(WrapMeasureKey o) => ReferenceEquals(_node, o._node) && _availW.Equals(o._availW);
        public override bool Equals(object? obj) => obj is WrapMeasureKey k && Equals(k);
        public override int GetHashCode() =>
            RuntimeHelpers.GetHashCode(_node) * 397 ^ BitConverter.SingleToInt32Bits(_availW);
    }

    /// <summary>
    /// Identifies the most recent layout pass, process-wide. Nodes stamp themselves with
    /// this so framework internals can read <see cref="Node.CachedBox"/> without a
    /// dictionary lookup, and can tell a fresh box from a leftover one.
    /// </summary>
    /// <remarks>
    /// Bumped through <see cref="Interlocked"/> because layout does not only run on the
    /// ImGui draw thread — <c>RenderApi</c> lays out and renders effect strips on
    /// thread-pool threads, and two passes must never be handed the same stamp.
    /// </remarks>
    private static long _stampCounter;

    /// <summary>The stamp written by the most recent <see cref="Compute"/> call on this engine.</summary>
    public ulong Stamp { get; private set; }

    /// <summary>Run layout on the full tree rooted at <paramref name="root"/>.</summary>
    public Dictionary<Node, LayoutBox> Compute(Node root, float availWidth, float availHeight)
    {
        _layout.Clear();
        _measure.Clear();
        _measureWrapped.Clear();
        Stamp = (ulong)Interlocked.Increment(ref _stampCounter);
        MeasureNode(root, ContentAvail(root.Style, availWidth));
        PlaceNode(root, 0, 0, availWidth, availHeight);
        return _layout;
    }

    // ── Width propagation ────────────────────────────────────────────────────

    /// <summary>
    /// The width available to a node's <i>content box</i> given the outer space its parent
    /// offers it — i.e. what wrapping text inside it may break against.
    /// </summary>
    /// <remarks>
    /// A <see cref="SizeMode.Fit"/> node gets the same bound as a Fill one: shrink-to-fit
    /// means "as narrow as the content wants, but no wider than what's on offer", so
    /// wrapping inside it must still respect the parent's width. The result may legitimately
    /// be enormous — an <see cref="OverflowMode.Scroll"/> container hands its children
    /// <c>float.MaxValue / 2</c> so they lay out at natural size — and
    /// <see cref="TextLayout.Wrap"/> simply finds nothing to break at that width.
    /// </remarks>
    private static float ContentAvail(Style s, float outerAvail)
    {
        float outer = s.WidthMode == SizeMode.Fixed
            ? s.Width
            : Math.Max(0f, outerAvail - s.Margin.Horizontal);

        if (s.MaxWidth > 0) outer = Math.Min(outer, s.MaxWidth);
        if (s.MinWidth > 0) outer = Math.Max(outer, s.MinWidth);

        return Math.Max(0f, outer - s.Padding.Horizontal);
    }

    /// <summary>True when <paramref name="n"/> or any descendant wraps its text, and the
    /// subtree's measurement therefore depends on the width it is given.</summary>
    private bool HasWrap(Node n)
    {
        if (n.HasWrapStamp == Stamp) return n.CachedHasWrap;

        bool result = n.Style.TextOverflow == TextOverflow.Wrap && !string.IsNullOrEmpty(n.NodeValue);
        if (!result)
        {
            var kids = n.Children;
            for (int i = 0; i < kids.Count; i++)
                if (HasWrap(kids[i])) { result = true; break; }
        }

        n.CachedHasWrap = result;
        n.HasWrapStamp  = Stamp;
        return result;
    }

    // ── Pass 1: measure ──────────────────────────────────────────────────────

    /// <summary>
    /// Diagnostic switch: set false to re-measure from scratch on every call, the way
    /// layout worked before the memo existed. Memoisation is an optimisation only —
    /// with it off the computed boxes must be identical, just far slower to produce.
    /// </summary>
    internal static bool UseMeasureCache = true;

    /// <param name="availContentW">Width available to <paramref name="node"/>'s content box.
    /// Only read by subtrees that wrap; ignored (and safely stale in the memo) otherwise.</param>
    private (float w, float h) MeasureNode(Node node, float availContentW)
    {
        if (!HasWrap(node))
        {
            if (!UseMeasureCache) return MeasureNodeUncached(node, availContentW);
            if (_measure.TryGetValue(node, out var cached)) return cached;
            var result = MeasureNodeUncached(node, availContentW);
            _measure[node] = result;
            return result;
        }

        var key = new WrapMeasureKey(node, availContentW);
        if (UseMeasureCache && _measureWrapped.TryGetValue(key, out var wrapped)) return wrapped;
        var measured = MeasureNodeUncached(node, availContentW);
        if (UseMeasureCache) _measureWrapped[key] = measured;
        return measured;
    }

    private (float w, float h) MeasureNodeUncached(Node node, float availContentW)
    {
        Style s = node.Style;

        // Leaf with text
        if (node.Children.Count == 0)
        {
            if (!string.IsNullOrEmpty(node.NodeValue))
            {
                var font = FontCache.Get(s);

                if (s.TextOverflow == TextOverflow.Wrap)
                {
                    // The wrapped block is cached by (text, font, width, maxLines), so the
                    // renderer's identical call later this frame costs a dictionary probe.
                    var block = TextLayout.Wrap(
                        node.NodeValue!, s.FontSize, s.Bold, s.Italic, availContentW, s.MaxLines);
                    return (block.MaxLineWidth, block.Lines.Length * font.TextHeight * s.LineHeight);
                }

                float tw = font.Font.MeasureText(node.NodeValue);
                float th = font.TextHeight * s.LineHeight;
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
                var (cw, ch) = MeasureChild(child, availContentW);
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
            // Fill-width children in a row don't get the whole row to wrap against — they
            // get whatever the fixed-size siblings leave. Working that out here (the same
            // split PlaceHorizontal performs) is what lets a Fit-height row containing an
            // icon and a wrapping label report the label's real, multi-line height.
            float fillW = HorizontalFillWidth(flowChildren, s, availContentW);

            float cursorX = 0, maxH = 0;
            for (int i = 0; i < flowChildren.Count; i++)
            {
                var child = flowChildren[i];
                var (cw, ch) = MeasureChild(
                    child, child.Style.WidthMode == SizeMode.Fill ? fillW : availContentW);
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
                var (cw, ch) = MeasureChild(child, availContentW);
                cursorY += child.Style.Margin.Top + ch + child.Style.Margin.Bottom;
                if (i < flowChildren.Count - 1) cursorY += s.Gap;
                maxW = Math.Max(maxW, child.Style.Margin.Left + cw + child.Style.Margin.Right);
            }
            contentW = maxW;
            contentH = cursorY;
        }

        return (contentW, contentH);
    }

    /// <summary>
    /// Outer width each Fill child of a horizontal row receives. Mirrors
    /// <see cref="PlaceHorizontal"/>'s split exactly — including its treatment of margins —
    /// so a measurement and the placement that follows it agree.
    /// </summary>
    private float HorizontalFillWidth(IReadOnlyList<Node> children, Style s, float availContentW)
    {
        int n = children.Count;
        int fillCount = 0;
        float used = n > 1 ? s.Gap * (n - 1) : 0f;

        for (int i = 0; i < n; i++)
        {
            var child = children[i];
            used += child.Style.Margin.Horizontal;
            if (child.Style.WidthMode == SizeMode.Fill) fillCount++;
            else used += MeasureNaturalOuter(child, availContentW).w;
        }

        return fillCount > 0 && availContentW < float.MaxValue / 2f
            ? Math.Max(0f, (availContentW - used) / fillCount)
            : 0f;
    }

    /// <param name="parentContentAvail">Width the parent has available for its content box —
    /// the outer space this child is offered.</param>
    private (float w, float h) MeasureChild(Node child, float parentContentAvail)
    {
        Style s = child.Style;
        var (contentW, contentH) = MeasureNode(child, ContentAvail(s, parentContentAvail));

        float w = s.WidthMode  == SizeMode.Fixed ? s.Width  : contentW + s.Padding.Horizontal;
        float h = s.HeightMode == SizeMode.Fixed ? s.Height : contentH + s.Padding.Vertical;

        if (s.WidthMode  == SizeMode.Fill) w = 0;
        if (s.HeightMode == SizeMode.Fill) h = 0;

        return (w, h);
    }

    private (float w, float h) MeasureNaturalOuter(Node child, float parentContentAvail)
    {
        Style s = child.Style;
        var (contentW, contentH) = MeasureNode(child, ContentAvail(s, parentContentAvail));
        float w = s.WidthMode  == SizeMode.Fixed ? s.Width  : contentW + s.Padding.Horizontal;
        float h = s.HeightMode == SizeMode.Fixed ? s.Height : contentH + s.Padding.Vertical;
        return (w, h);
    }

    // ── Pass 2: place ────────────────────────────────────────────────────────

    private void PlaceNode(Node node, float x, float y, float availW, float availH)
    {
        Style s = node.Style;

        float availContentW = ContentAvail(s, availW);
        var (intrinsicW, intrinsicH) = MeasureNode(node, availContentW);

        float nodeW = s.WidthMode switch
        {
            SizeMode.Fixed => s.Width,
            SizeMode.Fill  => Math.Max(0, availW - s.Margin.Horizontal),
            SizeMode.Fit   => intrinsicW + s.Padding.Horizontal,
            _              => availW,
        };

        // ── Min / Max width ──────────────────────────────────────────────────
        // Applied before the height is derived: with wrapping text in the subtree the
        // height is a function of the final width, so the width has to be settled first.
        if (s.MinWidth > 0) nodeW = Math.Max(nodeW, s.MinWidth);
        if (s.MaxWidth > 0) nodeW = Math.Min(nodeW, s.MaxWidth);

        // A shrink-to-fit box, or one a Min/Max clamp just moved, ends up narrower or wider
        // than the bound the measurement assumed. Re-measure at the width the node actually
        // got so the line count — and therefore the height — matches what will be painted.
        if (HasWrap(node))
        {
            float finalContentW = Math.Max(0f, nodeW - s.Padding.Horizontal);
            if (finalContentW != availContentW)
                (intrinsicW, intrinsicH) = MeasureNode(node, finalContentW);
        }

        float nodeH = s.HeightMode switch
        {
            SizeMode.Fixed => s.Height,
            SizeMode.Fill  => Math.Max(0, availH - s.Margin.Vertical),
            SizeMode.Fit   => intrinsicH + s.Padding.Vertical,
            _              => availH,
        };

        // ── Min / Max height ─────────────────────────────────────────────────
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

        var placed = new LayoutBox(nodeX, nodeY, nodeW, nodeH);
        _layout[node]    = placed;
        node.CachedBox   = placed;
        node.LayoutStamp = Stamp;

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
            // For OverflowX.Scroll, allow children to lay out at their natural width —
            // OverflowY's mirror image, a few lines below.
            float childAvailW = s.OverflowX == OverflowMode.Scroll
                ? float.MaxValue / 2f
                : contentW;
            PlaceHorizontal(flowChildren, contentX, contentY, childAvailW, contentH, s, n);

            // Record total content width for scroll clamping
            if (s.OverflowX == OverflowMode.Scroll && flowChildren.Count > 0)
            {
                float maxRight = contentX;
                foreach (var child in flowChildren)
                {
                    if (_layout.TryGetValue(child, out var cb))
                        maxRight = Math.Max(maxRight, cb.Right + child.Style.Margin.Right);
                }
                float totalContentW = (maxRight - contentX) + s.Padding.Right;
                var withContent = _layout[node] with { ContentWidth = totalContentW };
                _layout[node]   = withContent;
                node.CachedBox  = withContent;
            }
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
                var withContent = _layout[node] with { ContentHeight = totalContentH };
                _layout[node]   = withContent;
                node.CachedBox  = withContent;
            }
        }

        // ── Absolute children ─────────────────────────────────────────────────
        var allChildren = node.Children;
        for (int i = 0; i < allChildren.Count; i++)
        {
            var child = allChildren[i];
            if (child.Style.Position != PositionMode.Absolute) continue;
            var (natW, natH) = MeasureNaturalOuter(child, contentW);
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
        IReadOnlyList<Node> children, float contentX, float contentY,
        float contentW, float contentH, Style s, int n)
    {
        float totalGap    = n > 1 ? s.Gap * (n - 1) : 0;
        int   fillWCount  = 0;
        float fixedWTotal = totalGap;

        for (int i = 0; i < n; i++)
        {
            var child = children[i];
            fixedWTotal += child.Style.Margin.Horizontal;
            if (child.Style.WidthMode == SizeMode.Fill)
                fillWCount++;
            else
                fixedWTotal += MeasureNaturalOuter(child, contentW).w;
        }

        // The guard mirrors PlaceVertical's fillH: when this row is an OverflowX.Scroll
        // container, PlaceNode hands it float.MaxValue/2f as contentW so children lay out
        // at natural size — a Fill child in that state must not try to claim "half of
        // infinity".
        float fillW = fillWCount > 0 && contentW < float.MaxValue / 2f
            ? Math.Max(0, (contentW - fixedWTotal) / fillWCount)
            : 0;

        float cursor = 0;
        for (int i = 0; i < n; i++)
        {
            var child = children[i];
            float childAvailW = child.Style.WidthMode == SizeMode.Fill
                ? fillW
                : MeasureNaturalOuter(child, contentW).w;

            PlaceNode(child, contentX + cursor,
                contentY + CrossOffsetY(s, child, childAvailW, contentH),
                childAvailW, contentH);

            var box = _layout[child];
            cursor += child.Style.Margin.Left + box.Width + child.Style.Margin.Right;
            if (i < n - 1) cursor += s.Gap;
        }
    }

    // ── Cross-axis alignment ─────────────────────────────────────────────────

    private static AlignItems Align(Style parent, Style child) => child.AlignSelf ?? parent.AlignItems;

    private static float CrossOffset(AlignItems align, float free) =>
        free <= 0f ? 0f
        : align switch
        {
            AlignItems.Center => free * 0.5f,
            AlignItems.End    => free,
            _                 => 0f,
        };

    /// <summary>
    /// Vertical offset for a child of a horizontal row under the row's
    /// <see cref="Style.AlignItems"/>. Zero for the default Start, for a Fill-height child
    /// (which already spans the whole cross axis), and inside a scroll container that has
    /// handed down an unbounded cross extent.
    /// </summary>
    private float CrossOffsetY(Style parent, Node child, float childAvailW, float contentH)
    {
        var align = Align(parent, child.Style);
        if (align == AlignItems.Start
            || child.Style.HeightMode == SizeMode.Fill
            || contentH >= float.MaxValue / 2f) return 0f;

        float outerH = MeasureNaturalOuter(child, childAvailW).h + child.Style.Margin.Vertical;
        return CrossOffset(align, contentH - outerH);
    }

    /// <summary><see cref="CrossOffsetY"/>'s mirror for a child of a vertical stack.</summary>
    private float CrossOffsetX(Style parent, Node child, float contentW)
    {
        var align = Align(parent, child.Style);
        if (align == AlignItems.Start
            || child.Style.WidthMode == SizeMode.Fill
            || contentW >= float.MaxValue / 2f) return 0f;

        float outerW = MeasureNaturalOuter(child, contentW).w + child.Style.Margin.Horizontal;
        return CrossOffset(align, contentW - outerW);
    }

    private void PlaceHorizontalWrap(
        Node parent, IReadOnlyList<Node> children,
        float contentX, float contentY,
        float contentW, float contentH, Style s)
    {
        // Measure natural sizes upfront
        var sizes = new (float w, float h)[children.Count];
        for (int i = 0; i < children.Count; i++)
            sizes[i] = MeasureNaturalOuter(children[i], contentW);

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
                PlaceNode(child, contentX + cursorX,
                    contentY + rowY + CrossOffsetY(s, child, childW, rowH),
                    childW, rowH);
                var box = _layout[child];
                cursorX += child.Style.Margin.Left + box.Width + child.Style.Margin.Right;
                if (ri < row.Count - 1) cursorX += s.Gap;
            }
            rowY += rowH + s.Gap;
        }
    }

    private void PlaceVertical(
        IReadOnlyList<Node> children, float contentX, float contentY,
        float contentW, float contentH, Style s, int n)
    {
        float totalGap    = n > 1 ? s.Gap * (n - 1) : 0;
        int   fillHCount  = 0;
        float fixedHTotal = totalGap;

        for (int i = 0; i < n; i++)
        {
            var child = children[i];
            fixedHTotal += child.Style.Margin.Vertical;
            if (child.Style.HeightMode == SizeMode.Fill)
                fillHCount++;
            else
                fixedHTotal += MeasureNaturalOuter(child, contentW).h;
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
                : MeasureNaturalOuter(child, contentW).h;
            PlaceNode(child, contentX + CrossOffsetX(s, child, contentW),
                contentY + cursor, contentW, childAvailH);
            var box = _layout[child];
            cursor += child.Style.Margin.Top + box.Height + child.Style.Margin.Bottom;
            if (i < n - 1) cursor += s.Gap;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The flow-positioned subset of a node's children.
    /// </summary>
    /// <remarks>
    /// Absolutely-positioned children are rare, so the overwhelmingly common answer is
    /// "all of them". Returning the node's own child list in that case keeps layout
    /// allocation-free; only a node that actually mixes flow and absolute children
    /// pays for a filtered copy.
    /// </remarks>
    private static IReadOnlyList<Node> GetFlowChildren(Node node)
    {
        var children = node.Children;
        int count = children.Count;

        bool allFlow = true;
        for (int i = 0; i < count; i++)
        {
            if (children[i].Style.Position != PositionMode.Flow) { allFlow = false; break; }
        }
        if (allFlow) return children;

        var list = new List<Node>(count);
        for (int i = 0; i < count; i++)
            if (children[i].Style.Position == PositionMode.Flow) list.Add(children[i]);
        return list;
    }

    public void Dispose() { }
}

/// <summary>The computed screen-space rect for a node after layout.</summary>
public readonly record struct LayoutBox(float X, float Y, float Width, float Height)
{
    /// <summary>For OverflowY.Scroll: total natural height of children (may exceed Height).</summary>
    public float ContentHeight { get; init; } = 0f;

    /// <summary>For OverflowX.Scroll: total natural width of children (may exceed Width).</summary>
    public float ContentWidth { get; init; } = 0f;

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
