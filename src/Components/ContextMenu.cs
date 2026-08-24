using System;
using System.Collections.Generic;
using PanacheUI.Core;

namespace PanacheUI.Components;

/// <summary>One selectable row in a <see cref="PUI.ContextMenu"/>, or a divider line.</summary>
public readonly struct ContextMenuItem
{
    public readonly string  Label;
    public readonly Action? OnSelect;
    public readonly bool    Enabled;
    public readonly bool    IsSeparator;

    public ContextMenuItem(string label, Action onSelect, bool enabled = true)
    {
        Label       = label;
        OnSelect    = onSelect;
        Enabled     = enabled;
        IsSeparator = false;
    }

    private ContextMenuItem(bool separator)
    {
        Label = string.Empty; OnSelect = null; Enabled = false; IsSeparator = separator;
    }

    /// <summary>A thin divider line between groups of items.</summary>
    public static ContextMenuItem Separator => new(true);
}

/// <summary>
/// Persistent state for one context menu. A consumer owns exactly one of these per menu
/// (or one shared instance for a whole list, using <see cref="Tag"/> to record which row it
/// is currently open for) and passes it to <see cref="PUI.ContextMenu"/> every frame.
/// </summary>
/// <remarks>
/// Node trees are rebuilt from scratch every frame throughout PanacheUI, so nothing about
/// "is the menu open" can live on a Node — it would vanish the instant the tree is rebuilt.
/// This mirrors how other transient interaction state (scroll offsets, drag capture) is
/// already owned outside the tree, in <see cref="InteractionManager"/> or the consumer.
/// </remarks>
public sealed class ContextMenuState
{
    /// <summary>True while the menu should be built and shown.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Surface-local pixel position the menu opens at.</summary>
    public float X { get; private set; }

    /// <summary>Surface-local pixel position the menu opens at.</summary>
    public float Y { get; private set; }

    /// <summary>
    /// Free-form payload identifying what the menu was opened for — a row id, an index, a
    /// reference to a data item. PanacheUI never reads this; it exists so one shared
    /// <see cref="ContextMenuState"/> serving many rows can tell which row the currently-open
    /// menu belongs to when building that row's item list.
    /// </summary>
    public object? Tag { get; private set; }

    /// <summary>Open the menu at a surface-local position, optionally tagging what it's for.</summary>
    public void Open(float x, float y, object? tag = null)
    {
        IsOpen = true;
        X = x; Y = y;
        Tag = tag;
    }

    public void Close() => IsOpen = false;

    // ── Hover highlight ──────────────────────────────────────────────────────
    //
    // One-frame-lag hover tracking, the same idiom already used elsewhere in this codebase
    // (e.g. GlamourDresserHelper's MainWindow _hoverId/_hoverNext) for exactly the same
    // reason: a node's Style is fixed at BuildTree time, which runs BEFORE
    // InteractionManager.Update computes this frame's hover state, so a freshly built node
    // cannot react to its own hover within the same frame — only to last frame's.
    //
    // Deliberately NOT using NodeEffect.HoverLift for this instead: any node carrying a
    // NodeEffect marks the whole surface "animated" in SurfaceFingerprint, which forces a
    // continuous repaint for as long as that node exists on screen (throttled to
    // PanacheSurface.AnimationFpsCap, but still ongoing cost). A context menu that just sits
    // open costs nothing until you move the mouse or click; effects would make merely having
    // it open cost 30 repaints a second for no visual benefit worth paying for.

    private int _hoveredIndex = -1;

    internal bool IsItemHovered(int index) => _hoveredIndex == index;
    internal void SetHovered(int index) => _hoveredIndex = index;
    internal void ClearHovered(int index) { if (_hoveredIndex == index) _hoveredIndex = -1; }
}

