using System.Collections.Generic;
using System.Numerics;
using PanacheUI.Layout;

namespace PanacheUI.Core;

/// <summary>
/// Updates NodeAnimState for all nodes based on mouse position, clicks, scroll, and keyboard input.
/// Call Update() each frame after layout is computed and before rendering.
/// </summary>
public static class InteractionManager
{
    // ── Keyboard focus ────────────────────────────────────────────────────────

    /// <summary>The node currently holding keyboard focus, or null.</summary>
    public static Node? FocusedNode { get; private set; }

    /// <summary>
    /// <see cref="Node.Id"/> of the focused node, or null. This — not
    /// <see cref="FocusedNode"/> — is the durable half of focus.
    /// </summary>
    /// <remarks>
    /// Same problem pointer capture and scroll offsets already have: consumers rebuild the
    /// whole node tree every frame, so the object that was focused last frame is an orphan
    /// by the time the next one is built. A widget deciding whether to draw itself as
    /// focused has to ask by Id (<see cref="IsFocused(string)"/>), because at tree-build
    /// time its own node for this frame does not exist yet, let alone hold focus.
    /// <see cref="FocusedNode"/> is re-resolved against the live tree at the start of every
    /// <see cref="Update"/>, so event routing still lands on this frame's instance.
    /// </remarks>
    public static string? FocusedId { get; private set; }

    /// <summary>Programmatically focus a node. The node must have IsFocusable = true.</summary>
    public static void SetFocus(Node? node)
    {
        bool ok    = node?.IsFocusable == true;
        FocusedNode = ok ? node : null;
        FocusedId   = ok && !string.IsNullOrEmpty(node!.Id) ? node.Id : null;
    }

    /// <summary>Focus by Id. Takes effect on the next <see cref="Update"/>, which resolves
    /// the Id against that frame's tree.</summary>
    public static void SetFocus(string? id)
    {
        FocusedId   = string.IsNullOrEmpty(id) ? null : id;
        FocusedNode = null;
    }

    /// <summary>True when the node with this Id currently holds keyboard focus.</summary>
    public static bool IsFocused(string id) =>
        !string.IsNullOrEmpty(id) && string.Equals(FocusedId, id, System.StringComparison.Ordinal);

    /// <summary>Drop keyboard focus entirely.</summary>
    public static void ClearFocus()
    {
        FocusedNode = null;
        FocusedId   = null;
    }

    /// <summary>Set while the current <see cref="Update"/> walk hands focus to some node, so
    /// a click that hit nothing focusable can blur instead of leaving stale focus behind.</summary>
    private static bool _focusClaimed;

    /// <summary>
    /// Route a key-down event to the focused node (if any).
    /// Call from your plugin's ImGui keyboard handling.
    /// </summary>
    public static void RouteKeyDown(int keyCode)
    {
        FocusedNode?.FireKeyDown(keyCode);
    }

    /// <summary>
    /// Route a typed character to the focused node (if any).
    /// Call from your plugin's ImGui text input handling.
    /// </summary>
    public static void RouteKeyChar(char c)
    {
        FocusedNode?.FireKeyChar(c);
    }

    // ── Pointer capture (drag) ────────────────────────────────────────────────

    /// <summary>
    /// The node currently holding pointer capture, or null. Set automatically when the
    /// primary button is pressed on a node with <see cref="Node.CapturesDrag"/>, and
    /// cleared when the button is released.
    /// </summary>
    /// <remarks>
    /// Re-resolved from <see cref="CapturedNodeId"/> against the live tree every frame.
    /// Most consumers rebuild their whole node tree each frame, so holding the original
    /// object across frames would leave capture pointing at an orphan that is absent from
    /// the current layout — the drag would silently do nothing. Capture is therefore keyed
    /// on <see cref="Node.Id"/>, and any node with <see cref="Node.CapturesDrag"/> must
    /// carry a stable non-empty Id. Nodes without an Id fall back to reference identity,
    /// which only works for trees that persist across frames.
    /// </remarks>
    public static Node? CapturedNode { get; private set; }

