using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SkiaSharp;

namespace PanacheUI.Core;

/// <summary>
/// Visual and layout properties for a Node. All properties are optional —
/// unset values use sensible defaults during layout and rendering.
/// </summary>
public class Style
{
    // ── Layout ──────────────────────────────────────────────────────────────

    /// <summary>How children are stacked. Default: Vertical.</summary>
    public Flow Flow { get; set; } = Flow.Vertical;

    /// <summary>How this node determines its width. Default: Fill parent.</summary>
    public SizeMode WidthMode { get; set; } = SizeMode.Fill;

    /// <summary>How this node determines its height. Default: Fit content.</summary>
    public SizeMode HeightMode { get; set; } = SizeMode.Fit;

    /// <summary>Explicit pixel width — used when WidthMode == Fixed.</summary>
    public float Width { get; set; }

    /// <summary>Explicit pixel height — used when HeightMode == Fixed.</summary>
    public float Height { get; set; }

    /// <summary>Interior spacing between border and children.</summary>
    public EdgeSize Padding { get; set; } = EdgeSize.Zero;

    /// <summary>Exterior spacing outside this node's border.</summary>
    public EdgeSize Margin { get; set; } = EdgeSize.Zero;

    /// <summary>Pixel gap inserted between children.</summary>
    public float Gap { get; set; }

    /// <summary>
    /// Cross-axis alignment of this node's flow children. Default <see cref="Core.AlignItems.Start"/>,
    /// which is the behaviour this engine had before the property existed.
    /// </summary>
    /// <remarks>
    /// This is the property that removes hand-computed centring margins like
    /// <c>Margin = new EdgeSize((rowHeight - iconSize) / 2f, 0, 0, 0)</c> — those break the
    /// moment either size changes, which is precisely what a UI-scale factor does to every
    /// size at once. Set <c>AlignItems = AlignItems.Center</c> on the row instead.
    /// Individual children can opt out via <see cref="AlignSelf"/>.
    /// </remarks>
    public AlignItems AlignItems { get; set; } = AlignItems.Start;

    /// <summary>
    /// Per-child override of the parent's <see cref="AlignItems"/>. Null (default) inherits.
    /// </summary>
    public AlignItems? AlignSelf { get; set; }

    /// <summary>
    /// When true and Flow == Horizontal, children that exceed the available width
    /// wrap onto a new row. Fill children are treated as Fit in wrapped rows.
    /// </summary>
    public bool FlowWrap { get; set; }

    /// <summary>
    /// Controls Y-axis overflow behavior. Scroll lays out children at their natural
    /// height and enables scroll-wheel interaction. Default: Clip.
    /// </summary>
    public OverflowMode OverflowY { get; set; } = OverflowMode.Clip;

    /// <summary>
    /// Controls X-axis overflow behavior — <see cref="OverflowY"/>'s horizontal
    /// counterpart. Only meaningful on a <see cref="Flow.Horizontal"/> node without
    /// <see cref="FlowWrap"/>: Scroll lays out children at their natural width regardless
    /// of this node's own width, clips to it, and enables scroll-wheel panning. A pure
    /// horizontal scroller (this set, <see cref="OverflowY"/> left at Clip) also accepts
    /// the plain vertical wheel delta as a horizontal pan — see
    /// <see cref="Rendering.PanacheSurface.Render"/>'s <c>scrollDelta</c> remarks — since
    /// most mice have no horizontal wheel. Default: Clip.
    /// </summary>
    public OverflowMode OverflowX { get; set; } = OverflowMode.Clip;

    /// <summary>Minimum pixel width (0 = unconstrained).</summary>
    public float MinWidth { get; set; }

    /// <summary>Maximum pixel width (0 = unconstrained).</summary>
    public float MaxWidth { get; set; }

    /// <summary>Minimum pixel height (0 = unconstrained).</summary>
    public float MinHeight { get; set; }

    /// <summary>Maximum pixel height (0 = unconstrained).</summary>
    public float MaxHeight { get; set; }

    /// <summary>
    /// Width / height aspect ratio. When > 0, one dimension is derived from the other.
    /// 1.0 = square, 16f/9f = widescreen. The fixed or fill dimension drives; the other is derived.
    /// </summary>
    public float AspectRatio { get; set; }

    /// <summary>
    /// Position mode. Absolute removes the node from flow and places it at
    /// (Left, Top) relative to the parent's content area.
    /// </summary>
    public PositionMode Position { get; set; } = PositionMode.Flow;

