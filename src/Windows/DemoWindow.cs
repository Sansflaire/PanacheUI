using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using PanacheUI.Components;
using PanacheUI.Core;
using PanacheUI.Layout;
using PanacheUI.Rendering;
using ImTextureID = Dalamud.Bindings.ImGui.ImTextureID;

namespace PanacheUI.Windows;

/// <summary>
/// Proof-of-concept window that renders a PanacheUI node tree using SkiaSharp
/// and displays the result via ImGui.Image — showcasing capabilities far
/// beyond vanilla ImGui. Automatically resizes to match the window content area.
/// </summary>
public sealed class DemoWindow : IDisposable
{
    public bool IsVisible = true;

    private readonly ITextureProvider _texProvider;
    private readonly HelpWindow       _help;
    private readonly LayoutEngine     _layout;
    private readonly SkiaRenderer     _renderer;

    private System.Numerics.Vector2? _windowPos;  // null = use ImGui default on first frame

    private RenderSurface  _surface;
    private TextureManager _textures;
    private Node           _root;

    private int          _surfaceW;
    private int          _surfaceH;
    private ImTextureID? _texHandle;
    private bool         _needsRender = true;
    private float        _animTime;

    // Layout snapshot for hit-testing
    private Dictionary<Node, LayoutBox> _lastLayout = new();
    private Node?                        _btnNode;

    public DemoWindow(ITextureProvider texProvider, HelpWindow help)
    {
        _texProvider = texProvider;
        _help        = help;
        _layout      = new LayoutEngine();
        _renderer    = new SkiaRenderer();

        // Start with a reasonable default; will resize to window on first frame
        _surfaceW = 520;
        _surfaceH = 420;
        _surface  = new RenderSurface(_surfaceW, _surfaceH);
        _textures = new TextureManager(_texProvider);
        _root     = BuildTree(_surfaceW, _surfaceH);
    }

    public void Draw()
    {
        if (!IsVisible) return;

        ImGui.SetNextWindowSize(new Vector2(540, 460), ImGuiCond.FirstUseEver);
        if (_windowPos.HasValue)
            ImGui.SetNextWindowPos(_windowPos.Value, ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoTitleBar
                  | ImGuiWindowFlags.NoScrollbar
                  | ImGuiWindowFlags.NoScrollWithMouse;
        if (!ImGui.Begin("##panacheui_demo", ref IsVisible, flags))
        {
            ImGui.End();
            return;
        }

        // Capture window position after first frame so we can drive dragging
        if (!_windowPos.HasValue)
            _windowPos = ImGui.GetWindowPos();

        // Surface fills the entire content area — nothing lives outside PanacheUI
        var avail = ImGui.GetContentRegionAvail();
        int newW  = Math.Max(100, (int)avail.X);
        int newH  = Math.Max(100, (int)avail.Y);

        if (newW != _surfaceW || newH != _surfaceH)
        {
            _surfaceW = newW;
            _surfaceH = newH;

            _surface.Dispose();
            _textures.Dispose();
            _surface  = new RenderSurface(_surfaceW, _surfaceH);
            _textures = new TextureManager(_texProvider);
            _root     = BuildTree(_surfaceW, _surfaceH);
            _btnNode  = _root.FindById("btn-overview");
            _needsRender = true;
        }

        // Animate banner gradient each frame
        _animTime += ImGui.GetIO().DeltaTime;
        UpdateAnimatedNode();

        if (_needsRender || _root.IsDirty)
        {
            _lastLayout  = _layout.Compute(_root, _surfaceW, _surfaceH);
            _renderer.Render(_surface.Canvas, _root, _lastLayout, _animTime);
            _texHandle   = _textures.Upload(_surface);
            _needsRender = false;
            _root.ClearDirty();
        }

        if (_texHandle.HasValue)
        {
            var imagePos    = ImGui.GetCursorScreenPos();
            ImGui.Image(_texHandle.Value, new Vector2(_surfaceW, _surfaceH));
            bool imageHovered = ImGui.IsItemHovered();

            // Close button — top-right corner
            const float BtnSize = 24f;
            const float BtnPad  = 8f;
            var btnPos = new Vector2(imagePos.X + _surfaceW - BtnSize - BtnPad,
                                     imagePos.Y + BtnPad);
            ImGui.SetCursorScreenPos(btnPos);
            ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0f,    0f,    0f,    0.30f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.85f, 0.20f, 0.20f, 0.80f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.85f, 0.10f, 0.10f, 1.00f));
            ImGui.PushStyleColor(ImGuiCol.Text,          new Vector4(1f,    1f,    1f,    0.90f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
            if (ImGui.Button("X##close_demo", new Vector2(BtnSize, BtnSize)))
                IsVisible = false;
            ImGui.PopStyleColor(4);
            ImGui.PopStyleVar();

            var mouse = ImGui.GetMousePos();
            float mx = mouse.X - imagePos.X;
            float my = mouse.Y - imagePos.Y;

            bool overClose = mouse.X >= btnPos.X && mouse.X < btnPos.X + BtnSize
                          && mouse.Y >= btnPos.Y && mouse.Y < btnPos.Y + BtnSize;

            // Feature Overview button bounds
            bool overOverview = _btnNode != null
                             && _lastLayout.TryGetValue(_btnNode, out var btnBox)
                             && mx >= btnBox.X && mx <= btnBox.Right
                             && my >= btnBox.Y && my <= btnBox.Bottom;

            // Drag: anywhere on the surface except buttons
            if (!overClose && !overOverview && imageHovered
             && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                var delta = ImGui.GetIO().MouseDelta;
                _windowPos = (_windowPos ?? ImGui.GetWindowPos()) + delta;
            }

            // Hit-test: Feature Overview button click
            if (overOverview && imageHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                _help.IsVisible = !_help.IsVisible;
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), "Render failed — check dalamud.log");
        }

        ImGui.End();
    }