    /// <summary>Id of the node holding pointer capture, or null. See <see cref="CapturedNode"/>.</summary>
    public static string? CapturedNodeId { get; private set; }

    /// <summary>
    /// Force-release pointer capture without firing <see cref="Node.OnDragEnd"/>.
    /// Call when tearing down a window whose tree held capture, so a stale node
    /// reference is not kept alive across rebuilds.
    /// </summary>
    public static void ReleaseCapture()
    {
        CapturedNode   = null;
        CapturedNodeId = null;
    }

    // ── Scroll persistence ────────────────────────────────────────────────────

    /// <summary>
    /// Scroll offsets, keyed by <see cref="Node.Id"/>.
    /// </summary>
    /// <remarks>
    /// Same problem as pointer capture: consumers rebuild their node tree every frame, and
    /// <see cref="NodeAnimState"/> is per-Node-object, so a freshly built scroll container
    /// starts at offset 0 every frame. The wheel would nudge it and it would snap straight
    /// back — visible as jitter. Offsets are therefore remembered by Id and reapplied at the
    /// start of each frame. A scroll container needs a stable non-empty Id to scroll at all.
    /// </remarks>
    private static readonly Dictionary<string, float> ScrollState = new();

    /// <summary>Horizontal counterpart to <see cref="ScrollState"/> — a separate dictionary
    /// rather than a shared key scheme so a node can (independently) remember an X offset,
    /// a Y offset, or both.</summary>
    private static readonly Dictionary<string, float> ScrollStateX = new();

    /// <summary>Forget all remembered scroll offsets, both axes (e.g. when a window closes).</summary>
    public static void ResetScroll() { ScrollState.Clear(); ScrollStateX.Clear(); }

    /// <summary>Forget one remembered scroll offset, both axes.</summary>
    public static void ResetScroll(string id) { ScrollState.Remove(id); ScrollStateX.Remove(id); }

    /// <summary>The horizontal scroll offset currently persisted for a scroller Id, or 0 if
    /// none has been recorded yet. For a node other than the scroller itself — a scrollbar,
    /// a "jump to top" button — that wants to read the scroller's live position without
    /// holding a reference to its (possibly last-frame, possibly not-yet-built) Node.</summary>
    public static float GetScrollOffsetX(string id) => ScrollStateX.TryGetValue(id, out var v) ? v : 0f;

    /// <summary>Vertical counterpart to <see cref="GetScrollOffsetX"/>.</summary>
    public static float GetScrollOffsetY(string id) => ScrollState.TryGetValue(id, out var v) ? v : 0f;

    /// <summary>
    /// Directly set a scroller's persisted horizontal offset from outside the scroller
    /// node itself — a scrollbar thumb's <c>OnDrag</c>, a page/jump button. Only the lower
    /// bound is enforced here; the scroller clamps the upper bound itself against its own
    /// content width on its next interaction pass (see <c>UpdateNode</c>'s isScrollerX
    /// block), so an over-large value self-corrects rather than needing this call to know
    /// the scroller's extents.
    /// </summary>
    /// <remarks>
    /// Takes effect from that next pass's restore-from-ScrollStateX — one frame of lag,
    /// the same consistency window every other cross-frame interaction in this codebase
    /// already accepts (hover highlight, drag-capture resolution). Imperceptible in
    /// practice: a dragged scrollbar thumb and the content it drives are both driven by
    /// the same persisted value, they just each pick it up on their own next read.
    /// </remarks>
    public static void SetScrollOffsetX(string id, float value) => ScrollStateX[id] = System.Math.Max(0f, value);

    /// <summary>Vertical counterpart to <see cref="SetScrollOffsetX"/>.</summary>
    public static void SetScrollOffsetY(string id, float value) => ScrollState[id] = System.Math.Max(0f, value);

