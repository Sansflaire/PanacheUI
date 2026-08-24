using System;
using System.Globalization;
using PanacheUI.Core;
using PanacheUI.Theming;

namespace PanacheUI.Components;

/// <summary>
/// Legacy alias — reads the active <see cref="PanacheTheme"/>.
///
/// Kept so pre-theming code that references <c>Theme.Base</c> etc. keeps
/// working. New code should snapshot <see cref="PanacheThemes.Active"/>
/// once at init and subscribe to <see cref="PanacheThemes.ActiveChanged"/>
/// (see <c>Theming/PanacheTheme.cs</c>), not read these properties per
/// frame.
/// </summary>
public static class Theme
{
    /// <summary>Root window background — the darkest layer.</summary>
    public static PColor Base       => PanacheThemes.Active.Base;
    /// <summary>Section panel background — slightly lighter than Base.</summary>
    public static PColor Panel      => PanacheThemes.Active.Panel;
    /// <summary>Inner card background — between Base and Panel.</summary>
    public static PColor Panel2     => PanacheThemes.Active.Panel2;
    /// <summary>Muted body text.</summary>
    public static PColor TextMuted  => PanacheThemes.Active.TextMuted;
    /// <summary>Subtle body text.</summary>
    public static PColor TextSubtle => PanacheThemes.Active.TextSubtle;
}

/// <summary>
/// Reusable Umbra-style layout components for PanacheUI windows.
///
/// Core visual rules (document these in UMBRA_VISUAL_TECHNIQUES.md):
///   • SectionWrap  — full-width panel with left accent bar + top highlight line
///   • SectionDivider — 1px gradient fade between sections (not a gap)
///   • SectionLabel — small uppercase label above section content
///   • Header gradients end at Theme.Panel so they bleed into sections below
///   • No BorderRadius on full-width strips; only inset elements get radius
///   • No drop shadows on sections; depth from Panel/Panel2 color shift only
/// </summary>
public static partial class PUI
{
    /// <summary>
    /// Wraps <paramref name="content"/> in an Umbra-style section panel:
    /// top highlight line (1px, accent-tinted, fades right) +
    /// body row with 3px left accent bar beside the content.
    /// </summary>
    public static Node SectionWrap(PColor accent, Node content)
    {
        // 1px top highlight — "light from above"
        var highlight = new Node().WithStyle(s =>
        {
            s.WidthMode             = SizeMode.Fill;
            s.HeightMode            = SizeMode.Fixed; s.Height = 1;
            s.BackgroundColor       = accent.WithOpacity(0.18f);
            s.BackgroundGradientEnd = PColor.Transparent;
            s.Flow                  = Flow.Horizontal;
        });

        // 3px left accent bar
        var accentBar = new Node().WithStyle(s =>
        {
            s.WidthMode       = SizeMode.Fixed; s.Width  = 3;
            s.HeightMode      = SizeMode.Fill;
            s.BackgroundColor = accent.WithOpacity(0.70f);
        });

        var body = new Node().WithStyle(s =>
        {
            s.Flow       = Flow.Horizontal;
            s.WidthMode  = SizeMode.Fill;
            s.HeightMode = SizeMode.Fit;
        });
        body.AppendChild(accentBar);
        body.AppendChild(content);

        var outer = new Node().WithStyle(s =>
        {
            s.Flow            = Flow.Vertical;
            s.WidthMode       = SizeMode.Fill;
            s.HeightMode      = SizeMode.Fit;
            s.BackgroundColor = Theme.Panel;
        });
        outer.AppendChild(highlight);
        outer.AppendChild(body);
        return outer;
    }

    /// <summary>
    /// A 1px gradient line between sections — blends to transparent on the right.
    /// Use instead of gaps; never leave raw spacing between sections.
    /// </summary>
    public static Node SectionDivider(PColor color) =>
        new Node().WithStyle(s =>
        {
            s.WidthMode             = SizeMode.Fill;
            s.HeightMode            = SizeMode.Fixed; s.Height = 1;
            s.BackgroundColor       = color;
            s.BackgroundGradientEnd = PColor.Transparent;
            s.Flow                  = Flow.Horizontal;
        });

