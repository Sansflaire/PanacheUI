using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using PanacheUI.Core;
using PanacheUI.Diagnostics;
using PanacheUI.Layout;

namespace PanacheUI.Rendering;

/// <summary>
/// Convenience wrapper that owns the full PanacheUI rendering pipeline for a single surface:
/// RenderSurface + LayoutEngine + SkiaRenderer + TextureManager.
///
/// Replaces the four-object setup (create surface, create layout engine, create renderer,
/// create texture manager) with a single object and a single Render() call per frame.
///
/// <para><b>Repaint gating.</b> Layout and interaction run every frame — they are cheap
/// and hit-testing needs them — but rasterising the tree, reading the pixels back and
/// uploading a new GPU texture only happen when the surface would actually look
/// different. See <see cref="SurfaceFingerprint"/> for how "different" is decided and
/// what it deliberately cannot detect.</para>
/// </summary>
public sealed class PanacheSurface : IDisposable
{
    private RenderSurface  _surface;
    private readonly LayoutEngine   _layout   = new();
    private readonly SkiaRenderer   _renderer = new();
    private readonly TextureManager _textures;
    private bool _disposed;

    private ulong _lastFingerprint;
    private bool  _hasPainted;

    /// <summary>Physical pixel width of the backing bitmap — the size to blit at.</summary>
    public int Width  { get; private set; }

    /// <summary>Physical pixel height of the backing bitmap — the size to blit at.</summary>
    public int Height { get; private set; }

    private float _scale = 1f;

    /// <summary>
    /// UI scale factor. 1.0 (default) is 1 layout unit per physical pixel; 1.5 makes
    /// everything half again as large without any node needing to know.
    /// </summary>
    /// <remarks>
    /// <para><b>This scales the layout, not the bitmap.</b> The tree is laid out against a
    /// viewport of <see cref="LogicalWidth"/> × <see cref="LogicalHeight"/> — the physical
    /// size divided by the scale — and the canvas is then scaled before the tree is
    /// painted, so Skia rasterises every glyph at its <i>effective</i> size and text stays
    /// crisp. Rendering at native size and stretching the resulting texture would be blurry
    /// at exactly the scales people actually want.</para>
    ///
    /// <para><b>Mouse coordinates are divided for you.</b> <see cref="Render"/> converts the
    /// surface-local position it is handed into logical space before interaction runs, so
    /// callers keep passing the same physical-pixel position they always did. The layout
    /// dictionary it returns is in <i>logical</i> units, though — code that hit-tests those
    /// boxes by hand against a raw ImGui mouse position must convert it first, with
    /// <see cref="ToLogical(System.Numerics.Vector2)"/>. Getting that wrong makes every
    /// click land somewhere other than where it looks, which is a baffling failure rather
    /// than an obvious one.</para>
    ///
    /// <para>Values are clamped to a sane range; 0 or negative would divide the viewport to
    /// nothing. Changing it invalidates the surface.</para>
    /// </remarks>
    public float Scale
    {
        get => _scale;
        set
        {
            float clamped = Math.Clamp(value, 0.25f, 8f);
            if (Math.Abs(clamped - _scale) < 0.0001f) return;
            _scale = clamped;
            Invalidate();
        }
    }

    /// <summary>Width of the layout viewport in logical units — <see cref="Width"/> ÷ <see cref="Scale"/>.</summary>
    public float LogicalWidth => Width / _scale;

    /// <summary>Height of the layout viewport in logical units — <see cref="Height"/> ÷ <see cref="Scale"/>.</summary>
    public float LogicalHeight => Height / _scale;

    /// <summary>
    /// Convert a surface-local position in physical pixels (e.g. ImGui's mouse position
    /// minus the image's screen origin) into the logical coordinate space the returned
    /// layout boxes live in.
    /// </summary>
    public Vector2 ToLogical(Vector2 surfaceLocal) => surfaceLocal / _scale;

    /// <summary><see cref="ToLogical"/>'s inverse — logical units back to physical pixels.</summary>
    public Vector2 ToPhysical(Vector2 logical) => logical * _scale;

    /// <summary>
    /// Repaint on every frame, bypassing the fingerprint check entirely.
    /// </summary>
    /// <remarks>
    /// Only needed when a surface's appearance depends on something the fingerprint
    /// cannot see — most realistically, an <see cref="Core.Style.ImageBitmap"/> whose
    /// pixels are rewritten in place behind a stable object reference. Prefer a single
    /// <see cref="Invalidate"/> call at the moment of the change; this property gives
    /// back the old always-repaint cost for the whole lifetime of the surface.
    /// </remarks>
    public bool AlwaysRedraw { get; set; }

    /// <summary>True when the last <see cref="Render"/> call actually rasterised and uploaded.</summary>
    public bool LastFrameRepainted { get; private set; }