    // ── Node tree ────────────────────────────────────────────────────────────
    // Uses PUI.SectionWrap / SectionDivider / SectionLabel from PanacheUI.PUI.
    // See PanacheComponents.cs for the full Umbra-technique implementations.

    private Node BuildTree(int w, int h)
    {
        var root = PUI.RootNode(w, h);

        root.AppendChild(BuildHeader());
        root.AppendChild(PUI.SectionDivider(PColor.FromHex("#9966FF").WithOpacity(0.25f)));
        root.AppendChild(BuildStatSection());
        root.AppendChild(PUI.SectionDivider(PColor.FromHex("#FFFFFF").WithOpacity(0.05f)));
        root.AppendChild(BuildFeaturesSection());
        root.AppendChild(PUI.SectionDivider(PColor.FromHex("#FFFFFF").WithOpacity(0.05f)));
        root.AppendChild(BuildProgressSection());
        root.AppendChild(PUI.SectionDivider(PColor.FromHex("#FFFFFF").WithOpacity(0.05f)));
        root.AppendChild(BuildAnimatedBanner());
        root.AppendChild(BuildOverviewButton());

        return root;
    }

    // ── Sections ─────────────────────────────────────────────────────────────

    private static Node BuildHeader()
    {
        var header = new Node().WithId("header").WithStyle(s =>
        {
            s.Flow                  = Flow.Vertical;
            s.WidthMode             = SizeMode.Fill;
            s.HeightMode            = SizeMode.Fit;
            s.BackgroundColor       = PColor.FromHex("#1E1040");
            s.BackgroundGradientEnd = Theme.Panel;   // ← blends into Panel sections below
            s.Padding               = new EdgeSize(14, 20, 10, 20);
            s.Gap                   = 4;
        });

        header.AppendChild(new Node().WithText("PanacheUI Framework").WithStyle(s =>
        {
            s.WidthMode        = SizeMode.Fill;
            s.HeightMode       = SizeMode.Fit;
            s.FontSize         = 22f;
            s.Bold             = true;
            s.Color            = PColor.FromHex("#D4AAFF");
            s.TextAlign        = TextAlign.Center;
            s.TextOutlineColor = PColor.FromHex("#000000").WithOpacity(0.7f);
            s.TextOutlineSize  = 1.2f;
        }));

        header.AppendChild(new Node().WithText("SkiaSharp · Node Tree · Box Layout · Gradients · Shadows").WithStyle(s =>
        {
            s.WidthMode  = SizeMode.Fill;
            s.HeightMode = SizeMode.Fit;
            s.FontSize   = 10.5f;
            s.Color      = PColor.FromHex("#7766AA");
            s.TextAlign  = TextAlign.Center;
        }));

        return header;
    }

    private static Node BuildStatSection()
    {
        var accent = PColor.FromHex("#9966FF");

        var content = new Node().WithStyle(s =>
        {
            s.Flow       = Flow.Vertical;
            s.WidthMode  = SizeMode.Fill;
            s.HeightMode = SizeMode.Fit;
            s.Padding    = new EdgeSize(10, 14);
            s.Gap        = 8;
        });

        content.AppendChild(PUI.SectionLabel("COMBAT STATS", accent));

        var row = new Node().WithStyle(s =>
        {
            s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; s.Gap = 10;
        });
        row.AppendChild(StatCard("DPS",  "24,810", PColor.FromHex("#FF6B6B")));
        row.AppendChild(StatCard("HPS",  " 8,420", PColor.FromHex("#6BFFB8")));
        row.AppendChild(StatCard("DTkn", "12,005", PColor.FromHex("#6BB8FF")));
        content.AppendChild(row);

        return PUI.SectionWrap(accent, content);
    }