    /// <summary>
    /// Small uppercase section label — place at the top of content inside SectionWrap.
    /// </summary>
    public static Node SectionLabel(string text, PColor accent) =>
        new Node().WithText(text).WithStyle(s =>
        {
            s.WidthMode  = SizeMode.Fill;
            s.HeightMode = SizeMode.Fit;
            s.FontSize   = 9.5f;
            s.Bold       = true;
            s.Color      = accent.WithOpacity(0.65f);
            s.Margin     = new EdgeSize(0, 0, 6, 0);
        });

    /// <summary>
    /// Standard inset card — slightly lighter than Panel, low-contrast accent border, no shadow.
    /// </summary>
    public static Node Card(PColor accent, float borderRadius = 4f) =>
        new Node().WithStyle(s =>
        {
            s.Flow            = Flow.Vertical;
            s.WidthMode       = SizeMode.Fill;
            s.HeightMode      = SizeMode.Fit;
            s.BackgroundColor = Theme.Panel2;
            s.BorderRadius    = borderRadius;
            s.BorderColor     = accent.WithOpacity(0.22f);
            s.BorderWidth     = 1;
            s.Padding         = new EdgeSize(9, 12);
            s.Gap             = 5;
        });

    /// <summary>
    /// Pill-style button node. Hit-test via layout map after ImGui.Image.
    /// </summary>
    public static Node PillButton(string id, string text, PColor accent) =>
        new Node().WithId(id).WithText(text).WithStyle(s =>
        {
            s.WidthMode             = SizeMode.Fit;
            s.HeightMode            = SizeMode.Fit;
            s.BackgroundColor       = accent.WithOpacity(0.18f);
            s.BackgroundGradientEnd = accent.WithOpacity(0.08f);
            s.Flow                  = Flow.Horizontal;
            s.BorderRadius          = 6;
            s.BorderColor           = accent.WithOpacity(0.55f);
            s.BorderWidth           = 1;
            s.Padding               = new EdgeSize(6, 16);
            s.FontSize              = 11f;
            s.Bold                  = true;
            s.Color                 = accent.WithOpacity(0.95f);
        });

    // ── Sliders ─────────────────────────────────────────────────────────────
    //
    // Native Panache sliders. These supersede the old "put ImGui.SliderFloat in a
    // strip below the surface" workaround — that does not scale to a panel with
    // dozens of values, and it breaks the rule that everything visible is a node.
    //
    // Drag handling relies on Node.CapturesDrag: the outer node grabs the pointer on
    // press and keeps receiving OnDrag until release, so the knob still tracks the
    // cursor when it wanders off the track. Track/fill/knob are PointerEvents.None so
    // every event lands on the outer node.

    /// <summary>Default slider track width in pixels.</summary>
    public const float SliderWidth = 150f;
    /// <summary>Default slider row height in pixels — also the pointer target height.</summary>
    public const float SliderHeight = 16f;

    private const float SliderTrackH = 4f;
    private const float SliderKnobD  = 11f;

