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
/// Proof-of-concept window that renders a PanacheUI node tree using SkiaSharp
/// and displays the result via ImGui.Image — showcasing capabilities far
/// beyond vanilla ImGui. Automatically resizes to match the window content area.
/// </summary>
public sealed class DemoWindow : IDisposable
{
    public bool IsVisible = false;

    private readonly ITextureProvider _texProvider;
    private readonly HelpWindow       _help;
    private readonly Action           _openIconBrowser;

    private System.Numerics.Vector2? _windowPos;  // null = use ImGui default on first frame

    private PanacheSurface _surface;
    private Node           _root;

    private int          _surfaceW;
    private int          _surfaceH;
    private ImTextureID? _texHandle;
    private float        _animTime;

    // Layout snapshot for hit-testing
    private Dictionary<Node, LayoutBox> _lastLayout = new();
    private Node?                        _btnNode;

    // ── Right-click demo state ──────────────────────────────────────────────
    // All three live outside the node tree because the tree is rebuilt every frame —
    // exactly the same reason ContextMenuState exists as a standalone class.
    private bool   _rcLocked;
    private int    _rcColorIndex;
    private readonly ContextMenuState _rcMenu = new();

    private static readonly PColor[] RightClickCycleColors =
    {
        PColor.FromHex("#6BB8FF"), PColor.FromHex("#6BFFB8"),
        PColor.FromHex("#FFD46B"), PColor.FromHex("#FF6B8F"),
    };

    public DemoWindow(ITextureProvider texProvider, HelpWindow help, Action openIconBrowser)
    {
        _texProvider     = texProvider;
        _help            = help;
        _openIconBrowser = openIconBrowser;

        // Start with a reasonable default; will resize to window on first frame
        _surfaceW = 520;
        _surfaceH = 420;

        // PanacheSurface rather than the raw RenderSurface/TextureManager pair: it owns
        // the repaint gating and registers with PanacheStats, so this window shows up in
        // /panacheui stats like any consumer window. Building the pipeline by hand here
        // is exactly how this window used to escape both.
        _surface = new PanacheSurface(_texProvider, _surfaceW, _surfaceH);
        _surface.Stats.Label = "Demo";
        _root    = BuildTree(_surfaceW, _surfaceH);
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

            _surface.Resize(_surfaceW, _surfaceH);
            _root    = BuildTree(_surfaceW, _surfaceH);
            _btnNode = _root.FindById("btn-overview");
        }

        // Animate banner gradient each frame
        _animTime += ImGui.GetIO().DeltaTime;
        UpdateAnimatedNode();

        // Cursor position is stable until ImGui.Image consumes it, so sampling it here
        // gives the same origin the image will occupy — needed to put the mouse into
        // surface-local space before rendering.
        var origin     = ImGui.GetCursorScreenPos();
        var mousePos   = ImGui.GetMousePos();
        var localMouse = new Vector2(mousePos.X - origin.X, mousePos.Y - origin.Y);
        bool windowHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows
                                                  | ImGuiHoveredFlags.AllowWhenBlockedByPopup
                                                  | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        bool mouseDown  = ImGui.IsMouseDown(ImGuiMouseButton.Left);
        bool mouseClick = ImGui.IsMouseClicked(ImGuiMouseButton.Left) && windowHovered;
        bool rightDown  = ImGui.IsMouseDown(ImGuiMouseButton.Right);
        bool rightClick = ImGui.IsMouseClicked(ImGuiMouseButton.Right) && windowHovered;

        var (tex, layout) = _surface.Render(_root, _animTime, localMouse, mouseDown, mouseClick,
                                            ImGui.GetIO().MouseWheel, ImGui.GetIO().DeltaTime,
                                            forceRedraw: false,
                                            rightMouseDown: rightDown, rightMouseClicked: rightClick);
        _texHandle  = tex;
        _lastLayout = layout;