    private static Node StatCard(string label, string value, PColor accent)
    {
        var card = new Node().WithStyle(s =>
        {
            s.Flow            = Flow.Vertical;
            s.WidthMode       = SizeMode.Fill;
            s.HeightMode      = SizeMode.Fit;
            s.BackgroundColor = accent.WithOpacity(0.10f);
            s.BorderColor     = accent.WithOpacity(0.35f);
            s.BorderWidth     = 1;
            s.BorderRadius    = 4;
            s.Padding         = new EdgeSize(8, 12);
            s.Gap             = 2;
        });

        card.AppendChild(new Node().WithText(label).WithStyle(s =>
        {
            s.WidthMode  = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
            s.FontSize   = 9.5f;
            s.Color      = accent.WithOpacity(0.80f);
            s.TextAlign  = TextAlign.Center;
        }));

        card.AppendChild(new Node().WithText(value).WithStyle(s =>
        {
            s.WidthMode  = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
            s.FontSize   = 17f;
            s.Bold       = true;
            s.Color      = PColor.White;
            s.TextAlign  = TextAlign.Center;
        }));

        return card;
    }

    private static Node BuildFeaturesSection()
    {
        var accent = PColor.FromHex("#CC88FF");

        var content = new Node().WithStyle(s =>
        {
            s.Flow       = Flow.Vertical;
            s.WidthMode  = SizeMode.Fill;
            s.HeightMode = SizeMode.Fit;
            s.Padding    = new EdgeSize(10, 14);
            s.Gap        = 8;
        });

        content.AppendChild(PUI.SectionLabel("RENDERING FEATURES", accent));

        var row = new Node().WithStyle(s =>
        {
            s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; s.Gap = 10;
        });
        row.AppendChild(FeatureCard("Rounded corners", "Any radius — Skia draws smooth anti-aliased curves.", PColor.FromHex("#AA88FF")));
        row.AppendChild(FeatureCard("Drop shadows",    "Configurable blur, offset, and color per node.",      PColor.FromHex("#FF88AA")));
        content.AppendChild(row);

        return PUI.SectionWrap(accent, content);
    }

    private static Node FeatureCard(string title, string body, PColor accent)
    {
        // Cards within a section use a slightly lighter background than Panel,
        // a subtle accent border, no shadow (depth is from section context, not cards)
        var card = new Node().WithStyle(s =>
        {
            s.Flow            = Flow.Vertical;
            s.WidthMode       = SizeMode.Fill;
            s.HeightMode      = SizeMode.Fit;
            s.BackgroundColor = Theme.Panel2;
            s.BorderRadius    = 4;
            s.BorderColor     = accent.WithOpacity(0.22f);
            s.BorderWidth     = 1;
            s.Padding         = new EdgeSize(9, 12);
            s.Gap             = 5;
        });

        card.AppendChild(new Node().WithText(title).WithStyle(s =>
        {
            s.WidthMode  = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
            s.FontSize   = 12.5f;
            s.Bold       = true;
            s.Color      = accent;
        }));

        card.AppendChild(new Node().WithText(body).WithStyle(s =>
        {
            s.WidthMode    = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
            s.FontSize     = 10f;
            s.Color        = PColor.FromHex("#9999BB");
            s.TextOverflow = TextOverflow.Ellipsis;
        }));

        return card;
    }

    private static Node BuildProgressSection()
    {
        var accent = PColor.FromHex("#6BDDFF");

        var content = new Node().WithStyle(s =>
        {
            s.Flow       = Flow.Vertical;
            s.WidthMode  = SizeMode.Fill;
            s.HeightMode = SizeMode.Fit;
            s.Padding    = new EdgeSize(10, 14);
            s.Gap        = 8;
        });

        content.AppendChild(PUI.SectionLabel("GRADIENT BACKGROUNDS", accent));
        content.AppendChild(GradientBar(PColor.FromHex("#FF6B6B"), PColor.FromHex("#FFB86B"), 0.72f));
        content.AppendChild(GradientBar(PColor.FromHex("#6B8FFF"), PColor.FromHex("#B86BFF"), 0.54f));
        content.AppendChild(GradientBar(PColor.FromHex("#6BFFB8"), PColor.FromHex("#6BD4FF"), 0.88f));

        return PUI.SectionWrap(accent, content);
    }