    /// <summary>X offset from parent content origin when Position == Absolute.</summary>
    public float Left { get; set; }

    /// <summary>Y offset from parent content origin when Position == Absolute.</summary>
    public float Top { get; set; }

    /// <summary>
    /// Draw order among siblings. Higher values render on top.
    /// Default 0 preserves document (tree) order. Also affects hit-test priority.
    /// </summary>
    public int ZIndex { get; set; }

    // ── Background ──────────────────────────────────────────────────────────

    public PColor? BackgroundColor { get; set; }

    /// <summary>
    /// Second stop for a gradient. By default runs in the Flow direction (linear).
    /// Set BackgroundGradientRadial = true for a circular/radial gradient.
    /// </summary>
    public PColor? BackgroundGradientEnd { get; set; }

    /// <summary>
    /// When true, BackgroundColor → BackgroundGradientEnd is rendered as a radial
    /// gradient emanating from (BackgroundGradientCenterX, BackgroundGradientCenterY).
    /// </summary>
    public bool BackgroundGradientRadial { get; set; }

    /// <summary>Radial gradient center X, 0..1 relative to node width. Default 0.5 (center).</summary>
    public float BackgroundGradientCenterX { get; set; } = 0.5f;

    /// <summary>Radial gradient center Y, 0..1 relative to node height. Default 0.5 (center).</summary>
    public float BackgroundGradientCenterY { get; set; } = 0.5f;

    // ── Image ────────────────────────────────────────────────────────────────

    /// <summary>
    /// CPU-side bitmap drawn inside this node, scaled to fill the node rect.
    /// Use this to embed game icons or thumbnails inside a PanacheUI surface.
    /// </summary>
    public SKBitmap? ImageBitmap { get; set; }

    /// <summary>Optional color multiplied over ImageBitmap pixels.</summary>
    public PColor? ImageTint { get; set; }

    // ── Border ──────────────────────────────────────────────────────────────

    /// <summary>Uniform corner radius for all four corners.</summary>
    public float BorderRadius { get; set; }

    /// <summary>Top-left corner radius. Overrides BorderRadius when set.</summary>
    public float? BorderRadiusTopLeft { get; set; }

    /// <summary>Top-right corner radius. Overrides BorderRadius when set.</summary>
    public float? BorderRadiusTopRight { get; set; }

    /// <summary>Bottom-right corner radius. Overrides BorderRadius when set.</summary>
    public float? BorderRadiusBottomRight { get; set; }

    /// <summary>Bottom-left corner radius. Overrides BorderRadius when set.</summary>
    public float? BorderRadiusBottomLeft { get; set; }

    public PColor? BorderColor { get; set; }
    public float BorderWidth { get; set; }

    // ── Drop shadow ─────────────────────────────────────────────────────────

    public PColor? ShadowColor { get; set; }
    public float ShadowBlur    { get; set; }
    public float ShadowOffsetX { get; set; }
    public float ShadowOffsetY { get; set; } = 2f;

    // ── Text ────────────────────────────────────────────────────────────────

    public PColor? Color  { get; set; }
    public float FontSize { get; set; } = 14f;
    public bool Bold      { get; set; }
    public bool Italic    { get; set; }
    public TextAlign TextAlign       { get; set; } = TextAlign.Left;

    /// <summary>
    /// How text that doesn't fit the node's content width is handled. Default Clip.
    /// </summary>
    /// <remarks>
    /// <see cref="Core.TextOverflow.Wrap"/> is the only value that changes <i>layout</i>:
    /// a wrapping node measures its intrinsic height from the number of lines the text
    /// actually breaks into at the width it ends up with, so a <see cref="SizeMode.Fit"/>
    /// height (and every Fit ancestor above it) grows to hold the whole block. Clip and
    /// Ellipsis are purely render-time and always measure as a single line.
    /// </remarks>
    public TextOverflow TextOverflow { get; set; } = TextOverflow.Clip;

    /// <summary>
    /// Maximum number of wrapped lines. 0 (default) = unlimited. Only meaningful with
    /// <see cref="Core.TextOverflow.Wrap"/>; the last kept line is ellipsized when text
    /// remains.
    /// </summary>
    public int MaxLines { get; set; }

    public float LineHeight          { get; set; } = 1.2f;

    /// <summary>Outline / stroke painted behind text glyphs.</summary>
    public PColor? TextOutlineColor { get; set; }
    public float TextOutlineSize    { get; set; } = 1f;