    /// <summary>
    /// Maximum repaints per second for <i>purely time-driven</i> animation. Default 30.
    /// Set to 0 for uncapped (repaint on every host frame).
    /// </summary>
    /// <remarks>
    /// <para>This throttles one thing only: surfaces whose appearance is changing because
    /// a <see cref="Core.NodeEffect"/> is advancing on the clock — a cycling gradient, a
    /// pulsing glow, drifting noise. Anything driven by <i>content</i> — a click, a hover,
    /// a scroll, new data, a resize — moves the tree fingerprint and repaints immediately
    /// at the full frame rate, whatever this is set to. Interaction never feels throttled.</para>
    ///
    /// <para>The reason for a cap at all: a decorative gradient cycling its hue looks
    /// identical at 30 Hz and at 144 Hz, but at 144 Hz it costs nearly five times as much.
    /// A full rasterise plus pixel readback plus a fresh GPU texture is roughly a
    /// millisecond for a mid-sized window, and at high frame rates a millisecond is worth
    /// ten frames per second. Paying that for animation nobody can perceive is the single
    /// most expensive thing a Panache window can do.</para>
    /// </remarks>
    public int AnimationFpsCap { get; set; } = 30;

    private long _lastAnimPaintTick;

    private readonly SurfaceStats _stats;

    /// <summary>
    /// Live cost accounting for this surface — phase timings and repaint ratio.
    /// Also exposed process-wide at <c>GET http://localhost:17779/stats</c>.
    /// </summary>
    public SurfaceStats Stats => _stats;

    public PanacheSurface(ITextureProvider texProvider, int width, int height)
    {
        Width    = width;
        Height   = height;
        _surface = new RenderSurface(width, height);
        _textures = new TextureManager(texProvider);
        _stats    = PanacheStats.Register(OwningAssemblyName(), width, height);
    }

    /// <summary>
    /// Name of the plugin assembly that constructed this surface, so the stats readout can
    /// say "GlamourDresserHelper is costing you 2 ms" rather than "some surface is".
    /// Resolved once at construction; never on a per-frame path.
    /// </summary>
    /// <remarks>
    /// Walks out of PanacheUI's own frames to find the caller. Runtime frames are skipped
    /// too: PanacheUI's built-in windows live inside PanacheUI itself, so the first
    /// non-PanacheUI frame above them is whatever CLR machinery invoked the plugin —
    /// reporting "System.Private.CoreLib" as the owner would be worse than useless.
    /// </remarks>
    private static string OwningAssemblyName()
    {
        try
        {
            var trace = new StackTrace(fNeedFileInfo: false);
            var self  = typeof(PanacheSurface).Assembly;
            for (int i = 0; i < trace.FrameCount; i++)
            {
                var asm = trace.GetFrame(i)?.GetMethod()?.DeclaringType?.Assembly;
                if (asm == null || asm == self) continue;

                var name = asm.GetName().Name;
                if (string.IsNullOrEmpty(name)) continue;
                if (IsRuntimeAssembly(name)) continue;

                return name;
            }
        }
        catch
        {
            // Diagnostics must never be able to break a surface.
        }
        // Nothing but PanacheUI and the runtime on the stack — this is one of Panache's
        // own windows.
        return "PanacheUI";
    }

