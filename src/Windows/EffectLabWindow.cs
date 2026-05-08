using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using PanacheUI.Core;
using PanacheUI.Layout;
using PanacheUI.Rendering;
using ImTextureID = Dalamud.Bindings.ImGui.ImTextureID;

namespace PanacheUI.Windows;

/// <summary>
/// Floating debug window for live-tuning effect parameters without rebuilding.
/// Opens via /panacheui lab. All sliders feed directly into the preview node.
/// </summary>
public sealed class EffectLabWindow : IDisposable
{
    public bool IsVisible = false;

    private const int PreviewW = 360;
    private const int PreviewH = 160;

    private readonly ITextureProvider _texProvider;
    private readonly LayoutEngine     _layout   = new();
    private readonly SkiaRenderer     _renderer = new();

    private RenderSurface?  _surf;
    private TextureManager? _tex;
    private ImTextureID?    _handle;
    private float           _animTime;
    private float           _lastRender = -99f;
    private bool            _animating  = false;

    private Vector2? _windowPos;

    // Tunable params
    private NodeEffect _effect    = NodeEffect.PerlinNoise;
    private Vector4    _color1    = new(0.42f, 0.83f, 1.00f, 1f);
    private Vector4    _color2    = new(0.50f, 0.30f, 1.00f, 1f);
    private float      _scale     = 1.0f;
    private float      _speed     = 0.4f;
    private float      _intensity = 0.35f;

    public EffectLabWindow(ITextureProvider texProvider)
    {
        _texProvider = texProvider;
        _surf = new RenderSurface(PreviewW, PreviewH);
        _tex  = new TextureManager(_texProvider);
    }

    public void Draw()
    {
        if (!IsVisible) return;

        ImGui.SetNextWindowSize(new Vector2(420, 480), ImGuiCond.FirstUseEver);
        if (_windowPos.HasValue)
            ImGui.SetNextWindowPos(_windowPos.Value, ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoTitleBar
                  | ImGuiWindowFlags.NoScrollbar
                  | ImGuiWindowFlags.NoScrollWithMouse;
        if (!ImGui.Begin("##panacheui_lab", ref IsVisible, flags))
        {
            ImGui.End();
            return;
        }

        if (!_windowPos.HasValue)
            _windowPos = ImGui.GetWindowPos();

        _animTime += Math.Min(ImGui.GetIO().DeltaTime, 0.05f);

        // ── Header bar (drag handle) ──────────────────────────────────────────
        var headerPos = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(
            headerPos,
            headerPos + new Vector2(ImGui.GetContentRegionAvail().X, 36f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.08f, 0.06f, 0.18f, 1f)));

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 8f);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12f);
        ImGui.TextColored(new Vector4(0.90f, 0.75f, 1.00f, 1f), "Effect Lab");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.50f, 0.50f, 0.70f, 1f), "— live parameter tuning");

        // Drag on header
        var mouse = ImGui.GetMousePos();
        float mxH = mouse.X - headerPos.X;
        float myH = mouse.Y - headerPos.Y;
        if (mxH >= 0 && mxH < ImGui.GetContentRegionAvail().X && myH >= 0 && myH < 36f
         && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            _windowPos = (_windowPos ?? ImGui.GetWindowPos()) + ImGui.GetIO().MouseDelta;

        ImGui.Spacing();

        // ── Preview surface ───────────────────────────────────────────────────
        bool timeExpired = _animating && (_animTime - _lastRender) > 0.033f;
        if (_handle == null || timeExpired)
        {
            _lastRender = _animTime;
            var previewNode = BuildPreviewNode();
            var map = _layout.Compute(previewNode, PreviewW, PreviewH);
            _renderer.Render(_surf!.Canvas, previewNode, map, _animTime);
            _handle = _tex!.Upload(_surf);
        }

        var imgPos = ImGui.GetCursorScreenPos();
        if (_handle.HasValue)
            ImGui.Image(_handle.Value, new Vector2(PreviewW, PreviewH));

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // ── Controls ─────────────────────────────────────────────────────────
        ImGui.PushItemWidth(260f);

        // Effect selector
        ImGui.Text("Effect");
        ImGui.SameLine(100);
        int effectIdx = (int)_effect;
        string[] effectNames = Enum.GetNames<NodeEffect>();
        if (ImGui.Combo("##effect", ref effectIdx, effectNames, effectNames.Length))
        {
            _effect  = (NodeEffect)effectIdx;
            _handle  = null;
        }

        ImGui.Spacing();

        // Color pickers
        ImGui.Text("Color 1");
        ImGui.SameLine(100);
        if (ImGui.ColorEdit4("##c1", ref _color1, ImGuiColorEditFlags.NoInputs))
            _handle = null;

        ImGui.Text("Color 2");
        ImGui.SameLine(100);
        if (ImGui.ColorEdit4("##c2", ref _color2, ImGuiColorEditFlags.NoInputs))
            _handle = null;

        ImGui.Spacing();

        // Sliders
        ImGui.Text("Scale");
        ImGui.SameLine(100);
        if (ImGui.SliderFloat("##scale", ref _scale, 0.1f, 5f))
            _handle = null;

        ImGui.Text("Speed");
        ImGui.SameLine(100);
        if (ImGui.SliderFloat("##speed", ref _speed, 0f, 3f))
            _handle = null;

        ImGui.Text("Intensity");
        ImGui.SameLine(100);
        if (ImGui.SliderFloat("##intensity", ref _intensity, 0f, 1f))
            _handle = null;

        ImGui.PopItemWidth();

        ImGui.Spacing();

        // Animate toggle
        bool anim = _animating;
        if (ImGui.Checkbox("Animate", ref anim))
        {
            _animating = anim;
            _handle = null;
        }

        ImGui.End();
    }

    private Node BuildPreviewNode()
    {
        var c1 = new PColor(
            (byte)(_color1.X * 255), (byte)(_color1.Y * 255),
            (byte)(_color1.Z * 255), (byte)(_color1.W * 255));
        var c2 = new PColor(
            (byte)(_color2.X * 255), (byte)(_color2.Y * 255),
            (byte)(_color2.Z * 255), (byte)(_color2.W * 255));

        return new Node().WithId("lab-root").WithStyle(s =>
        {
            s.WidthMode       = SizeMode.Fixed; s.Width  = PreviewW;
            s.HeightMode      = SizeMode.Fixed; s.Height = PreviewH;
            s.Flow            = Flow.Vertical;
            s.BackgroundColor = PColor.FromHex("#0F0F22");
            s.BorderRadius    = 6;
            s.Effect          = _effect;
            s.EffectColor1    = c1;
            s.EffectColor2    = c2;
            s.EffectScale     = _scale;
            s.EffectSpeed     = _speed;
            s.EffectIntensity = _intensity;
            s.Padding         = new EdgeSize(10, 14);
        }).WithChildren(
            new Node().WithText(_effect.ToString()).WithStyle(s =>
            {
                s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                s.FontSize = 18f; s.Bold = true;
                s.Color = c1.WithOpacity(0.90f);
            }),
            new Node().WithText($"Scale {_scale:F2}  ·  Speed {_speed:F2}  ·  Intensity {_intensity:F2}").WithStyle(s =>
            {
                s.WidthMode = SizeMode.Fill; s.HeightMode = SizeMode.Fit;
                s.FontSize = 11f;
                s.Color = PColor.FromHex("#9999BB");
            })
        );
    }

    public void Dispose()
    {
        _surf?.Dispose();
        _tex?.Dispose();
        _layout.Dispose();
        _renderer.Dispose();
    }
}
