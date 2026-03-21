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

    // ── Background ──────────────────────────────────────────────────────────

    public PColor? BackgroundColor { get; set; }

    /// <summary>Second color for a gradient. Gradient runs in the Flow direction.</summary>
    public PColor? BackgroundGradientEnd { get; set; }

    // ── Border ──────────────────────────────────────────────────────────────

    public float BorderRadius { get; set; }
    public PColor? BorderColor { get; set; }
    public float BorderWidth { get; set; }

    // ── Drop shadow ─────────────────────────────────────────────────────────

    public PColor? ShadowColor { get; set; }
    public float ShadowBlur  { get; set; }
    public float ShadowOffsetX { get; set; }
    public float ShadowOffsetY { get; set; } = 2f;

    // ── Text ────────────────────────────────────────────────────────────────

    public PColor? Color     { get; set; }
    public float FontSize    { get; set; } = 14f;
    public bool Bold         { get; set; }
    public bool Italic       { get; set; }
    public TextAlign TextAlign    { get; set; } = TextAlign.Left;
    public TextOverflow TextOverflow { get; set; } = TextOverflow.Clip;
    public float LineHeight  { get; set; } = 1.2f;

    /// <summary>Outline/stroke painted behind text glyphs.</summary>
    public PColor? TextOutlineColor { get; set; }
    public float TextOutlineSize { get; set; } = 1f;

    // ── Misc ────────────────────────────────────────────────────────────────

    public float Opacity { get; set; } = 1f;

    /// <summary>Clip children to this node's bounds.</summary>
    public bool ClipContent { get; set; }

    // ── Generative / Animated Effect ────────────────────────────────────────

    /// <summary>Overlay effect drawn on top of the background. Default: None.</summary>
    public NodeEffect Effect { get; set; } = NodeEffect.None;

    /// <summary>Primary color for the effect (noise color, shimmer tint, etc.).</summary>
    public PColor EffectColor1 { get; set; } = PColor.White;

    /// <summary>Secondary color for the effect (gradient end, plasma accent, etc.).</summary>
    public PColor EffectColor2 { get; set; } = PColor.Black;

    /// <summary>
    /// Spatial scale of the effect. Higher = coarser/larger features.
    /// Default: 1.0. For noise, controls frequency. For scanlines, controls line spacing.
    /// </summary>
    public float EffectScale { get; set; } = 1f;

    /// <summary>
    /// Animation speed multiplier. 0 = static. 1 = normal. Default: 1.0.
    /// </summary>
    public float EffectSpeed { get; set; } = 1f;

    /// <summary>
    /// Blend strength of the effect over the background. 0 = invisible, 1 = full. Default: 0.3.
    /// </summary>
    public float EffectIntensity { get; set; } = 0.3f;
}
