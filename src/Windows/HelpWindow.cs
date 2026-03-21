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

// ── Data types ────────────────────────────────────────────────────────────────

[Flags] internal enum CtrlFlags { None = 0, Speed = 1, Scale = 2, Intensity = 4, C1 = 8, C2 = 16 }

internal record CodeLine(string Text, string ColorHex);

internal record FeatureSection(
    string Title,
    string AccentHex,
    string Tagline,
    string SummaryLine1,
    string SummaryLine2,
    CodeLine[] Code,
    string Case1, string Case2, string Case3,
    string ProTip);

// ── Window ────────────────────────────────────────────────────────────────────

public sealed class HelpWindow : IDisposable
{
    public bool IsVisible;

    private const int HeaderH  = 72;
    private const int SidebarW = 235;

    private readonly ITextureProvider _texProvider;
    private readonly LayoutEngine     _layout   = new();
    private readonly SkiaRenderer     _renderer = new();

    // Header (animated, re-rendered ~20fps)
    private RenderSurface?  _hdrSurf;
    private TextureManager? _hdrTex;
    private ImTextureID?    _hdrHandle;
    private int             _hdrW;
    private float           _lastHdrRender = -99f;

    // Detail card
    private RenderSurface?  _detSurf;
    private TextureManager? _detTex;
    private ImTextureID?    _detHandle;
    private int             _detW, _detH;
    private bool            _detDirty    = true;
    private float           _lastDetRender = -99f;

    private int         _selectedIdx = 0;
    private string      _search      = "";
    private List<int>   _visible     = new();
    private float       _animTime;
    private bool        _previewAnim = true;
    private System.Numerics.Vector2? _windowPos;

    // Per-section live tuning params (42 entries, one per feature section)
    private const int NSections = 42;
    private readonly float[]   _secScale     = new float[NSections];
    private readonly float[]   _secSpeed     = new float[NSections];
    private readonly float[]   _secIntensity = new float[NSections];
    private readonly Vector4[] _secC1        = new Vector4[NSections];
    private readonly Vector4[] _secC2        = new Vector4[NSections];

    // Separate mini surface for the live example strip at the bottom of the detail pane
    private const int ControlsH = 120;   // height reserved at bottom of detail area for per-feature controls
    private const int ContentH  = 750;   // fixed render height for detail Panache surface (scrollable)

    private static readonly FeatureSection[] Sections = BuildSections();

    public HelpWindow(ITextureProvider texProvider)
    {
        _texProvider = texProvider;
        for (int i = 0; i < NSections; i++)
        {
            _secScale[i]     = 1.0f;
            _secSpeed[i]     = 1.0f;
            _secIntensity[i] = 0.35f;
            _secC1[i]        = new Vector4(0.42f, 0.83f, 1.00f, 1f);
            _secC2[i]        = new Vector4(0.50f, 0.30f, 1.00f, 1f);
        }
        RebuildVisible();
    }

    // ── Draw ─────────────────────────────────────────────────────────────────

    public void Draw()
    {
        if (!IsVisible) return;

        ImGui.SetNextWindowSize(new Vector2(900, 640), ImGuiCond.FirstUseEver);
        if (_windowPos.HasValue)
            ImGui.SetNextWindowPos(_windowPos.Value, ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoTitleBar
                  | ImGuiWindowFlags.NoScrollbar
                  | ImGuiWindowFlags.NoScrollWithMouse;
        if (!ImGui.Begin("##panacheui_help", ref IsVisible, flags))
        {
            ImGui.End();
            return;
        }

        if (!_windowPos.HasValue)
            _windowPos = ImGui.GetWindowPos();

        _animTime += ImGui.GetIO().DeltaTime;

        // Header strip
        var winAvail = ImGui.GetContentRegionAvail();
        int hdrW     = Math.Max(100, (int)winAvail.X);

        bool hdrSizeChanged = hdrW != _hdrW || _hdrSurf == null;
        bool hdrTimeExpired = (_animTime - _lastHdrRender) > 0.05f;

        if (hdrSizeChanged)
        {
            _hdrW = hdrW;
            _hdrSurf?.Dispose(); _hdrTex?.Dispose();
            _hdrSurf = new RenderSurface(hdrW, HeaderH);
            _hdrTex  = new TextureManager(_texProvider);
        }

        if (hdrSizeChanged || hdrTimeExpired)
        {
            _lastHdrRender = _animTime;
            RenderHeader(_hdrSurf!, hdrW);
            _hdrHandle = _hdrTex!.Upload(_hdrSurf!);
        }

        if (_hdrHandle.HasValue)
        {
            var hdrPos = ImGui.GetCursorScreenPos();
            ImGui.Image(_hdrHandle.Value, new Vector2(hdrW, HeaderH));

            // Drag the window by clicking anywhere in the header
            var mouse = ImGui.GetMousePos();
            float mx = mouse.X - hdrPos.X;
            float my = mouse.Y - hdrPos.Y;
            if (mx >= 0 && mx < hdrW && my >= 0 && my < HeaderH
             && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                var delta = ImGui.GetIO().MouseDelta;
                _windowPos = (_windowPos ?? ImGui.GetWindowPos()) + delta;
            }
        }

        // Body: sidebar + detail
        var bodyAvail = ImGui.GetContentRegionAvail();

        // Sidebar background
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.04f, 0.04f, 0.10f, 1f));
        ImGui.BeginChild("##psidebar", new Vector2(SidebarW, bodyAvail.Y), false, ImGuiWindowFlags.None);
        ImGui.PopStyleColor();
        DrawSidebar();
        ImGui.EndChild();

        ImGui.SameLine();

