using System;
using PanacheUI.Core;

namespace PanacheUI.Components;

public static partial class PUI
{
    /// <summary>
    /// A close ("X") button built from the bundled icon <c>#0005</c> rather than a raw
    /// ImGui-drawn glyph — the house standard going forward for every window's close
    /// button, so the visual lives inside the Panache surface like everything else instead
    /// of being the one label ImGui itself renders on top.
    /// </summary>
    /// <remarks>
    /// Returned as a plain flow-positioned node with no <see cref="Style.Position"/> set —
    /// append it as a header row's trailing child and normal flow layout reserves room for
    /// it automatically (no overlap with whatever else is in that row, unlike a
    /// hand-positioned absolute overlay computed independently of the row's real content).
    /// A window whose header has nowhere natural to flow it into can still call
    /// <c>.WithStyle(s => { s.Position = PositionMode.Absolute; s.Left = ...; s.Top = ...; })</c>
    /// on the result.
    /// </remarks>
    /// <param name="id">Stable Id — needed for hit-testing lookups (e.g. excluding this
    /// button's box from a header's drag region) exactly like any other interactive node.</param>
    /// <param name="size">Width and height of the button's hit area in pixels.</param>
    /// <param name="accent">Tint for the X glyph.</param>
    /// <param name="onClick">Invoked on click. Typically just <c>() => IsVisible = false</c>.</param>
    public static Node CloseButton(string id, float size, PColor accent, Action onClick)
    {
        var btn = new Node().WithId(id).WithStyle(s =>
        {
            s.WidthMode       = SizeMode.Fixed; s.Width  = size;
            s.HeightMode      = SizeMode.Fixed; s.Height = size;
            s.BackgroundColor = PColor.Black.WithOpacity(0.30f);
            s.BorderRadius    = size * 0.22f;
            s.Flow            = Flow.Horizontal;
            s.Padding         = new EdgeSize(size * 0.24f);
        });
        btn.IsInteractive = true;
        btn.OnClick += _ => onClick();
        btn.AppendChild(Icon(5, size * 0.52f, tint: accent));
        return btn;
    }

    /// <summary>
    /// A draggable horizontal scrollbar for an <see cref="Style.OverflowX"/>.Scroll node
    /// elsewhere in the tree. Not wired automatically — plenty of horizontal scrollers
    /// (a marquee strip, a tab bar) don't want a visible bar — so the caller builds one
    /// explicitly, pointing it at the scroller's own Id and its current metrics.
    /// </summary>
    /// <remarks>
    /// <para>Dragging the thumb writes through
    /// <see cref="InteractionManager.SetScrollOffsetX"/> rather than mutating the
    /// scroller's <c>Anim</c> directly — the thumb and the scroller are different nodes,
    /// both rebuilt fresh every frame, so there is no live <c>Anim</c> reference in common
    /// for the thumb to reach into. See that method's remarks for the one-frame-lag this
    /// implies (imperceptible in practice).</para>
    ///
    /// <para>The whole track is draggable, not just the thumb — <c>Node.CapturesDrag</c>
    /// fires <c>OnDrag</c> starting on the very frame capture is acquired, so a single
    /// click anywhere on the bar already jumps the thumb under the cursor and starts
    /// dragging from there. That is the same "click track to jump" convention every OS
    /// scrollbar already uses, and it means a user is never forced to pixel-hunt a thumb
    /// that may be much narrower than the track.</para>
    ///
    /// <para><b>The interactive hit area is taller than the visual bar</b> — deliberately.
    /// <c>track</c>/<c>thumb</c> carry <see cref="PointerEvents.None"/>, so only the
    /// <c>outer</c> node is ever actually hit-tested; its height is
    /// <paramref name="hitHeight"/>, not <paramref name="barHeight"/>. Making those the
    /// same value was the original bug here: a thin visual bar sitting flush at the top of
    /// a taller row (this framework's layout doesn't vertically centre flow children) left
    /// most of what looked clickable outside the actual hit box, so a drag started a few
    /// pixels off the hairline did nothing. <paramref name="hitHeight"/> should match
    /// whatever row/strip this bar sits inside so the entire visible band is grabbable, not
    /// just the thin line drawn in the middle of it.</para>
    /// </remarks>
    /// <param name="scrollerId">Id of the <see cref="Style.OverflowX"/>.Scroll node this
    /// bar drives.</param>
    /// <param name="offsetX">The scroller's current offset — typically
    /// <see cref="InteractionManager.GetScrollOffsetX"/>(<paramref name="scrollerId"/>).</param>
    /// <param name="contentWidth">The scroller's last-computed
    /// <see cref="Layout.LayoutBox.ContentWidth"/>.</param>
    /// <param name="viewportWidth">The scroller's own (visible) width.</param>
    /// <param name="accent">Thumb tint.</param>
    /// <param name="width">Total bar width in pixels.</param>
    /// <param name="hitHeight">Height of the actual draggable hit area — make this match
    /// the surrounding row so there's no dead space around the visual bar.</param>
    /// <param name="barHeight">Height of the visual track/thumb line, centred within
    /// <paramref name="hitHeight"/>. Kept thinner than the hit area on purpose — a fat bar
    /// is not needed once the hit area is already comfortably sized.</param>
    public static Node ScrollbarX(
        string scrollerId, float offsetX, float contentWidth, float viewportWidth,
        PColor accent, float width, float hitHeight = 20f, float barHeight = 6f)
    {
        float maxScroll  = Math.Max(0.01f, contentWidth - viewportWidth);
        float thumbFrac  = viewportWidth / Math.Max(viewportWidth, Math.Max(contentWidth, 0.01f));
        float thumbW     = Math.Max(24f, width * thumbFrac);
        float travel     = Math.Max(1f, width - thumbW);
        float frac       = maxScroll > 0f ? Math.Clamp(offsetX / maxScroll, 0f, 1f) : 0f;
        float thumbX     = frac * travel;
        float barY       = (hitHeight - barHeight) / 2f;

        var track = new Node().WithStyle(s =>
        {
            s.Position        = PositionMode.Absolute;
            s.Left = 0; s.Top = barY;
            s.WidthMode       = SizeMode.Fixed; s.Width  = width;
            s.HeightMode      = SizeMode.Fixed; s.Height = barHeight;
            s.BackgroundColor = PColor.White.WithOpacity(0.08f);
            s.BorderRadius    = barHeight * 0.5f;
            s.PointerEvents   = PointerEvents.None;
        });

        var thumb = new Node().WithStyle(s =>
        {
            s.Position        = PositionMode.Absolute;
            s.Left = thumbX; s.Top = barY;
            s.WidthMode       = SizeMode.Fixed; s.Width  = thumbW;
            s.HeightMode      = SizeMode.Fixed; s.Height = barHeight;
            s.BackgroundColor = accent.WithOpacity(0.70f);
            s.BorderRadius    = barHeight * 0.5f;
            s.PointerEvents   = PointerEvents.None;
        });

        var outer = new Node().WithId($"scrollbar-{scrollerId}").WithStyle(s =>
        {
            s.WidthMode       = SizeMode.Fixed; s.Width  = width;
            s.HeightMode      = SizeMode.Fixed; s.Height = hitHeight;
            s.Flow            = Flow.Horizontal;
            // Faint full-height wash so the real (larger-than-the-hairline) hit area is
            // visible, not just guessable — this is as much a fix for "I can't tell where
            // to click" as the hit-box enlargement is a fix for "clicking there did nothing".
            s.BackgroundColor = PColor.White.WithOpacity(0.025f);
        });
        outer.IsInteractive = true;
        outer.CapturesDrag  = true;
        outer.AppendChild(track);
        outer.AppendChild(thumb);

        outer.OnDrag += (_, localX, _) =>
        {
            float t = maxScroll > 0f ? Math.Clamp((localX - thumbW / 2f) / travel, 0f, 1f) : 0f;
            InteractionManager.SetScrollOffsetX(scrollerId, t * maxScroll);
        };

        return outer;
    }