    /// <summary>Drop shadow painted behind text glyphs (separate from node box shadow).</summary>
    public PColor? TextShadowColor  { get; set; }

    /// <summary>Blur radius of the text shadow. Default 3.</summary>
    public float TextShadowBlur     { get; set; } = 3f;

    /// <summary>Horizontal pixel offset of the text shadow.</summary>
    public float TextShadowOffsetX  { get; set; }

    /// <summary>Vertical pixel offset of the text shadow. Default 1.</summary>
    public float TextShadowOffsetY  { get; set; } = 1f;

    // ── Hover ───────────────────────────────────────────────────────────────
    //
    // Set any of these and the renderer paints the hover cue itself, cross-fading from
    // the base value over NodeAnimState.HoverT. Nothing else is required: hover state is
    // tracked for every node in the layout, not only IsInteractive ones.
    //
    // This exists because every consumer was otherwise reimplementing the same
    // hover tracker — an OnMouseEnter handler per row, a _hoverId field, and a re-style
    // that lands a frame late because the tree is rebuilt before the event that changes
    // it is dispatched. DESIGN_SYSTEM §7.2 makes a hover cue mandatory, so that was
    // boilerplate the design system forced on everyone.

    /// <summary>Background painted at full hover. Cross-faded from <see cref="BackgroundColor"/>.</summary>
    public PColor? HoverBackgroundColor { get; set; }

    /// <summary>
    /// Gradient end painted at full hover, for a node whose base background is a gradient.
    /// Ignored unless <see cref="HoverBackgroundColor"/> is also set.
    /// </summary>
    public PColor? HoverBackgroundGradientEnd { get; set; }

    /// <summary>Border color at full hover. Cross-faded from <see cref="BorderColor"/>.</summary>
    public PColor? HoverBorderColor { get; set; }

    /// <summary>Text color at full hover. Cross-faded from <see cref="Color"/>.</summary>
    public PColor? HoverColor { get; set; }

    /// <summary>True when any hover color is set, so the renderer and the fingerprint
    /// only pay for hover on nodes that actually asked for it.</summary>
    internal bool HasHoverStyle =>
        HoverBackgroundColor.HasValue || HoverBorderColor.HasValue || HoverColor.HasValue;

    // ── Clip ────────────────────────────────────────────────────────────────

    /// <summary>Clip children to this node's bounds (rounded if any radius is set).</summary>
    public bool ClipContent { get; set; }

    /// <summary>
    /// Arbitrary SkiaSharp clip path applied in node-local coordinates (origin at node top-left).
    /// Applied after ClipContent; both may coexist.
    /// </summary>
    public SKPath? ClipPath { get; set; }

    // ── Pointer Events ──────────────────────────────────────────────────────

    /// <summary>
    /// Whether this node and its entire subtree participate in pointer hit-testing.
    /// Set to None for decorative overlay nodes that should pass clicks through.
    /// </summary>
    public PointerEvents PointerEvents { get; set; } = PointerEvents.Auto;

    // ── Misc ────────────────────────────────────────────────────────────────

    public float Opacity { get; set; } = 1f;

    // ── Generative / Animated Effects ───────────────────────────────────────

    private List<NodeEffect>? _effects;

    /// <summary>
    /// Primary overlay effect. Setting this writes to Effects[0].
    /// Use AddEffect() to layer multiple effects on the same node.
    /// </summary>
    public NodeEffect Effect
    {
        get => _effects is { Count: > 0 } e ? e[0] : NodeEffect.None;
        set
        {
            if (value == NodeEffect.None) { _effects = null; return; }
            _effects ??= new List<NodeEffect>(2);
            if (_effects.Count == 0) _effects.Add(value);
            else _effects[0] = value;
        }
    }

    /// <summary>All effects on this node, drawn in list order (front-to-back stacking).</summary>
    public IReadOnlyList<NodeEffect> Effects =>
        (IReadOnlyList<NodeEffect>?)_effects ?? Array.Empty<NodeEffect>();

    /// <summary>Add an additional effect layer. Duplicates are silently ignored.</summary>
    public void AddEffect(NodeEffect effect)
    {
        if (effect == NodeEffect.None) return;
        _effects ??= new List<NodeEffect>(2);
        if (!_effects.Contains(effect)) _effects.Add(effect);
    }

