using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using PanacheUI.Components;
using PanacheUI.Core;
using PanacheUI.Icons;
using PanacheUI.Layout;
using PanacheUI.Rendering;
using ImTextureID = Dalamud.Bindings.ImGui.ImTextureID;

namespace PanacheUI.Windows;

/// <summary>
/// Browses every icon in <see cref="PanacheIcons"/> ten at a time — page 1 is
/// <c>#0001</c>–<c>#0010</c>, page 2 is <c>#0011</c>–<c>#0020</c>, and so on — with a
/// scale picker so a consumer can eyeball how an icon reads at the actual size they're
/// about to use it at. Opens via <c>/panacheui icons</c> or the "Browse Icons" button in
/// the demo window's Icons section.
/// </summary>
/// <remarks>
/// This window used to be a single horizontally-scrolling strip (real
/// <see cref="Core.Style.OverflowX"/> scroll, plus a draggable <see cref="PUI.ScrollbarX"/>
/// once the first cut's undersized hit-area got fixed). Both remain genuine, tested
/// framework capabilities and are the right call for some UIs — but for browsing a flat,
/// ever-growing icon list specifically, continuous scrolling meant the experience hinged
/// on wheel-speed tuning and drag precision, and it kept not landing well in practice.
/// Paging sidesteps that entirely: Prev/Next are unambiguous, discrete, and impossible to
/// "not quite" hit.
/// </remarks>
public sealed class IconBrowserWindow : IDisposable
{
    public bool IsVisible;

    private static readonly PColor Accent = PColor.FromHex("#6BFFB8");

    // Discrete scale presets rather than a slider — "a few sizes to eyeball" reads faster
    // than dialing in an arbitrary float, and it's what "view at different scales" asks for.
    private static readonly (string Label, float Size)[] ScalePresets =
    {
        ("S", 28f), ("M", 44f), ("L", 64f), ("XL", 96f),
    };
    private int _scaleIndex = 1;   // "M" by default

    private const int   PageSize    = 10;
    private const int   HeaderH     = 40;
    private const int   FooterH     = 40;
    private const float PadX        = 14f;
    private const string CloseBtnId = "close-btn";
    private const string PrevBtnId  = "page-prev";
    private const string NextBtnId  = "page-next";

    private int _currentPage;   // 0-indexed internally; shown 1-indexed

    private readonly ITextureProvider _texProvider;
    private PanacheSurface _surface;
    private Node            _root;

    private int          _surfaceW;
    private int          _surfaceH;
    private ImTextureID? _texHandle;
    private Dictionary<Node, LayoutBox> _lastLayout = new();
    private Vector2?     _windowPos;

    public IconBrowserWindow(ITextureProvider texProvider)
    {
        _texProvider = texProvider;
        _surfaceW = 620;
        _surfaceH = 260;
        _surface  = new PanacheSurface(_texProvider, _surfaceW, _surfaceH);
        _surface.Stats.Label = "IconBrowser";
        _root = BuildTree(_surfaceW, _surfaceH);
    }