        if (_texHandle.HasValue)
        {
            var imagePos    = origin;
            ImGui.Image(_texHandle.Value, new Vector2(_surfaceW, _surfaceH));
            bool imageHovered = ImGui.IsItemHovered();

            var mouse = ImGui.GetMousePos();
            float mx = mouse.X - imagePos.X;
            float my = mouse.Y - imagePos.Y;

            // Close button bounds — a real node now (PUI.CloseButton, icon #0005), so its
            // own OnClick already fired during _surface.Render above; this lookup exists
            // only to exclude its box from the drag region below.
            var closeBtnNode = _root.FindById("btn-close");
            bool overClose = closeBtnNode != null
                          && _lastLayout.TryGetValue(closeBtnNode, out var closeBox)
                          && mx >= closeBox.X && mx <= closeBox.Right
                          && my >= closeBox.Y && my <= closeBox.Bottom;

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
        root.AppendChild(PUI.SectionDivider(PColor.FromHex("#FFFFFF").WithOpacity(0.05f)));
        root.AppendChild(BuildRightClickSection());
        root.AppendChild(PUI.SectionDivider(PColor.FromHex("#FFFFFF").WithOpacity(0.05f)));
        root.AppendChild(BuildIconsSection());
        root.AppendChild(BuildOverviewButton());

        // Close button — top-right corner overlay. The header above is a vertical title
        // block with nothing else competing for the top-right corner, so an absolute
        // overlay (rather than IconBrowserWindow's flow-appended button, which existed
        // specifically to fix a real overlap in THAT window's horizontal header) is the
        // right call here — same visual result, no restructuring the title block needs.
        const float CloseBtnSize = 24f, CloseBtnPad = 8f;
        root.AppendChild(PUI.CloseButton("btn-close", CloseBtnSize, PColor.White.WithOpacity(0.85f),
            () => IsVisible = false).WithStyle(s =>
        {
            s.Position = PositionMode.Absolute;
            s.Left = w - CloseBtnSize - CloseBtnPad;
            s.Top  = CloseBtnPad;
        }));

        // Appended last so it draws over every sibling above (same-ZIndex nodes paint in
        // child order) — required for an overlay regardless of how the rest of the tree
        // is arranged. Safe to append unconditionally: PUI.ContextMenu returns an inert
        // zero-size node while _rcMenu is closed.
        root.AppendChild(PUI.ContextMenu(_rcMenu, PColor.FromHex("#9966FF"), new[]
        {
            new ContextMenuItem("Reset lock",  () => _rcLocked = false),
            new ContextMenuItem("Reset color", () => _rcColorIndex = 0),
            ContextMenuItem.Separator,
            new ContextMenuItem("Open Help",   () => _help.IsVisible = true),
            new ContextMenuItem("(disabled)",  () => { }, enabled: false),
        }, w, h));

        return root;
    }

    // ── PanacheUI Icons ──────────────────────────────────────────────────────
    // Every bundled icon, by ID only, run through PUI.Icon exactly as a consumer plugin
    // would call it. Wrapping FlowWrap keeps this readable regardless of window width.

    // A short teaser row (not the full library — that's what the browser is for) plus a
    // button opening IconBrowserWindow: the real horizontally-scrolling, scale-adjustable
    // view. Cramming every icon in here made the demo window tall and defeated the point
    // of having a dedicated browser at all.
    private Node BuildIconsSection()
    {
        var accent = PColor.FromHex("#6BFFB8");
        var allIds = PanacheIcons.AllIds();

        var content = new Node().WithStyle(s =>
        {
            s.Flow       = Flow.Vertical;
            s.WidthMode  = SizeMode.Fill;
            s.HeightMode = SizeMode.Fit;
            s.Padding    = new EdgeSize(10, 14);
            s.Gap        = 8;
        });

        content.AppendChild(PUI.SectionLabel($"PANACHE ICONS — {allIds.Count} AVAILABLE", accent));

        var row = new Node().WithStyle(s =>
        {
            s.Flow = Flow.Horizontal;
            s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
            s.Gap = 10;
        });

        const int TeaserCount = 8;
        for (int i = 0; i < allIds.Count && i < TeaserCount; i++)
            row.AppendChild(PUI.Icon(allIds[i], 22f, tint: accent.WithOpacity(0.85f)));

        if (allIds.Count > TeaserCount)
            row.AppendChild(new Node().WithText($"+{allIds.Count - TeaserCount} more").WithStyle(s =>
            {
                s.WidthMode  = SizeMode.Fit; s.HeightMode = SizeMode.Fit;
                s.FontSize   = 10f;
                s.Color      = Theme.TextMuted;
            }));

        row.AppendChild(new Node().WithStyle(s => { s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; }));

        var browseBtn = PUI.PillButton("btn-browse-icons", "Browse Icons →", accent);
        browseBtn.OnClick += _ => _openIconBrowser();
        row.AppendChild(browseBtn);

        content.AppendChild(row);
        return PUI.SectionWrap(accent, content);
    }