    private static bool IsRuntimeAssembly(string name) =>
        name.StartsWith("System.", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.", StringComparison.Ordinal)
        || name is "mscorlib" or "netstandard" or "Dalamud";

    /// <summary>
    /// Force the next <see cref="Render"/> to repaint, whatever the fingerprint says.
    /// </summary>
    public void Invalidate()
    {
        _hasPainted = false;
    }

    /// <summary>
    /// Resize the surface to a new pixel dimensions.
    /// Destroys and recreates the RenderSurface; the next Render() will upload a fresh texture.
    /// </summary>
    public void Resize(int width, int height)
    {
        if (Width == width && Height == height) return;
        _surface.Dispose();
        _surface = new RenderSurface(width, height);
        Width  = width;
        Height = height;
        Invalidate();   // the old texture is the wrong size — never reuse it
    }

    /// <summary>
    /// Run the full pipeline in one call: layout → interaction → repaint (if needed) → upload.
    ///
    /// Returns the ImGui texture handle to pass to <c>ImGui.Image()</c> and the layout dict
    /// for manual hit-testing or position queries.
    /// </summary>
    /// <param name="root">Root of the UI tree.</param>
    /// <param name="time">Elapsed seconds — drives animated effects.</param>
    /// <param name="mousePos">Mouse position in surface-local pixels.</param>
    /// <param name="mouseDown">True if primary mouse button is held.</param>
    /// <param name="mouseClicked">True on the frame the primary button was pressed.</param>
    /// <param name="scrollDelta">Vertical mouse-wheel delta (positive = up). Default 0. Also
    /// accepted as a pan input by a pure horizontal scroller — see
    /// <see cref="Core.Style.OverflowX"/>.</param>
    /// <param name="dt">Frame delta time in seconds. Default 0.</param>
    /// <param name="forceRedraw">
    /// Repaint this frame even if the tree fingerprint is unchanged. Equivalent to a
    /// one-shot <see cref="Invalidate"/>.
    /// <para>Historically this meant "don't trust <c>IsDirty</c>", and callers set it
    /// permanently because <c>IsDirty</c> was useless for trees rebuilt every frame.
    /// That is now handled by the fingerprint, so leaving it <c>true</c> costs a full
    /// rasterise + readback + texture upload on every frame for no benefit. Prefer
    /// leaving it <c>false</c>.</para>
    /// </param>
    /// <param name="rightMouseDown">
    /// True if the secondary (right) mouse button is held. Feeds
    /// <see cref="Core.NodeAnimState.IsRightPressed"/>. Optional and defaults to
    /// <c>false</c> so existing callers compile unchanged — pass
    /// <c>ImGui.IsMouseDown(ImGuiMouseButton.Right)</c> to enable right-click on a surface.
    /// </param>
    /// <param name="rightMouseClicked">
    /// True on the frame the secondary mouse button was pressed. Fires
    /// <see cref="Core.Node.OnRightClick"/> on whatever is hovered — see that event's
    /// remarks for the coordinate space it reports. Pass
    /// <c>ImGui.IsMouseClicked(ImGuiMouseButton.Right)</c>, gated the same way the existing
    /// left-click callers gate <c>mouseClicked</c> (window-hover check, etc.).
    /// </param>
    /// <param name="scrollDeltaX">
    /// Horizontal mouse-wheel delta (positive = left). Default 0. Pass
    /// <c>ImGui.GetIO().MouseWheelH</c> to enable real horizontal-wheel/trackpad panning —
    /// optional even for a horizontal list, since <paramref name="scrollDelta"/> already
    /// drives one that has no vertical scrolling of its own.
    /// </param>
    public (ImTextureID? handle, Dictionary<Node, LayoutBox> layout) Render(
        Node root,
        float time,
        Vector2 mousePos,
        bool mouseDown,
        bool mouseClicked,
        float scrollDelta        = 0f,
        float dt                 = 0f,
        bool forceRedraw         = false,
        bool rightMouseDown      = false,
        bool rightMouseClicked   = false,
        float scrollDeltaX       = 0f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        float scale = _scale;

        long t0 = Stopwatch.GetTimestamp();
        var layoutResult = _layout.Compute(root, Width / scale, Height / scale);

        long t1 = Stopwatch.GetTimestamp();
        // Layout boxes are in logical units, so the pointer has to be too — otherwise at
        // any scale but 1 every click lands somewhere other than where it looks.
        InteractionManager.Update(root, layoutResult, mousePos / scale, mouseDown, mouseClicked,
            rightMouseDown, rightMouseClicked, scrollDelta, scrollDeltaX, dt);

        // Fingerprint after interaction: hover/press/scroll updates land in animation
        // state, and a scroll offset change has to move the fingerprint.
        long t2 = Stopwatch.GetTimestamp();
        ulong fingerprint = SurfaceFingerprint.Compute(root, _layout.Stamp, out bool animated);
        long t3 = Stopwatch.GetTimestamp();

        // Content change vs. clock advance are treated differently: content repaints now,
        // animation repaints at most AnimationFpsCap times a second. See that property.
        bool contentChanged = !_hasPainted || fingerprint != _lastFingerprint;
        bool repaint        = forceRedraw || AlwaysRedraw || contentChanged;

        if (!repaint && animated)
        {
            if (AnimationFpsCap <= 0)
            {
                repaint = true;
            }
            else
            {
                double minGapMs = 1000.0 / AnimationFpsCap;
                if (Ms(_lastAnimPaintTick, t3) >= minGapMs || _lastAnimPaintTick == 0)
                    repaint = true;
            }
            if (repaint) _lastAnimPaintTick = t3;
        }

        long t4 = t3;
        double readbackMs = 0, textureMs = 0;
        if (repaint)
        {
            var canvas = _surface.Canvas;
            if (scale != 1f)
            {
                // Scale the canvas, not the output: glyphs and strokes rasterise at their
                // effective size instead of being resampled up from a 1× bitmap.
                int save = canvas.Save();
                canvas.Scale(scale);
                _renderer.Render(canvas, root, layoutResult, time);
                canvas.RestoreToCount(save);
            }
            else
            {
                _renderer.Render(canvas, root, layoutResult, time);
            }
            t4 = Stopwatch.GetTimestamp();

            _textures.Upload(_surface);
            readbackMs = _textures.LastReadbackMs;
            textureMs  = _textures.LastCreateMs;

            _lastFingerprint = fingerprint;
            _hasPainted      = true;
        }

        _stats.Width  = Width;
        _stats.Height = Height;
        _stats.Record(
            layoutMs:      Ms(t0, t1),
            interactionMs: Ms(t1, t2),
            fingerprintMs: Ms(t2, t3),
            renderMs:      Ms(t3, t4),
            readbackMs:    readbackMs,
            uploadMs:      textureMs,
            repainted:     repaint);

        LastFrameRepainted = repaint;
        root.ClearDirty();

        return (_textures.Handle, layoutResult);
    }

    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

    private static double Ms(long from, long to) => (to - from) * TicksToMs;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PanacheStats.Unregister(_stats);
        _surface.Dispose();
        _renderer.Dispose();
        _textures.Dispose();
        _layout.Dispose();
    }
}