    /// <summary>
    /// A bare horizontal slider. <paramref name="onChange"/> fires on every drag frame with
    /// the new value, already mapped into [<paramref name="min"/>, <paramref name="max"/>]
    /// and clamped.
    ///
    /// Set <paramref name="step"/> above 0 to quantise (e.g. 1f for integers).
    /// </summary>
    public static Node Slider(
        string id, float value, float min, float max, PColor accent,
        Action<float>? onChange = null,
        float width  = SliderWidth,
        float height = SliderHeight,
        float step   = 0f)
    {
        float span = max - min;
        float frac = span > 0f ? Math.Clamp((value - min) / span, 0f, 1f) : 0f;

        // Knob centre travels from knobR to width-knobR, so the usable travel is width-knobD.
        float knobR  = SliderKnobD * 0.5f;
        float travel = Math.Max(1f, width - SliderKnobD);
        float centre = knobR + frac * travel;

        var track = new Node().WithStyle(s =>
        {
            s.Position        = PositionMode.Absolute;
            s.Left            = 0;
            s.Top             = (height - SliderTrackH) * 0.5f;
            s.WidthMode       = SizeMode.Fixed; s.Width  = width;
            s.HeightMode      = SizeMode.Fixed; s.Height = SliderTrackH;
            s.BackgroundColor = accent.WithOpacity(0.14f);
            s.BorderRadius    = SliderTrackH * 0.5f;
            s.PointerEvents   = PointerEvents.None;
        });

        var fill = new Node().WithStyle(s =>
        {
            s.Position        = PositionMode.Absolute;
            s.Left            = 0;
            s.Top             = (height - SliderTrackH) * 0.5f;
            s.WidthMode       = SizeMode.Fixed; s.Width  = centre;
            s.HeightMode      = SizeMode.Fixed; s.Height = SliderTrackH;
            s.BackgroundColor = accent.WithOpacity(0.70f);
            s.BorderRadius    = SliderTrackH * 0.5f;
            s.PointerEvents   = PointerEvents.None;
        });

        var knob = new Node().WithStyle(s =>
        {
            s.Position        = PositionMode.Absolute;
            s.Left            = centre - knobR;
            s.Top             = (height - SliderKnobD) * 0.5f;
            s.WidthMode       = SizeMode.Fixed; s.Width  = SliderKnobD;
            s.HeightMode      = SizeMode.Fixed; s.Height = SliderKnobD;
            s.BackgroundColor = accent.WithOpacity(0.95f);
            s.BorderRadius    = knobR;
            s.BorderColor     = PColor.White.WithOpacity(0.30f);
            s.BorderWidth     = 1;
            s.PointerEvents   = PointerEvents.None;
        });

        var outer = new Node().WithId(id).WithStyle(s =>
        {
            s.WidthMode  = SizeMode.Fixed; s.Width  = width;
            s.HeightMode = SizeMode.Fixed; s.Height = height;
            s.Flow       = Flow.Horizontal;
        });
        outer.IsInteractive = true;
        outer.CapturesDrag  = true;
        outer.AppendChild(track);
        outer.AppendChild(fill);
        outer.AppendChild(knob);

        if (onChange != null)
        {
            outer.OnDrag += (_, localX, _) =>
            {
                float t = Math.Clamp((localX - knobR) / travel, 0f, 1f);
                float v = min + t * span;
                if (step > 0f) v = MathF.Round(v / step) * step;
                onChange(Math.Clamp(v, min, max));
            };
        }

        return outer;
    }

    /// <summary>
    /// A labelled slider row: name on the left, slider in the middle, formatted value on the
    /// right. This is the workhorse for property panels — one call per editable float.
    /// </summary>
    public static Node SliderRow(
        string id, string label, float value, float min, float max, PColor accent,
        Action<float>? onChange = null,
        string format      = "0.###",
        float  labelWidth  = 108f,
        float  valueWidth  = 46f,
        float  sliderWidth = SliderWidth,
        float  step        = 0f)
    {
        var labelNode = new Node().WithText(label).WithStyle(s =>
        {
            s.WidthMode     = SizeMode.Fixed; s.Width = labelWidth;
            s.HeightMode    = SizeMode.Fit;
            s.FontSize      = 10.5f;
            s.Color         = Theme.TextMuted;
            s.TextOverflow  = TextOverflow.Ellipsis;
            s.PointerEvents = PointerEvents.None;
        });

        var valueNode = new Node().WithText(value.ToString(format, CultureInfo.InvariantCulture)).WithStyle(s =>
        {
            s.WidthMode     = SizeMode.Fixed; s.Width = valueWidth;
            s.HeightMode    = SizeMode.Fit;
            s.FontSize      = 10.5f;
            s.Bold          = true;
            s.Color         = accent.WithOpacity(0.95f);
            s.TextAlign     = TextAlign.Right;
            s.PointerEvents = PointerEvents.None;
        });

        return new Node().WithId(id + "_row").WithStyle(s =>
        {
            s.Flow       = Flow.Horizontal;
            s.WidthMode  = SizeMode.Fill;
            s.HeightMode = SizeMode.Fixed; s.Height = SliderHeight + 4f;
            s.Gap        = 8;
        }).WithChildren(
            labelNode,
            Slider(id, value, min, max, accent, onChange, sliderWidth, SliderHeight, step),
            valueNode);
    }

    /// <summary>
    /// Creates a root node that fills a fixed w×h surface using PanacheUI base theme.
    /// </summary>
    public static Node RootNode(int w, int h) =>
        new Node().WithId("root").WithStyle(s =>
        {
            s.WidthMode       = SizeMode.Fixed; s.Width  = w;
            s.HeightMode      = SizeMode.Fixed; s.Height = h;
            s.Flow            = Flow.Vertical;
            s.BackgroundColor = Theme.Base;
        });
}
