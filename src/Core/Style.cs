using System;
using System.Collections.Generic;
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
    /// When true and Flow == Horizontal, children that exceed the available width
    /// wrap onto a new row. Fill children are treated as Fit in wrapped rows.
    /// </summary>
    public bool FlowWrap { get; set; }

    /// <summary>
    /// Controls Y-axis overflow behavior. Scroll lays out children at their natural
    /// height and enables scroll-wheel interaction. Default: Clip.
    /// </summary>
    public OverflowMode OverflowY { get; set; } = OverflowMode.Clip;

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
    public TextOverflow TextOverflow { get; set; } = TextOverflow.Clip;
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
}