    /// <summary>
    /// A node displaying one of PanacheUI's bundled icons (see
    /// <see cref="Icons.PanacheIcons"/>) by numeric ID.
    /// </summary>
    /// <remarks>
    /// <para>Falls back to a plain placeholder swatch — <see cref="Theme.Panel2"/> fill,
    /// <see cref="Theme.Panel"/> border — when the id can't be loaded, so a missing or
    /// mistyped icon degrades to an empty box instead of leaving a hole or throwing.
    /// Mirrors the fallback GlamourDresserHelper already uses for its own
    /// <c>GameIconCache</c>-backed icon nodes.</para>
    ///
    /// <para><b>Icons are inert by default</b> — <see cref="PointerEvents.None"/> — and carry
    /// no <see cref="Node.Id"/>. Both were footguns the other way round: an icon inside a
    /// button silently stole the button's hover and killed its cue, and every icon sharing
    /// a generated <c>pui-icon-{id}</c> Id meant twenty rows showing the same glyph were
    /// twenty nodes claiming one identity. Pass <c>interactive: true</c> / <c>nodeId:</c> for
    /// the rare icon that wants either.</para>
    /// </remarks>
    /// <param name="id">Icon ID — see the PanacheUI Icons set (<c>#0001</c> onward, run
    /// <c>/panacheui icons</c> for the current list). IDs only; never by name.</param>
    /// <param name="size">Width and height in pixels — every bundled icon is square.</param>
    /// <param name="tint">Optional recolor, multiplied over the icon's pixels. The bundled
    /// icons are opaque white glyphs on transparent backgrounds specifically so this works
    /// cleanly — see <see cref="Style.ImageTint"/>.</param>
    /// <param name="radius">Corner radius, applied to both the icon and the placeholder.</param>
    /// <param name="interactive">Opt the icon back into hit-testing. Leave false (the
    /// default) for decoration, which is what an icon almost always is — see the remarks on
    /// pointer events above.</param>
    /// <param name="nodeId">Optional stable <see cref="Node.Id"/>. Icons carry no Id by
    /// default: twenty rows each showing icon #36 would otherwise be twenty nodes sharing
    /// one Id, which is harmless only for as long as Ids key nothing but scroll offsets.
    /// Supply one when this particular icon needs to be found in the layout map.</param>
    public static Node Icon(int id, float size, PColor? tint = null, float radius = 0f,
        bool interactive = false, string? nodeId = null)
    {
        var bmp = Icons.PanacheIcons.Get(id);
        var node = new Node();
        if (!string.IsNullOrEmpty(nodeId)) node.WithId(nodeId!);
        return node.WithStyle(s =>
        {
            s.WidthMode    = SizeMode.Fixed; s.Width  = size;
            s.HeightMode   = SizeMode.Fixed; s.Height = size;
            s.BorderRadius = radius;

            // Decorative by default. An icon placed inside a button is a child that sits on
            // top of it, so with Auto pointer events it swallows the hover the button needed
            // to draw its own cue — the button goes dead exactly where the user aims. A
            // decorative glyph has no use for clicks, so the safe default is the useful one.
            if (!interactive) s.PointerEvents = PointerEvents.None;

            if (bmp != null)
            {
                s.ImageBitmap = bmp;
                if (tint.HasValue) s.ImageTint = tint.Value;
            }
            else
            {
                s.BackgroundColor = Theme.Panel2;
                s.BorderColor     = Theme.Panel;
                s.BorderWidth     = 1f;
            }
        });
    }
}