    /// <summary>
    /// Sum of ScrollOffsetY for every scrolling ancestor. Layout boxes for children of a
    /// scroll container are in unscrolled content space, and the recursive walk compensates
    /// by offsetting the mouse. Capture bypasses that walk, so it must apply the same offset.
    /// </summary>
    /// <summary>
    /// Find this frame's instance of the captured node. Prefers Id lookup against the live
    /// tree (survives per-frame rebuilds); falls back to the retained reference for
    /// persistent trees whose capturing node has no Id. Returns null and drops capture if
    /// the node has vanished from the tree entirely.
    /// </summary>
    private static Node? ResolveCaptured(Node root)
    {
        if (CapturedNodeId == null && CapturedNode == null)
            return null;

        if (!string.IsNullOrEmpty(CapturedNodeId))
        {
            var live = root.FindById(CapturedNodeId!);
            if (live != null)
            {
                CapturedNode = live;
                return live;
            }
            // Id no longer present — the widget was removed mid-drag.
            ReleaseCapture();
            return null;
        }

        return CapturedNode;
    }

    /// <summary>Re-point <see cref="FocusedNode"/> at this frame's instance of
    /// <see cref="FocusedId"/>, dropping focus if that node has left the tree.</summary>
    private static void ResolveFocus(Node root)
    {
        if (string.IsNullOrEmpty(FocusedId))
        {
            // A node focused without an Id can only be tracked by reference, which works
            // for a persistent tree and nothing else. Leave it alone.
            return;
        }

        var live = root.FindById(FocusedId!);
        if (live is { IsFocusable: true }) FocusedNode = live;
        else if (live == null) ClearFocus();
    }

    private static Vector2 AccumulatedScrollOffset(Node node)
    {
        float sumX = 0f, sumY = 0f;
        for (var p = node.Parent; p != null; p = p.Parent)
        {
            var a = p.AnimOrNull;
            if (a == null) continue;
            if (p.Style.OverflowY == OverflowMode.Scroll) sumY += a.ScrollOffsetY;
            if (p.Style.OverflowX == OverflowMode.Scroll) sumX += a.ScrollOffsetX;
        }
        return new Vector2(sumX, sumY);
    }

    // ── Per-frame update ──────────────────────────────────────────────────────

