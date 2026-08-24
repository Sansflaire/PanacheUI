using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;

namespace PanacheUI.Rendering;

/// <summary>
/// Converts a RenderSurface's pixel buffer into a Dalamud texture for use
/// with ImGui.Image(). Recreates the texture each time Upload() is called.
/// </summary>
public sealed class TextureManager : IDisposable
{
    private static readonly double MsPerTick = 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    private readonly ITextureProvider _texProvider;
    private IDalamudTextureWrap? _texture;
    private byte[]? _pixelBuffer;
    private bool _disposed;

    public TextureManager(ITextureProvider texProvider)
    {
        _texProvider = texProvider;
    }

    /// <summary>
    /// Upload new pixel data from <paramref name="surface"/> and return the
    /// ImGui texture handle. Returns null if upload fails.
    /// </summary>
    /// <summary>
    /// Milliseconds the last <see cref="Upload"/> spent pulling pixels out of Skia
    /// (CPU copy + premultiplied→straight alpha conversion).
    /// </summary>
    public double LastReadbackMs { get; private set; }

    /// <summary>
    /// Milliseconds the last <see cref="Upload"/> spent creating the GPU texture.
    /// Reported separately from readback because they have entirely different fixes:
    /// readback is memory bandwidth, this is a D3D11 resource allocation.
    /// </summary>
    public double LastCreateMs { get; private set; }

    public ImTextureID? Upload(RenderSurface surface)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastReadbackMs = LastCreateMs = 0;

        int byteCount = surface.Width * surface.Height * 4;
        if (_pixelBuffer == null || _pixelBuffer.Length != byteCount)
        {
            // Pinned-object heap: this buffer is pinned on every readback anyway, and at
            // several megabytes it would otherwise sit in the LOH and fragment it.
            _pixelBuffer = GC.AllocateArray<byte>(byteCount, pinned: true);
        }

        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        bool ok = surface.ReadPixels(_pixelBuffer);
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        LastReadbackMs = (t1 - t0) * MsPerTick;

        if (!ok) return null;

        // Dalamud exposes no way to update an existing texture's pixels — CreateFromRaw
        // always builds a new D3D11 texture + SRV — so every upload costs a full GPU
        // allocation. That is precisely why PanacheSurface gates this behind a content
        // fingerprint rather than calling it every frame.
        var spec = RawImageSpecification.Rgba32(surface.Width, surface.Height);
        var fresh = _texProvider.CreateFromRaw(spec, _pixelBuffer.AsSpan(), "PanacheUI.RenderSurface");
        LastCreateMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t1) * MsPerTick;

        // Swap only after the new texture exists, so a failed create leaves the previous
        // frame on screen instead of a blank surface.
        if (fresh == null) return _texture?.Handle;

        _texture?.Dispose();
        _texture = fresh;

        return _texture.Handle;
    }

    /// <summary>The handle of the most recently uploaded texture, or null.</summary>
    public ImTextureID? Handle => _texture?.Handle;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _texture?.Dispose();
        _texture = null;
    }
}
