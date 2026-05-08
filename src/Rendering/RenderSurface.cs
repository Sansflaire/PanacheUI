using System;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace PanacheUI.Rendering;

/// <summary>
/// Owns an SKSurface (CPU-backed RGBA raster) of a fixed pixel size.
/// Resize by creating a new RenderSurface and disposing the old one.
/// </summary>
public sealed class RenderSurface : IDisposable
{
    public int Width  { get; }
    public int Height { get; }

    private readonly SKSurface _surface;
    private bool _disposed;

    public RenderSurface(int width, int height)
    {
        Width  = width;
        Height = height;

        // RGBA8888 — matches RawImageSpecification.Rgba32 used in TextureManager
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        _surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("SkiaSharp: failed to create CPU surface.");
    }

    public SKCanvas Canvas => _surface.Canvas;

    /// <summary>
    /// Returns a snapshot SKImage backed by the current surface pixels.
    /// Caller must dispose the returned image before drawing to this surface again.
    /// </summary>
    public SKImage Snapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _surface.Snapshot();
    }

    /// <summary>Encodes the current surface as a PNG and returns the raw bytes.</summary>
    public byte[] EncodePng()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var img  = _surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public bool ReadPixels(byte[] destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Flush before reading — no-op for CPU raster surfaces (drawing is synchronous)
        // but required for any future GPU-backed surface and makes intent explicit.
        _surface.Canvas.Flush();

        var handle = GCHandle.Alloc(destination, GCHandleType.Pinned);
        try
        {
            // Unpremul here converts premul→straight on readback so ImGui's SrcAlpha blend
            // doesn't multiply alpha in a second time (which causes black halos on blurs/glows).
            // The surface itself stays Premul — required for Skia filter compositing math.
            var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            return _surface.ReadPixels(info, handle.AddrOfPinnedObject(), Width * 4, 0, 0);
        }
        finally
        {
            handle.Free();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _surface.Dispose();
    }
}