    /// <summary>
    /// Update interaction state for the entire node tree.
    /// </summary>
    /// <param name="root">Root of the UI tree.</param>
    /// <param name="layout">Layout dict from LayoutEngine.Compute().</param>
    /// <param name="mousePos">Current mouse position in surface-local pixels.</param>
    /// <param name="mouseDown">True if the primary mouse button is held.</param>
    /// <param name="mouseClicked">True on the frame the primary mouse button was pressed.</param>
    /// <param name="rightMouseDown">True if the secondary (right) mouse button is held.</param>
    /// <param name="rightMouseClicked">True on the frame the secondary mouse button was pressed.</param>
    /// <param name="scrollDelta">Vertical mouse-wheel delta for this frame (positive = scroll
    /// up). Also accepted by a pure horizontal scroller (OverflowX.Scroll, OverflowY left at
    /// Clip) as a pan input — see <see cref="Style.OverflowX"/> — since most mice have no
    /// horizontal wheel.</param>
    /// <param name="scrollDeltaX">Horizontal mouse-wheel delta for this frame (positive =
    /// scroll left), e.g. from a trackpad gesture or Shift+wheel. <see cref="scrollDelta"/>'s
    /// counterpart.</param>
    /// <param name="dt">Frame delta time in seconds.</param>
    public static void Update(
        Node root,
        Dictionary<Node, LayoutBox> layout,
        Vector2 mousePos,
        bool mouseDown,
        bool mouseClicked,
        bool rightMouseDown = false,
        bool rightMouseClicked = false,
        float scrollDelta = 0f,
        float scrollDeltaX = 0f,
        float dt = 0f)
    {
        // Re-resolve capture against THIS frame's tree before anything else — the tree is
        // typically rebuilt every frame, so last frame's node object is already an orphan.
        // Capture is a left-button-only concept; right-click never acquires or is affected by it.
        var captured = ResolveCaptured(root);

        // Focus is keyed on Id for the same reason capture is — re-point it at this frame's
        // instance so routed key events reach a node that is actually in the tree.
        ResolveFocus(root);

        // A node holding capture owns the pointer for the duration of the drag. Suppress
        // pressed state on every other node so nothing else lights up mid-drag.
        bool hasCapture = captured != null;

        _focusClaimed = false;
        UpdateNode(root, layout, mousePos, mousePos, mouseDown && !hasCapture, mouseClicked,
            rightMouseDown, rightMouseClicked, scrollDelta, scrollDeltaX, dt, blockPointer: false);

        // Click-outside blurs. Without this a text field keeps eating keystrokes after the
        // user has visibly moved on, which reads as the whole window being stuck.
        if ((mouseClicked || rightMouseClicked) && !_focusClaimed) ClearFocus();

        // A click during the walk may have just acquired capture; pick that up so the new
        // drag gets its first OnDrag on the same frame it started.
        captured ??= CapturedNode;
        if (captured == null)
            return;

        if (!mouseDown)
        {
            ReleaseCapture();
            captured.Anim.IsPressed = false;
            captured.FireDragEnd();
            return;
        }

        // Capture is applied AFTER the walk so it wins over the hover-based pressed state.
        captured.Anim.IsPressed = true;
        if (layout.TryGetValue(captured, out var capturedBox))
        {
            var scroll = AccumulatedScrollOffset(captured);
            captured.FireDrag(mousePos.X + scroll.X - capturedBox.X, mousePos.Y + scroll.Y - capturedBox.Y);
        }
    }

