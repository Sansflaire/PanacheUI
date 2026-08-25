using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SkiaSharp;

namespace PanacheUI.Rendering;

/// <summary>
/// Pre-resampled copies of a source bitmap at the exact pixel sizes it is actually drawn at.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> The bundled icon set ships at 313×313 and is drawn at 11–30
/// logical pixels — a 10× to 28× reduction. Skia's samplers are texture filters: nearest reads
/// one source pixel, linear reads 2×2, and even a cubic resampler reads only 4×4. None of them
/// look at more than a sliver of a 313-pixel source, so a 313→13 draw is decided by roughly
/// sixteen pixels out of ninety-eight thousand. Which sixteen depends on sub-pixel position, so
/// thin strokes flickered, dropped out, or came back doubled as a window moved. That is the
/// "low resolution" look — not a shortage of source detail, but nearly all of it being skipped.</para>
///
/// <para><b>The fix is to reduce in steps.</b> Halving with a linear filter averages an exact
/// 2×2 block, so every source pixel contributes; repeat until within 2× of the target and the
/// final short hop is a genuine cubic resample. This is what mipmapping does internally, done
/// explicitly so it does not depend on Skia choosing to build mip chains for a raster surface —
/// and cached, so the cost is a handful of resizes at startup rather than per frame. Drawing a
/// 30px bitmap into a 30px rect is also markedly cheaper than rescaling 313px every frame.</para>
///
/// <para><b>Sizes are device pixels, not layout units.</b> The caller multiplies by the canvas
/// matrix, so a surface at <see cref="PanacheSurface.Scale"/> 1.32 caches a genuinely sharper
/// copy rather than upscaling the 1.0 one. That is the whole point of scaling the layout instead
/// of the bitmap, carried through to images.</para>
///
/// <para>Entries hang off the source bitmap via <see cref="ConditionalWeakTable{TKey,TValue}"/>,
/// so a bitmap that becomes garbage takes its variants with it. Icons are cached for the life of
/// the process by <see cref="Icons.PanacheIcons"/>, so in practice theirs are too — a few dozen
/// small bitmaps, which is the trade this is making on purpose.</para>
/// </remarks>
internal static class BitmapScaleCache
{
    /// <summary>
    /// Below this reduction the source is already close enough that a direct cubic draw is
    /// indistinguishable, and pre-resampling would only add a rounding error of its own.
    /// </summary>
    private const float MinRatio = 1.6f;

    /// <summary>
    /// Guards against a pathological layout (or a bad scale) asking for a multi-thousand-pixel
    /// variant of every icon on screen. Well above any real UI size.
    /// </summary>
    private const int MaxSide = 1024;

    /// <summary>Cache key for "the source at its own size", which has no width/height of its own.</summary>
    private const long UnscaledKey = 0L;

    private static readonly ConditionalWeakTable<SKBitmap, Dictionary<long, SKImage>> Variants = new();

    /// <summary>
    /// The best available copy of <paramref name="src"/> for a draw covering
    /// <paramref name="dstWpx"/> × <paramref name="dstHpx"/> device pixels, as an
    /// <see cref="SKImage"/> ready for <c>DrawImage</c>. Returns a full-size image when the draw
    /// is not reducing enough to be worth pre-computing. Never throws — a resize failure falls
    /// back to the unscaled image.
    /// </summary>
    /// <remarks>
    /// An image rather than a bitmap because SkiaSharp 3.x dropped the sampling-aware
    /// <c>DrawBitmap</c> overloads; sampling options are only honoured via <c>DrawImage</c>.
    /// Wrapping happens once here and is cached, so the change costs nothing per frame.
    /// </remarks>
    public static SKImage? ForSize(SKBitmap src, float dstWpx, float dstHpx)
    {
        if (src.Width <= 0 || src.Height <= 0) return null;

        int w = (int)MathF.Ceiling(dstWpx);
        int h = (int)MathF.Ceiling(dstHpx);

        // Only reduction is worth pre-computing. Enlarging has no detail to recover, and the
        // renderer already asks for a cubic sampler in that direction.
        bool worthReducing = w > 0 && h > 0 && w <= MaxSide && h <= MaxSide
                          && (src.Width >= w * MinRatio || src.Height >= h * MinRatio);

        long key   = worthReducing ? (((long)w << 32) | (uint)h) : UnscaledKey;
        var  table = Variants.GetOrCreateValue(src);

        lock (table)
        {
            if (table.TryGetValue(key, out var hit)) return hit;

            SKImage? image = null;
            try
            {
                if (worthReducing)
                {
                    using var reduced = Reduce(src, w, h);
                    if (reduced != null) image = SKImage.FromBitmap(reduced);
                }

                image ??= SKImage.FromBitmap(src);
            }
            catch
            {
                // Out of memory, an unsupported color type, anything: a missing icon is a far
                // better outcome than taking the render thread down over one glyph.
            }

            // Cached even when null, so a bitmap that cannot be wrapped is not retried every frame.
            table[key] = image!;
            return image;
        }
    }

    /// <summary>
    /// Progressive halving down to within 2× of the target, then one cubic resample.
    /// </summary>
    /// <remarks>
    /// Each halving is a linear filter over an exactly 2×2 block, which is a box average — every
    /// source pixel is read exactly once per step, which is precisely the property a single
    /// large-ratio resample lacks. Mitchell on the final short hop softens the remaining
    /// stair-stepping without the ringing Catmull-Rom would add to hard-edged glyph artwork.
    /// </remarks>
    /// <returns>
    /// A newly allocated bitmap the caller owns, or null if Skia declined every step.
    /// <b>Never the source</b> — the caller disposes what it gets back, and returning
    /// <paramref name="src"/> here would destroy an icon cached for the life of the process
    /// and leave every later frame drawing from freed pixels.
    /// </returns>
    private static SKBitmap? Reduce(SKBitmap src, int w, int h)
    {
        var ct = src.ColorType;
        var at = src.AlphaType;
        var cs = src.ColorSpace;

        SKBitmap  current = src;
        SKBitmap? scratch = null;   // the only thing this method may dispose

        while (current.Width >= w * 2 && current.Height >= h * 2)
        {
            int nw = Math.Max(w, current.Width  / 2);
            int nh = Math.Max(h, current.Height / 2);

            var half = current.Resize(new SKImageInfo(nw, nh, ct, at, cs),
                                      new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));

            // A failed step leaves `current` valid — stop halving and let the cubic pass below
            // finish from wherever we reached.
            if (half == null) break;

            scratch?.Dispose();
            scratch = half;
            current = half;
        }

        // Already exactly right: hand back the intermediate (which the caller owns) and detach
        // it from `scratch` so the cleanup below cannot dispose the thing being returned.
        if (current.Width == w && current.Height == h && !ReferenceEquals(current, src))
            return scratch;

        var final = current.Resize(new SKImageInfo(w, h, ct, at, cs),
                                   new SKSamplingOptions(SKCubicResampler.Mitchell));

        scratch?.Dispose();
        return final;
    }
}