    // ── Right-click demo ─────────────────────────────────────────────────────
    // Three examples of what OnRightClick enables, matching Trist's request exactly:
    //   1. right-click opens a context menu       → the card's own right-click
    //   2. right-click locks/unlocks a window      → the lock pill
    //   3. right-click cycles through colors       → the color pill

    private Node BuildRightClickSection()
    {
        var accent = PColor.FromHex("#FFB86B");

        var content = new Node().WithStyle(s =>
        {
            s.Flow       = Flow.Vertical;
            s.WidthMode  = SizeMode.Fill;
            s.HeightMode = SizeMode.Fit;
            s.Padding    = new EdgeSize(10, 14);
            s.Gap        = 8;
        });

        content.AppendChild(PUI.SectionLabel("RIGHT-CLICK", accent));

        var row = new Node().WithStyle(s =>
        {
            s.Flow = Flow.Horizontal; s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit; s.Gap = 10;
        });

        // 1. Lock / unlock toggle.
        var lockPill = new Node().WithId("rc-lock").WithText(_rcLocked ? "🔒 Locked" : "🔓 Unlocked").WithStyle(s =>
        {
            s.WidthMode       = SizeMode.Fill;
            s.HeightMode      = SizeMode.Fit;
            s.BackgroundColor = (_rcLocked ? PColor.FromHex("#DD6B6B") : accent).WithOpacity(0.16f);
            s.BorderColor     = (_rcLocked ? PColor.FromHex("#DD6B6B") : accent).WithOpacity(0.55f);
            s.BorderWidth     = 1;
            s.BorderRadius    = 6;
            s.Padding         = new EdgeSize(8, 10);
            s.FontSize        = 11f;
            s.Bold            = true;
            s.Color           = PColor.White.WithOpacity(0.92f);
            s.TextAlign       = TextAlign.Center;
        });
        lockPill.IsInteractive = true;
        lockPill.OnRightClick += (_, _, _) => _rcLocked = !_rcLocked;
        row.AppendChild(lockPill);

        // 2. Color cycle.
        var cycleColor = RightClickCycleColors[_rcColorIndex % RightClickCycleColors.Length];
        var colorPill = new Node().WithId("rc-color").WithText("Right-click to cycle").WithStyle(s =>
        {
            s.WidthMode       = SizeMode.Fill;
            s.HeightMode      = SizeMode.Fit;
            s.BackgroundColor = cycleColor.WithOpacity(0.20f);
            s.BorderColor     = cycleColor.WithOpacity(0.65f);
            s.BorderWidth     = 1;
            s.BorderRadius    = 6;
            s.Padding         = new EdgeSize(8, 10);
            s.FontSize        = 11f;
            s.Bold            = true;
            s.Color           = cycleColor;
            s.TextAlign       = TextAlign.Center;
        });
        colorPill.IsInteractive = true;
        colorPill.OnRightClick += (_, _, _) => _rcColorIndex = (_rcColorIndex + 1) % RightClickCycleColors.Length;
        row.AppendChild(colorPill);

        // 3. Context menu.
        var menuPill = new Node().WithId("rc-menu").WithText("Right-click for menu").WithStyle(s =>
        {
            s.WidthMode       = SizeMode.Fill;
            s.HeightMode      = SizeMode.Fit;
            s.BackgroundColor = accent.WithOpacity(0.16f);
            s.BorderColor     = accent.WithOpacity(0.55f);
            s.BorderWidth     = 1;
            s.BorderRadius    = 6;
            s.Padding         = new EdgeSize(8, 10);
            s.FontSize        = 11f;
            s.Bold            = true;
            s.Color           = PColor.White.WithOpacity(0.92f);
            s.TextAlign       = TextAlign.Center;
        });
        menuPill.IsInteractive = true;
        menuPill.OnRightClick += (_, x, y) => _rcMenu.Open(x, y, tag: "rc-menu");
        row.AppendChild(menuPill);

        content.AppendChild(row);
        return PUI.SectionWrap(accent, content);
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
        // PanacheSurface owns the layout engine, renderer and texture manager.
        _surface.Dispose();
    }
}
