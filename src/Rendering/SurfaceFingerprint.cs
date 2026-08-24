using PanacheUI.Core;

namespace PanacheUI.Rendering;

/// <summary>
/// Reduces a laid-out node tree to a single 64-bit value that changes whenever the
/// tree would paint different pixels.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> Every consumer of PanacheUI rebuilds its whole node tree each
/// frame — that is the intended authoring style — so <see cref="Node.IsDirty"/> is
/// always true and the dirty flag never prevented a single repaint. A surface was
/// therefore re-rasterising, reading back, and re-uploading a full RGBA texture on
/// every frame even when the window had not changed at all. Comparing fingerprints
/// gives the framework a redraw signal that survives the rebuild-every-frame pattern,
/// because it is computed from what the tree <i>looks like</i>, not from object identity.</para>
///
/// <para><b>What is covered.</b> Each node contributes its computed layout box (which
/// subsumes every geometric style property), its visual style properties (see
/// <c>Style.AppendVisualHash</c>), its text, and the animation state the renderer reads
/// for effect-free nodes — the scroll offset and any in-flight one-shot flash. Tree
/// shape is folded in as well, so adding or removing a node always changes the result.</para>
///
/// <para><b>What forces a redraw regardless.</b> A node carrying any
/// <see cref="NodeEffect"/> is time-driven — noise scrolls, glows pulse, text types
/// itself — and its appearance changes with the <c>time</c> argument rather than with
/// any tree state. <see cref="Compute"/> reports that via <c>animated</c>, and the
/// surface then redraws unconditionally. Effects are rare in application windows and
/// common in the demo/lab windows, which is exactly the right split.</para>
///
/// <para><b>Known limit.</b> An <see cref="Style.ImageBitmap"/> is fingerprinted by
/// reference, so mutating a bitmap's pixels in place without swapping the object is
/// invisible here. Call <see cref="PanacheSurface.Invalidate"/> after such an edit, or
/// set <see cref="PanacheSurface.AlwaysRedraw"/> on that surface.</para>
/// </remarks>
internal static class SurfaceFingerprint
{
    /// <summary>
    /// Fingerprint the tree that <paramref name="layoutStamp"/>'s pass just placed.
    /// <paramref name="animated"/> is set when any node paints a time-driven effect,
    /// meaning the fingerprint alone cannot decide whether to redraw.
    /// </summary>
    public static ulong Compute(Node root, ulong layoutStamp, out bool animated)
    {
        bool anim = false;
        ulong h = Walk(root, layoutStamp, Hash.Seed, ref anim);
        animated = anim;
        return h;
    }

    private static ulong Walk(Node node, ulong stamp, ulong h, ref bool animated)
    {
        if (node.LayoutStamp == stamp)
        {
            var box = node.CachedBox;
            h = Hash.F32(h, box.X);
            h = Hash.F32(h, box.Y);
            h = Hash.F32(h, box.Width);
            h = Hash.F32(h, box.Height);
            h = Hash.F32(h, box.ContentHeight);
            h = Hash.F32(h, box.ContentWidth);
        }
        else
        {
            // Unplaced nodes are skipped by the renderer; record the absence so a node
            // dropping out of layout still moves the fingerprint.
            h = Hash.U64(h, 0xDEAD_BEEFUL);
        }

        var s = node.Style;
        h = s.AppendVisualHash(h);
        h = Hash.Str(h, node.NodeValue);

        if (s.HasEffects) animated = true;

        var a = node.AnimOrNull;
        if (a != null)
        {
            // Scroll position moves content without any style or layout change.
            h = Hash.F32(h, a.ScrollOffsetY);
            h = Hash.F32(h, a.ScrollOffsetX);

            // Hover cross-fade. Only for nodes that actually declare hover colors — every
            // other node's HoverT is state the renderer never reads, and hashing it would
            // repaint the whole surface every time the cursor crossed inert decoration.
            // No `animated` flag is needed: HoverT moving IS a fingerprint change, so the
            // fade repaints at the full frame rate and then stops dead once it settles.
            if (s.HasHoverStyle) h = Hash.F32(h, a.HoverT);

            // A one-shot flash animates on its own clock until it expires.
            if (a.FlashEffect != NodeEffect.None && a.FlashT > 0f) animated = true;

            // Ripple, hover-lift and press-depress only paint when the node also carries
            // the matching effect, which already set `animated` above.
        }

        var children = node.Children;
        int count = children.Count;
        h = Hash.I32(h, count);
        for (int i = 0; i < count; i++)
            h = Walk(children[i], stamp, h, ref animated);

        return h;
    }
}