    /// <param name="mousePos">
    /// Pointer position used for hit-testing. Descending into a scroll container adds that
    /// container's scroll offset, so this drifts from the real surface position as the walk
    /// goes deeper — which is exactly what hit-testing against unscrolled content boxes needs.
    /// </param>
    /// <param name="rawMousePos">
    /// The original surface-local pointer position, unadjusted, unchanged for the entire
    /// recursion. <see cref="Node.FireRightClick"/> reports this rather than
    /// <paramref name="mousePos"/>: a context menu built from that event is normally attached
    /// to the root as a <see cref="PositionMode.Absolute"/> node, so it needs the position in
    /// the root's coordinate space, not whatever scroll-adjusted space the clicked descendant
    /// happens to be hit-tested in.
    /// </param>
    private static void UpdateNode(
        Node node,
        Dictionary<Node, LayoutBox> layout,
        Vector2 mousePos,
        Vector2 rawMousePos,
        bool mouseDown,
        bool mouseClicked,
        bool rightMouseDown,
        bool rightMouseClicked,
        float scrollDelta,
        float scrollDeltaX,
        float dt,
        bool blockPointer)
    {
        // PointerEvents.None blocks this node AND all descendants
        bool effectivelyBlocked = blockPointer || node.Style.PointerEvents == PointerEvents.None;

        // Restore this node's remembered scroll offset(s) before anything reads them — the
        // node object is usually brand new this frame and starts at 0.
        bool isScroller  = node.Style.OverflowY == OverflowMode.Scroll && !string.IsNullOrEmpty(node.Id);
        bool isScrollerX = node.Style.OverflowX == OverflowMode.Scroll && !string.IsNullOrEmpty(node.Id);
        if (isScroller && ScrollState.TryGetValue(node.Id, out var savedScroll))
            node.Anim.ScrollOffsetY = savedScroll;
        if (isScrollerX && ScrollStateX.TryGetValue(node.Id, out var savedScrollX))
            node.Anim.ScrollOffsetX = savedScrollX;

        if (layout.TryGetValue(node, out var box))
        {
            bool isHovered = !effectivelyBlocked
                && mousePos.X >= box.X && mousePos.X <= box.Right
                && mousePos.Y >= box.Y && mousePos.Y <= box.Bottom;

            // Only touch (and thereby allocate) animation state for nodes that actually
            // have something to record. An inert node that isn't hovered and never was
            // has nothing to update: wasHovered would be false and both flags already are.
            var anim = node.AnimOrNull;
            if (isHovered || anim != null)
            {
                anim ??= node.Anim;

                bool wasHovered = anim.IsHovered;
                anim.IsHovered      = isHovered;
                anim.IsPressed      = isHovered && mouseDown;
                anim.IsRightPressed = isHovered && rightMouseDown;

                if (isHovered && !wasHovered) node.FireMouseEnter();
                if (!isHovered && wasHovered) node.FireMouseLeave();

                if (isHovered && mouseClicked)
                {
                    node.FireClick();
                    anim.RippleX      = mousePos.X - box.X;
                    anim.RippleY      = mousePos.Y - box.Y;
                    anim.RippleRadius = 0f;
                    anim.RippleAlpha  = 1f;

                    // Focus on click if focusable
                    if (node.IsFocusable)
                    {
                        SetFocus(node);
                        _focusClaimed = true;
                    }

                    // Acquire pointer capture. Deepest hit wins: the walk is depth-first, so a
                    // capturing descendant overwrites a capturing ancestor set earlier this frame.
                    // Left-button only — right-click has no capture/drag concept.
                    if (node.CapturesDrag)
                    {
                        CapturedNode   = node;
                        CapturedNodeId = string.IsNullOrEmpty(node.Id) ? null : node.Id;
                    }
                }

                if (isHovered && rightMouseClicked)
                {
                    // Reports rawMousePos (surface space), not mousePos (hit-test space) —
                    // see the parameter remarks above.
                    node.FireRightClick(rawMousePos.X, rawMousePos.Y);

                    // Same click-feedback ripple as the left button. Harmless on nodes that
                    // don't carry NodeEffect.Ripple: SkiaRenderer only draws it when a node's
                    // own Style opted in, so this write is inert dead state otherwise — exactly
                    // how the left-click path already behaves for non-Ripple nodes.
                    anim.RippleX      = mousePos.X - box.X;
                    anim.RippleY      = mousePos.Y - box.Y;
                    anim.RippleRadius = 0f;
                    anim.RippleAlpha  = 1f;

                    if (node.IsFocusable)
                    {
                        SetFocus(node);
                        _focusClaimed = true;
                    }
                }

                // Scroll wheel on scroll containers
                if (node.Style.OverflowY == OverflowMode.Scroll && isHovered && scrollDelta != 0f)
                {
                    float contentH = box.ContentHeight > 0 ? box.ContentHeight : anim.ScrollContentH;
                    float maxScroll = System.Math.Max(0f, contentH - box.Height);
                    anim.ScrollOffsetY = System.Math.Clamp(
                        anim.ScrollOffsetY - scrollDelta * 40f, 0f, maxScroll);
                    node.FireScroll();
                }

                // Horizontal scroll wheel on scroll containers. A pure horizontal scroller
                // (no vertical scrolling of its own) also accepts the plain vertical wheel
                // delta as a pan input — see Style.OverflowX's remarks for why: most mice
                // have no horizontal wheel, and without this fallback a horizontal list
                // would only respond to Shift+wheel or a trackpad gesture, which most users
                // won't discover.
                bool isPureHorizontalScroller = node.Style.OverflowX == OverflowMode.Scroll
                                              && node.Style.OverflowY != OverflowMode.Scroll;
                float effectiveDeltaX = scrollDeltaX != 0f
                    ? scrollDeltaX
                    : (isPureHorizontalScroller ? scrollDelta : 0f);

                if (node.Style.OverflowX == OverflowMode.Scroll && isHovered && effectiveDeltaX != 0f)
                {
                    float contentW = box.ContentWidth > 0 ? box.ContentWidth : anim.ScrollContentW;
                    float maxScrollX = System.Math.Max(0f, contentW - box.Width);
                    anim.ScrollOffsetX = System.Math.Clamp(
                        anim.ScrollOffsetX - effectiveDeltaX * 40f, 0f, maxScrollX);
                    node.FireScroll();
                }
            }

            // Re-clamp against this frame's content size, then remember it. Clamping here
            // as well as on wheel input keeps the offset valid when the content shrinks
            // (e.g. the user selects a simulator with fewer chains).
            if (isScroller)
            {
                var scrollAnim  = node.Anim;
                float contentH  = box.ContentHeight > 0 ? box.ContentHeight : scrollAnim.ScrollContentH;
                float maxScroll = System.Math.Max(0f, contentH - box.Height);
                scrollAnim.ScrollOffsetY = System.Math.Clamp(scrollAnim.ScrollOffsetY, 0f, maxScroll);
                ScrollState[node.Id] = scrollAnim.ScrollOffsetY;
            }
            if (isScrollerX)
            {
                var scrollAnim   = node.Anim;
                float contentW   = box.ContentWidth > 0 ? box.ContentWidth : scrollAnim.ScrollContentW;
                float maxScrollX = System.Math.Max(0f, contentW - box.Width);
                scrollAnim.ScrollOffsetX = System.Math.Clamp(scrollAnim.ScrollOffsetX, 0f, maxScrollX);
                ScrollStateX[node.Id] = scrollAnim.ScrollOffsetX;
            }
        }

        node.AnimOrNull?.Update(dt);

        // ── Recurse to children ───────────────────────────────────────────────
        bool scrollsY = node.Style.OverflowY == OverflowMode.Scroll;
        bool scrollsX = node.Style.OverflowX == OverflowMode.Scroll;
        if ((scrollsY || scrollsX) && !effectivelyBlocked
            && layout.TryGetValue(node, out var scrollBox))
        {
            // Only process children whose visual position is inside the scroll viewport
            bool mouseInViewport = mousePos.X >= scrollBox.X && mousePos.X <= scrollBox.Right
                                && mousePos.Y >= scrollBox.Y && mousePos.Y <= scrollBox.Bottom;

            // Offset mouse to account for scroll so children receive correct relative
            // position. A node can scroll either axis independently (or, in principle,
            // both), so only the axis actually in Scroll mode contributes an offset.
            var scrollAnimForRecurse = node.AnimOrNull;
            float offX = scrollsX ? (scrollAnimForRecurse?.ScrollOffsetX ?? 0f) : 0f;
            float offY = scrollsY ? (scrollAnimForRecurse?.ScrollOffsetY ?? 0f) : 0f;
            var adjustedMouse = mouseInViewport
                ? new Vector2(mousePos.X + offX, mousePos.Y + offY)
                : new Vector2(-99999f, -99999f);

            // Indexed loops throughout the per-frame walkers: `foreach` over the
            // IReadOnlyList<Node> facade boxes a List<T>.Enumerator on every node.
            var scrollKids = node.Children;
            for (int i = 0; i < scrollKids.Count; i++)
                UpdateNode(scrollKids[i], layout, adjustedMouse, rawMousePos, mouseDown, mouseClicked,
                    rightMouseDown, rightMouseClicked, scrollDelta, scrollDeltaX, dt, effectivelyBlocked);
        }
        else
        {
            var kids = node.Children;
            for (int i = 0; i < kids.Count; i++)
                UpdateNode(kids[i], layout, mousePos, rawMousePos, mouseDown, mouseClicked,
                    rightMouseDown, rightMouseClicked, scrollDelta, scrollDeltaX, dt, effectivelyBlocked);
        }
    }
}
