using PanacheUI.Core;

namespace PanacheUI.Components;

/// <summary>
/// Shared theme colors for PanacheUI windows.
/// Use these as the base palette so all windows look consistent.
/// </summary>
public static class Theme
{
    /// <summary>Root window background — the darkest layer.</summary>
    public static readonly PColor Base   = PColor.FromHex("#0D0D1A");
    /// <summary>Section panel background — slightly lighter than Base.</summary>
    public static readonly PColor Panel  = PColor.FromHex("#131328");
    /// <summary>Inner card background — between Base and Panel.</summary>
    public static readonly PColor Panel2 = PColor.FromHex("#0F0F22");
    /// <summary>Muted body text.</summary>
    public static readonly PColor TextMuted = PColor.FromHex("#9999BB");
    /// <summary>Subtle body text.</summary>
    public static readonly PColor TextSubtle = PColor.FromHex("#666688");
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
public static class PUI
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