public static partial class PUI
{
    /// <summary>
    /// A floating context menu: an invisible full-surface scrim (click outside to dismiss)
    /// plus a positioned card of selectable rows.
    /// </summary>
    /// <remarks>
    /// <para><b>Must be attached directly to the root node</b> (or another
    /// <see cref="PositionMode.Absolute"/>, full-<see cref="SizeMode.Fill"/> node with no
    /// padding). The scrim uses Fill sizing, which only spans the true surface when its
    /// parent's content box already is the whole surface; the card uses
    /// <see cref="PositionMode.Absolute"/>, which places it relative to that same parent's
    /// content origin. Nesting this inside a card, section, or scroll container will not
    /// cover the full surface and will misplace the card.</para>
    ///
    /// <para><b>Safe to call unconditionally.</b> Returns an inert zero-size node when
    /// <paramref name="state"/>.IsOpen is false or <paramref name="items"/> is empty, so
    /// callers can do <c>root.AppendChild(PUI.ContextMenu(...))</c> every frame without an
    /// extra branch — exactly like every other PUI builder.</para>
    ///
    /// <para>Typical wiring — open it from a row's right-click, in the same BuildTree that
    /// appends it:</para>
    /// <code>
    /// row.OnRightClick += (_, x, y) => _menu.Open(x, y, tag: rowId);
    /// ...
    /// root.AppendChild(PUI.ContextMenu(_menu, accent, new[]
    /// {
    ///     new ContextMenuItem("Rename", () => Rename(rowId)),
    ///     new ContextMenuItem("Delete", () => Delete(rowId), enabled: CanDelete(rowId)),
    ///     ContextMenuItem.Separator,
    ///     new ContextMenuItem("Copy ID", () => CopyId(rowId)),
    /// }, surfaceWidth, surfaceHeight));
    /// </code>
    /// </remarks>
    /// <param name="state">This menu's persistent open/position/hover state.</param>
    /// <param name="accent">Border, hover-highlight, and shadow tint.</param>
    /// <param name="items">Rows top to bottom. Rebuilt fresh every call — cheap to
    /// recompute conditionally (e.g. based on <see cref="ContextMenuState.Tag"/>).</param>
    /// <param name="surfaceWidth">Full surface width, for keeping the card on-screen.</param>
    /// <param name="surfaceHeight">Full surface height, for keeping the card on-screen.</param>
    /// <param name="onDismiss">Called when the scrim is clicked (menu closed without a
    /// selection). Not called when an item is selected — <paramref name="items"/>' own
    /// <c>OnSelect</c> already ran in that case.</param>
    /// <param name="itemWidth">Card width in pixels. Rows fill it; text ellipsises past it.</param>
    public static Node ContextMenu(
        ContextMenuState state,
        PColor accent,
        IReadOnlyList<ContextMenuItem> items,
        float surfaceWidth,
        float surfaceHeight,
        Action? onDismiss = null,
        float itemWidth = 170f)
    {
        if (!state.IsOpen || items.Count == 0)
            return InertNode();

        const float RowH = 24f;
        const float SepH = 7f;
        const float PadV = 4f;

        float estH = PadV * 2f;
        for (int i = 0; i < items.Count; i++)
            estH += items[i].IsSeparator ? SepH : RowH;

        // Keep the card fully on-screen — an item list opened near an edge must not spill
        // past the surface bounds it has no way to scroll or clip itself against.
        float x = Math.Clamp(state.X, 0f, Math.Max(0f, surfaceWidth  - itemWidth - 2f));
        float y = Math.Clamp(state.Y, 0f, Math.Max(0f, surfaceHeight - estH      - 2f));

        // Wrapper is itself Position.Absolute + Fill so it never participates in the root's
        // own flow layout (a Fill-height flow child would otherwise consume all remaining
        // vertical space in the root, however briefly, distorting whatever else is there).
        var wrapper = new Node().WithId("ctxmenu").WithStyle(s =>
        {
            s.Position   = PositionMode.Absolute;
            s.Left = 0; s.Top = 0;
            s.WidthMode  = SizeMode.Fill;
            s.HeightMode = SizeMode.Fill;
        });

        var scrim = new Node().WithId("ctxmenu_scrim").WithStyle(s =>
        {
            s.Position   = PositionMode.Absolute;
            s.Left = 0; s.Top = 0;
            s.WidthMode  = SizeMode.Fill;
            s.HeightMode = SizeMode.Fill;
        });
        scrim.IsInteractive = true;
        scrim.OnClick += _ => { state.Close(); onDismiss?.Invoke(); };

        var card = new Node().WithId("ctxmenu_card").WithStyle(s =>
        {
            s.Position        = PositionMode.Absolute;
            s.Left = x; s.Top = y;
            s.WidthMode        = SizeMode.Fixed; s.Width = itemWidth;
            s.HeightMode       = SizeMode.Fit;
            s.Flow             = Flow.Vertical;
            s.BackgroundColor  = Theme.Panel2;
            s.BorderRadius     = 6f;
            s.BorderColor      = accent.WithOpacity(0.45f);
            s.BorderWidth      = 1f;
            s.ShadowColor      = PColor.Black.WithOpacity(0.55f);
            s.ShadowBlur       = 10f;
            s.Padding          = new EdgeSize(PadV, 0f);
        });

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.IsSeparator)
            {
                card.AppendChild(new Node().WithStyle(s =>
                {
                    s.WidthMode       = SizeMode.Fill;
                    s.HeightMode      = SizeMode.Fixed; s.Height = 1f;
                    s.Margin          = new EdgeSize(3f, 8f);
                    s.BackgroundColor = accent.WithOpacity(0.18f);
                }));
                continue;
            }

            int index = i;   // captured by value for the closures below
            bool hovered = item.Enabled && state.IsItemHovered(index);

            var row = new Node().WithId($"ctxmenu_item_{index}").WithText(item.Label).WithStyle(s =>
            {
                s.WidthMode       = SizeMode.Fill;
                s.HeightMode      = SizeMode.Fixed; s.Height = RowH;
                s.Padding         = new EdgeSize(0f, 10f);
                s.FontSize        = 11.5f;
                s.Flow            = Flow.Horizontal;
                s.TextOverflow    = TextOverflow.Ellipsis;
                s.Color           = item.Enabled
                    ? PColor.White.WithOpacity(0.92f)
                    : PColor.White.WithOpacity(0.32f);
                s.BackgroundColor = hovered ? accent.WithOpacity(0.16f) : PColor.Transparent;
                s.BorderRadius    = 4f;
            });

            if (item.Enabled)
            {
                row.IsInteractive = true;
                row.OnMouseEnter += _ => state.SetHovered(index);
                row.OnMouseLeave += _ => state.ClearHovered(index);
                row.OnClick += _ =>
                {
                    item.OnSelect?.Invoke();
                    state.Close();
                };
            }

            card.AppendChild(row);
        }

        wrapper.AppendChild(scrim);
        wrapper.AppendChild(card);
        return wrapper;
    }

    private static Node InertNode() =>
        new Node().WithStyle(s =>
        {
            s.WidthMode  = SizeMode.Fixed; s.Width  = 0f;
            s.HeightMode = SizeMode.Fixed; s.Height = 0f;
        });
}