    /// <summary>Remove a specific effect from the stack.</summary>
    public void RemoveEffect(NodeEffect effect) => _effects?.Remove(effect);

    /// <summary>Remove all effects.</summary>
    public void ClearEffects() => _effects = null;

    /// <summary>Primary color for all effects (noise tint, shimmer, glow, etc.).</summary>
    public PColor EffectColor1    { get; set; } = PColor.White;

    /// <summary>Secondary color for all effects (gradient end, plasma accent, back-face tint, etc.).</summary>
    public PColor EffectColor2    { get; set; } = PColor.Black;

    /// <summary>Spatial scale of the effect. Higher = coarser/larger features. Default 1.0.</summary>
    public float EffectScale      { get; set; } = 1f;

    /// <summary>Animation speed multiplier. 0 = static. 1 = normal. Default 1.0.</summary>
    public float EffectSpeed      { get; set; } = 1f;

    /// <summary>Blend strength of the effect over the background. 0 = invisible, 1 = full. Default 0.3.</summary>
    public float EffectIntensity  { get; set; } = 0.3f;

    // ── Internal helpers ────────────────────────────────────────────────────

    /// <summary>True if any corner has a non-zero radius.</summary>
    internal bool HasAnyRadius =>
        BorderRadius > 0 ||
        (BorderRadiusTopLeft.HasValue     && BorderRadiusTopLeft.Value     > 0) ||
        (BorderRadiusTopRight.HasValue    && BorderRadiusTopRight.Value    > 0) ||
        (BorderRadiusBottomRight.HasValue && BorderRadiusBottomRight.Value > 0) ||
        (BorderRadiusBottomLeft.HasValue  && BorderRadiusBottomLeft.Value  > 0);

    /// <summary>Returns per-corner radii, falling back to BorderRadius for unset corners.</summary>
    internal (float tl, float tr, float br, float bl) GetCornerRadii()
    {
        float r = BorderRadius;
        return (
            BorderRadiusTopLeft     ?? r,
            BorderRadiusTopRight    ?? r,
            BorderRadiusBottomRight ?? r,
            BorderRadiusBottomLeft  ?? r
        );
    }

    // ── Visual fingerprint ──────────────────────────────────────────────────

    /// <summary>
    /// Mixes every property that <see cref="Rendering.SkiaRenderer"/> reads into a
    /// running hash, so a surface can tell whether a rebuilt node tree would paint
    /// the same pixels as the last one.
    /// </summary>
    /// <remarks>
    /// <para>Purely geometric properties (Width/Height/*Mode, Margin, Gap, FlowWrap,
    /// Min/Max, AspectRatio, Position/Left/Top, LineHeight) are deliberately absent:
    /// they only reach the renderer through the computed <see cref="Layout.LayoutBox"/>,
    /// which the caller hashes alongside this. Padding <i>is</i> included because the
    /// renderer reads it directly when placing text inside the box.</para>
    ///
    /// <para><b>If you add a property that the renderer reads, add it here too.</b>
    /// A field that affects painting but not this hash will show up as a stale
    /// surface that only refreshes when something else changes.</para>
    /// </remarks>
    internal ulong AppendVisualHash(ulong h)
    {
        // Background / shape
        h = Hash.Color(h, BackgroundColor);
        h = Hash.Color(h, BackgroundGradientEnd);
        h = Hash.Bool (h, BackgroundGradientRadial);
        h = Hash.F32  (h, BackgroundGradientCenterX);
        h = Hash.F32  (h, BackgroundGradientCenterY);
        h = Hash.I32  (h, (int)Flow);              // drives linear-gradient direction

        h = Hash.Ref  (h, ImageBitmap);
        h = Hash.Color(h, ImageTint);

        h = Hash.Color(h, BorderColor);
        h = Hash.F32  (h, BorderWidth);
        h = Hash.F32  (h, BorderRadius);
        h = Hash.F32N (h, BorderRadiusTopLeft);
        h = Hash.F32N (h, BorderRadiusTopRight);
        h = Hash.F32N (h, BorderRadiusBottomRight);
        h = Hash.F32N (h, BorderRadiusBottomLeft);

        h = Hash.Color(h, ShadowColor);
        h = Hash.F32  (h, ShadowBlur);
        h = Hash.F32  (h, ShadowOffsetX);
        h = Hash.F32  (h, ShadowOffsetY);

        // Clipping
        h = Hash.Bool (h, ClipContent);
        h = Hash.I32  (h, (int)OverflowY);
        h = Hash.I32  (h, (int)OverflowX);
        h = Hash.Ref  (h, ClipPath);

        // Text
        h = Hash.Color(h, Color);
        h = Hash.F32  (h, FontSize);
        h = Hash.Bool (h, Bold);
        h = Hash.Bool (h, Italic);
        h = Hash.I32  (h, (int)TextAlign);
        h = Hash.I32  (h, (int)TextOverflow);
        h = Hash.I32  (h, MaxLines);
        // LineHeight is normally geometry-only, but a Wrap node paints multiple baselines
        // from it directly rather than through the box.
        if (TextOverflow == TextOverflow.Wrap) h = Hash.F32(h, LineHeight);
        h = Hash.Color(h, TextOutlineColor);
        h = Hash.F32  (h, TextOutlineSize);
        h = Hash.Color(h, TextShadowColor);
        h = Hash.F32  (h, TextShadowBlur);
        h = Hash.F32  (h, TextShadowOffsetX);
        h = Hash.F32  (h, TextShadowOffsetY);

        // Padding — read directly by the renderer when placing text.
        var p = Padding;
        h = Hash.F32(h, p.Top);
        h = Hash.F32(h, p.Right);
        h = Hash.F32(h, p.Bottom);
        h = Hash.F32(h, p.Left);

        // Hover — the target colors only. The live HoverT that interpolates towards them
        // is animation state, hashed by SurfaceFingerprint alongside the scroll offset.
        h = Hash.Color(h, HoverBackgroundColor);
        h = Hash.Color(h, HoverBackgroundGradientEnd);
        h = Hash.Color(h, HoverBorderColor);
        h = Hash.Color(h, HoverColor);

        // Compositing / ordering
        h = Hash.F32(h, Opacity);
        h = Hash.I32(h, ZIndex);

        // Effects
        var effects = _effects;
        if (effects != null)
        {
            for (int i = 0; i < effects.Count; i++) h = Hash.I32(h, (int)effects[i]);
            h = Hash.Color(h, EffectColor1);
            h = Hash.Color(h, EffectColor2);
            h = Hash.F32  (h, EffectScale);
            h = Hash.F32  (h, EffectSpeed);
            h = Hash.F32  (h, EffectIntensity);
        }

        return h;
    }

