namespace PanacheUI.Core;

/// <summary>Direction children are stacked within a node.</summary>
public enum Flow
{
    Vertical,
    Horizontal,
}

/// <summary>How a node determines its own width or height.</summary>
public enum SizeMode
{
    /// <summary>Expand to fill available space from the parent.</summary>
    Fill,
    /// <summary>Shrink to fit content (children or text).</summary>
    Fit,
    /// <summary>Use the explicit pixel value from Style.Width / Style.Height.</summary>
    Fixed,
}

/// <summary>Horizontal alignment of text within a node.</summary>
public enum TextAlign
{
    Left,
    Center,
    Right,
}

/// <summary>What happens when text overflows the node width.</summary>
public enum TextOverflow
{
    Clip,
    Ellipsis,

    /// <summary>
    /// Break the text onto as many lines as it needs. Wrapping happens on whitespace,
    /// with over-long single words hard-broken mid-word; embedded <c>\n</c> is always a
    /// hard break. A <see cref="SizeMode.Fit"/> height reports the <i>wrapped</i> height,
    /// so a node using this grows to hold its text instead of clipping it.
    /// </summary>
    /// <remarks>
    /// Combine with <see cref="Style.MaxLines"/> to cap the block and ellipsize the last
    /// kept line. See <see cref="Style.TextOverflow"/> for the layout consequences.
    /// </remarks>
    Wrap,
}

/// <summary>
/// Cross-axis alignment of a node's flow children — the axis <i>perpendicular</i> to
/// <see cref="Flow"/>. Horizontal flow aligns children vertically; vertical flow aligns
/// them horizontally.
/// </summary>
/// <remarks>
/// Only affects children that are <i>not</i> <see cref="SizeMode.Fill"/> on the cross
/// axis — a Fill child already consumes the whole cross extent, so there is nothing left
/// to align. <see cref="Start"/> is the default and is exactly what this engine did
/// before the property existed.
/// </remarks>
public enum AlignItems
{
    /// <summary>Children sit at the top (horizontal flow) / left (vertical flow).</summary>
    Start,
    /// <summary>Children are centred on the cross axis.</summary>
    Center,
    /// <summary>Children sit at the bottom (horizontal flow) / right (vertical flow).</summary>
    End,
}

/// <summary>Position mode — controls whether a node participates in normal flow layout.</summary>
public enum PositionMode
{
    /// <summary>Default — node participates in parent flow layout.</summary>
    Flow,
    /// <summary>
    /// Removed from flow. Placed at (Style.Left, Style.Top) relative to the parent's content area.
    /// Does not affect parent size. Renders in tree order unless ZIndex overrides draw order.
    /// </summary>
    Absolute,
}

/// <summary>Controls whether a node and its subtree receive pointer events.</summary>
public enum PointerEvents
{
    /// <summary>Default — node receives hover, click, and scroll events normally.</summary>
    Auto,
    /// <summary>
    /// Node and all descendants are excluded from hit-testing.
    /// Pointer events pass through to nodes behind/below this subtree.
    /// </summary>
    None,
}

/// <summary>How overflow content is handled on the Y axis.</summary>
public enum OverflowMode
{
    /// <summary>Default — children outside the bounds are clipped.</summary>
    Clip,
    /// <summary>
    /// Children are laid out at their natural height regardless of the node's Height.
    /// The node clips to its Height and is scrollable via scroll wheel.
    /// </summary>
    Scroll,
}

/// <summary>
/// Generative / animated overlay effect drawn on top of a node's background.
/// Set via Style.Effect or Style.AddEffect(). Parameters are in Style.Effect*.
/// </summary>
public enum NodeEffect
{
    None,

    // ── Generative Textures ─────────────────────────────────────────────────
    /// <summary>Fractal Perlin noise overlay. Animates if EffectSpeed > 0.</summary>
    PerlinNoise,
    /// <summary>Animated plasma blobs from layered sin/cos math.</summary>
    Plasma,
    /// <summary>Voronoi / cell pattern tessellation.</summary>
    Voronoi,
    /// <summary>1px-on/1px-off scanline / CRT overlay.</summary>
    Scanline,
    /// <summary>Grid of circles scaling with distance from a focal point.</summary>
    DotMatrix,
    /// <summary>Audio-style waveform bar strip from noise data.</summary>
    Waveform,

    // ── Animated Overlays ───────────────────────────────────────────────────
    /// <summary>Diagonal shimmer stripe sweeps across the node.</summary>
    Shimmer,
    /// <summary>Border / glow pulses with a breathing rhythm.</summary>
    PulseGlow,
    /// <summary>Bright segment chases around the node perimeter.</summary>
    ChaseLight,

    // ── Lighting ────────────────────────────────────────────────────────────
    /// <summary>Thick soft-glow rim on one edge (simulates backlight).</summary>
    RimLight,
    /// <summary>Radial darkening at inner corners where panels meet.</summary>
    AmbientOcclusion,
    /// <summary>Bright elements bleed soft bloom light outward.</summary>
    Bloom,

    // ── Visual Distortion ───────────────────────────────────────────────────
    /// <summary>Node shakes violently then snaps back.</summary>
    Shake,
    /// <summary>Characters undulate in a vertical sine wave.</summary>
    TextWave,
    /// <summary>Content resampled with scrolling noise UV offset.</summary>
    HeatHaze,

    // ── Particles / Text Animation ──────────────────────────────────────────
    /// <summary>Tiny particles drift upward from the node.</summary>
    Particles,
    /// <summary>Text reveals one character at a time with a blinking cursor.</summary>
    TypewriterText,
    /// <summary>Numeric text animates from 0 to target value.</summary>
    RollingCounter,

    // ── Interaction Effects ──────────────────────────────────────────────────
    /// <summary>Card scales up and brightens on hover.</summary>
    HoverLift,
    /// <summary>Node squishes on press, springs back on release.</summary>
    PressDepress,
    /// <summary>Circular wave expands from click point.</summary>
    Ripple,

    // ── Lighting ────────────────────────────────────────────────────────────
    /// <summary>Moving light source sweeps highlight across surfaces.</summary>
    SpecularSweep,

    // ── Panel / Transition ──────────────────────────────────────────────────
    /// <summary>Siblings appear in sequence with staggered delay.</summary>
    StaggeredEntrance,
    /// <summary>Node slides in from the left on a looping timer.</summary>
    SlideTransition,
    /// <summary>Node flips on its Y-axis revealing a second face.</summary>
    CardFlip,
}