        // Detail column — own child so height and layout are precise
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.04f, 0.04f, 0.10f, 1f));
        ImGui.BeginChild("##pdetail", new Vector2(0, bodyAvail.Y), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleColor();

        var innerAvail = ImGui.GetContentRegionAvail();
        int detW     = Math.Max(100, (int)innerAvail.X);
        int detPaneH = Math.Max(60,  (int)innerAvail.Y - ControlsH);
        int detH     = ContentH; // render at fixed tall height; pane scrolls into it

        bool detSizeChanged = _detSurf == null || detW != _detW || detH != _detH;
        bool detTimeExpired = _previewAnim && (_animTime - _lastDetRender) > 0.033f;

        if (detSizeChanged || _detDirty || detTimeExpired)
        {
            _detW = detW; _detH = detH;
            if (detSizeChanged)
            {
                _detSurf?.Dispose(); _detTex?.Dispose();
                _detSurf = new RenderSurface(detW, detH);
                _detTex  = new TextureManager(_texProvider);
            }
            _lastDetRender = _animTime;
            RenderDetail(_detSurf!, detW, detH);
            _detHandle = _detTex!.Upload(_detSurf!);
            _detDirty = false;
        }

        // Scrollable viewport into the full-height Panache surface
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg,   new Vector4(0.04f, 0.04f, 0.10f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab,        new Vector4(0.28f, 0.22f, 0.50f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.38f, 0.30f, 0.65f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive,  new Vector4(0.50f, 0.40f, 0.80f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 8f);
        ImGui.BeginChild("##pscroll", new Vector2(detW, detPaneH), false, ImGuiWindowFlags.None);
        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar();
        if (_detHandle.HasValue)
            ImGui.Image(_detHandle.Value, new Vector2(detW, detH));
        ImGui.EndChild();

        // Controls strip — always visible at bottom, never scrolls
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.04f, 0.03f, 0.10f, 1f));
        ImGui.BeginChild("##pcontrols", new Vector2(detW, ControlsH), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleColor();
        DrawControlsStrip(detW);
        ImGui.EndChild();

        ImGui.EndChild(); // ##pdetail
        ImGui.End();
    }

    // ── Sidebar ───────────────────────────────────────────────────────────────

    private void DrawSidebar()
    {
        ImGui.Spacing();

        // Search box
        ImGui.PushStyleColor(ImGuiCol.FrameBg,        new Vector4(0.08f, 0.08f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.12f, 0.12f, 0.24f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive,  new Vector4(0.16f, 0.16f, 0.30f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8, 6));
        ImGui.SetNextItemWidth(SidebarW - 16);

        string s = _search;
        if (ImGui.InputTextWithHint("##psearch", "Search...", ref s, 128))
        {
            _search = s;
            RebuildVisible();
        }

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(3);

        ImGui.Spacing();

        ImGui.Spacing();

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,  new Vector2(0, 3));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 6));

        foreach (int idx in _visible)
        {
            var sec    = Sections[idx];
            var accent = PColor.FromHex(sec.AccentHex);
            float ar   = accent.R / 255f, ag = accent.G / 255f, ab = accent.B / 255f;

            bool selected = _selectedIdx == idx;

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(ar, ag, ab, 1f));
            ImGui.Text("  ");
            ImGui.PopStyleColor();
            ImGui.SameLine(0, 0);

            ImGui.PushStyleColor(ImGuiCol.Header,        new Vector4(ar*0.22f, ag*0.22f, ab*0.22f, 0.85f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(ar*0.30f, ag*0.30f, ab*0.30f, 0.95f));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive,  new Vector4(ar*0.40f, ag*0.40f, ab*0.40f, 1.00f));
            ImGui.PushStyleColor(ImGuiCol.Text, selected
                ? new Vector4(1f, 1f, 1f, 1f)
                : new Vector4(0.72f, 0.72f, 0.82f, 1f));

            if (ImGui.Selectable($"{idx + 1:D2}  {sec.Title}##psel{idx}", selected,
                    ImGuiSelectableFlags.None, new Vector2(SidebarW - 20, 0)))
            {
                _selectedIdx = idx;
                _detDirty    = true;
            }

            ImGui.PopStyleColor(4);
        }

        if (_visible.Count == 0)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.45f, 0.45f, 0.55f, 1f));
            ImGui.TextWrapped("No features match.");
            ImGui.PopStyleColor();
        }

        ImGui.PopStyleVar(2);
    }

    private void RebuildVisible()
    {
        _visible.Clear();
        string q = _search.Trim();
        for (int i = 0; i < Sections.Length; i++)
        {
            var sec = Sections[i];
            if (q.Length == 0
             || sec.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
             || sec.Tagline.Contains(q, StringComparison.OrdinalIgnoreCase)
             || sec.SummaryLine1.Contains(q, StringComparison.OrdinalIgnoreCase)
             || sec.Case1.Contains(q, StringComparison.OrdinalIgnoreCase)
             || sec.Case2.Contains(q, StringComparison.OrdinalIgnoreCase)
             || sec.Case3.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                _visible.Add(i);
            }
        }
        if (_visible.Count > 0 && !_visible.Contains(_selectedIdx))
        {
            _selectedIdx = _visible[0];
            _detDirty    = true;
        }
    }

    // ── Header renderer ───────────────────────────────────────────────────────

    private void RenderHeader(RenderSurface surf, int w)
    {
        float hue = (_animTime * 18f) % 360f;
        var   c1  = HsvToRgb(hue,           0.70f, 0.28f);
        var   c2  = HsvToRgb((hue + 90f) % 360f, 0.80f, 0.22f);

        // Header gradient ends at Theme.Panel — bleeds into detail section below
        var root = new Node().WithStyle(s =>
        {
            s.WidthMode             = SizeMode.Fixed; s.Width  = w;
            s.HeightMode            = SizeMode.Fixed; s.Height = HeaderH;
            s.Flow                  = Flow.Horizontal;
            s.BackgroundColor       = c1;
            s.BackgroundGradientEnd = Theme.Panel;
            s.Padding               = new EdgeSize(0, 20);
            s.Gap                   = 10;
        });

        // Left accent bar (animated color)
        root.AppendChild(new Node().WithStyle(s =>
        {
            s.WidthMode       = SizeMode.Fixed; s.Width  = 3;
            s.HeightMode      = SizeMode.Fill;
            s.BackgroundColor = c2.WithOpacity(0.85f);
        }));

        root.AppendChild(new Node().WithText("PanacheUI").WithStyle(s =>
        {
            s.WidthMode        = SizeMode.Fit;
            s.HeightMode       = SizeMode.Fill;
            s.FontSize         = 24f;
            s.Bold             = true;
            s.Color            = PColor.White;
            s.TextOutlineColor = PColor.Black.WithOpacity(0.45f);
            s.TextOutlineSize  = 1.2f;
        }));

        root.AppendChild(new Node().WithText(" — Feature Reference").WithStyle(s =>
        {
            s.WidthMode  = SizeMode.Fit;
            s.HeightMode = SizeMode.Fill;
            s.FontSize   = 12f;
            s.Color      = PColor.White.WithOpacity(0.55f);
        }));

        root.AppendChild(new Node().WithStyle(s => { s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; }));

        root.AppendChild(new Node().WithText($"{Sections.Length} features").WithStyle(s =>
        {
            s.WidthMode       = SizeMode.Fit;
            s.HeightMode      = SizeMode.Fit;
            s.BackgroundColor = PColor.White.WithOpacity(0.10f);
            s.BorderColor     = PColor.White.WithOpacity(0.22f);
            s.BorderWidth     = 1;
            s.BorderRadius    = 10;
            s.Padding         = new EdgeSize(4, 12);
            s.FontSize        = 10.5f;
            s.Color           = PColor.White.WithOpacity(0.80f);
        }));

        var map = _layout.Compute(root, w, HeaderH);
        _renderer.Render(surf.Canvas, root, map, _animTime);
    }

    // ── Detail card renderer ──────────────────────────────────────────────────

    private void RenderDetail(RenderSurface surf, int w, int h)
    {
        var root = PUI.RootNode(w, h);

        if (_visible.Count == 0 || _selectedIdx >= Sections.Length)
        {
            root.AppendChild(new Node().WithText("No results.").WithStyle(s =>
            {
                s.WidthMode  = SizeMode.Fill;
                s.HeightMode = SizeMode.Fill;
                s.FontSize   = 14f;
                s.Color      = Theme.TextSubtle;
                s.TextAlign  = TextAlign.Center;
            }));
            _renderer.Render(surf.Canvas, root, _layout.Compute(root, w, h), _animTime);
            return;
        }

        var sec    = Sections[_selectedIdx];
        var accent = PColor.FromHex(sec.AccentHex);

        // ── Hero — gradient bleeds into Theme.Panel below ─────────────────────
        var hero = new Node().WithStyle(s =>
        {
            s.Flow                  = Flow.Vertical;
            s.WidthMode             = SizeMode.Fill;
            s.HeightMode            = SizeMode.Fixed; s.Height = 100;
            s.BackgroundColor       = accent.WithOpacity(0.22f);
            s.BackgroundGradientEnd = Theme.Panel;
            s.Padding               = new EdgeSize(16, 20, 10, 20);
            s.Gap                   = 5;
            s.ClipContent           = true;
        });

        hero.AppendChild(new Node().WithStyle(s =>
        {
            s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; s.Gap = 10;
        }).WithChildren(
            new Node().WithText(sec.Title).WithStyle(s =>
            {
                s.WidthMode        = SizeMode.Fill;
                s.HeightMode       = SizeMode.Fit;
                s.FontSize         = 24f;
                s.Bold             = true;
                s.Color            = PColor.White;
                s.TextOutlineColor = PColor.Black.WithOpacity(0.55f);
                s.TextOutlineSize  = 1.5f;
            }),
            new Node().WithText($"#{_selectedIdx + 1:D2}").WithStyle(s =>
            {
                s.WidthMode       = SizeMode.Fit; s.HeightMode = SizeMode.Fit;
                s.Margin          = new EdgeSize(4, 0, 0, 0);
                s.BackgroundColor = accent.WithOpacity(0.22f);
                s.BorderColor     = accent.WithOpacity(0.45f);
                s.BorderWidth     = 1; s.BorderRadius = 6;
                s.Padding         = new EdgeSize(3, 9);
                s.FontSize        = 11f; s.Bold = true; s.Color = accent;
            })
        ));

        hero.AppendChild(new Node().WithText(sec.Tagline).WithStyle(s =>
        {
            s.WidthMode  = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
            s.FontSize   = 11.5f;
            s.Color      = accent.WithOpacity(0.80f);
        }));

        root.AppendChild(hero);
        root.AppendChild(PUI.SectionDivider(accent.WithOpacity(0.40f)));

        // ── Body sections ─────────────────────────────────────────────────────
        root.AppendChild(DetailSection("OVERVIEW", accent,
            new Node().WithStyle(s =>
            {
                s.Flow = Flow.Vertical; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; s.Gap = 3;
            }).WithChildren(
                TextLine(sec.SummaryLine1, 12f, PColor.FromHex("#CCCCEE")),
                TextLine(sec.SummaryLine2, 12f, PColor.FromHex("#AAAACC"))
            )
        ));

        root.AppendChild(PUI.SectionDivider(PColor.FromHex("#FFFFFF").WithOpacity(0.04f)));

        // LIVE EXAMPLE
        var example = BuildLiveExample(_selectedIdx, accent,
            _secScale[_selectedIdx], _secSpeed[_selectedIdx], _secIntensity[_selectedIdx],
            SecC1(), SecC2());
        if (example != null)
        {
            root.AppendChild(DetailSection("LIVE EXAMPLE", accent, example));
            root.AppendChild(PUI.SectionDivider(PColor.FromHex("#FFFFFF").WithOpacity(0.04f)));
        }

        root.AppendChild(DetailSection("HOW TO USE", accent, BuildCodeBlock(sec.Code)));
        root.AppendChild(PUI.SectionDivider(PColor.FromHex("#FFFFFF").WithOpacity(0.04f)));
        root.AppendChild(DetailSection("USE CASES", accent, BuildCasePills(sec, accent)));
        root.AppendChild(PUI.SectionDivider(PColor.FromHex("#FFFFFF").WithOpacity(0.04f)));

        // PRO TIP
        root.AppendChild(DetailSection("PRO TIP", accent,
            new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                s.BackgroundColor = accent.WithOpacity(0.07f);
                s.BorderColor = accent.WithOpacity(0.22f); s.BorderWidth = 1; s.BorderRadius = 6;
                s.Padding = new EdgeSize(8, 12); s.Gap = 8;
            }).WithChildren(
                new Node().WithText(">").WithStyle(s =>
                {
                    s.WidthMode = SizeMode.Fit; s.HeightMode = SizeMode.Fit;
                    s.FontSize = 11f; s.Bold = true; s.Color = accent.WithOpacity(0.7f);
                }),
                new Node().WithText(sec.ProTip).WithStyle(s =>
                {
                    s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                    s.FontSize = 11f; s.Italic = true; s.Color = Theme.TextMuted;
                })
            )
        ));

        var map = _layout.Compute(root, w, h);
        _renderer.Render(surf.Canvas, root, map, _animTime);
    }

    // ── Per-section param helpers ─────────────────────────────────────────────

    private PColor SecC1() => new(
        (byte)(_secC1[_selectedIdx].X * 255), (byte)(_secC1[_selectedIdx].Y * 255),
        (byte)(_secC1[_selectedIdx].Z * 255), (byte)(_secC1[_selectedIdx].W * 255));

    private PColor SecC2() => new(
        (byte)(_secC2[_selectedIdx].X * 255), (byte)(_secC2[_selectedIdx].Y * 255),
        (byte)(_secC2[_selectedIdx].Z * 255), (byte)(_secC2[_selectedIdx].W * 255));

    // ── Controls strip — mask-driven, only shows params the effect actually uses ──

    private static CtrlFlags CtrlsFor(int idx) => idx switch
    {
        // Structural / conceptual features
        0  => CtrlFlags.C1 | CtrlFlags.C2,                                                          // Node Tree
        1  => CtrlFlags.None,                                                                        // Box Layout
        2  => CtrlFlags.C1 | CtrlFlags.C2,                                                          // PColor
        3  => CtrlFlags.C1 | CtrlFlags.C2,                                                          // Gradients
        4  => CtrlFlags.Speed | CtrlFlags.C1,                                                        // Typography
        5  => CtrlFlags.C1,                                                                          // Borders
        6  => CtrlFlags.Intensity | CtrlFlags.C1,                                                    // Drop Shadows
        7  => CtrlFlags.Intensity,                                                                   // Opacity
        8  => CtrlFlags.None,                                                                        // Clip Content
        9  => CtrlFlags.None,                                                                        // Dirty Tracking
        10 => CtrlFlags.None,                                                                        // Fluent Builder
        11 => CtrlFlags.None,                                                                        // Texture Pipeline
        // Rendering effects
        12 => CtrlFlags.Speed | CtrlFlags.Scale | CtrlFlags.Intensity | CtrlFlags.C1 | CtrlFlags.C2, // Plasma
        13 => CtrlFlags.Speed | CtrlFlags.Scale | CtrlFlags.Intensity | CtrlFlags.C1 | CtrlFlags.C2, // Perlin Noise
        14 => CtrlFlags.Speed | CtrlFlags.Scale | CtrlFlags.Intensity | CtrlFlags.C1 | CtrlFlags.C2, // Voronoi
        15 => CtrlFlags.Speed | CtrlFlags.Scale | CtrlFlags.Intensity | CtrlFlags.C1,               // Scanline
        16 => CtrlFlags.Speed | CtrlFlags.Scale | CtrlFlags.Intensity | CtrlFlags.C1,               // Dot Matrix
        17 => CtrlFlags.Speed | CtrlFlags.Scale | CtrlFlags.Intensity | CtrlFlags.C1,               // Waveform
        18 => CtrlFlags.Speed | CtrlFlags.Intensity | CtrlFlags.C1,                                 // Shimmer
        19 => CtrlFlags.Speed | CtrlFlags.Intensity | CtrlFlags.C1,                                 // Breathing Pulse
        20 => CtrlFlags.Speed | CtrlFlags.Scale | CtrlFlags.Intensity | CtrlFlags.C1 | CtrlFlags.C2, // Particle Emitter
        21 => CtrlFlags.Speed,                                                                       // Typewriter
        22 => CtrlFlags.Speed,                                                                       // Rolling Number
        23 => CtrlFlags.Speed | CtrlFlags.Intensity | CtrlFlags.C1,                                 // Chase-Light
        24 => CtrlFlags.Intensity | CtrlFlags.C1,                                                   // Hover Lift
        25 => CtrlFlags.Intensity,                                                                   // Press Depress
        26 => CtrlFlags.Speed | CtrlFlags.Intensity | CtrlFlags.C1,                                 // Ripple
        27 => CtrlFlags.None,                                                                        // Hover Tooltip
        28 => CtrlFlags.Speed | CtrlFlags.Intensity | CtrlFlags.C1 | CtrlFlags.C2,                   // Magnetic Neighbor
        29 => CtrlFlags.Intensity | CtrlFlags.C1,                                                    // Accordion (intensity = expand amount)
        30 => CtrlFlags.Speed | CtrlFlags.Intensity | CtrlFlags.C1,                                 // Cross-Panel Spotlight
        31 => CtrlFlags.Intensity,                                                                   // Shared Value Binding (intensity = fill %)
        32 => CtrlFlags.Speed | CtrlFlags.Intensity | CtrlFlags.C1,                                 // Staggered Entrance
        33 => CtrlFlags.Speed | CtrlFlags.Intensity | CtrlFlags.C1 | CtrlFlags.C2,                  // Specular Light
        34 => CtrlFlags.Intensity | CtrlFlags.C1 | CtrlFlags.C2,                                      // Rim Lighting
        35 => CtrlFlags.Intensity | CtrlFlags.C1,                                                   // AO Corners
        36 => CtrlFlags.Intensity | CtrlFlags.C1 | CtrlFlags.C2,                                    // Bloom
        37 => CtrlFlags.Speed | CtrlFlags.Intensity | CtrlFlags.C1,                                  // Text Wave
        38 => CtrlFlags.Speed | CtrlFlags.Intensity,                                                 // Shake
        39 => CtrlFlags.Speed | CtrlFlags.Scale | CtrlFlags.Intensity | CtrlFlags.C1 | CtrlFlags.C2, // Heat Haze
        40 => CtrlFlags.Speed | CtrlFlags.C1,                                                        // Slide Transition
        41 => CtrlFlags.Speed | CtrlFlags.Intensity | CtrlFlags.C2,                                 // Card Flip
        _  => CtrlFlags.Speed | CtrlFlags.Scale | CtrlFlags.Intensity | CtrlFlags.C1 | CtrlFlags.C2,
    };

    private static float IntensityMax(int idx) => idx switch
    {
        36 => 3.0f,  // Bloom — glow can greatly exceed 1
        _ => 1.0f,
    };

    private void DrawControlsStrip(int detW)
    {
        int idx    = _selectedIdx;
        var sec    = Sections[idx];
        var accent = PColor.FromHex(sec.AccentHex);
        float ar   = accent.R / 255f, ag = accent.G / 255f, ab = accent.B / 255f;
        var mask   = CtrlsFor(idx);

        const float LM = 12f;   // left margin
        const float LW = 80f;   // label column width

        // Background + top accent line
        var stripPos = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(
            stripPos, stripPos + new Vector2(detW, ControlsH),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.04f, 0.03f, 0.10f, 1f)));
        ImGui.GetWindowDrawList().AddLine(
            stripPos, stripPos + new Vector2(detW, 0),
            ImGui.ColorConvertFloat4ToU32(new Vector4(ar * 0.6f, ag * 0.6f, ab * 0.6f, 0.8f)), 1f);

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 7);

        // Title + animate toggle
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + LM);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(ar, ag, ab, 0.85f));
        ImGui.Text($"{sec.Title} — Parameters");
        ImGui.PopStyleColor();
        ImGui.SameLine(0, 12);
        ImGui.PushStyleColor(ImGuiCol.CheckMark,      new Vector4(ar, ag, ab, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg,        new Vector4(0.08f, 0.08f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.12f, 0.12f, 0.24f, 1f));
        bool anim = _previewAnim;
        if (ImGui.Checkbox($"##anim{idx}", ref anim)) { _previewAnim = anim; _detDirty = true; }
        ImGui.PopStyleColor(3);
        ImGui.SameLine(0, 4);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(ar, ag, ab, _previewAnim ? 1f : 0.45f));
        ImGui.Text(_previewAnim ? "Animating" : "Animate");
        ImGui.PopStyleColor();

        if (mask == CtrlFlags.None)
        {
            ImGui.SetCursorPosX(LM);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.40f, 0.40f, 0.50f, 1f));
            ImGui.Text("No adjustable live parameters for this feature.");
            ImGui.PopStyleColor();
            return;
        }

        // Slider styles
        ImGui.PushStyleColor(ImGuiCol.FrameBg,          new Vector4(0.08f, 0.08f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered,   new Vector4(0.12f, 0.12f, 0.24f, 1f));
        ImGui.PushStyleColor(ImGuiCol.SliderGrab,       new Vector4(ar * 0.7f, ag * 0.7f, ab * 0.7f, 1f));
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, new Vector4(ar, ag, ab, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,  new Vector2(4, 2));

        float sliderW = detW - LM - LW - 16f;

        if (mask.HasFlag(CtrlFlags.Speed))
        {
            ImGui.SetCursorPosX(LM);
            ImGui.Text("Speed"); ImGui.SameLine(LM + LW);
            ImGui.SetNextItemWidth(sliderW);
            float sp = _secSpeed[idx];
            if (ImGui.SliderFloat($"##sp{idx}", ref sp, 0f, 3f, "%.2f")) { _secSpeed[idx] = sp; _detDirty = true; }
        }

        if (mask.HasFlag(CtrlFlags.Scale))
        {
            ImGui.SetCursorPosX(LM);
            ImGui.Text("Scale"); ImGui.SameLine(LM + LW);
            ImGui.SetNextItemWidth(sliderW);
            float sc = _secScale[idx];
            if (ImGui.SliderFloat($"##sc{idx}", ref sc, 0.1f, 5f, "%.2f")) { _secScale[idx] = sc; _detDirty = true; }
        }

        if (mask.HasFlag(CtrlFlags.Intensity))
        {
            ImGui.SetCursorPosX(LM);
            ImGui.Text("Intensity"); ImGui.SameLine(LM + LW);
            ImGui.SetNextItemWidth(sliderW);
            float it = _secIntensity[idx];
            float itMax = IntensityMax(idx);
            if (ImGui.SliderFloat($"##it{idx}", ref it, 0f, itMax, "%.2f")) { _secIntensity[idx] = it; _detDirty = true; }
        }

        bool hasC1 = mask.HasFlag(CtrlFlags.C1), hasC2 = mask.HasFlag(CtrlFlags.C2);
        if (hasC1 || hasC2)
        {
            ImGui.SetCursorPosX(LM);
            ImGui.Text("Color"); ImGui.SameLine(LM + LW);
            if (hasC1)
            {
                var c1 = _secC1[idx];
                if (ImGui.ColorEdit4($"##c1{idx}", ref c1, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                    { _secC1[idx] = c1; _detDirty = true; }
                if (hasC2) ImGui.SameLine(0, 4);
            }
            if (hasC2)
            {
                var c2 = _secC2[idx];
                if (ImGui.ColorEdit4($"##c2{idx}", ref c2, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                    { _secC2[idx] = c2; _detDirty = true; }
            }
        }

        // Reset — bottom-right corner of the strip
        ImGui.SetCursorPos(new Vector2(detW - 58f, ControlsH - 26f));
        if (ImGui.SmallButton($"Reset##{idx}"))
        {
            _secScale[idx] = 1f; _secSpeed[idx] = 1f; _secIntensity[idx] = 0.35f;
            _secC1[idx] = new Vector4(0.42f, 0.83f, 1.00f, 1f);
            _secC2[idx] = new Vector4(0.50f, 0.30f, 1.00f, 1f);
            _detDirty = true;
        }

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(4);
    }

    // ── Live examples ─────────────────────────────────────────────────────────

    private Node? BuildLiveExample(int idx, PColor accent,
        float lscale, float lspeed, float lintensity, PColor lc1, PColor lc2)
    {
        return idx switch
        {
            // 0: Node Tree — parent box with two visible children
            0 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Vertical; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                s.BackgroundColor = accent.WithOpacity(0.08f); s.BorderRadius = 6;
                s.BorderColor = accent.WithOpacity(0.25f); s.BorderWidth = 1;
                s.Padding = new EdgeSize(8); s.Gap = 6;
            }).WithChildren(
                TextLine("root  (parent node)", 10f, accent.WithOpacity(0.7f)),
                new Node().WithStyle(s =>
                {
                    s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; s.Gap = 6;
                }).WithChildren(
                    BoxNode("child A", accent), BoxNode("child B", accent.WithOpacity(0.6f))
                )
            ),

            // 1: Box Layout — fill vs fit vs fixed side-by-side
            1 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; s.Gap = 8;
            }).WithChildren(
                LabelledBox("Fill", SizeMode.Fill, 0, accent),
                LabelledBox("Fit",  SizeMode.Fit,  0, accent.WithOpacity(0.7f)),
                LabelledBox("Fixed 60px", SizeMode.Fixed, 60, accent.WithOpacity(0.5f))
            ),

            // 2: PColor — opacity swatches
            2 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; s.Gap = 6;
            }).WithChildren(
                Swatch(accent, 1.0f, "100%"), Swatch(accent, 0.70f, "70%"),
                Swatch(accent, 0.45f, "45%"), Swatch(accent, 0.20f, "20%"),
                Swatch(accent, 0.08f, "8%")
            ),

            // 3: Gradients — three gradient bars
            3 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Vertical; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; s.Gap = 6;
            }).WithChildren(
                GradientStrip(PColor.FromHex("#FF6B6B"), PColor.FromHex("#FFB86B")),
                GradientStrip(PColor.FromHex("#6B8FFF"), PColor.FromHex("#B86BFF")),
                GradientStrip(PColor.FromHex("#6BFFB8"), PColor.FromHex("#6BD4FF"))
            ),

            // 4: Typography — size / weight / alignment / outline
            4 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Vertical; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; s.Gap = 4;
            }).WithChildren(
                new Node().WithText("Bold  24px  Centered").WithStyle(s =>
                {
                    s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                    s.FontSize = 24f; s.Bold = true;
                    s.Color = accent; s.TextAlign = TextAlign.Center;
                }),
                TextLine("Normal  12px  Left-aligned", 12f, Theme.TextMuted),
                new Node().WithText("Outline  14px  Right").WithStyle(s =>
                {
                    s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                    s.FontSize = 14f; s.Bold = true;
                    s.Color = accent; s.TextAlign = TextAlign.Right;
                    s.TextOutlineColor = PColor.Black.WithOpacity(0.8f); s.TextOutlineSize = 1.5f;
                })
            ),

            // 5: Borders — different radii
            5 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; s.Gap = 8;
            }).WithChildren(
                BorderBox("r=0",  0f, accent),
                BorderBox("r=6",  6f, accent),
                BorderBox("r=12", 12f, accent),
                BorderBox("pill", 20f, accent)
            ),

            // 6: Drop Shadows — boxes with varying blur
            6 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; s.Gap = 16;
                s.Padding = new EdgeSize(10);
            }).WithChildren(
                ShadowBox("blur 4",  accent, 4f, 2f),
                ShadowBox("blur 10", accent, 10f, 3f),
                ShadowBox("blur 18", accent, 18f, 4f)
            ),

            // 7: Opacity — same box at different opacity levels
            7 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; s.Gap = 8;
            }).WithChildren(
                OpacityBox(accent, 1.0f, "1.0"),
                OpacityBox(accent, 0.65f, "0.65"),
                OpacityBox(accent, 0.35f, "0.35"),
                OpacityBox(accent, 0.12f, "0.12")
            ),

            // 8: Clip Content — fill node clipped vs. overflowing
            8 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; s.Gap = 12;
            }).WithChildren(
                ClipDemo(accent, true,  "ClipContent = true"),
                ClipDemo(accent, false, "ClipContent = false")
            ),

            // 9: Dirty Tracking — concept illustration
            9 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; s.Gap = 8;
            }).WithChildren(
                DirtyBadge("node", accent, false),
                new Node().WithText("→").WithStyle(s => { s.WidthMode = SizeMode.Fit; s.HeightMode = SizeMode.Fit; s.FontSize = 14f; s.Color = Theme.TextSubtle; }),
                DirtyBadge("MarkDirty()", accent, true),
                new Node().WithText("→").WithStyle(s => { s.WidthMode = SizeMode.Fit; s.HeightMode = SizeMode.Fit; s.FontSize = 14f; s.Color = Theme.TextSubtle; }),
                DirtyBadge("re-render", PColor.FromHex("#6BFFB8"), false)
            ),

            // 10: Fluent Builder — show the node it builds
            10 => new Node().WithId("demo").WithStyle(s =>
            {
                s.Flow = Flow.Vertical; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                s.BackgroundColor = accent.WithOpacity(0.10f); s.BorderRadius = 8;
                s.BorderColor = accent.WithOpacity(0.30f); s.BorderWidth = 1;
                s.Padding = new EdgeSize(10, 14); s.Gap = 5;
            }).WithChildren(
                TextLine("Built with .WithId().WithStyle().WithChildren()", 11f, accent),
                TextLine("Every method returns 'this' — fully chainable.", 10.5f, Theme.TextMuted)
            ),

            // 11: Texture Pipeline — visual diagram
            11 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; s.Gap = 6;
            }).WithChildren(
                PipelineStep("SKCanvas",    PColor.FromHex("#FF9B6B")),
                Arrow(),
                PipelineStep("ReadPixels",  PColor.FromHex("#9B6BFF")),
                Arrow(),
                PipelineStep("CreateFromRaw", PColor.FromHex("#6BDDFF")),
                Arrow(),
                PipelineStep("ImGui.Image", PColor.FromHex("#6BFFB8"))
            ),

            // 12: Plasma / Lava Lamp — two panels side by side, different palette offsets
            12 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 80; s.Gap = 8;
            }).WithChildren(
                PlasmaPanel("A", lc1, lspeed, lscale, lintensity),
                PlasmaPanel("B", lc2, lspeed, lscale, lintensity)
            ),

            // 13: Perlin Noise Background — three panels showing static / slow / fast noise
            13 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 72; s.Gap = 8;
            }).WithChildren(
                NoisePanel("Static",  lc1, 0f,       lscale, lintensity),
                NoisePanel("Slow",    lc1, lspeed * 0.3f, lscale, lintensity),
                NoisePanel("Fast",    lc1, lspeed * 1.2f, lscale, lintensity)
            ),

            // 14: Voronoi — three panels with different hue offsets
            14 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 72; s.Gap = 8;
            }).WithChildren(
                EffectPanel("Warm",  lc1, NodeEffect.Voronoi, lspeed * 0.3f, lscale, lintensity),
                EffectPanel("Cool",  lc2, NodeEffect.Voronoi, lspeed * 0.4f, lscale, lintensity),
                EffectPanel("Mix",   new PColor((byte)((lc1.R + lc2.R) / 2), (byte)((lc1.G + lc2.G) / 2), (byte)((lc1.B + lc2.B) / 2), 255), NodeEffect.Voronoi, lspeed * 0.5f, lscale, lintensity)
            ),

            // 15: Scanline — two panels showing different spacing
            15 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 60; s.Gap = 8;
            }).WithChildren(
                EffectPanel("Fine",   lc1, NodeEffect.Scanline, 0f, 0.8f, lintensity),
                EffectPanel("Medium", lc1, NodeEffect.Scanline, 0f, 1.5f, lintensity),
                EffectPanel("Coarse", lc1, NodeEffect.Scanline, 0f, 3.0f, lintensity)
            ),

            // 16: Dot Matrix — three panels with different speeds
            16 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 68; s.Gap = 8;
            }).WithChildren(
                EffectPanel("Slow",  lc1, NodeEffect.DotMatrix, 0.4f, lscale, lintensity),
                EffectPanel("Med",   lc1, NodeEffect.DotMatrix, 1.0f, lscale, lintensity),
                EffectPanel("Fast",  lc1, NodeEffect.DotMatrix, 2.5f, lscale, lintensity)
            ),

            // 17: Waveform — two panels side by side
            17 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 68; s.Gap = 8;
            }).WithChildren(
                EffectPanel("A", lc1, NodeEffect.Waveform, lspeed * 0.5f, lscale, lintensity),
                EffectPanel("B", lc2, NodeEffect.Waveform, lspeed * 1.8f, lscale, lintensity)
            ),

            // 18: Shimmer — button-like box
            18 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 44; s.Gap = 8;
            }).WithChildren(
                EffectPanel("Slow",   lc1, NodeEffect.Shimmer, 0.3f, lscale, lintensity),
                EffectPanel("Normal", lc1, NodeEffect.Shimmer, 1.0f, lscale, lintensity),
                EffectPanel("Fast",   lc1, NodeEffect.Shimmer, 3.0f, lscale, lintensity)
            ),

            // 19: Pulse Glow — three colored orbs
            19 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 60; s.Gap = 8;
            }).WithChildren(
                EffectPanel("Alert",  lc1, NodeEffect.PulseGlow, lspeed * 0.8f, 1f, lintensity),
                EffectPanel("Notify", lc2, NodeEffect.PulseGlow, lspeed * 0.5f, 1f, lintensity),
                EffectPanel("Active", accent, NodeEffect.PulseGlow, lspeed * 1.2f, 1f, lintensity)
            ),

            // 20: Particles — two panels
            20 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 80; s.Gap = 8;
            }).WithChildren(
                EffectPanel("A", lc1, NodeEffect.Particles, lspeed * 0.6f, lscale, lintensity),
                EffectPanel("B", lc2, NodeEffect.Particles, lspeed * 1.4f, lscale, lintensity)
            ),

            // 21: Typewriter — text panel with typewriter effect
            21 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Vertical; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 56; s.Gap = 6;
            }).WithChildren(
                new Node().WithText("Loading system modules...").WithStyle(s =>
                {
                    s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fill;
                    s.BackgroundColor = PColor.FromHex("#050510");
                    s.BorderRadius = 6; s.BorderColor = accent.WithOpacity(0.30f); s.BorderWidth = 1;
                    s.Padding = new EdgeSize(10, 14);
                    s.FontSize = 13f; s.Color = accent;
                    s.Effect = NodeEffect.TypewriterText; s.EffectSpeed = lspeed;
                })
            ),

            // 22: Rolling Counter — three counters
            22 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 52; s.Gap = 8;
            }).WithChildren(
                CounterNode("9999",  PColor.FromHex("#6BDDFF"), 1.0f),
                CounterNode("12345", PColor.FromHex("#FF9B6B"), 0.7f),
                CounterNode("500",   PColor.FromHex("#B8FF6B"), 1.3f)
            ),

            // 23: Chase Light — border animation demo
            23 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 56; s.Gap = 8;
            }).WithChildren(
                EffectPanel("Slow",  lc1, NodeEffect.ChaseLight, 0.4f, 1f, lintensity),
                EffectPanel("Med",   lc1, NodeEffect.ChaseLight, 1.0f, 1f, lintensity),
                EffectPanel("Fast",  lc1, NodeEffect.ChaseLight, 2.5f, 1f, lintensity)
            ),

            // 24: Hover Lift — three cards
            24 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 52; s.Gap = 8;
            }).WithChildren(
                EffectPanel("Card A", lc1, NodeEffect.HoverLift, 0f, 1f, lintensity),
                EffectPanel("Card B", lc2, NodeEffect.HoverLift, 0f, 1f, lintensity),
                EffectPanel("Card C", accent, NodeEffect.HoverLift, 0f, 1f, lintensity)
            ),

            // 25: Press Depress — three buttons
            25 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 44; s.Gap = 8;
            }).WithChildren(
                EffectPanel("OK",     lc1, NodeEffect.PressDepress, 0f, 1f, lintensity),
                EffectPanel("Cancel", lc2, NodeEffect.PressDepress, 0f, 1f, lintensity),
                EffectPanel("Apply",  accent, NodeEffect.PressDepress, 0f, 1f, lintensity)
            ),

            // 26: Ripple — click target boxes
            26 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 56; s.Gap = 8;
            }).WithChildren(
                EffectPanel("Click me!", lc1, NodeEffect.Ripple, 0f, 1f, lintensity),
                EffectPanel("Tap here",  lc2, NodeEffect.Ripple, 0f, 1f, lintensity)
            ),

            // 27: Hover Tooltip — card with description
            27 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Vertical; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fit; s.Gap = 6; s.Padding = new EdgeSize(4);
            }).WithChildren(
                new Node().WithText("Hover Target Card").WithStyle(s =>
                {
                    s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                    s.BackgroundColor = accent.WithOpacity(0.15f);
                    s.BorderColor = accent.WithOpacity(0.40f); s.BorderWidth = 1; s.BorderRadius = 6;
                    s.Padding = new EdgeSize(8, 14); s.FontSize = 12f; s.Color = accent; s.TextAlign = TextAlign.Center;
                }),
                new Node().WithText("[Tooltip]: Assign to OnMouseEnter/Leave events").WithStyle(s =>
                {
                    s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                    s.BackgroundColor = PColor.FromHex("#FFD96B").WithOpacity(0.12f);
                    s.BorderColor = PColor.FromHex("#FFD96B").WithOpacity(0.35f); s.BorderWidth = 1; s.BorderRadius = 4;
                    s.Padding = new EdgeSize(5, 10); s.FontSize = 10f; s.Color = PColor.FromHex("#FFD96B").WithOpacity(0.85f);
                    s.Italic = true;
                })
            ),

            // 28: Magnetic Pull — three cards in a row
            28 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 52; s.Gap = 8;
            }).WithChildren(
                EffectPanel("Item A", lc1, NodeEffect.HoverLift, 0f, 1f, lintensity),
                EffectPanel("Item B", lc2, NodeEffect.HoverLift, 0f, 1f, lintensity),
                EffectPanel("Item C", accent, NodeEffect.HoverLift, 0f, 1f, lintensity)
            ),

            // 29: Accordion — Intensity slider drives expand height
            29 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Vertical; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fit; s.Gap = 4; s.Padding = new EdgeSize(4);
            }).WithChildren(
                new Node().WithText("▼  Section Header  — drag Intensity to expand").WithStyle(s =>
                {
                    s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                    s.BackgroundColor = lc1.WithOpacity(0.15f);
                    s.BorderColor = lc1.WithOpacity(0.40f); s.BorderWidth = 1; s.BorderRadius = 5;
                    s.Padding = new EdgeSize(7, 12); s.FontSize = 11f; s.Color = lc1; s.Bold = true;
                }),
                new Node().WithStyle(s =>
                {
                    s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fixed;
                    s.Height = (int)(lintensity * 80f); // Intensity controls expand amount
                    s.BackgroundColor = lc1.WithOpacity(0.06f);
                    s.BorderColor = lc1.WithOpacity(0.20f); s.BorderWidth = 1; s.BorderRadius = 5;
                    s.ClipContent = true;
                }).WithChildren(
                    new Node().WithText("Body content — in code, height is driven by Anim.ExpandT").WithStyle(s =>
                    {
                        s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                        s.Padding = new EdgeSize(7, 12); s.FontSize = 10.5f; s.Color = lc1.WithOpacity(0.70f); s.Italic = true;
                    })
                )
            ),

            // 30: Cross-Panel Spotlight — spotlight sweeps across panels over time
            30 => BuildSpotlightExample(lc1, lspeed, lintensity),

            // 31: Shared Value Binding — Intensity slider drives the bar fill live
            31 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Vertical; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fit; s.Gap = 6; s.Padding = new EdgeSize(4);
            }).WithChildren(
                new Node().WithText($"Binding<float> value = new({lintensity:F2f})  — drag Intensity slider").WithStyle(s =>
                {
                    s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                    s.FontSize = 10.5f; s.Color = PColor.FromHex("#9B6BFF"); s.Italic = true;
                }),
                new Node().WithStyle(s =>
                {
                    s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
                    s.HeightMode = SizeMode.Fixed; s.Height = 14;
                    s.BackgroundColor = Theme.Panel2; s.BorderRadius = 4;
                    s.BorderColor = PColor.FromHex("#9B6BFF").WithOpacity(0.30f); s.BorderWidth = 1;
                    s.ClipContent = true;
                }).WithChildren(
                    new Node().WithStyle(s =>
                    {
                        s.HeightMode = SizeMode.Fill;
                        s.BackgroundColor = PColor.FromHex("#9B6BFF").WithOpacity(0.70f);
                        s.BackgroundGradientEnd = PColor.FromHex("#6BDDFF").WithOpacity(0.90f);
                        s.WidthMode = SizeMode.Fixed; s.Width = lintensity * 100f; // live!
                    })
                ),
                new Node().WithText("hpBar width + hpLabel text both update from the same Binding<T>").WithStyle(s =>
                {
                    s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                    s.FontSize = 10f; s.Color = Theme.TextSubtle;
                })
            ),

            // 32: Staggered Entrance — time-driven entrance animation
            32 => BuildStaggeredExample(lc1, lspeed, lintensity),

            // 33: Specular Sweep — metallic panels
            33 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 56; s.Gap = 8;
            }).WithChildren(
                EffectPanel("A", lc1, NodeEffect.SpecularSweep, lspeed * 0.4f, lscale, lintensity),
                EffectPanel("B", lc2, NodeEffect.SpecularSweep, lspeed * 0.6f, lscale, lintensity)
            ),

            // 34: Rim Light — side-lit panels
            34 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 60; s.Gap = 8;
            }).WithChildren(
                EffectPanel("A",   lc1, NodeEffect.RimLight, 0f, lscale, lintensity),
                EffectPanel("B",   lc2, NodeEffect.RimLight, 0f, lscale, lintensity),
                EffectPanel("Mix", new PColor((byte)((lc1.R + lc2.R) / 2), (byte)((lc1.G + lc2.G) / 2), (byte)((lc1.B + lc2.B) / 2), 255), NodeEffect.RimLight, 0f, lscale, lintensity)
            ),

            // 35: Ambient Occlusion — light bg panels so corner darkening is visible
            35 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 60; s.Gap = 8;
            }).WithChildren(
                AoPanelNode("Subtle", lc1, 0.8f, lintensity * 0.5f),
                AoPanelNode("Medium", lc1, 1.2f, lintensity),
                AoPanelNode("Strong", lc1, 1.8f, MathF.Min(1f, lintensity * 1.5f))
            ),

            // 36: Bloom — glowing border panels
            36 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 60; s.Gap = 8;
            }).WithChildren(
                EffectPanel("A",   lc1,   NodeEffect.Bloom, 0f, lscale, lintensity),
                EffectPanel("B",   lc2,   NodeEffect.Bloom, 0f, lscale, lintensity),
                EffectPanel("Mix", accent, NodeEffect.Bloom, 0f, lscale, lintensity)
            ),

            // 37: Text Wave — animated wave text
            37 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Vertical; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 56; s.Padding = new EdgeSize(4);
            }).WithChildren(
                new Node().WithText("TextWave Effect!").WithStyle(s =>
                {
                    s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fill;
                    s.BackgroundColor = lc1.WithOpacity(0.10f);
                    s.BorderColor = lc1.WithOpacity(0.35f); s.BorderWidth = 1; s.BorderRadius = 6;
                    s.Padding = new EdgeSize(6, 14);
                    s.FontSize = 18f; s.Bold = true; s.Color = lc1; s.TextAlign = TextAlign.Center;
                    s.Effect = NodeEffect.TextWave; s.EffectSpeed = lspeed; s.EffectIntensity = lintensity;
                })
            ),

            // 38: Shake — concept panel
            38 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Vertical; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fit; s.Gap = 6; s.Padding = new EdgeSize(4);
            }).WithChildren(
                new Node().WithText("ERROR: Invalid action!").WithStyle(s =>
                {
                    s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                    s.BackgroundColor = PColor.FromHex("#FF6B6B").WithOpacity(0.15f);
                    s.BorderColor = PColor.FromHex("#FF6B6B").WithOpacity(0.55f); s.BorderWidth = 1.5f; s.BorderRadius = 6;
                    s.Padding = new EdgeSize(8, 14); s.FontSize = 12f; s.Bold = true;
                    s.Color = PColor.FromHex("#FF6B6B"); s.TextAlign = TextAlign.Center;
                    s.Effect = NodeEffect.Shake; s.EffectSpeed = lspeed; s.EffectIntensity = lintensity;
                    s.EffectColor1 = lc1;
                }),
                new Node().WithText("Auto-repeating shake — Speed and Intensity sliders apply").WithStyle(s =>
                {
                    s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                    s.FontSize = 10f; s.Color = Theme.TextSubtle; s.TextAlign = TextAlign.Center;
                })
            ),

            // 39: Heat Haze — two panels
            39 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 68; s.Gap = 8;
            }).WithChildren(
                EffectPanel("A", lc1, NodeEffect.HeatHaze, lspeed * 0.8f, lscale, lintensity),
                EffectPanel("B", lc2, NodeEffect.HeatHaze, lspeed * 1.2f, lscale, lintensity)
            ),

            // 40: Slide Transition — live animated panel
            40 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Vertical; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 64;
                s.ClipContent = true;
            }).WithChildren(
                new Node().WithText("← Slide Transition").WithStyle(s =>
                {
                    s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fill;
                    s.BackgroundColor = lc1.WithOpacity(0.14f);
                    s.BorderColor = lc1.WithOpacity(0.4f); s.BorderWidth = 1; s.BorderRadius = 6;
                    s.Padding = new EdgeSize(10, 16); s.FontSize = 14f; s.Bold = true;
                    s.Color = lc1; s.TextAlign = TextAlign.Center;
                    s.Effect = NodeEffect.SlideTransition; s.EffectSpeed = lspeed;
                })
            ),

            // 41: Card Flip — live animated flip card
            41 => new Node().WithStyle(s =>
            {
                s.Flow = Flow.Vertical; s.WidthMode = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = 64;
            }).WithChildren(
                new Node().WithText("Card Flip  ·  front / back").WithStyle(s =>
                {
                    s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fill;
                    s.BackgroundColor = lc1.WithOpacity(0.18f);
                    s.BorderColor = lc1.WithOpacity(0.45f); s.BorderWidth = 1; s.BorderRadius = 6;
                    s.Padding = new EdgeSize(10, 14); s.FontSize = 14f; s.Bold = true;
                    s.Color = lc1; s.TextAlign = TextAlign.Center;
                    s.Effect = NodeEffect.CardFlip; s.EffectSpeed = lspeed;
                    s.EffectColor1 = lc1; s.EffectColor2 = lc2;
                    s.EffectIntensity = lintensity;
                })
            ),

            _ => null,
        };
    }

    private static Node PlasmaPanel(string label, PColor color, float speed, float scale, float intensity) =>
        new Node().WithStyle(s =>
        {
            s.WidthMode       = SizeMode.Fill; s.HeightMode = SizeMode.Fill;
            s.BackgroundColor = Theme.Base;
            s.BorderRadius    = 6; s.BorderColor = color.WithOpacity(0.25f); s.BorderWidth = 1;
            s.Effect          = NodeEffect.Plasma;
            s.EffectColor1    = color;
            s.EffectScale     = scale;
            s.EffectSpeed     = speed;
            s.EffectIntensity = intensity;
            s.Padding         = new EdgeSize(8, 10);
        }).WithChildren(
            new Node().WithText(label).WithStyle(s =>
            {
                s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                s.FontSize = 10f; s.Bold = true; s.Color = color.WithOpacity(0.90f);
                s.TextAlign = TextAlign.Center;
            })
        );

    private static Node EffectPanel(string label, PColor color, NodeEffect effect, float speed, float scale, float intensity) =>
        new Node().WithStyle(s =>
        {
            s.WidthMode       = SizeMode.Fill; s.HeightMode = SizeMode.Fill;
            s.BackgroundColor = Theme.Base;
            s.BorderRadius    = 6; s.BorderColor = color.WithOpacity(0.25f); s.BorderWidth = 1;
            s.Effect          = effect;
            s.EffectColor1    = color;
            s.EffectScale     = scale;
            s.EffectSpeed     = speed;
            s.EffectIntensity = intensity;
            s.Padding         = new EdgeSize(6, 10);
        }).WithChildren(
            new Node().WithText(label).WithStyle(s =>
            {
                s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                s.FontSize = 10f; s.Bold = true; s.Color = color.WithOpacity(0.90f);
                s.TextAlign = TextAlign.Center;
            })
        );

    private static Node CounterNode(string value, PColor color, float speed) =>
        new Node().WithText(value).WithStyle(s =>
        {
            s.WidthMode       = SizeMode.Fill; s.HeightMode = SizeMode.Fill;
            s.BackgroundColor = color.WithOpacity(0.10f);
            s.BorderRadius    = 6; s.BorderColor = color.WithOpacity(0.30f); s.BorderWidth = 1;
            s.FontSize        = 20f; s.Bold = true; s.Color = color; s.TextAlign = TextAlign.Center;
            s.Effect          = NodeEffect.RollingCounter;
            s.EffectSpeed     = speed;
        });

    private static Node SpotlightPanel(string label, PColor color, float opacity) =>
        new Node().WithText(label).WithStyle(s =>
        {
            s.WidthMode       = SizeMode.Fill; s.HeightMode = SizeMode.Fill;
            s.BackgroundColor = color.WithOpacity(0.12f);
            s.BorderRadius    = 6; s.BorderColor = color.WithOpacity(0.35f); s.BorderWidth = 1;
            s.Padding         = new EdgeSize(8, 10); s.FontSize = 11f; s.Bold = true;
            s.Color           = color; s.TextAlign = TextAlign.Center; s.Opacity = opacity;
        });

    private static Node StaggeredCard(string label, PColor color) =>
        new Node().WithText(label).WithStyle(s =>
        {
            s.WidthMode       = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
            s.BackgroundColor = color.WithOpacity(0.10f);
            s.BorderRadius    = 5; s.BorderColor = color.WithOpacity(0.30f); s.BorderWidth = 1;
            s.Padding         = new EdgeSize(7, 12); s.FontSize = 11f;
            s.Color           = color;
            s.Effect          = NodeEffect.StaggeredEntrance;
        });

    private Node BuildStaggeredExample(PColor color, float speed, float intensity)
    {
        float period = MathF.Max(0.8f, 2.5f / MathF.Max(0.1f, speed));
        float ct     = _animTime % period;
        float rate   = 3f * MathF.Max(0.1f, intensity * 2.5f);

        var c0 = StaggeredCard("Item 01", color);
        c0.Anim.EntranceT       = Math.Clamp(ct * rate, 0f, 1f);
        c0.Anim.EntranceStarted = true;

        var c1 = StaggeredCard("Item 02", color);
        c1.Anim.EntranceT       = Math.Clamp((ct - 0.18f) * rate, 0f, 1f);
        c1.Anim.EntranceStarted = true;

        var c2 = StaggeredCard("Item 03", color);
        c2.Anim.EntranceT       = Math.Clamp((ct - 0.36f) * rate, 0f, 1f);
        c2.Anim.EntranceStarted = true;

        return new Node().WithStyle(s =>
        {
            s.Flow = Flow.Vertical; s.WidthMode = SizeMode.Fill;
            s.HeightMode = SizeMode.Fit; s.Gap = 5;
            s.ClipContent = true;
        }).WithChildren(c0, c1, c2);
    }

    private Node BuildSpotlightExample(PColor color, float speed, float intensity)
    {
        float phase = (_animTime * MathF.Max(0.1f, speed) * 0.8f) % 3f;
        float OpFor(float idx) => 0.15f + intensity * 0.85f * MathF.Max(0f, 1f - MathF.Abs(phase - idx));

        return new Node().WithStyle(s =>
        {
            s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill;
            s.HeightMode = SizeMode.Fixed; s.Height = 56; s.Gap = 6;
        }).WithChildren(
            SpotlightPanel("Panel A", color, OpFor(0f)),
            SpotlightPanel("Panel B", color, OpFor(1f)),
            SpotlightPanel("Panel C", color, OpFor(2f))
        );
    }

    private static Node AoPanelNode(string label, PColor color, float scale, float intensity) =>
        new Node().WithStyle(s =>
        {
            s.WidthMode       = SizeMode.Fill; s.HeightMode = SizeMode.Fill;
            s.BackgroundColor = color.WithOpacity(0.38f);  // lighter bg so AO darkening is visible
            s.BorderRadius    = 6;
            s.Effect          = NodeEffect.AmbientOcclusion;
            s.EffectColor1    = color; s.EffectScale = scale; s.EffectIntensity = intensity;
            s.Padding         = new EdgeSize(6, 10);
        }).WithChildren(
            new Node().WithText(label).WithStyle(s =>
            {
                s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                s.FontSize  = 10f; s.Bold = true; s.Color = color; s.TextAlign = TextAlign.Center;
            })
        );

    private static Node NoisePanel(string label, PColor accent, float speed, float scale, float intensity) =>
        new Node().WithStyle(s =>
        {
            s.WidthMode       = SizeMode.Fill; s.HeightMode = SizeMode.Fill;
            s.BackgroundColor = Theme.Panel2;
            s.BorderRadius    = 6; s.BorderColor = accent.WithOpacity(0.25f); s.BorderWidth = 1;
            s.Effect          = NodeEffect.PerlinNoise;
            s.EffectColor1    = accent;
            s.EffectScale     = scale;
            s.EffectSpeed     = speed;
            s.EffectIntensity = intensity;
            s.Padding         = new EdgeSize(6, 8);
        }).WithChildren(
            new Node().WithText(label).WithStyle(s =>
            {
                s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                s.FontSize = 10f; s.Bold = true; s.Color = accent.WithOpacity(0.85f);
                s.TextAlign = TextAlign.Center;
            })
        );

    // ── Live example helpers ──────────────────────────────────────────────────

    private static Node BoxNode(string label, PColor color) =>
        new Node().WithText(label).WithStyle(s =>
        {
            s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
            s.BackgroundColor = color.WithOpacity(0.18f);
            s.BorderColor = color.WithOpacity(0.45f); s.BorderWidth = 1; s.BorderRadius = 4;
            s.Padding = new EdgeSize(6, 10); s.FontSize = 10f; s.Color = color; s.TextAlign = TextAlign.Center;
        });

    private static Node LabelledBox(string label, SizeMode mode, float fixedW, PColor color)
    {
        var n = new Node().WithText(label).WithStyle(s =>
        {
            s.WidthMode = mode; s.HeightMode = SizeMode.Fit;
            if (mode == SizeMode.Fixed) s.Width = fixedW;
            s.BackgroundColor = color.WithOpacity(0.15f);
            s.BorderColor = color.WithOpacity(0.40f); s.BorderWidth = 1; s.BorderRadius = 4;
            s.Padding = new EdgeSize(7, 8); s.FontSize = 9.5f; s.Color = color; s.TextAlign = TextAlign.Center;
            s.TextOverflow = TextOverflow.Ellipsis;
        });
        return n;
    }

    private static Node Swatch(PColor color, float opacity, string label) =>
        new Node().WithStyle(s =>
        {
            s.Flow = Flow.Vertical; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; s.Gap = 3;
        }).WithChildren(
            new Node().WithStyle(s =>
            {
                s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fixed; s.Height = 24;
                s.BackgroundColor = color.WithOpacity(opacity);
                s.BorderRadius = 4; s.BorderColor = color.WithOpacity(0.3f); s.BorderWidth = 1;
            }),
            new Node().WithText(label).WithStyle(s =>
            {
                s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                s.FontSize = 9f; s.Color = Theme.TextSubtle; s.TextAlign = TextAlign.Center;
            })
        );

    private static Node GradientStrip(PColor from, PColor to) =>
        new Node().WithStyle(s =>
        {
            s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fixed; s.Height = 14;
            s.BackgroundColor = from; s.BackgroundGradientEnd = to;
            s.Flow = Flow.Horizontal; s.BorderRadius = 4;
        });

    private static Node BorderBox(string label, float radius, PColor color) =>
        new Node().WithText(label).WithStyle(s =>
        {
            s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
            s.BackgroundColor = color.WithOpacity(0.08f);
            s.BorderColor = color.WithOpacity(0.60f); s.BorderWidth = 1.5f; s.BorderRadius = radius;
            s.Padding = new EdgeSize(8, 6); s.FontSize = 9.5f; s.Color = color; s.TextAlign = TextAlign.Center;
        });

    private static Node ShadowBox(string label, PColor color, float blur, float offsetY) =>
        new Node().WithText(label).WithStyle(s =>
        {
            s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
            s.BackgroundColor = color.WithOpacity(0.15f);
            s.BorderRadius = 6; s.BorderColor = color.WithOpacity(0.30f); s.BorderWidth = 1;
            s.Padding = new EdgeSize(8, 8); s.FontSize = 9.5f; s.Color = color; s.TextAlign = TextAlign.Center;
            s.ShadowColor = color.WithOpacity(0.50f); s.ShadowBlur = blur; s.ShadowOffsetY = offsetY;
        });

    private static Node OpacityBox(PColor color, float opacity, string label) =>
        new Node().WithStyle(s =>
        {
            s.Flow = Flow.Vertical; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
            s.BackgroundColor = color.WithOpacity(0.25f);
            s.BorderRadius = 4; s.BorderColor = color.WithOpacity(0.40f); s.BorderWidth = 1;
            s.Padding = new EdgeSize(6); s.Gap = 4; s.Opacity = opacity;
        }).WithChildren(
            new Node().WithText(label).WithStyle(s =>
            {
                s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                s.FontSize = 10f; s.Color = color; s.TextAlign = TextAlign.Center;
            })
        );

    private static Node ClipDemo(PColor color, bool clip, string label)
    {
        var container = new Node().WithStyle(s =>
        {
            s.Flow = Flow.Vertical; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
            s.Gap = 4;
        });
        container.AppendChild(new Node().WithText(label).WithStyle(s =>
        {
            s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
            s.FontSize = 9.5f; s.Color = Theme.TextSubtle; s.TextAlign = TextAlign.Center;
        }));
        var box = new Node().WithStyle(s =>
        {
            s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fixed; s.Height = 28;
            s.BackgroundColor = Theme.Panel2; s.BorderRadius = 6;
            s.BorderColor = color.WithOpacity(0.35f); s.BorderWidth = 1;
            s.ClipContent = clip;
        });
        box.AppendChild(new Node().WithStyle(s =>
        {
            s.WidthMode = SizeMode.Fixed; s.Width = 999; s.HeightMode = SizeMode.Fill;
            s.BackgroundColor = color.WithOpacity(0.35f);
            s.BackgroundGradientEnd = color.WithOpacity(0.05f);
            s.Flow = Flow.Horizontal; s.BorderRadius = clip ? 6f : 0f;
        }));
        container.AppendChild(box);
        return container;
    }

    private static Node DirtyBadge(string label, PColor color, bool highlighted) =>
        new Node().WithText(label).WithStyle(s =>
        {
            s.WidthMode = SizeMode.Fit; s.HeightMode = SizeMode.Fit;
            s.BackgroundColor = highlighted ? color.WithOpacity(0.22f) : Theme.Panel2;
            s.BorderRadius = 5; s.BorderColor = color.WithOpacity(highlighted ? 0.65f : 0.22f); s.BorderWidth = 1;
            s.Padding = new EdgeSize(5, 10); s.FontSize = 10f;
            s.Color = highlighted ? color : Theme.TextMuted;
        });

    private static Node Arrow() =>
        new Node().WithText("→").WithStyle(s =>
        {
            s.WidthMode = SizeMode.Fit; s.HeightMode = SizeMode.Fit;
            s.FontSize = 12f; s.Color = Theme.TextSubtle;
        });

    private static Node PipelineStep(string label, PColor color) =>
        new Node().WithText(label).WithStyle(s =>
        {
            s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
            s.BackgroundColor = color.WithOpacity(0.12f);
            s.BorderRadius = 4; s.BorderColor = color.WithOpacity(0.40f); s.BorderWidth = 1;
            s.Padding = new EdgeSize(6, 6); s.FontSize = 9f; s.Bold = true;
            s.Color = color; s.TextAlign = TextAlign.Center; s.TextOverflow = TextOverflow.Ellipsis;
        });

    /// <summary>Detail section using PUI.SectionWrap — replaces old MakeSection.</summary>
    private static Node DetailSection(string label, PColor accent, Node content)
    {
        var inner = new Node().WithStyle(s =>
        {
            s.Flow = Flow.Vertical; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
            s.Padding = new EdgeSize(10, 16); s.Gap = 8;
        });
        inner.AppendChild(PUI.SectionLabel(label, accent));
        inner.AppendChild(content);
        return PUI.SectionWrap(accent, inner);
    }

    // ── Component builders ────────────────────────────────────────────────────

    private static Node TextLine(string text, float size, PColor color) =>
        new Node().WithText(text).WithStyle(s =>
        {
            s.WidthMode  = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
            s.FontSize   = size; s.Color = color;
        });

    private static Node BuildCodeBlock(CodeLine[] lines)
    {
        var block = new Node().WithStyle(s =>
        {
            s.Flow            = Flow.Vertical;
            s.WidthMode       = SizeMode.Fill;
            s.HeightMode      = SizeMode.Fit;
            s.BackgroundColor = PColor.FromHex("#050510");
            s.BorderRadius    = 6;
            s.BorderColor     = PColor.FromHex("#FFFFFF").WithOpacity(0.06f);
            s.BorderWidth     = 1;
            s.Padding         = new EdgeSize(10, 14);
            s.Gap             = 5;
            s.ClipContent     = true;
        });

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var row  = new Node().WithStyle(s =>
            {
                s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; s.Gap = 10;
            });

            row.AppendChild(new Node().WithText($"{i + 1}").WithStyle(s =>
            {
                s.WidthMode  = SizeMode.Fixed; s.Width     = 12;
                s.HeightMode = SizeMode.Fit;   s.FontSize  = 10f;
                s.Color      = PColor.FromHex("#333355"); s.TextAlign = TextAlign.Right;
            }));

            row.AppendChild(new Node().WithText(line.Text).WithStyle(s =>
            {
                s.WidthMode  = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                s.FontSize   = 10.5f;
                s.Color      = PColor.FromHex(line.ColorHex);
            }));

            block.AppendChild(row);
        }

        return block;
    }

    private static Node BuildCasePills(FeatureSection sec, PColor accent)
    {
        var row = new Node().WithStyle(s =>
        {
            s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; s.Gap = 8;
        });

        foreach (var label in new[] { sec.Case1, sec.Case2, sec.Case3 })
        {
            row.AppendChild(new Node().WithText(label).WithStyle(s =>
            {
                s.WidthMode       = SizeMode.Fit; s.HeightMode = SizeMode.Fit;
                s.BackgroundColor = accent.WithOpacity(0.14f);
                s.BorderColor     = accent.WithOpacity(0.40f);
                s.BorderWidth     = 1; s.BorderRadius = 10;
                s.Padding         = new EdgeSize(4, 12);
                s.FontSize        = 10.5f;
                s.Color           = accent.WithOpacity(0.88f);
            }));
        }

        return row;
    }

    private static PColor HsvToRgb(float h, float s, float v)
    {
        float c = v * s, x = c * (1f - MathF.Abs((h / 60f) % 2f - 1f)), m = v - c;
        float r, g, b;
        if      (h < 60)  { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else              { r = c; g = 0; b = x; }
        return new PColor((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    // ── Feature data ──────────────────────────────────────────────────────────

    private static FeatureSection[] BuildSections() => new[]
    {
        new FeatureSection("Node Tree", "#B06BFF",
            "The foundation of every PanacheUI UI",
            "Every element is a Node forming a parent-child hierarchy.",
            "The root node drives the full layout and render pass each frame.",
            new CodeLine[] {
                new("var root  = new Node().WithId(\"container\");", "#A8D8EA"),
                new("var label = new Node().WithText(\"Hello!\");",  "#C3E88D"),
                new("root.AppendChild(label);",                       "#F78C6C"),
                new("engine.Compute(root, width, height);",           "#FFD9AA"),
            },
            "Dashboard panels", "Item list rows", "Nested cards",
            "Use FindById() and FindByClass() to reach deep nodes without storing refs."),

        new FeatureSection("Box Layout", "#6B8FFF",
            "CSS-inspired box model with Fill, Fit, and Fixed sizing",
            "Choose Flow direction and how each axis sizes itself.",
            "Add Padding, Margin, and Gap to control spacing between children.",
            new CodeLine[] {
                new("s.Flow      = Flow.Horizontal;",                   "#A8D8EA"),
                new("s.WidthMode = SizeMode.Fill;  // expand to parent","#C3E88D"),
                new("s.HeightMode= SizeMode.Fit;   // shrink to content","#F78C6C"),
                new("s.Gap = 10; s.Padding = new EdgeSize(8, 12);",     "#FFD9AA"),
            },
            "Toolbar rows", "Stat grids", "Form layouts",
            "Fill children in a horizontal row split the remaining width equally, like flexbox."),

        new FeatureSection("PColor", "#FF8C6B",
            "Lightweight RGBA with hex parsing and opacity helpers",
            "PColor wraps R/G/B/A bytes with a hex parser and opacity shorthand.",
            "It has an implicit cast to SkiaSharp's SKColor — no conversion needed.",
            new CodeLine[] {
                new("var col = PColor.FromHex(\"#FF6B6B\");", "#A8D8EA"),
                new("var dim = col.WithOpacity(0.4f);",        "#C3E88D"),
                new("PColor.White / .Black / .Transparent",    "#F78C6C"),
                new("SKColor sk = (SKColor)myColor; // implicit cast","#FFD9AA"),
            },
            "Accent themes", "Transparent overlays", "Health bars",
            "WithOpacity() returns a new PColor — it does not mutate. Safe to use inline."),

        new FeatureSection("Gradients", "#FF9B6B",
            "Two-color linear gradients in any Flow direction",
            "Set BackgroundColor and BackgroundGradientEnd on any node.",
            "The gradient flows along the node's current Flow axis automatically.",
            new CodeLine[] {
                new("s.BackgroundColor       = PColor.FromHex(\"#6B8FFF\");","#A8D8EA"),
                new("s.BackgroundGradientEnd = PColor.FromHex(\"#B86BFF\");","#C3E88D"),
                new("s.Flow = Flow.Horizontal; // direction of gradient",     "#F78C6C"),
                new("// Animate: change colors + MarkDirty() each frame",     "#666688"),
            },
            "Progress bars", "Header banners", "Button accents",
            "Animate by changing BackgroundColor each frame and calling MarkDirty()."),

        new FeatureSection("Typography", "#6BFFCC",
            "Full text control — size, weight, alignment, outline, overflow",
            "Text auto-centers vertically in its node. Control horizontal alignment,",
            "outline for legibility, and ellipsis truncation on overflow.",
            new CodeLine[] {
                new("s.FontSize = 18f; s.Bold = true;",                  "#A8D8EA"),
                new("s.TextAlign = TextAlign.Center;",                    "#C3E88D"),
                new("s.TextOutlineColor = PColor.Black.WithOpacity(0.8f);","#F78C6C"),
                new("s.TextOverflow = TextOverflow.Ellipsis;",             "#FFD9AA"),
            },
            "Stat numbers", "Truncated item names", "Section headers",
            "Pair TextOutlineColor with OutlineSize >= 1.5 for readable text on any background."),

        new FeatureSection("Borders", "#FFD36B",
            "Rounded corners, colored borders, and configurable width",
            "Every node supports a border with color, pixel width, and corner radius.",
            "Combine with ClipContent = true to clip children to the rounded shape.",
            new CodeLine[] {
                new("s.BorderRadius = 8f;",                              "#A8D8EA"),
                new("s.BorderColor  = PColor.FromHex(\"#6B8FFF\");",    "#C3E88D"),
                new("s.BorderWidth  = 1.5f;",                            "#F78C6C"),
                new("s.ClipContent  = true; // clip to rounded bounds",  "#FFD9AA"),
            },
            "Card outlines", "Input fields", "Pill badges",
            "A 1px border at 30-50% opacity looks more polished than full opacity."),

        new FeatureSection("Drop Shadows", "#6B9FFF",
            "Gaussian drop shadows — blur, offset, and color per node",
            "Shadows render behind each node as a blurred color layer.",
            "Control blur radius, XY offset, and shadow color independently.",
            new CodeLine[] {
                new("s.ShadowColor   = PColor.Black.WithOpacity(0.6f);","#A8D8EA"),
                new("s.ShadowBlur    = 10f;",                            "#C3E88D"),
                new("s.ShadowOffsetX = 0f;",                             "#F78C6C"),
                new("s.ShadowOffsetY = 4f;",                             "#FFD9AA"),
            },
            "Floating panels", "Card depth", "Modal elevation",
            "Colored shadows using accent@30% with high blur produce a glow effect."),

        new FeatureSection("Opacity", "#6BFFB8",
            "Per-node opacity applied uniformly to node and all descendants",
            "Set Opacity between 0.0 (invisible) and 1.0 (fully opaque).",
            "Internally uses SaveLayer so child compositing is always correct.",
            new CodeLine[] {
                new("s.Opacity = 0.5f;               // 0 = off, 1 = full","#A8D8EA"),
                new("s.Opacity = isEnabled ? 1f : 0.35f;",                 "#C3E88D"),
                new("// Affects ALL children uniformly",                    "#666688"),
                new("// Animate: change Opacity + MarkDirty() each frame",  "#666688"),
            },
            "Disabled states", "Fade effects", "Ghost elements",
            "Opacity composites after all child drawing, giving true translucency."),

        new FeatureSection("Clip Content", "#FF6BB0",
            "Constrain child rendering to this node's bounds and border radius",
            "ClipContent = true hides anything children draw outside the node box.",
            "The clip respects BorderRadius for smooth rounded clipping.",
            new CodeLine[] {
                new("s.ClipContent  = true;",               "#A8D8EA"),
                new("s.BorderRadius = 5f; // rounded clip", "#C3E88D"),
                new("// Fill children clip at the edges",    "#666688"),
                new("// Essential for progress bar fills",   "#666688"),
            },
            "Progress bar fills", "Avatar images", "Overflow hidden",
            "Without ClipContent a colored fill child bleeds past rounded corners."),

        new FeatureSection("Dirty Tracking", "#FFE76B",
            "Automatic change detection — re-render only when something changed",
            "Every Node tracks IsDirty. MarkDirty() propagates the flag to the root",
            "so the render loop skips re-uploading when nothing has changed.",
            new CodeLine[] {
                new("node.Style.Color = PColor.FromHex(\"#FF4444\");", "#A8D8EA"),
                new("node.MarkDirty();          // walks UP to root",   "#C3E88D"),
                new("if (root.IsDirty) {",                              "#F78C6C"),
                new("    Render(); root.ClearDirty(); }",               "#FFD9AA"),
            },
            "Live combat data", "Frame animations", "Reactive state",
            "Only call MarkDirty() after you have actually changed something on the node."),

        new FeatureSection("Fluent Builder", "#6BDDFF",
            "Chain builder methods for concise, readable node tree construction",
            "WithId(), WithText(), WithStyle(), WithClass(), and WithChildren()",
            "all return 'this', enabling inline composition without temp variables.",
            new CodeLine[] {
                new("new Node()",                                    "#A8D8EA"),
                new("    .WithId(\"card\").WithText(\"Hello\")",    "#C3E88D"),
                new("    .WithStyle(s => { s.Bold = true; })",      "#F78C6C"),
                new("    .WithChildren(child1, child2);",            "#FFD9AA"),
            },
            "Component factories", "Quick prototyping", "Readable layouts",
            "Wrap builders in static methods returning Node — the component pattern."),

        new FeatureSection("Texture Pipeline", "#B8FF6B",
            "SkiaSharp -> CPU bitmap -> Dalamud texture -> ImGui.Image",
            "RenderSurface holds an SKSurface. TextureManager uploads pixels via",
            "Dalamud's ITextureProvider.CreateFromRaw() and returns an ImGui handle.",
            new CodeLine[] {
                new("var surf = new RenderSurface(w, h);",              "#A8D8EA"),
                new("renderer.Render(surf.Canvas, root, layout);",       "#C3E88D"),
                new("ImTextureID? h = texManager.Upload(surf);",         "#F78C6C"),
                new("ImGui.Image(h.Value, new Vector2(w, h));",          "#FFD9AA"),
            },
            "Custom overlays", "Multiple panels", "Full plugin UI",
            "Use a dirty flag to skip Upload() when unchanged — it allocates a new texture each call."),

        // ── Feature 02 ────────────────────────────────────────────────────────
        new FeatureSection("Plasma / Lava Lamp", "#FF6B6B",
            "Animated color blobs orbiting inside any node",
            "Six radial-gradient blobs orbit at sin/cos paths with phase offsets.",
            "Colors cycle through HSV over time. EffectColor1 shifts the palette hue.",
            new CodeLine[] {
                new("node.WithStyle(s => {",                                    "#A8D8EA"),
                new("    s.Effect          = NodeEffect.Plasma;",               "#C3E88D"),
                new("    s.EffectColor1    = PColor.FromHex(\"#FF6B6B\");",     "#F78C6C"),
                new("    s.EffectScale     = 1.0f;  // blob radius",            "#FFD9AA"),
                new("    s.EffectSpeed     = 0.6f;  // orbit speed",            "#C3E88D"),
                new("    s.EffectIntensity = 0.55f; // glow brightness",        "#B8FF6B"),
                new("});",                                                       "#A8D8EA"),
            },
            "Panel header bg", "Status indicators", "Loading states",
            "EffectColor1.R controls the starting hue (0=red, 128=cyan, 255=back to red). Use Screen blend internally — looks best on dark backgrounds."),

        // ── Feature 01 ────────────────────────────────────────────────────────
        new FeatureSection("Perlin Noise Background", "#6BD4FF",
            "Fractal noise overlay on any node background",
            "Set Style.Effect = NodeEffect.PerlinNoise. The renderer uses SkiaSharp's built-in",
            "fractal noise shader blended over the background in Overlay mode.",
            new CodeLine[] {
                new("node.WithStyle(s => {",                                     "#A8D8EA"),
                new("    s.Effect          = NodeEffect.PerlinNoise;",           "#C3E88D"),
                new("    s.EffectColor1    = PColor.FromHex(\"#6BD4FF\");",      "#F78C6C"),
                new("    s.EffectScale     = 1.5f;  // coarser noise",           "#FFD9AA"),
                new("    s.EffectSpeed     = 0.4f;  // slow drift",              "#C3E88D"),
                new("    s.EffectIntensity = 0.35f; // blend strength",          "#B8FF6B"),
                new("});",                                                        "#A8D8EA"),
            },
            "Animated panel bg", "Accent texture on cards", "Depth in hero sections",
            "EffectSpeed = 0 for a static noise snapshot. Higher EffectScale = fewer, larger blobs."),

        // ── Feature 03 ────────────────────────────────────────────────────────
        new FeatureSection("Voronoi Cell Pattern", "#B8FF6B",
            "Generative tessellated cell background",
            "14 randomly-placed sites partition the node surface into colored Voronoi cells.",
            "Sites drift slowly over time. EffectColor1 shifts the base hue palette.",
            new CodeLine[] {
                new("node.WithStyle(s => {",                                     "#A8D8EA"),
                new("    s.Effect          = NodeEffect.Voronoi;",               "#C3E88D"),
                new("    s.EffectColor1    = PColor.FromHex(\"#B8FF6B\");",      "#F78C6C"),
                new("    s.EffectSpeed     = 0.4f;  // site drift speed",        "#FFD9AA"),
                new("    s.EffectIntensity = 0.75f; // cell alpha",              "#C3E88D"),
                new("});",                                                        "#A8D8EA"),
            },
            "Panel backgrounds", "Map regions", "Abstract art dividers",
            "Use Screen blend with a dark background for vibrant, glowing cells."),

        // ── Feature 04 ────────────────────────────────────────────────────────
        new FeatureSection("Scanline / CRT Overlay", "#66FFCC",
            "Retro CRT scanline overlay effect",
            "Draws thin horizontal lines over a node at configurable spacing.",
            "EffectScale controls line spacing. EffectIntensity controls line darkness.",
            new CodeLine[] {
                new("node.WithStyle(s => {",                                     "#A8D8EA"),
                new("    s.Effect          = NodeEffect.Scanline;",              "#C3E88D"),
                new("    s.EffectColor1    = PColor.FromHex(\"#000000\");",      "#F78C6C"),
                new("    s.EffectScale     = 1.0f;  // 2px spacing",             "#FFD9AA"),
                new("    s.EffectIntensity = 0.40f; // line opacity",            "#C3E88D"),
                new("});",                                                        "#A8D8EA"),
            },
            "Retro screens", "Pixel art overlays", "CRT monitors",
            "Combine with Plasma for an animated retro TV look."),

        // ── Feature 05 ────────────────────────────────────────────────────────
        new FeatureSection("Dot Matrix / Halftone", "#FFB86B",
            "Grid of circles scaling from center",
            "A regular grid of dots where size and brightness scale from the node center.",
            "EffectScale controls dot spacing; EffectSpeed pulses dot radius.",
            new CodeLine[] {
                new("node.WithStyle(s => {",                                     "#A8D8EA"),
                new("    s.Effect          = NodeEffect.DotMatrix;",             "#C3E88D"),
                new("    s.EffectColor1    = PColor.FromHex(\"#FFB86B\");",      "#F78C6C"),
                new("    s.EffectScale     = 1.0f;  // dot spacing",             "#FFD9AA"),
                new("    s.EffectSpeed     = 1.0f;  // pulse speed",             "#C3E88D"),
                new("    s.EffectIntensity = 0.60f; // max dot opacity",         "#B8FF6B"),
                new("});",                                                        "#A8D8EA"),
            },
            "Loading indicators", "Halftone textures", "Radar displays",
            "EffectIntensity * 2 drives max dot radius — keep under 0.6 to avoid overlap."),

        // ── Feature 06 ────────────────────────────────────────────────────────
        new FeatureSection("Waveform Strip", "#6BDDFF",
            "Procedural audio-style waveform",
            "48 bars whose heights are driven by layered sine noise simulate audio levels.",
            "EffectSpeed drives frequency sweep; EffectIntensity scales bar height.",
            new CodeLine[] {
                new("node.WithStyle(s => {",                                     "#A8D8EA"),
                new("    s.Effect          = NodeEffect.Waveform;",              "#C3E88D"),
                new("    s.EffectColor1    = PColor.FromHex(\"#6BDDFF\");",      "#F78C6C"),
                new("    s.EffectSpeed     = 1.0f;  // animation rate",          "#FFD9AA"),
                new("    s.EffectIntensity = 0.85f; // bar height scale",        "#C3E88D"),
                new("});",                                                        "#A8D8EA"),
            },
            "Music visualizers", "Status bars", "EQ displays",
            "Use a tall fixed-height node for the best-looking waveform proportions."),

        // ── Feature 07 ────────────────────────────────────────────────────────
        new FeatureSection("Shimmer Sweep", "#FFFFFF",
            "Diagonal highlight stripe sweeps across node",
            "A bright diagonal stripe sweeps left to right on a configurable loop period.",
            "EffectSpeed controls sweep frequency; EffectIntensity sets stripe brightness.",
            new CodeLine[] {
                new("node.WithStyle(s => {",                                     "#A8D8EA"),
                new("    s.Effect          = NodeEffect.Shimmer;",               "#C3E88D"),
                new("    s.EffectColor1    = PColor.FromHex(\"#FFFFFF\");",      "#F78C6C"),
                new("    s.EffectSpeed     = 1.0f;  // sweeps/sec",              "#FFD9AA"),
                new("    s.EffectIntensity = 0.70f; // stripe brightness",       "#C3E88D"),
                new("});",                                                        "#A8D8EA"),
            },
            "Button hover polish", "Card accents", "Loading indicators",
            "Set EffectSpeed low (0.3) for a subtle sheen that loops gently."),

        // ── Feature 08 ────────────────────────────────────────────────────────
        new FeatureSection("Breathing Pulse Glow", "#FF6BDD",
            "Border pulses with a breathing rhythm",
            "The node border glows and fades with a sinusoidal breathing cadence.",
            "EffectSpeed controls breaths-per-second; EffectIntensity scales glow size.",
            new CodeLine[] {
                new("node.WithStyle(s => {",                                     "#A8D8EA"),
                new("    s.Effect          = NodeEffect.PulseGlow;",             "#C3E88D"),
                new("    s.EffectColor1    = PColor.FromHex(\"#FF6BDD\");",      "#F78C6C"),
                new("    s.EffectSpeed     = 0.6f;  // breath rate",             "#FFD9AA"),
                new("    s.EffectIntensity = 0.80f; // glow intensity",          "#C3E88D"),
                new("});",                                                        "#A8D8EA"),
            },
            "Alert indicators", "Focus states", "Active pane highlights",
            "Works best on a dark background where the blur blooms visibly outward."),

        // ── Feature 09 ────────────────────────────────────────────────────────
        new FeatureSection("Particle Emitter", "#6BFF9B",
            "Particles drift upward from any node",
            "45 particles float upward with random speeds, sizes, and phase offsets.",
            "Fully deterministic — uses time-based math, no allocation each frame.",
            new CodeLine[] {
                new("node.WithStyle(s => {",                                     "#A8D8EA"),
                new("    s.Effect          = NodeEffect.Particles;",             "#C3E88D"),
                new("    s.EffectColor1    = PColor.FromHex(\"#6BFF9B\");",      "#F78C6C"),
                new("    s.EffectScale     = 1.0f;  // particle size",           "#FFD9AA"),
                new("    s.EffectSpeed     = 1.0f;  // rise speed",              "#C3E88D"),
                new("    s.EffectIntensity = 0.70f; // max alpha",               "#B8FF6B"),
                new("});",                                                        "#A8D8EA"),
            },
            "Spell effects", "XP gain", "Environment ambiance",
            "Clip the parent node to prevent particles spilling outside panel bounds."),

        // ── Feature 10 ────────────────────────────────────────────────────────
        new FeatureSection("Typewriter Text", "#FFE66B",
            "Text reveals character by character",
            "Characters appear one at a time at a configurable rate, then loop after a pause.",
            "A blinking cursor shows while the text is still being revealed.",
            new CodeLine[] {
                new("node.WithText(\"Hello, world!\").WithStyle(s => {",         "#A8D8EA"),
                new("    s.Effect      = NodeEffect.TypewriterText;",             "#C3E88D"),
                new("    s.EffectSpeed = 1.0f; // chars/sec = Speed * 12",       "#F78C6C"),
                new("});",                                                        "#FFD9AA"),
                new("// Text loops automatically: full → pause → restart",       "#666688"),
            },
            "Intro text", "Quest dialogue", "Command output",
            "EffectSpeed * 12 = characters per second. Use 0.5 for slow dramatic reveals."),

        // ── Feature 11 ────────────────────────────────────────────────────────
        new FeatureSection("Rolling Number Counter", "#6BDDFF",
            "Numbers animate from 0 to target value",
            "Numeric NodeValue animates from 0 up to the target over each loop cycle.",
            "Uses an ease-out-cubic curve for a natural deceleration feel.",
            new CodeLine[] {
                new("node.WithText(\"9999\").WithStyle(s => {",                  "#A8D8EA"),
                new("    s.Effect      = NodeEffect.RollingCounter;",             "#C3E88D"),
                new("    s.EffectSpeed = 1.0f; // cycle speed",                  "#F78C6C"),
                new("});",                                                        "#FFD9AA"),
                new("// NodeValue must be a parseable float",                     "#666688"),
            },
            "Score displays", "Gil counters", "Stat breakdowns",
            "Change NodeValue + MarkDirty() and the counter will re-animate to the new target."),

        // ── Feature 12 ────────────────────────────────────────────────────────
        new FeatureSection("Chase-Light Border", "#FF9B6B",
            "Bright segment orbits the node perimeter",
            "A glowing dashed segment travels around the node border continuously.",
            "EffectSpeed controls orbit speed; EffectIntensity controls brightness.",
            new CodeLine[] {
                new("node.WithStyle(s => {",                                     "#A8D8EA"),
                new("    s.Effect          = NodeEffect.ChaseLight;",            "#C3E88D"),
                new("    s.EffectColor1    = PColor.FromHex(\"#FF9B6B\");",      "#F78C6C"),
                new("    s.EffectSpeed     = 1.0f;  // orbit laps/sec",          "#FFD9AA"),
                new("    s.EffectIntensity = 0.85f; // glow brightness",         "#C3E88D"),
                new("});",                                                        "#A8D8EA"),
            },
            "Active states", "Loading rings", "Focus indicators",
            "Larger BorderRadius makes the arc look smoother as it rounds corners."),

        // ── Feature 13 ────────────────────────────────────────────────────────
        new FeatureSection("Hover Lift + Glow", "#B06BFF",
            "Card scales up and brightens on hover",
            "When the mouse enters the node, it scales up 4% and a glow blurs outward.",
            "Uses NodeAnimState.HoverT (0→1 lerp) for smooth ease-in/out transitions.",
            new CodeLine[] {
                new("node.WithStyle(s => {",                                     "#A8D8EA"),
                new("    s.Effect       = NodeEffect.HoverLift;",                "#C3E88D"),
                new("    s.EffectColor1 = PColor.FromHex(\"#B06BFF\");",         "#F78C6C"),
                new("    s.EffectIntensity = 0.6f; // glow brightness",          "#FFD9AA"),
                new("});",                                                        "#A8D8EA"),
                new("// Call InteractionManager.Update() each frame",            "#666688"),
            },
            "Card grids", "Menu items", "Selectable panels",
            "InteractionManager.Update() must be called each frame with mouse pos and dt."),

        // ── Feature 14 ────────────────────────────────────────────────────────
        new FeatureSection("Press Depress", "#6BD4FF",
            "Node squishes on press, springs back",
            "Mouse-down shrinks the node 4% toward center. Release spring-returns it.",
            "PressT (0→1) drives the scale factor. A dark overlay darkens on press.",
            new CodeLine[] {
                new("node.WithStyle(s => {",                                     "#A8D8EA"),
                new("    s.Effect       = NodeEffect.PressDepress;",             "#C3E88D"),
                new("    s.EffectColor1 = PColor.FromHex(\"#6BD4FF\");",         "#F78C6C"),
                new("});",                                                        "#FFD9AA"),
                new("// Call InteractionManager.Update() each frame",            "#666688"),
            },
            "Buttons", "Clickable cards", "Toggle switches",
            "Pair with HoverLift to get both a hover scale and a press squish."),

        // ── Feature 15 ────────────────────────────────────────────────────────
        new FeatureSection("Ripple on Click", "#6BFFB8",
            "Circular wave expands from click point",
            "A circular ring expands from the exact click position and fades out.",
            "RippleRadius grows at 180 px/s; RippleAlpha decays at 2.5/s.",
            new CodeLine[] {
                new("node.WithStyle(s => {",                                     "#A8D8EA"),
                new("    s.Effect       = NodeEffect.Ripple;",                   "#C3E88D"),
                new("    s.EffectColor1 = PColor.FromHex(\"#6BFFB8\");",         "#F78C6C"),
                new("});",                                                        "#FFD9AA"),
                new("// Call InteractionManager.Update() each frame",            "#666688"),
                new("// Ripple auto-triggers on click — no extra code needed",   "#666688"),
            },
            "Buttons", "Tap feedback", "Confirmation actions",
            "Enable ClipContent on the node to keep ripples inside rounded borders."),

        // ── Feature 16 ────────────────────────────────────────────────────────
        new FeatureSection("Hover Tooltip Node", "#FFD96B",
            "Floating info node appears near hover target",
            "PUI.TooltipWrap() creates a container with a hidden tooltip child.",
            "Toggle the tooltip's Opacity via OnMouseEnter/OnMouseLeave events.",
            new CodeLine[] {
                new("var card = new Node().WithStyle(/* ... */);",                "#A8D8EA"),
                new("var tip  = new Node().WithText(\"Info!\").WithStyle(s => {", "#C3E88D"),
                new("    s.Opacity = 0f; // hidden by default",                  "#F78C6C"),
                new("});",                                                        "#FFD9AA"),
                new("card.OnMouseEnter += _ => { tip.Style.Opacity = 1f; };",    "#C3E88D"),
                new("card.OnMouseLeave += _ => { tip.Style.Opacity = 0f; };",    "#B8FF6B"),
            },
            "Item descriptions", "Stat breakdowns", "Help hints",
            "Position the tooltip via absolute Margin offsets or a sibling node layout."),

        // ── Feature 17 ────────────────────────────────────────────────────────
        new FeatureSection("Magnetic Neighbor Pull", "#FF6B6B",
            "Siblings subtly shift toward hovered node",
            "When a node is hovered, siblings check HoverT and translate toward it.",
            "Implemented by reading neighbor HoverT values in DrawNode transforms.",
            new CodeLine[] {
                new("// Magnetic pull is a visual pattern, not a single NodeEffect.",  "#A8D8EA"),
                new("// Add HoverLift to each sibling — they all scale slightly,",     "#C3E88D"),
                new("// which creates the illusion of mutual pull.",                    "#F78C6C"),
                new("node.WithStyle(s => { s.Effect = NodeEffect.HoverLift; });",      "#FFD9AA"),
                new("// Use InteractionManager.Update() for consistent HoverT updates","#666688"),
            },
            "Item grids", "Tab bars", "Navigation menus",
            "The effect is subtle by design — stack with HoverLift for maximum impact."),

        // ── Feature 18 ────────────────────────────────────────────────────────
        new FeatureSection("Accordion Expand / Collapse", "#6BFFCC",
            "Sections animate height open and closed",
            "NodeAnimState.IsExpanded drives ExpandT (0→1) via a smooth lerp.",
            "ClipContent + a height derived from ExpandT gives the smooth slide effect.",
            new CodeLine[] {
                new("// Toggle accordion state on click:",                       "#A8D8EA"),
                new("header.OnClick += _ => {",                                  "#C3E88D"),
                new("    body.Anim.IsExpanded = !body.Anim.IsExpanded;",         "#F78C6C"),
                new("    body.MarkDirty();",                                      "#FFD9AA"),
                new("};",                                                         "#A8D8EA"),
                new("// ExpandT (0→1) lerps at 6/sec automatically",            "#666688"),
            },
            "Settings panels", "FAQ sections", "Collapsible groups",
            "NodeAnimState.ExpandT is always available — no NodeEffect enum value needed."),

        // ── Feature 19 ────────────────────────────────────────────────────────
        new FeatureSection("Cross-Panel Spotlight", "#B8FF6B",
            "Hovered section glows; others dim",
            "When one node is hovered, siblings reduce Opacity. The hovered node brightens.",
            "Implemented by reading sibling HoverT values in an OnMouseEnter/Leave handler.",
            new CodeLine[] {
                new("foreach (var panel in panels) {",                           "#A8D8EA"),
                new("    panel.OnMouseEnter += _ => {",                          "#C3E88D"),
                new("        foreach (var p in panels)",                         "#F78C6C"),
                new("            p.Style.Opacity = p == panel ? 1f : 0.4f;",    "#FFD9AA"),
                new("    };",                                                     "#A8D8EA"),
                new("}",                                                          "#C3E88D"),
            },
            "Dashboard sections", "Tab panels", "Info cards",
            "Combine with HoverLift for a full lift-and-dim panel focus effect."),

        // ── Feature 20 ────────────────────────────────────────────────────────
        new FeatureSection("Shared Value Binding", "#9B6BFF",
            "Two nodes reactively mirror a PanacheBinding<T>",
            "PanacheBinding<T>.Value fires OnChanged when set, calling MarkDirty() on subscribers.",
            "Any number of nodes can bind to the same PanacheBinding instance.",
            new CodeLine[] {
                new("var hp = new PanacheBinding<int>(100);",                    "#A8D8EA"),
                new("hp.Bind(hpBar);   // hpBar auto-marks dirty on change",     "#C3E88D"),
                new("hp.Bind(hpLabel); // hpLabel too",                          "#F78C6C"),
                new("// Later:",                                                  "#666688"),
                new("hp.Value = 75;    // both nodes re-render automatically",   "#FFD9AA"),
            },
            "HP/MP bars", "Countdown timers", "Live stat displays",
            "PanacheBinding is generic — use PanacheBinding<string> for text nodes too."),

        // ── Feature 21 ────────────────────────────────────────────────────────
        new FeatureSection("Staggered Entrance", "#6BDDFF",
            "Siblings appear in sequence with delay",
            "Each node slides up from 20px below and fades in via EntranceT (0→1).",
            "Set different EntranceDelay values per sibling for the cascade effect.",
            new CodeLine[] {
                new("for (int i = 0; i < cards.Length; i++) {",                  "#A8D8EA"),
                new("    cards[i].WithStyle(s => {",                             "#C3E88D"),
                new("        s.Effect = NodeEffect.StaggeredEntrance; });",      "#F78C6C"),
                new("    cards[i].Anim.EntranceDelay = i * 0.12f;",              "#FFD9AA"),
                new("}",                                                           "#A8D8EA"),
                new("// EntranceT lerps to 1 at 3/sec once delay expires",       "#666688"),
            },
            "List items", "Card grids", "Menu reveal",
            "EntranceT stays at 1 once reached — no performance cost after animation."),

        // ── Feature 22 ────────────────────────────────────────────────────────
        new FeatureSection("Specular Light Sweep", "#FFFFFF",
            "Moving light source sweeps across all surfaces",
            "A radial highlight oscillates horizontally across the node surface.",
            "Simulates a point light moving across a reflective surface.",
            new CodeLine[] {
                new("node.WithStyle(s => {",                                     "#A8D8EA"),
                new("    s.Effect          = NodeEffect.SpecularSweep;",         "#C3E88D"),
                new("    s.EffectColor1    = PColor.FromHex(\"#FFFFFF\");",      "#F78C6C"),
                new("    s.EffectSpeed     = 0.4f;  // sweep speed",             "#FFD9AA"),
                new("    s.EffectIntensity = 0.40f; // highlight brightness",    "#C3E88D"),
                new("});",                                                        "#A8D8EA"),
            },
            "Metallic surfaces", "Glass panes", "Gem highlights",
            "Use EffectColor1 as a warm white (#FFF8E0) for a more natural specular look."),

        // ── Feature 23 ────────────────────────────────────────────────────────
        new FeatureSection("Rim Lighting", "#6BFFB8",
            "Soft backlight glow on one panel edge",
            "A gradient fades from the EffectColor on the left edge to transparent.",
            "Simulates light bleeding in from behind a surface on one side.",
            new CodeLine[] {
                new("node.WithStyle(s => {",                                     "#A8D8EA"),
                new("    s.Effect          = NodeEffect.RimLight;",              "#C3E88D"),
                new("    s.EffectColor1    = PColor.FromHex(\"#6BFFB8\");",      "#F78C6C"),
                new("    s.EffectIntensity = 0.60f; // rim brightness",          "#FFD9AA"),
                new("});",                                                        "#A8D8EA"),
            },
            "Panels with backlight", "Character cards", "Sci-fi interfaces",
            "Pair with a dark background and a matching border color for best results."),

        // ── Feature 24 ────────────────────────────────────────────────────────
        new FeatureSection("Ambient Occlusion Corners", "#9999BB",
            "Corner darkening where panels meet",
            "Radial dark gradients at each corner simulate contact shadow.",
            "EffectScale drives the AO radius; EffectIntensity controls darkness.",
            new CodeLine[] {
                new("node.WithStyle(s => {",                                     "#A8D8EA"),
                new("    s.Effect          = NodeEffect.AmbientOcclusion;",      "#C3E88D"),
                new("    s.EffectScale     = 1.0f;  // AO radius",               "#F78C6C"),
                new("    s.EffectIntensity = 0.45f; // darkness strength",       "#FFD9AA"),
                new("});",                                                        "#A8D8EA"),
            },
            "Panel depth", "Card stacking illusion", "Interior shadows",
            "Keep EffectIntensity under 0.5 for subtle depth that doesn't overpower content."),

        // ── Feature 25 ────────────────────────────────────────────────────────
        new FeatureSection("Bloom / Volumetric Glow", "#FFB86B",
            "Bright elements bleed light outward",
            "A blurred copy of the node border is drawn with Screen blend in front.",
            "Creates the impression of an emissive, luminous surface.",
            new CodeLine[] {
                new("node.WithStyle(s => {",                                     "#A8D8EA"),
                new("    s.Effect          = NodeEffect.Bloom;",                 "#C3E88D"),
                new("    s.EffectColor1    = PColor.FromHex(\"#FFB86B\");",      "#F78C6C"),
                new("    s.EffectScale     = 1.0f;  // blur radius scale",       "#FFD9AA"),
                new("    s.EffectIntensity = 0.45f; // bloom brightness",        "#C3E88D"),
                new("});",                                                        "#A8D8EA"),
            },
            "Neon signs", "Energy effects", "Highlighted selections",
            "Works best over a dark background. Reduce EffectIntensity if it looks washed out."),

        // ── Feature 26 ────────────────────────────────────────────────────────
        new FeatureSection("Text Wave", "#FF6BDD",
            "Characters undulate in a vertical sine wave",
            "Each character is drawn individually with a sinusoidal vertical offset.",
            "EffectSpeed drives wave frequency; EffectIntensity scales wave amplitude.",
            new CodeLine[] {
                new("node.WithText(\"Wave Text!\").WithStyle(s => {",            "#A8D8EA"),
                new("    s.Effect          = NodeEffect.TextWave;",              "#C3E88D"),
                new("    s.EffectSpeed     = 1.0f;  // wave frequency",          "#F78C6C"),
                new("    s.EffectIntensity = 0.35f; // amplitude (% font size)", "#FFD9AA"),
                new("});",                                                        "#A8D8EA"),
            },
            "Titles", "Celebration text", "Alert messages",
            "Use a large FontSize (18-24) so the amplitude looks proportional."),

        // ── Feature 27 ────────────────────────────────────────────────────────
        new FeatureSection("Shake", "#FF6B6B",
            "Node violently jitters then snaps back",
            "Calling node.Anim.Shake() starts a timed jitter that decays to zero.",
            "Uses a deterministic pseudo-random offset derived from the remaining duration.",
            new CodeLine[] {
                new("node.WithStyle(s => { s.Effect = NodeEffect.Shake; });",    "#A8D8EA"),
                new("// Trigger on any event:",                                  "#666688"),
                new("node.OnClick += _ => node.Anim.Shake(0.5f, 6f);",          "#C3E88D"),
                new("// duration = 0.5s, intensity = 6px",                       "#666688"),
                new("// Shake decays automatically — no cleanup needed",          "#666688"),
            },
            "Error states", "Invalid input", "Damage feedback",
            "ShakeIntensity of 4-8 px is enough for clear feedback without being jarring."),

        // ── Feature 28 ────────────────────────────────────────────────────────
        new FeatureSection("Heat Haze Warp", "#FFB86B",
            "Shimmering distortion overlay",
            "Perlin turbulence noise scrolls horizontally over the node in Overlay blend.",
            "Creates a heat-shimmer distortion effect without requiring pixel readback.",
            new CodeLine[] {
                new("node.WithStyle(s => {",                                     "#A8D8EA"),
                new("    s.Effect          = NodeEffect.HeatHaze;",              "#C3E88D"),
                new("    s.EffectScale     = 1.0f;  // noise scale",             "#F78C6C"),
                new("    s.EffectSpeed     = 1.0f;  // scroll speed",            "#FFD9AA"),
                new("    s.EffectIntensity = 0.50f; // blend strength",          "#C3E88D"),
                new("});",                                                        "#A8D8EA"),
            },
            "Lava zones", "Portals", "Heat sources",
            "Use on top of a colorful background for maximum visible distortion."),

        // ── Feature 29 ────────────────────────────────────────────────────────
        new FeatureSection("Slide Transition", "#6BDDFF",
            "Content slides in from the side",
            "NodeAnimState.SlideT (0→1) drives a horizontal canvas translate.",
            "SlideT auto-advances to 1 at 4/sec. Reset to 0 to replay the slide-in.",
            new CodeLine[] {
                new("// Trigger a slide-in by resetting SlideT:",                "#A8D8EA"),
                new("node.Anim.SlideT = 0f; // resets the animation",           "#C3E88D"),
                new("// SlideT advances to 1 via NodeAnimState.Update(dt)",      "#666688"),
                new("// For custom SlideT usage in your layout:",                "#666688"),
                new("float offset = (1f - node.Anim.SlideT) * -120f;",          "#F78C6C"),
                new("// Apply as margin or canvas translate",                    "#FFD9AA"),
            },
            "Page transitions", "Modal entry", "Notification slides",
            "SlideForward controls direction — set false for a right-to-left slide-out."),

        // ── Feature 30 ────────────────────────────────────────────────────────
        new FeatureSection("Card Flip", "#B06BFF",
            "Node flips on Y-axis revealing alternate content",
            "FlipT (0→1) drives canvas X-scale through cos(FlipT * PI) for a flip illusion.",
            "FlipT auto-advances to 1. Reset to 0 to re-trigger the flip animation.",
            new CodeLine[] {
                new("// Trigger flip on click:",                                 "#A8D8EA"),
                new("card.OnClick += _ => { card.Anim.FlipT = 0f; };",          "#C3E88D"),
                new("// FlipT auto-advances — no per-frame code needed",         "#666688"),
                new("// Use two content children; hide one when FlipT < 0.5:",  "#F78C6C"),
                new("front.Style.Opacity = card.Anim.FlipT < 0.5f ? 1f : 0f;", "#FFD9AA"),
                new("back.Style.Opacity  = card.Anim.FlipT >= 0.5f ? 1f : 0f;","#C3E88D"),
            },
            "Flashcards", "Item detail reveal", "Double-sided displays",
            "Swap NodeValue (or child visibility) at FlipT == 0.5 for a true two-sided card."),
    };

    public void Dispose()
    {
        _hdrSurf?.Dispose(); _hdrTex?.Dispose();
        _detSurf?.Dispose(); _detTex?.Dispose();

        _layout.Dispose();
        _renderer.Dispose();
    }
}