    private static Node GradientBar(PColor from, PColor to, float fill)
    {
        var track = new Node().WithStyle(s =>
        {
            s.Flow            = Flow.Horizontal;
            s.WidthMode       = SizeMode.Fill;
            s.HeightMode      = SizeMode.Fixed; s.Height = 8;
            s.BackgroundColor = PColor.FromHex("#0A0A1E");
            s.BorderRadius    = 4;
            s.ClipContent     = true;
        });

        track.AppendChild(new Node().WithStyle(s =>
        {
            s.WidthMode             = SizeMode.Fill;
            s.HeightMode            = SizeMode.Fill;
            s.BackgroundColor       = from;
            s.BackgroundGradientEnd = to;
            s.Flow                  = Flow.Horizontal;
            s.Opacity               = fill;   // use opacity to represent "fill %" visually
        }));

        return track;
    }

    private static Node BuildOverviewButton()
    {
        // Footer section — same SectionWrap style, button centered inside
        var accent = PColor.FromHex("#9955DD");

        var content = new Node().WithStyle(s =>
        {
            s.Flow       = Flow.Horizontal;
            s.WidthMode  = SizeMode.Fill;
            s.HeightMode = SizeMode.Fit;
            s.Padding    = new EdgeSize(10, 14);
        });

        // Left spacer
        content.AppendChild(new Node().WithStyle(s =>
        {
            s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
        }));

        // The button pill
        content.AppendChild(new Node().WithId("btn-overview").WithText("Feature Overview").WithStyle(s =>
        {
            s.WidthMode             = SizeMode.Fit;
            s.HeightMode            = SizeMode.Fit;
            s.BackgroundColor       = PColor.FromHex("#1E0A38");
            s.BackgroundGradientEnd = PColor.FromHex("#380A1E");
            s.Flow                  = Flow.Horizontal;
            s.BorderRadius          = 6;
            s.BorderColor           = accent.WithOpacity(0.55f);
            s.BorderWidth           = 1;
            s.Padding               = new EdgeSize(6, 16);
            s.FontSize              = 11f;
            s.Bold                  = true;
            s.Color                 = PColor.FromHex("#CC88FF");
        }));

        // Right spacer
        content.AppendChild(new Node().WithStyle(s =>
        {
            s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
        }));

        return PUI.SectionWrap(accent, content);
    }

    private static Node BuildAnimatedBanner()
    {
        var banner = new Node().WithId("animated-banner").WithStyle(s =>
        {
            s.WidthMode             = SizeMode.Fill;
            s.HeightMode            = SizeMode.Fixed; s.Height = 34;
            s.BackgroundColor       = PColor.FromHex("#1A0A2E");
            s.BackgroundGradientEnd = PColor.FromHex("#2E0A1A");
            s.Flow                  = Flow.Horizontal;
            // No border radius — this is a full-width section strip
        });

        // Left accent bar (3px, animated color)
        banner.AppendChild(new Node().WithId("banner-accent").WithStyle(s =>
        {
            s.WidthMode       = SizeMode.Fixed; s.Width  = 3;
            s.HeightMode      = SizeMode.Fill;
            s.BackgroundColor = PColor.FromHex("#AA66FF").WithOpacity(0.7f);
        }));

        banner.AppendChild(new Node().WithText("Animated gradient — hues cycle each frame — zero ImGui widgets.").WithStyle(s =>
        {
            s.WidthMode  = SizeMode.Fill;
            s.HeightMode = SizeMode.Fill;
            s.FontSize   = 10.5f;
            s.Color      = PColor.FromHex("#CC99FF");
            s.TextAlign  = TextAlign.Center;
            s.Padding    = new EdgeSize(0, 12);
        }));

        return banner;
    }

    private void UpdateAnimatedNode()
    {
        var banner = _root.FindById("animated-banner");
        if (banner == null) return;

        float hue  = (_animTime * 30f) % 360f;
        float hue2 = (hue + 120f)      % 360f;
        float hue3 = (hue + 60f)       % 360f;

        banner.Style.BackgroundColor       = HsvToRgb(hue,  0.55f, 0.18f);
        banner.Style.BackgroundGradientEnd = HsvToRgb(hue2, 0.60f, 0.14f);

        var accentBar = _root.FindById("banner-accent");
        if (accentBar != null)
            accentBar.Style.BackgroundColor = HsvToRgb(hue3, 0.80f, 0.55f).WithOpacity(0.85f);

        banner.MarkDirty();
    }

    private static PColor HsvToRgb(float h, float s, float v)
    {
        float c = v * s;
        float x = c * (1f - MathF.Abs((h / 60f) % 2f - 1f));
        float m = v - c;
        float r, g, b;
        if      (h < 60)  { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else              { r = c; g = 0; b = x; }
        return new PColor((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    public void Dispose()
    {
        _surface.Dispose();
        _textures.Dispose();
        _layout.Dispose();
        _renderer.Dispose();
    }
}