    /// <summary>True when this node paints a time-driven effect and can never be cached across frames.</summary>
    internal bool HasEffects => _effects is { Count: > 0 };
}

/// <summary>
/// FNV-1a mixing helpers for the surface content fingerprint.
/// </summary>
/// <remarks>
/// Everything here is aggressively inlined: the fingerprint walk performs tens of
/// thousands of these per frame, and at that volume the call overhead alone was
/// measurable against the rasterisation it exists to avoid.
/// </remarks>
internal static class Hash
{
    private const ulong Prime = 1099511628211UL;

    /// <summary>The FNV-1a offset basis — start every fingerprint here.</summary>
    public const ulong Seed = 14695981039346656037UL;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong U64(ulong h, ulong v) => (h ^ v) * Prime;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong I32(ulong h, int v) => U64(h, (uint)v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong F32(ulong h, float v) => U64(h, (uint)BitConverter.SingleToInt32Bits(v));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong F32N(ulong h, float? v) =>
        U64(h, v.HasValue ? (uint)BitConverter.SingleToInt32Bits(v.Value) : 0xFFFF_FFFFUL);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Bool(ulong h, bool v) => U64(h, v ? 1UL : 2UL);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Color(ulong h, PColor c) => U64(h, Packed(c));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Color(ulong h, PColor? c) =>
        // The 33rd bit distinguishes "no color" from a color that happens to pack to 0.
        U64(h, c.HasValue ? Packed(c.Value) | 0x1_0000_0000UL : 0UL);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Packed(PColor c) =>
        ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    /// <summary>
    /// Mixes a string via the runtime's own vectorised hash rather than char-by-char.
    /// <c>string.GetHashCode()</c> is randomised per process but stable within one, and
    /// the fingerprint is only ever compared against the previous frame of the same
    /// process — so per-process randomisation is irrelevant here.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Str(ulong h, string? s) =>
        s == null ? U64(h, 0) : U64(h, ((ulong)(uint)s.Length << 32) | (uint)s.GetHashCode());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Ref(ulong h, object? o) =>
        U64(h, o == null ? 0UL : (ulong)(uint)RuntimeHelpers.GetHashCode(o));
}
