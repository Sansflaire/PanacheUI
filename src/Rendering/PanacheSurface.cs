using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using PanacheUI.Core;
using PanacheUI.Layout;

namespace PanacheUI.Rendering;

/// <summary>
/// Convenience wrapper that owns the full PanacheUI rendering pipeline for a single surface:
/// RenderSurface + LayoutEngine + SkiaRenderer + TextureManager.
///
/// Replaces the four-object setup (create surface, create layout engine, create renderer,
/// create texture manager) with a single object and a single Render() call per frame.
///
/// Dirty-check optimization: when the node tree is clean and forceRedraw is false,
/// the GPU texture upload is skipped and the previous handle is returned as-is.
/// </summary>
public sealed class PanacheSurface : IDisposable
{
    private RenderSurface  _surface;
    private readonly LayoutEngine   _layout   = new();
    private readonly SkiaRenderer   _renderer = new();
    private readonly TextureManager _textures;
    private bool _disposed;

    public int Width  { get; private set; }
    public int Height { get; private set; }

    public PanacheSurface(ITextureProvider texProvider, int width, int height)
    {
        Width    = width;
        Height   = height;
        _surface = new RenderSurface(width, height);
        _textures = new TextureManager(texProvider);
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
    }

    /// <summary>
    /// Run the full pipeline in one call: layout → interaction → render (if dirty) → upload.
    ///
    /// Returns the ImGui texture handle to pass to <c>ImGui.Image()</c> and the layout dict
    /// for manual hit-testing or position queries.
    ///
    /// The tree is only re-rendered when <paramref name="root"/>.IsDirty is true or
    /// <paramref name="forceRedraw"/> is set. IsDirty is cleared after rendering.
    /// </summary>
    /// <param name="root">Root of the UI tree.</param>
    /// <param name="time">Elapsed seconds — drives animated effects.</param>
    /// <param name="mousePos">Mouse position in surface-local pixels.</param>
    /// <param name="mouseDown">True if primary mouse button is held.</param>
    /// <param name="mouseClicked">True on the frame the primary button was pressed.</param>
    /// <param name="scrollDelta">Mouse-wheel delta (positive = up). Default 0.</param>
    /// <param name="dt">Frame delta time in seconds. Default 0.</param>
    /// <param name="forceRedraw">When true, always re-render even if tree is clean.</param>
    public (ImTextureID? handle, Dictionary<Node, LayoutBox> layout) Render(
        Node root,
        float time,
        Vector2 mousePos,
        bool mouseDown,
        bool mouseClicked,
        float scrollDelta  = 0f,
        float dt           = 0f,
        bool forceRedraw   = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var layoutResult = _layout.Compute(root, Width, Height);
        InteractionManager.Update(root, layoutResult, mousePos, mouseDown, mouseClicked, scrollDelta, dt);

        if (forceRedraw || root.IsDirty)
        {
            _renderer.Render(_surface.Canvas, root, layoutResult, time);
            _textures.Upload(_surface);
            root.ClearDirty();
        }

        return (_textures.Handle, layoutResult);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _surface.Dispose();
        _renderer.Dispose();
        _textures.Dispose();
        _layout.Dispose();
    }
}
