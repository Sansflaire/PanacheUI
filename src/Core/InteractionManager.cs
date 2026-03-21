using System.Collections.Generic;
using System.Numerics;
using PanacheUI.Layout;

namespace PanacheUI.Core;

/// <summary>
/// Updates NodeAnimState for all interactive nodes based on mouse position.
/// Call Update() each frame after layout is computed, before rendering.
/// </summary>
public static class InteractionManager
{
    public static void Update(
        Node root,
        Dictionary<Node, LayoutBox> layout,
        Vector2 mousePos,
        bool mouseDown,
        bool mouseClicked,
        float dt)
    {
        UpdateNode(root, layout, mousePos, mouseDown, mouseClicked, dt);
    }

    private static void UpdateNode(
        Node node,
        Dictionary<Node, LayoutBox> layout,
        Vector2 mousePos,
        bool mouseDown,
        bool mouseClicked,
        float dt)
    {
        if (layout.TryGetValue(node, out var box))
        {
            bool wasHovered = node.Anim.IsHovered;
            bool isHovered = mousePos.X >= box.X && mousePos.X <= box.Right
                          && mousePos.Y >= box.Y && mousePos.Y <= box.Bottom;

            node.Anim.IsHovered = isHovered;
            node.Anim.IsPressed = isHovered && mouseDown;

            if (isHovered && !wasHovered) node.FireMouseEnter();
            if (!isHovered && wasHovered) node.FireMouseLeave();

            if (isHovered && mouseClicked)
            {
                node.FireClick();
                // Start ripple at click position relative to node
                node.Anim.RippleX     = mousePos.X - box.X;
                node.Anim.RippleY     = mousePos.Y - box.Y;
                node.Anim.RippleRadius = 0f;
                node.Anim.RippleAlpha = 1f;
            }
        }

        node.Anim.Update(dt);

        foreach (var child in node.Children)
            UpdateNode(child, layout, mousePos, mouseDown, mouseClicked, dt);
    }
}
