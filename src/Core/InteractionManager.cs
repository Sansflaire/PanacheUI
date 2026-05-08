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

    /// <summary>Programmatically focus a node. The node must have IsFocusable = true.</summary>
    public static void SetFocus(Node? node)
    {
        FocusedNode = node?.IsFocusable == true ? node : null;
    }

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

    // ── Per-frame update ──────────────────────────────────────────────────────

    /// <summary>
    /// Update interaction state for the entire node tree.
    /// </summary>
    /// <param name="root">Root of the UI tree.</param>
    /// <param name="layout">Layout dict from LayoutEngine.Compute().</param>
    /// <param name="mousePos">Current mouse position in surface-local pixels.</param>
    /// <param name="mouseDown">True if the primary mouse button is held.</param>
    /// <param name="mouseClicked">True on the frame the primary mouse button was pressed.</param>
    /// <param name="scrollDelta">Mouse-wheel delta for this frame (positive = scroll up).</param>
    /// <param name="dt">Frame delta time in seconds.</param>
    public static void Update(
        Node root,
        Dictionary<Node, LayoutBox> layout,
        Vector2 mousePos,
        bool mouseDown,
        bool mouseClicked,
        float scrollDelta = 0f,
        float dt = 0f)
    {
        UpdateNode(root, layout, mousePos, mouseDown, mouseClicked, scrollDelta, dt, blockPointer: false);
    }

    private static void UpdateNode(
        Node node,
        Dictionary<Node, LayoutBox> layout,
        Vector2 mousePos,
        bool mouseDown,
        bool mouseClicked,
        float scrollDelta,
        float dt,
        bool blockPointer)
    {
        // PointerEvents.None blocks this node AND all descendants
        bool effectivelyBlocked = blockPointer || node.Style.PointerEvents == PointerEvents.None;

        if (layout.TryGetValue(node, out var box))
        {
            bool isHovered = !effectivelyBlocked
                && mousePos.X >= box.X && mousePos.X <= box.Right
                && mousePos.Y >= box.Y && mousePos.Y <= box.Bottom;

            bool wasHovered = node.Anim.IsHovered;
            node.Anim.IsHovered = isHovered;
            node.Anim.IsPressed = isHovered && mouseDown;

            if (isHovered && !wasHovered) node.FireMouseEnter();
            if (!isHovered && wasHovered) node.FireMouseLeave();

            if (isHovered && mouseClicked)
            {
                node.FireClick();
                node.Anim.RippleX      = mousePos.X - box.X;
                node.Anim.RippleY      = mousePos.Y - box.Y;
                node.Anim.RippleRadius = 0f;
                node.Anim.RippleAlpha  = 1f;

                // Focus on click if focusable
                if (node.IsFocusable)
                    FocusedNode = node;
            }

            // Scroll wheel on scroll containers
            if (node.Style.OverflowY == OverflowMode.Scroll && isHovered && scrollDelta != 0f)
            {
                float contentH = box.ContentHeight > 0 ? box.ContentHeight : node.Anim.ScrollContentH;
                float maxScroll = System.Math.Max(0f, contentH - box.Height);
                node.Anim.ScrollOffsetY = System.Math.Clamp(
                    node.Anim.ScrollOffsetY - scrollDelta * 40f, 0f, maxScroll);
                node.FireScroll();
            }
        }

        node.Anim.Update(dt);

        // ── Recurse to children ───────────────────────────────────────────────
        if (node.Style.OverflowY == OverflowMode.Scroll && !effectivelyBlocked
            && layout.TryGetValue(node, out var scrollBox))
        {
            // Only process children whose visual position is inside the scroll viewport
            bool mouseInViewport = mousePos.X >= scrollBox.X && mousePos.X <= scrollBox.Right
                                && mousePos.Y >= scrollBox.Y && mousePos.Y <= scrollBox.Bottom;

            // Offset mouse to account for scroll so children receive correct relative position
            var adjustedMouse = mouseInViewport
                ? new Vector2(mousePos.X, mousePos.Y + node.Anim.ScrollOffsetY)
                : new Vector2(-99999f, -99999f);

            foreach (var child in node.Children)
                UpdateNode(child, layout, adjustedMouse, mouseDown, mouseClicked, scrollDelta, dt, effectivelyBlocked);
        }
        else
        {
            foreach (var child in node.Children)
                UpdateNode(child, layout, mousePos, mouseDown, mouseClicked, scrollDelta, dt, effectivelyBlocked);
        }
    }
}