    public void Draw()
    {
        if (!IsVisible) return;

        ImGui.SetNextWindowSize(new Vector2(640, 300), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(360, 220), new Vector2(1600, 900));
        if (_windowPos.HasValue)
            ImGui.SetNextWindowPos(_windowPos.Value, ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoTitleBar
                  | ImGuiWindowFlags.NoScrollbar
                  | ImGuiWindowFlags.NoScrollWithMouse;
        if (!ImGui.Begin("##panacheui_icon_browser", ref IsVisible, flags))
        {
            ImGui.End();
            return;
        }

        if (!_windowPos.HasValue)
            _windowPos = ImGui.GetWindowPos();

        var avail = ImGui.GetContentRegionAvail();
        int newW = Math.Max(200, (int)avail.X);
        int newH = Math.Max(160, (int)avail.Y);
        if (newW != _surfaceW || newH != _surfaceH)
        {
            _surfaceW = newW;
            _surfaceH = newH;
            _surface.Resize(_surfaceW, _surfaceH);
        }

        _root = BuildTree(_surfaceW, _surfaceH);

        var origin      = ImGui.GetCursorScreenPos();
        var mousePos    = ImGui.GetMousePos();
        var localMouse  = new Vector2(mousePos.X - origin.X, mousePos.Y - origin.Y);
        bool windowHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows
                                                  | ImGuiHoveredFlags.AllowWhenBlockedByPopup
                                                  | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        bool mouseDown  = ImGui.IsMouseDown(ImGuiMouseButton.Left);
        bool mouseClick = ImGui.IsMouseClicked(ImGuiMouseButton.Left) && windowHovered;

        var (tex, layout) = _surface.Render(_root, 0f, localMouse, mouseDown, mouseClick,
            dt: ImGui.GetIO().DeltaTime, forceRedraw: false);
        _texHandle  = tex;
        _lastLayout = layout;

        if (_texHandle.HasValue)
        {
            var imagePos = origin;
            ImGui.Image(_texHandle.Value, new Vector2(_surfaceW, _surfaceH));
            bool imageHovered = ImGui.IsItemHovered();

            // Drag anywhere on the header band except the close button (its own box, not
            // a hand-tracked pixel rect — the button is a real flow-laid-out node now).
            var closeBtn = _root.FindById(CloseBtnId);
            bool overClose = closeBtn != null && _lastLayout.TryGetValue(closeBtn, out var closeBox)
                          && localMouse.X >= closeBox.X && localMouse.X <= closeBox.Right
                          && localMouse.Y >= closeBox.Y && localMouse.Y <= closeBox.Bottom;

            bool overHeader = localMouse.Y >= 0 && localMouse.Y < HeaderH;
            if (!overClose && overHeader && imageHovered && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                var delta = ImGui.GetIO().MouseDelta;
                _windowPos = (_windowPos ?? ImGui.GetWindowPos()) + delta;
            }
        }

        ImGui.End();
    }

    // ── Node tree ────────────────────────────────────────────────────────────

    private Node BuildTree(int w, int h)
    {
        var ids = PanacheIcons.AllIds();
        int totalPages = Math.Max(1, (ids.Count + PageSize - 1) / PageSize);
        _currentPage = Math.Clamp(_currentPage, 0, totalPages - 1);

        var root = PUI.RootNode(w, h);
        root.AppendChild(BuildHeader(ids.Count));
        root.AppendChild(BuildPage(ids, w, h - HeaderH - FooterH));
        root.AppendChild(BuildFooter(totalPages));
        return root;
    }

    private Node BuildHeader(int totalIconCount)
    {
        var header = new Node().WithStyle(s =>
        {
            s.Flow            = Flow.Horizontal;
            s.WidthMode       = SizeMode.Fill;
            s.HeightMode      = SizeMode.Fixed; s.Height = HeaderH;
            s.BackgroundColor = PColor.FromHex("#12101A");
            s.Padding         = new EdgeSize(0, PadX);
            s.Gap             = 10;
        });

        header.AppendChild(new Node().WithText($"PanacheUI Icons — {totalIconCount}").WithStyle(s =>
        {
            s.WidthMode  = SizeMode.Fit;
            s.HeightMode = SizeMode.Fill;
            s.FontSize   = 13f;
            s.Bold       = true;
            s.Color      = Accent;
        }));

        // Spacer pushes the scale picker and close button to the right.
        header.AppendChild(new Node().WithStyle(s => { s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; }));

        var scaleGroup = new Node().WithStyle(s =>
        {
            s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fit; s.HeightMode = SizeMode.Fill; s.Gap = 4;
        });
        for (int i = 0; i < ScalePresets.Length; i++)
        {
            bool active = i == _scaleIndex;
            int captured = i;   // closures below must not all capture the shared loop var
            var pill = new Node().WithId($"scale-pill-{i}").WithText(ScalePresets[i].Label).WithStyle(s =>
            {
                s.WidthMode             = SizeMode.Fixed; s.Width = 30;
                s.HeightMode            = SizeMode.Fixed; s.Height = 22;
                s.BackgroundColor       = active ? Accent.WithOpacity(0.28f) : PColor.White.WithOpacity(0.06f);
                s.BorderColor           = active ? Accent.WithOpacity(0.75f) : PColor.White.WithOpacity(0.15f);
                s.BorderWidth           = 1;
                s.BorderRadius          = 5;
                s.FontSize              = 10.5f;
                s.Bold                  = true;
                s.Color                 = active ? Accent : Theme.TextMuted;
                s.TextAlign             = TextAlign.Center;
                s.Flow                  = Flow.Horizontal;
            });
            pill.IsInteractive = true;
            // Mutating _scaleIndex from inside this handler is safe even though it fires
            // mid-Render: layout for THIS frame is already computed by the time
            // InteractionManager (and therefore this click) runs, so the change is only
            // ever visible starting next frame's BuildTree — exactly like every other
            // rebuild-driven state change in this codebase (ContextMenu selection, page
            // navigation below, etc.).
            pill.OnClick += _ => _scaleIndex = captured;
            scaleGroup.AppendChild(pill);
        }
        header.AppendChild(scaleGroup);

        // Trailing flow child — normal layout reserves its space automatically, so it can
        // never overlap the scale pills the way an independently-computed absolute overlay
        // could (and did).
        header.AppendChild(PUI.CloseButton(CloseBtnId, 24f, PColor.White.WithOpacity(0.85f), () => IsVisible = false));

        return header;
    }

    private Node BuildPage(IReadOnlyList<int> ids, int w, int pageH)
    {
        var page = new Node().WithStyle(s =>
        {
            s.Flow            = Flow.Horizontal;
            s.FlowWrap         = true;
            s.WidthMode       = SizeMode.Fixed; s.Width = w;
            s.HeightMode      = SizeMode.Fixed; s.Height = Math.Max(1, pageH);
            s.BackgroundColor = PColor.FromHex("#0A0914");
            s.Padding         = new EdgeSize(10, PadX);
            s.Gap             = 8;
            // Defensive: at a large scale + a short window, 10 tiles might not all fit
            // vertically. Clipping the overflow beats letting icons spill past the
            // window's own bounds — the scale picker is right there to size back down.
            s.ClipContent     = true;
        });

        float size  = ScalePresets[_scaleIndex].Size;
        float cellW = size + 16f;
        float cellH = MathF.Max(60f, size + 26f);

        int start = _currentPage * PageSize;
        int end   = Math.Min(start + PageSize, ids.Count);
        for (int i = start; i < end; i++)
        {
            int id = ids[i];
            var tile = new Node().WithStyle(s =>
            {
                s.Flow         = Flow.Vertical;
                s.WidthMode    = SizeMode.Fixed; s.Width  = cellW;
                s.HeightMode   = SizeMode.Fixed; s.Height = cellH;
                s.Padding      = new EdgeSize(6, 4);
                s.Gap          = 4;
                s.BorderRadius = 6f;
            });

            var iconWrap = new Node().WithStyle(s =>
            {
                s.WidthMode  = SizeMode.Fill;
                s.HeightMode = SizeMode.Fixed; s.Height = size;
                s.Flow       = Flow.Horizontal;
                s.Padding    = new EdgeSize(0, (cellW - 8f - size) / 2f);
            });
            iconWrap.AppendChild(PUI.Icon(id, size, tint: Accent.WithOpacity(0.92f)));
            tile.AppendChild(iconWrap);

            tile.AppendChild(new Node().WithText($"#{id:0000}").WithStyle(s =>
            {
                s.WidthMode  = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                s.FontSize   = 8.5f;
                s.Color      = Theme.TextMuted;
                s.TextAlign  = TextAlign.Center;
            }));

            page.AppendChild(tile);
        }

        return page;
    }

    private Node BuildFooter(int totalPages)
    {
        var footer = new Node().WithStyle(s =>
        {
            s.Flow            = Flow.Horizontal;
            s.WidthMode       = SizeMode.Fill;
            s.HeightMode      = SizeMode.Fixed; s.Height = FooterH;
            s.BackgroundColor = PColor.FromHex("#12101A");
            s.Padding         = new EdgeSize(0, PadX);
            s.Gap             = 10;
        });

        bool hasPrev = _currentPage > 0;
        bool hasNext = _currentPage < totalPages - 1;

        footer.AppendChild(PageNavButton(PrevBtnId, "‹ Prev", hasPrev, () => _currentPage--));
        footer.AppendChild(new Node().WithStyle(s => { s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; }));
        footer.AppendChild(new Node().WithText($"Page {_currentPage + 1} of {totalPages}").WithStyle(s =>
        {
            s.WidthMode  = SizeMode.Fit; s.HeightMode = SizeMode.Fill;
            s.FontSize   = 11f;
            s.Bold       = true;
            s.Color      = Theme.TextMuted;
            s.TextAlign  = TextAlign.Center;
        }));
        footer.AppendChild(new Node().WithStyle(s => { s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; }));
        footer.AppendChild(PageNavButton(NextBtnId, "Next ›", hasNext, () => _currentPage++));

        return footer;
    }

    private static Node PageNavButton(string id, string label, bool enabled, Action onClick)
    {
        var btn = new Node().WithId(id).WithText(label).WithStyle(s =>
        {
            s.WidthMode             = SizeMode.Fit;
            s.HeightMode            = SizeMode.Fixed; s.Height = 24;
            s.BackgroundColor       = enabled ? Accent.WithOpacity(0.16f) : PColor.White.WithOpacity(0.04f);
            s.BorderColor           = enabled ? Accent.WithOpacity(0.55f) : PColor.White.WithOpacity(0.10f);
            s.BorderWidth           = 1;
            s.BorderRadius          = 6;
            s.Padding               = new EdgeSize(4, 14);
            s.FontSize              = 11f;
            s.Bold                  = true;
            s.Color                 = enabled ? Accent : Theme.TextMuted.WithOpacity(0.5f);
            s.TextAlign             = TextAlign.Center;
            s.Flow                  = Flow.Horizontal;
        });
        // No IsInteractive/OnClick at all when disabled — a boundary page's Prev/Next is
        // genuinely inert, not just styled to look that way (same convention as
        // ContextMenu's disabled items).
        if (enabled)
        {
            btn.IsInteractive = true;
            btn.OnClick += _ => onClick();
        }
        return btn;
    }

    public void Dispose()
    {
        InteractionManager.ReleaseCapture();
        _surface.Dispose();
    }
}
